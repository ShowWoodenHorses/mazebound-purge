using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Level;
using Purge.Core.Navigation;
using Purge.Core.Waves;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Run;
using UnityEngine;

namespace Purge.Gameplay.Waves
{
    public enum WavePhase
    {
        /// <summary>The level has no waves.</summary>
        Inactive = 0,
        /// <summary>Before the first wave.</summary>
        Delay = 1,
        Running = 2,
        /// <summary>Between waves (ТЗ: отсчёт, «Пропустить +N»): the wave's drop lies until the next wave.</summary>
        Pause = 3,
        /// <summary>The last wave is cleared: the level is completed.</summary>
        Completed = 4,
    }

    /// <summary>
    /// The run's waves (stage 6a): on a level whose horde has a <see cref="WaveScenario"/> (and is not a dev level),
    /// generates each wave (<see cref="WaveGenerator"/>, × the run's difficulty, seed of the run) and spawns it at the
    /// level's spawn points (<see cref="LevelData.MonsterSpawns"/>): a pack climbs out within <see cref="SpawnRadius"/>
    /// of one point; points closer than <see cref="MinPlayerDistance"/> to the player or cut off from them are skipped
    /// (all of them — the farthest reachable one). A wave is cleared when all of it has appeared and no monster is
    /// alive; after the last one <see cref="Completed"/> (<see cref="WavesClearedRule"/> completes the level). Ticks after
    /// <see cref="HordeSystem"/>.
    /// <para>Stage 6b: the pause between waves counts down <see cref="WaveScenario.PauseTime"/>; <see cref="Skip"/> starts
    /// the next wave at once and pays <see cref="SkipBonus"/> into the <see cref="RunWallet"/>. When the next wave starts
    /// the monsters' drop still lying disappears (<see cref="PickupSystem.ClearDropped"/>).</para>
    /// </summary>
    public sealed class WaveSystem : ILevelLoadStep, ILevelTickable
    {
        /// <summary>Cells around the spawn point a monster may climb out at.</summary>
        public const float SpawnRadius = 2f;

        /// <summary>Spawn points this close to the player (cells, straight line) wait.</summary>
        public const float MinPlayerDistance = 8f;

        private const int SpawnsPerTick = 8;

        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly PlayerSystem _player;
        private readonly PickupSystem _pickups;
        private readonly RunWallet _wallet;
        private readonly List<WaveSpawn> _spawns = new List<WaveSpawn>();
        private readonly Dictionary<int, int> _packPoints = new Dictionary<int, int>();
        private readonly List<int> _allowed = new List<int>();
        private int[] _kinds = Array.Empty<int>();
        private DeterministicRandom _random;
        private int _next;
        private float _waveTime;

        public WaveSystem(LevelData level, HordeSystem horde, PlayerSystem player, LevelLaunchOptions options = null,
            PickupSystem pickups = null, RunWallet wallet = null)
        {
            _level = level;
            _horde = horde;
            _player = player;
            _pickups = pickups;
            _wallet = wallet;
            Seed = options?.Seed ?? Environment.TickCount;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public WaveScenario Scenario { get; private set; }
        public WavePhase Phase { get; private set; }
        public bool IsActive => Phase != WavePhase.Inactive;

        /// <summary>The run's seed: wave N is generated from it (the run save keeps it, stage 6e).</summary>
        public int Seed { get; }

        /// <summary>Current (or last) wave, from 0; -1 before the first.</summary>
        public int WaveIndex { get; private set; } = -1;

        public int WaveCount => Scenario != null ? Scenario.Count : 0;

        /// <summary>Seconds left of <see cref="WavePhase.Delay"/> or <see cref="WavePhase.Pause"/>.</summary>
        public float TimeLeft { get; private set; }

        /// <summary>Monsters of the current wave still to appear.</summary>
        public int PendingCount => Phase == WavePhase.Running ? _spawns.Count - _next : 0;

        /// <summary>Monsters of the current wave in total.</summary>
        public int WaveSize => _spawns.Count;

        /// <summary>Enemies of the current wave left: alive (raised by a Shaman too) and still to appear; 0 between waves.</summary>
        public int RemainingCount => Phase == WavePhase.Running ? _horde.AliveCount + PendingCount : 0;

        /// <summary>Seconds the current pause has lasted (0 outside one).</summary>
        public float PauseElapsed => Phase == WavePhase.Pause ? Mathf.Max(0f, Scenario.PauseTime - TimeLeft) : 0f;

        /// <summary>
        /// What <see cref="Skip"/> pays now: the whole seconds left of the pause × the next wave's
        /// <see cref="WaveScenario.SkipRateOf"/> × the difficulty's money factor; 0 outside the pause between waves.
        /// </summary>
        public int SkipBonus
        {
            get
            {
                if (Phase != WavePhase.Pause) return 0;
                var factor = _horde.Difficulty != null ? _horde.Difficulty.Money : 1f;
                return Mathf.FloorToInt(Mathf.Ceil(TimeLeft) * Scenario.SkipRateOf(WaveIndex + 1) * factor + 1e-4f);
            }
        }

        /// <summary>A wave starts (its index).</summary>
        public event Action<int> WaveStarted;

        /// <summary>The pause was skipped (the bonus paid), right before <see cref="WaveStarted"/>.</summary>
        public event Action<int> Skipped;

        /// <summary>A wave is cleared (its index); the last one is followed by <see cref="Completed"/>.</summary>
        public event Action<int> WaveCleared;

        public event Action Completed;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_horde.UsesWaves || !_horde.IsActive || _horde.Horde.Waves.Count == 0)
                return UniTask.CompletedTask;

            Scenario = _horde.Horde.Waves;
            _random = new DeterministicRandom((int)StableHash.Of($"{_level.name}/spawns/{Seed}"));
            if (_level.MonsterSpawns.Count == 0)
                GameLog.Warning(LogChannel.Gameplay, $"Level '{_level.name}' has waves but no monster spawn points: monsters appear anywhere far from the player.");
            Phase = WavePhase.Delay;
            TimeLeft = Scenario.FirstDelay;
            GameLog.Info(LogChannel.Gameplay, $"Waves: {Scenario.Count}, seed {Seed}, {_level.MonsterSpawns.Count} spawn point(s).");
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            switch (Phase)
            {
                case WavePhase.Delay:
                case WavePhase.Pause:
                    TimeLeft -= deltaTime;
                    if (TimeLeft <= 0f) StartWave(WaveIndex + 1);
                    break;
                case WavePhase.Running:
                    _waveTime += deltaTime;
                    SpawnDue();
                    if (_next >= _spawns.Count && _horde.AliveCount == 0) ClearWave();
                    break;
            }
        }

        /// <summary>Starts the next wave at once, without the bonus (tests, dev). False when not waiting.</summary>
        public bool StartNextWave()
        {
            if (Phase != WavePhase.Delay && Phase != WavePhase.Pause) return false;
            StartWave(WaveIndex + 1);
            return true;
        }

        /// <summary>
        /// «Пропустить +N» of the pause between waves: pays <see cref="SkipBonus"/> into the run's wallet and starts the
        /// next wave. False outside the pause (the short delay before the first wave is not skipped).
        /// </summary>
        public bool Skip()
        {
            if (Phase != WavePhase.Pause) return false;
            var bonus = SkipBonus;
            _wallet?.Add(bonus);
            GameLog.Info(LogChannel.Gameplay, $"Pause skipped with {TimeLeft:0.#} s left: +{bonus}.");
            Skipped?.Invoke(bonus);
            StartWave(WaveIndex + 1);
            return true;
        }

        private void StartWave(int index)
        {
            // ТЗ: дроп волны лежит до начала следующей волны, потом исчезает.
            if (index > 0) _pickups?.ClearDropped();
            WaveIndex = index;
            var wave = Scenario.Waves[index];
            _spawns.Clear();
            _spawns.AddRange(WaveGenerator.Generate(wave, _horde.Difficulty, WaveGenerator.SeedOf(Seed, index)));
            if (_kinds.Length < _spawns.Count) _kinds = new int[_spawns.Count];
            for (var i = 0; i < _spawns.Count; i++)
                _kinds[i] = _horde.KindIndexOf(_spawns[i].Monster);
            _packPoints.Clear();
            _next = 0;
            _waveTime = 0f;
            TimeLeft = 0f;
            Phase = WavePhase.Running;
            GameLog.Info(LogChannel.Gameplay, $"Wave {index + 1}/{Scenario.Count}: {_spawns.Count} monster(s).");
            WaveStarted?.Invoke(index);
        }

        private void SpawnDue()
        {
            var wave = Scenario.Waves[WaveIndex];
            var limit = Mathf.Min(wave.MaxAlive, _horde.MaxCount);
            for (var n = 0; n < SpawnsPerTick && _next < _spawns.Count; n++)
            {
                var spawn = _spawns[_next];
                if (spawn.Time > _waveTime || _horde.AliveCount >= limit) return;

                var kind = _kinds[_next];
                if (kind < 0)
                {
                    // Not one of the horde's kinds (a scenario changed after Build Monsters): skipped, not stuck.
                    _next++;
                    continue;
                }

                if (!_packPoints.TryGetValue(spawn.Pack, out var point))
                {
                    point = PickPoint(spawn.Point);
                    _packPoints[spawn.Pack] = point;
                }

                var center = point >= 0 ? CenterOf(point) : FallbackCenter();
                if (_horde.SpawnEmerging(center, kind, SpawnRadius) < 0)
                    return; // no free slot or cell this tick: tries again
                _next++;
            }
        }

        private void ClearWave()
        {
            GameLog.Info(LogChannel.Gameplay, $"Wave {WaveIndex + 1}/{Scenario.Count} cleared.");
            WaveCleared?.Invoke(WaveIndex);
            if (WaveIndex + 1 >= Scenario.Count)
            {
                Phase = WavePhase.Completed;
                Completed?.Invoke();
                return;
            }

            Phase = WavePhase.Pause;
            TimeLeft = Scenario.PauseTime;
        }

        /// <summary>
        /// A spawn point on the player's floor: <paramref name="wanted"/> when allowed, else a random allowed one (far
        /// enough, reachable), else the farthest reachable; -1 = none on this floor.
        /// </summary>
        private int PickPoint(int wanted)
        {
            var points = _level.MonsterSpawns;
            var player = _player.Position;
            var floor = _player.Floor;
            _allowed.Clear();
            var farthest = -1;
            var farthestDistance = -1f;
            for (var i = 0; i < points.Count; i++)
            {
                var cell = points[i].Position;
                if (cell.Floor != floor || _horde.FlowDistance(cell.X, cell.Y) == FlowField.Unreachable) continue;
                var distance = (new Vector2(cell.X, cell.Y) - player).magnitude;
                if (distance >= MinPlayerDistance) _allowed.Add(i);
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthest = i;
                }
            }

            if (wanted >= 0 && _allowed.Contains(wanted)) return wanted;
            return _allowed.Count > 0 ? _allowed[_random.NextInt(_allowed.Count)] : farthest;
        }

        private Vector2 CenterOf(int point)
        {
            var cell = _level.MonsterSpawns[point].Position;
            return new Vector2(cell.X, cell.Y);
        }

        /// <summary>No spawn point: a random reachable cell far enough from the player (or any reachable one).</summary>
        private Vector2 FallbackCenter()
        {
            var width = _level.Geometry.Width;
            var any = -1;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var cell = _horde.RandomReachableCell(_random);
                if (cell < 0) break;
                any = cell;
                var position = new Vector2(cell % width, cell / width);
                if ((position - _player.Position).magnitude >= MinPlayerDistance) return position;
            }

            return any >= 0 ? new Vector2(any % width, any / width) : _player.Position;
        }
    }
}
