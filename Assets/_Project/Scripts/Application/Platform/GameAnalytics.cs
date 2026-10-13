using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Flow;
using Maze.Application.Levels;
using Maze.Application.Services;
using Maze.Gameplay.Level;
using Maze.Gameplay.Navigation;
using Maze.Gameplay.Pickups;
using Maze.Gameplay.Tutorial;
using Maze.Gameplay.Player;
using Maze.Gameplay.Zombies;
using UnityEngine;

namespace Maze.Application.Platform
{
    /// <summary>
    /// What happened during the current run, gathered by the level's <see cref="LevelAnalytics"/> for the level events
    /// of <see cref="GameAnalytics"/> (which outlive the level scope). Reset when a level starts loading.
    /// </summary>
    public sealed class LevelRunStats
    {
        public float Elapsed;
        public int HintsUsed;
        public int LayersUnlocked;
        public int WeaponsPicked;
        public string LastAttacker;
        public Vector2Int PlayerCell;
        public int Frames;
        public float FrameTime;

        public float AverageFps => FrameTime > 0f ? Frames / FrameTime : 0f;

        public void Reset()
        {
            Elapsed = 0f;
            HintsUsed = LayersUnlocked = WeaponsPicked = Frames = 0;
            FrameTime = 0f;
            LastAttacker = null;
            PlayerCell = default;
        }
    }

    /// <summary>
    /// Level scope part of the analytics: counts hints, weapon pickups, the last zombie that hit the player, where the
    /// player is, play time and the frame rate into <see cref="LevelRunStats"/>; sends the in-level events (hint used,
    /// weapon picked up, tutorial step started — the funnel of the tutorial). Ticks are trivial assignments; events are rare.
    /// </summary>
    public sealed class LevelAnalytics : ILevelLoadStep, ILevelTickable, ILevelLateTickable, IDisposable
    {
        private readonly LevelRunStats _stats;
        private readonly AnalyticsService _analytics;
        private readonly LevelProgress _progress;
        private readonly PlayerSystem _player;
        private readonly RouteHintSystem _route;
        private readonly PickupSystem _pickups;
        private readonly ZombieSystem _zombies;
        private readonly TutorialSystem _tutorial;
        private readonly string _levelId;
        private bool _subscribed;

        public LevelAnalytics(LevelRunStats stats, AnalyticsService analytics, LevelProgress progress, PlayerSystem player,
            RouteHintSystem route, PickupSystem pickups, ZombieSystem zombies, Maze.Core.Level.LevelData level,
            TutorialSystem tutorial = null)
        {
            _tutorial = tutorial;
            _stats = stats;
            _analytics = analytics;
            _progress = progress;
            _player = player;
            _route = route;
            _pickups = pickups;
            _zombies = zombies;
            _levelId = level.name;
        }

        public LevelLoadStage Stage => LevelLoadStage.Warmup;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed) return UniTask.CompletedTask;
            _route.Requested += OnHint;
            _pickups.Removed += OnPickupRemoved;
            _zombies.Attacked += OnAttacked;
            if (_tutorial != null) _tutorial.StepStarted += OnTutorialStep;
            _subscribed = true;
            // The first step started at an earlier load stage.
            if (_tutorial != null && _tutorial.Current != null) OnTutorialStep(_tutorial.StepIndex);
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            _stats.Elapsed = _progress.Elapsed;
            var cell = _player.Cell;
            _stats.PlayerCell = new Vector2Int(cell.X, cell.Y);
        }

        public void LateTick(float deltaTime)
        {
            _stats.Frames++;
            _stats.FrameTime += Time.unscaledDeltaTime;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _route.Requested -= OnHint;
            _pickups.Removed -= OnPickupRemoved;
            _zombies.Attacked -= OnAttacked;
            if (_tutorial != null) _tutorial.StepStarted -= OnTutorialStep;
            _subscribed = false;
        }

        private void OnHint(RouteTarget target)
        {
            _stats.HintsUsed++;
            var parameters = _analytics.Begin();
            parameters["level"] = _levelId;
            parameters["target"] = target.ToString().ToLowerInvariant();
            parameters["time"] = Mathf.RoundToInt(_progress.Elapsed);
            _analytics.Send(AnalyticsEvents.HintUsed);
        }

        private void OnPickupRemoved(Pickup pickup)
        {
            if (pickup.Kind != PickupKind.Weapon || pickup.Weapon == null) return;
            _stats.WeaponsPicked++;
            var parameters = _analytics.Begin();
            parameters["level"] = _levelId;
            parameters["weapon"] = pickup.Weapon.Definition != null ? pickup.Weapon.Definition.Id : "unknown";
            _analytics.Send(AnalyticsEvents.WeaponPickup);
        }

        private void OnTutorialStep(int index)
        {
            var parameters = _analytics.Begin();
            parameters["level"] = _levelId;
            parameters["step"] = index + 1;
            parameters["steps"] = _tutorial.StepCount;
            parameters["time"] = Mathf.RoundToInt(_progress.Elapsed);
            _analytics.Send(AnalyticsEvents.TutorialStep);
        }

        private void OnAttacked(ZombieRuntime zombie) =>
            _stats.LastAttacker = zombie.Definition != null ? zombie.Definition.name : "zombie";
    }

    /// <summary>
    /// Level events from <see cref="GameFlow"/>: start (with attempt number and where it was started from), complete
    /// (time, stars, kills, fragments, deaths before the win, hints, map layers bought, new record, average FPS), fail
    /// (time, where, by whom), quit (left from the pause). Attempts are counted per level for the session.
    /// </summary>
    public sealed class GameAnalytics : IDisposable
    {
        private readonly GameFlow _flow;
        private readonly AnalyticsService _analytics;
        private readonly LevelRunStats _stats;
        private readonly ILevelCatalog _catalog;
        private readonly Dictionary<string, int> _attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _failsSinceWin = new Dictionary<string, int>(StringComparer.Ordinal);
        private GameFlowState _previous = GameFlowState.None;
        private string _previousLevel;
        private bool _sessionSent;
        private bool _inRun;
        private bool _initialized;

        public GameAnalytics(GameFlow flow, AnalyticsService analytics, LevelRunStats stats, ILevelCatalog catalog)
        {
            _flow = flow;
            _analytics = analytics;
            _stats = stats;
            _catalog = catalog;
        }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _flow.StateChanged += OnStateChanged;
        }

        public void Dispose()
        {
            if (_initialized) _flow.StateChanged -= OnStateChanged;
            _initialized = false;
        }

        private void OnStateChanged(GameFlowState state)
        {
            var level = _flow.CurrentLevelId;
            switch (state)
            {
                case GameFlowState.Loading when level != null:
                    if (_inRun) SendQuit(_previousLevel);
                    _stats.Reset();
                    break;
                case GameFlowState.MainMenu:
                    if (_inRun) SendQuit(_previousLevel);
                    if (!_sessionSent) SendSession();
                    break;
                case GameFlowState.Playing when _previous == GameFlowState.Loading:
                    SendStart(level);
                    break;
                case GameFlowState.Completed:
                    SendEnd(level, true);
                    break;
                case GameFlowState.Failed:
                    SendEnd(level, false);
                    break;
            }

            if (state == GameFlowState.Loading) _startedFrom = _previous;
            _previous = state;
            if (level != null) _previousLevel = level;
        }

        private GameFlowState _startedFrom;

        /// <summary>Once per launch, when the main menu first opens (sinks are initialized by then).</summary>
        private void SendSession()
        {
            _sessionSent = true;
            var parameters = _analytics.Begin();
            parameters["platform"] = UnityEngine.Application.platform.ToString();
            parameters["version"] = UnityEngine.Application.version;
            parameters["device"] = SystemInfo.deviceModel;
            parameters["memory_mb"] = SystemInfo.systemMemorySize;
            parameters["screen"] = Screen.width + "x" + Screen.height;
            parameters["system_language"] = UnityEngine.Application.systemLanguage.ToString();
            parameters["levels"] = _catalog.Levels.Count;
            _analytics.Send(AnalyticsEvents.SessionStart);
        }

        private void SendStart(string level)
        {
            _inRun = true;
            _attempts.TryGetValue(level, out var attempt);
            _attempts[level] = ++attempt;
            var parameters = Level(level);
            parameters["attempt"] = attempt;
            parameters["source"] = _startedFrom switch
            {
                GameFlowState.MainMenu => "menu",
                GameFlowState.Completed => "next_or_retry",
                GameFlowState.Failed => "retry_after_death",
                GameFlowState.Paused => "retry_from_pause",
                _ => "other",
            };
            _analytics.Send(AnalyticsEvents.LevelStart);
        }

        private void SendEnd(string level, bool completed)
        {
            if (!_inRun) return;
            _inRun = false;
            var result = _flow.LastResult ?? default;
            var parameters = Level(level);
            parameters["time"] = Mathf.RoundToInt(result.Time);
            parameters["kills"] = result.Kills;
            parameters["zombies"] = result.TotalZombies;
            parameters["fragments"] = result.Fragments;
            parameters["fragments_total"] = result.TotalFragments;
            parameters["hints"] = _stats.HintsUsed;
            parameters["avg_fps"] = Mathf.RoundToInt(_stats.AverageFps);
            _failsSinceWin.TryGetValue(level, out var fails);
            if (completed)
            {
                var previousBest = _flow.LastPreviousBestTime;
                parameters["stars"] = result.Stars;
                parameters["star_zombies"] = result.AllZombiesKilled;
                parameters["star_map"] = result.AllFragmentsCollected;
                parameters["deaths_before"] = fails;
                parameters["layers_unlocked"] = _stats.LayersUnlocked;
                parameters["new_best"] = previousBest <= 0f || result.Time < previousBest;
                _failsSinceWin[level] = 0;
                _analytics.Send(AnalyticsEvents.LevelComplete);
            }
            else
            {
                parameters["cell_x"] = _stats.PlayerCell.x;
                parameters["cell_y"] = _stats.PlayerCell.y;
                parameters["killer"] = _stats.LastAttacker ?? "unknown";
                _failsSinceWin[level] = fails + 1;
                _analytics.Send(AnalyticsEvents.LevelFail);
            }
        }

        private void SendQuit(string level)
        {
            _inRun = false;
            if (level == null) return;
            var parameters = Level(level);
            parameters["time"] = Mathf.RoundToInt(_stats.Elapsed);
            parameters["hints"] = _stats.HintsUsed;
            _analytics.Send(AnalyticsEvents.LevelQuit);
        }

        private Dictionary<string, object> Level(string level)
        {
            var parameters = _analytics.Begin();
            parameters["level"] = level;
            parameters["number"] = NumberOf(level);
            return parameters;
        }

        /// <summary>1-based catalog number; 0 for a development level.</summary>
        private int NumberOf(string level)
        {
            var levels = _catalog.Levels;
            for (var i = 0; i < levels.Count; i++)
                if (levels[i].LevelId == level) return i + 1;
            return 0;
        }
    }
}
