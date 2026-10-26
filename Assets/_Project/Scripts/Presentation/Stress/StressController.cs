using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Platform;
using Purge.Core.Common;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using Purge.Presentation.Visual;
using Purge.Presentation.Visual.Horde;
using Unity.Profiling;
using UnityEngine;

namespace Purge.Presentation.Stress
{
    /// <summary>
    /// Stage 2 stress test (levels with a <see cref="StressTestDefinition"/>): applies the start settings (view
    /// radius, camera distance, autopilot), measures frames (FPS, 1% low, per-system milliseconds, draw calls) and
    /// shows them in <see cref="StressOverlay"/>, whose buttons change the load at run time. The benchmark runs a
    /// fixed list of configurations (<see cref="BenchmarkPlan"/>), each after a warm-up, and logs one line per
    /// configuration plus a summary to the Performance channel (read from the browser/adb console).
    /// </summary>
    public sealed class StressController : ILevelLoadStep, ILevelTickable, ILevelLateTickable, IDisposable
    {
        private const int LiveFrames = 120;
        private const int MaxSampleFrames = 8192;
        private const float OverlayInterval = 0.5f;

        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly HordeRenderer _renderer;
        private readonly StressWeapons _weapons;
        private readonly StressEffectsView _effects;
        private readonly VisibilitySystem _visibility;
        private readonly TopDownCamera _camera;
        private readonly StressAutopilotInput _autopilot;
        private readonly CombatSandbox _sandbox;
        private readonly PlayerSystem _player;
        private readonly float[] _live = new float[LiveFrames];
        private readonly float[] _sorted = new float[MaxSampleFrames];
        private readonly float[] _samples = new float[MaxSampleFrames];
        private readonly StringBuilder _text = new StringBuilder(1024);
        private readonly List<BenchmarkResult> _results = new List<BenchmarkResult>();

        private StressTestDefinition _settings;
        private StressOverlay _overlay;
        private FeelOverlay _feelOverlay;
        private readonly Core.Feel.FeelChannels _feel;
        private ProfilerRecorder _drawCalls, _setPass, _triangles, _gcAlloc;
        private int _liveIndex, _liveCount;
        private float _overlayTimer;
        private float _resolutionScale = 1f;
        private int _previousFrameRate;

        // Benchmark state.
        private BenchmarkConfig[] _plan;
        private BenchmarkConfig _before;
        private int _step = -1;
        private float _stepTime;
        private int _sampleCount;
        private float _simMax, _fovMax, _windowSimSum, _windowSimMax;
        private int _windowFrames;
        private float _shownSimAvg, _shownSimMax;
        private double _simSum, _renderSum, _weaponSum, _effectsSum, _drawnSum, _aliveSum, _drawCallSum, _trianglesSum;
        private int _metricFrames;
        private readonly Application.Services.SettingsService _playerSettings;

        public StressController(LevelData level, HordeSystem horde, HordeRenderer renderer, StressWeapons weapons,
            StressEffectsView effects, VisibilitySystem visibility, TopDownCamera camera, IPlayerInput input,
            PlayerSystem player, CombatSandbox sandbox, Core.Feel.FeelChannels feel = null, Gameplay.Doors.DoorSystem doors = null,
            Application.Services.SettingsService playerSettings = null)
        {
            _playerSettings = playerSettings;
            _resolutionScale = PlayerResolutionScale; // what the screen renders at now
            _doors = doors;
            _feel = feel;
            _sandbox = sandbox;
            _level = level;
            _horde = horde;
            _renderer = renderer;
            _weapons = weapons;
            _effects = effects;
            _visibility = visibility;
            _camera = camera;
            _autopilot = input as StressAutopilotInput;
            _player = player;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool IsActive => _settings != null;
        public bool BenchmarkRunning => _step >= 0;
        public IReadOnlyList<BenchmarkResult> Results => _results;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _settings = _level.StressTest;
            if (_settings == null || !_horde.IsActive)
            {
                _settings = null;
                return UniTask.CompletedTask;
            }

            _autopilot?.Attach(_player);
            // Stand and sandbox start in god mode (the overlay toggles it); real levels never do.
            _horde.GodMode = true;
            // The sandbox is played by hand: its autopilot runs only in the benchmark.
            if (_settings.IsSandbox) Autopilot = false;
            // Unity caps mobile players at 30 FPS by default: measure up to the display's refresh rate.
            _previousFrameRate = UnityEngine.Application.targetFrameRate;
            SetFrameRateCap(MaxFrameRate);
            _visibility.SetRadius(_settings.VisibilityRadius);
            if (_camera != null) _camera.FollowHalfExtent = _settings.StartCameraHalfExtent;

            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");

            _overlay = new StressOverlay(this);
            if (_settings.IsSandbox && _feel != null) _feelOverlay = new FeelOverlay(_feel);
            ApplyOverlaysVisible();
            GameLog.Info(LogChannel.Performance, "Stress test ready. " + DeviceLine());
            return UniTask.CompletedTask;
        }

        // ---- Settings the overlay changes ----

        public StressTestDefinition Settings => _settings;
        public HordeSystem Horde => _horde;
        public HordeRenderer Renderer => _renderer;
        public StressWeapons Weapons => _weapons;
        public StressEffectsView Effects => _effects;
        public float CameraHalfExtent => _camera != null ? _camera.FollowHalfExtent : 0f;
        public float ResolutionScale => _resolutionScale;

        public bool Autopilot
        {
            get => _autopilot != null && _autopilot.Enabled;
            set { if (_autopilot != null) _autopilot.Enabled = value; }
        }

        public void SetCount(int count) => _horde.SetTargetCount(count);

        /// <summary>Combat sandbox: every ammo type back to the sandbox amount.</summary>
        public void RefillAmmo() => _sandbox?.Refill();

        /// <summary>Sandbox: a full stack of the next throwable kind.</summary>
        public void NextThrowable() => _sandbox?.NextThrowable();

        /// <summary>Sandbox (5b): every monster hunts / perception by the monsters' data.</summary>
        public bool AllHunters
        {
            get => _horde.Perception.AllHunters;
            set => _horde.Perception.AllHunters = value;
        }

        /// <summary>Sandbox (5b): a group of wanderers far from the player, out of view.</summary>
        public void SpawnWanderers() => SpawnAway(Core.Monsters.MonsterPerception.Wanderer, null);

        /// <summary>Sandbox (5b): a caller among a group of wanderers, far from the player, out of view.</summary>
        public void SpawnCaller() =>
            SpawnAway(Core.Monsters.MonsterPerception.Wanderer, Core.Monsters.MonsterPerception.Caller);

        /// <summary>Sandbox (5c): the officers its buttons call up (<see cref="StressTestDefinition.OnDemandMonsters"/>).</summary>
        public int OfficerCount => _horde.Horde != null ? _horde.Horde.OnDemandMonsters.Length : 0;

        /// <summary>Short button label of officer <paramref name="index"/> (the asset name without its rank words).</summary>
        public string OfficerLabel(int index)
        {
            var monster = _horde.Horde.OnDemandMonsters[index];
            if (monster == null) return "-";
            // "Distant_Castle" → "DistC", "Armadillo_Common" → "Armad": fits a button.
            var name = monster.name;
            var cut = name.IndexOf('_');
            var head = cut < 0 ? name : name.Substring(0, cut);
            head = head.Substring(0, Mathf.Min(head.Length, head.Length > 5 ? 4 : 5));
            var biome = cut < 0 || name.EndsWith("_Common", System.StringComparison.Ordinal) ? string.Empty : name.Substring(cut + 1, 1);
            return head + biome;
        }

        /// <summary>Rank of on-demand monster <paramref name="index"/> (the overlay's rows: officers, elite).</summary>
        public Core.Monsters.MonsterRank OfficerRank(int index)
        {
            var monster = _horde.Horde.OnDemandMonsters[index];
            return monster != null ? monster.Rank : Core.Monsters.MonsterRank.Officer;
        }

        private const string OverlaysHiddenKey = "Purge.Dev.OverlaysHidden";

        /// <summary>The dev panels (stress / sandbox, FEEL) are shown; remembered between runs (dev levels only).</summary>
        public bool OverlaysVisible { get; private set; } = true;

        /// <summary>The DEV button: hides or shows every dev panel.</summary>
        public void ToggleOverlays()
        {
            OverlaysVisible = !OverlaysVisible;
            PlayerPrefs.SetInt(OverlaysHiddenKey, OverlaysVisible ? 0 : 1);
            ApplyOverlaysVisible();
        }

        private void ApplyOverlaysVisible()
        {
            OverlaysVisible = PlayerPrefs.GetInt(OverlaysHiddenKey, 0) == 0;
            _overlay?.SetPanelVisible(OverlaysVisible);
            if (_feelOverlay != null) _feelOverlay.Visible = OverlaysVisible;
        }

        /// <summary>Sandbox (5c): officer <paramref name="index"/> 7–9 cells from the player (in view when possible).</summary>
        public void SpawnOfficer(int index)
        {
            var monster = _horde.Horde.OnDemandMonsters[index];
            var kind = _horde.KindIndexOf(monster);
            if (kind < 0) return;
            _groupRandom ??= new DeterministicRandom((int)StableHash.Of(_level.name + "/sandbox-groups"));
            var best = -1;
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var angle = (float)(_groupRandom.NextDouble() * System.Math.PI * 2.0);
                var at = _player.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (7f + 2f * (float)_groupRandom.NextDouble());
                var slot = _horde.SpawnAt(at, monster.Perception, kind);
                if (slot < 0) continue;
                best = slot;
                break;
            }

            GameLog.Info(LogChannel.Gameplay, best >= 0 ? $"Sandbox: officer '{monster.name}' in slot {best}." : $"Sandbox: no room for '{monster.name}'.");
        }

        /// <summary>Sandbox (5e2): affixes its champion buttons call up (<see cref="StressTestDefinition.Champions"/>).</summary>
        public int ChampionAffixCount => _horde.Champions?.Affixes.Length ?? 0;

        /// <summary>The champion buttons call up officers instead of the rank-and-file.</summary>
        public bool ChampionOfficers { get; set; }

        /// <summary>Short button label of affix <paramref name="index"/> ("Affix_Explosive" → "Explo").</summary>
        public string ChampionLabel(int index)
        {
            var affix = _horde.Champions.Affixes[index];
            if (affix == null) return "-";
            var name = affix.name.StartsWith("Affix_", System.StringComparison.Ordinal) ? affix.name.Substring(6) : affix.name;
            return name.Substring(0, Mathf.Min(name.Length, 5));
        }

        /// <summary>
        /// Sandbox (5e2): a champion with affix <paramref name="affix"/> 7–9 cells from the player — a hunting
        /// rank-and-file of the waves' kinds, or a random officer (<see cref="ChampionOfficers"/>).
        /// </summary>
        public void SpawnChampion(int affix)
        {
            _groupRandom ??= new DeterministicRandom((int)StableHash.Of(_level.name + "/sandbox-groups"));
            var kind = -1;
            if (ChampionOfficers)
            {
                var officers = new System.Collections.Generic.List<int>();
                for (var o = 0; o < OfficerCount; o++)
                    if (OfficerRank(o) == Core.Monsters.MonsterRank.Officer)
                        officers.Add(_horde.KindIndexOf(_horde.Horde.OnDemandMonsters[o]));
                officers.RemoveAll(index => index < 0);
                if (officers.Count == 0) return;
                kind = officers[_groupRandom.NextInt(officers.Count)];
            }

            for (var attempt = 0; attempt < 32; attempt++)
            {
                var angle = (float)(_groupRandom.NextDouble() * System.Math.PI * 2.0);
                var at = _player.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (7f + 2f * (float)_groupRandom.NextDouble());
                var perception = kind >= 0 ? _horde.KindList[kind].Definition.Perception : Core.Monsters.MonsterPerception.Hunter;
                var slot = _horde.SpawnAt(at, perception, kind);
                if (slot < 0) continue;
                var made = _horde.MakeChampion(slot, affix);
                GameLog.Info(LogChannel.Gameplay,
                    $"Sandbox: champion '{ChampionLabel(affix)}' {_horde.DefinitionOf(slot)?.name ?? "built-in"} in slot {slot}{(made ? "" : " (not made)")}.");
                return;
            }

            GameLog.Info(LogChannel.Gameplay, "Sandbox: no room for a champion.");
        }

        private const int SandboxGroupSize = 10;
        private const float SandboxGroupMinDistance = 10f;
        private const float SandboxGroupMaxDistance = 20f;

        private void SpawnAway(Core.Monsters.MonsterPerception perception, Core.Monsters.MonsterPerception? leader)
        {
            _groupRandom ??= new DeterministicRandom((int)StableHash.Of(_level.name + "/sandbox-groups"));
            var width = _level.Geometry.Width;
            var fallback = -1;
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var cell = _horde.RandomReachableCell(_groupRandom);
                if (cell < 0) break;
                var position = new Vector2(cell % width, cell / width);
                var distance = (position - _player.Position).magnitude;
                if (distance < SandboxGroupMinDistance || distance > SandboxGroupMaxDistance) continue;
                fallback = cell;
                if (_visibility.IsVisible(new Core.Grid.GridPosition(cell % width, cell / width, _player.Floor))) continue;
                Spawn(cell);
                return;
            }

            if (fallback >= 0) Spawn(fallback);

            void Spawn(int cell)
            {
                var count = _horde.SpawnGroup(new Vector2(cell % width, cell / width), SandboxGroupSize, perception, leader);
                GameLog.Info(LogChannel.Gameplay, $"Sandbox: {count} monster(s) at ({cell % width}, {cell / width}), leader {leader?.ToString() ?? "none"}.");
            }
        }

        private DeterministicRandom _groupRandom;

        public void SetCamera(float halfExtent)
        {
            if (_camera != null) _camera.FollowHalfExtent = halfExtent;
        }

        /// <summary>The display's refresh rate (the highest cap that makes sense).</summary>
        public static int MaxFrameRate => Mathf.Max(30, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));

        public int FrameRateCap => UnityEngine.Application.targetFrameRate;

        public void SetFrameRateCap(int fps) => UnityEngine.Application.targetFrameRate = fps;

        /// <summary>The player's render resolution (Android, settings «Game»); 1 elsewhere.</summary>
        private float PlayerResolutionScale =>
            UnityEngine.Application.platform == RuntimePlatform.Android && _playerSettings != null
                ? _playerSettings.Game.ResolutionScale
                : 1f;

        public void SetResolutionScale(float scale)
        {
            _resolutionScale = scale;
            WebPage.SetResolutionScale(scale);
        }

        public void StartBenchmark()
        {
            if (!IsActive || BenchmarkRunning) return;
            _plan = _settings.IsSandbox ? BenchmarkPlan.CreateSandbox(_settings) : BenchmarkPlan.Create(_settings);
            _before = Capture();
            _beforeHunters = AllHunters;
            _beforeGod = _horde.GodMode;
            if (_settings.IsSandbox)
            {
                // Everyone comes for the player, nothing hurts them (the bench must run to the end).
                AllHunters = true;
                _horde.GodMode = true;
                if (_sandbox != null && !_sandbox.ArmForBench())
                    GameLog.Warning(LogChannel.Performance, "BENCH: no bench weapon — the player fires what is in hands.");
            }

            _results.Clear();
            GameLog.Info(LogChannel.Performance, $"BENCH start: {_plan.Length} configs × " +
                                                 $"({_settings.BenchmarkWarmup:0}+{_settings.BenchmarkSample:0}) s. {DeviceLine()}");
            BeginStep(0);
        }

        public void StopBenchmark()
        {
            if (!BenchmarkRunning) return;
            _step = -1;
            Apply(_before);
            RestoreSandbox();
            GameLog.Info(LogChannel.Performance, "BENCH stopped.");
        }

        // ---- Ticks ----

        public void Tick(float deltaTime)
        {
            if (!IsActive) return;

            var frame = Time.unscaledDeltaTime;
            _live[_liveIndex] = frame;
            _liveIndex = (_liveIndex + 1) % LiveFrames;
            _liveCount = Math.Min(_liveCount + 1, LiveFrames);

            _windowSimSum += _horde.SimulationMs;
            _windowSimMax = Mathf.Max(_windowSimMax, _horde.SimulationMs);
            _windowFrames++;

            if (BenchmarkRunning)
                TickBenchmark(frame);
        }

        public void LateTick(float deltaTime)
        {
            if (!IsActive || _overlay == null) return;
            _overlayTimer -= Time.unscaledDeltaTime;
            if (_overlayTimer > 0f) return;
            _overlayTimer = OverlayInterval;
            _shownSimAvg = _windowFrames > 0 ? _windowSimSum / _windowFrames : 0f;
            _shownSimMax = _windowSimMax;
            _windowSimSum = _windowSimMax = 0f;
            _windowFrames = 0;
            _overlay.SetStats(BuildStats());
            _overlay.Refresh();
        }

        public void Dispose()
        {
            _drawCalls.Dispose();
            _setPass.Dispose();
            _triangles.Dispose();
            _gcAlloc.Dispose();
            _overlay?.Dispose();
            _overlay = null;
            _feelOverlay?.Dispose();
            _feelOverlay = null;
            if (!Mathf.Approximately(_resolutionScale, PlayerResolutionScale))
                WebPage.SetResolutionScale(PlayerResolutionScale); // back to the player's setting
            if (_settings != null)
                UnityEngine.Application.targetFrameRate = _previousFrameRate;
        }

        // ---- Benchmark ----

        private void BeginStep(int step)
        {
            _step = step;
            _stepTime = 0f;
            _sampleCount = 0;
            _simMax = _fovMax = 0f;
            _simSum = _renderSum = _weaponSum = _effectsSum = _drawnSum = _aliveSum = _drawCallSum = _trianglesSum = 0;
            _metricFrames = 0;
            Apply(_plan[step]);
            // A frozen horde measures drawing the crowd the previous step gathered around the player; every other
            // step starts from a fresh wave.
            if (!_plan[step].Freeze)
                _horde.Reset();
            _weapons.ClearAll();
            if (_settings.IsSandbox) SpawnExtras(_plan[step]);
        }

        // ---- Sandbox benchmark (stage 5) ----

        private readonly Gameplay.Doors.DoorSystem _doors;
        private bool _beforeHunters, _beforeGod;

        /// <summary>
        /// A fresh step of the sandbox bench: doors closed again (officers chase through the second flow field), the
        /// step's officers, elite and champions on top of the fresh wave; the player's ammo topped up and the bench
        /// rifle firing (or not) as the step says.
        /// </summary>
        private void SpawnExtras(BenchmarkConfig config)
        {
            _sandbox?.Refill();
            if (_autopilot != null) _autopilot.Shooting = config.Shoot;
            if (_doors != null)
                foreach (var door in _doors.Doors)
                    if (_doors.IsOpen(door.Id) && !_doors.IsBroken(door.Id) && !_horde.AnyBodyInCell(door.Position))
                        _doors.SetOpen(door.Id, false);

            for (var o = 0; o < OfficerCount; o++)
            {
                var rank = OfficerRank(o);
                var copies = rank == Core.Monsters.MonsterRank.Officer && config.Officers ? 2 :
                    rank == Core.Monsters.MonsterRank.Elite && config.Elite ? 1 : 0;
                for (var n = 0; n < copies; n++) SpawnOfficer(o);
            }

            var officers = ChampionOfficers;
            for (var c = 0; c < config.Champions && ChampionAffixCount > 0; c++)
            {
                ChampionOfficers = c % 3 == 2;
                SpawnChampion(c % ChampionAffixCount);
            }

            ChampionOfficers = officers;
        }

        private void RestoreSandbox()
        {
            if (!_settings.IsSandbox) return;
            if (_autopilot != null) _autopilot.Shooting = false;
            _sandbox?.Disarm();
            AllHunters = _beforeHunters;
            _horde.GodMode = _beforeGod;
        }

        private void TickBenchmark(float frame)
        {
            _stepTime += frame;
            if (_stepTime < _settings.BenchmarkWarmup) return;

            if (_sampleCount < MaxSampleFrames)
                _samples[_sampleCount++] = frame;
            _simSum += _horde.SimulationMs;
            _simMax = Mathf.Max(_simMax, _horde.SimulationMs);
            _fovMax = Mathf.Max(_fovMax, _visibility.LastComputeMs);
            _renderSum += _renderer.RenderMs;
            _weaponSum += _weapons.TickMs;
            _effectsSum += _effects.RenderMs;
            _drawnSum += _renderer.DrawnCount;
            _aliveSum += _horde.AliveCount;
            if (_drawCalls.Valid) _drawCallSum += _drawCalls.LastValue;
            if (_triangles.Valid) _trianglesSum += _triangles.LastValue;
            _metricFrames++;

            if (_stepTime < _settings.BenchmarkWarmup + _settings.BenchmarkSample) return;

            var result = Summarize(_plan[_step]);
            _results.Add(result);
            GameLog.Info(LogChannel.Performance, "BENCH " + result.Line());

            if (_step + 1 < _plan.Length)
            {
                BeginStep(_step + 1);
                return;
            }

            _step = -1;
            Apply(_before);
            RestoreSandbox();
            var summary = new StringBuilder("BENCH done. ").Append(DeviceLine()).Append('\n');
            summary.Append(BenchmarkResult.Header).Append('\n');
            foreach (var r in _results)
                summary.Append(r.Line()).Append('\n');
            GameLog.Info(LogChannel.Performance, summary.ToString());
        }

        private BenchmarkResult Summarize(BenchmarkConfig config)
        {
            var n = Math.Max(1, _sampleCount);
            Array.Copy(_samples, _sorted, _sampleCount);
            Array.Sort(_sorted, 0, _sampleCount);
            double total = 0;
            for (var i = 0; i < _sampleCount; i++) total += _sorted[i];
            var p99 = _sorted[Math.Min(_sampleCount - 1, (int)(_sampleCount * 0.99f))];
            var p50 = _sorted[_sampleCount / 2];
            var frames = Math.Max(1, _metricFrames);
            return new BenchmarkResult
            {
                Config = config,
                AvgFps = (float)(n / Math.Max(total, 1e-6)),
                Low1Fps = 1f / Mathf.Max(p99, 1e-6f),
                MedianMs = p50 * 1000f,
                SimMs = (float)(_simSum / frames),
                SimMaxMs = _simMax,
                FovMaxMs = _fovMax,
                FlowMs = _horde.FlowMs,
                RenderMs = (float)(_renderSum / frames),
                WeaponMs = (float)(_weaponSum / frames),
                EffectsMs = (float)(_effectsSum / frames),
                Drawn = (float)(_drawnSum / frames),
                Alive = (float)(_aliveSum / frames),
                DrawCalls = _drawCalls.Valid ? (float)(_drawCallSum / frames) : -1f,
                Triangles = _triangles.Valid ? (float)(_trianglesSum / frames) : -1f,
            };
        }

        private BenchmarkConfig Capture() => new BenchmarkConfig
        {
            Name = "before",
            Count = _horde.TargetCount,
            Camera = CameraHalfExtent,
            Weapon = _weapons.Weapon,
            Storm = _weapons.RateMultiplier,
            Fire = _weapons.KeepFireCells,
            Vat = _renderer.UseVat,
            Lerp = _renderer.Interpolate,
            Shadows = _renderer.Shadows,
            Render = _renderer.Enabled,
            Freeze = _horde.Frozen,
            Effects = _effects.Particles,
            Resolution = _resolutionScale,
            Cull = _renderer.CullByVisibility,
        };

        private void Apply(BenchmarkConfig config)
        {
            _horde.SetTargetCount(config.Count);
            SetCamera(config.Camera);
            _weapons.Weapon = config.Weapon;
            _weapons.RateMultiplier = config.Storm;
            _weapons.KeepFireCells = config.Fire;
            _renderer.UseVat = config.Vat;
            _renderer.Interpolate = config.Lerp;
            _renderer.Shadows = config.Shadows;
            _renderer.Enabled = config.Render;
            _renderer.CullByVisibility = config.Cull;
            _horde.Frozen = config.Freeze;
            _effects.Particles = config.Effects;
            _effects.Tracers = config.Effects;
            _effects.FireCells = config.Effects;
            if (!Mathf.Approximately(config.Resolution, _resolutionScale))
                SetResolutionScale(config.Resolution);
            // The sandbox is played by hand outside its benchmark.
            Autopilot = BenchmarkRunning || !_settings.IsSandbox;
            _weapons.AutoFire = true;
            SetFrameRateCap(MaxFrameRate);
        }

        // ---- Text ----

        private StringBuilder BuildStats()
        {
            var t = _text;
            t.Clear();
            LiveFps(out var fps, out var low);
            t.Append("FPS ").Append(fps.ToString("0")).Append("  1% ").Append(low.ToString("0"))
                .Append("  ").Append((1000f / Mathf.Max(fps, 0.01f)).ToString("0.0")).Append(" ms\n");
            t.Append("Monsters ").Append(_horde.AliveCount).Append('/').Append(_horde.TargetCount)
                .Append("  drawn ").Append(_renderer.DrawnCount).Append("  kills ").Append(_horde.Kills).Append('\n');
            if (_settings.IsSandbox)
            {
                int wander = 0, hear = 0, chase = 0;
                var awareness = _horde.Perception.Awareness;
                var states = _horde.States;
                for (var i = 0; i < _horde.HighWater && i < awareness.Length; i++)
                {
                    if (!HordeSystem.IsAlive(states[i])) continue;
                    switch (awareness[i])
                    {
                        case HordeAwareness.Wander: wander++; break;
                        case HordeAwareness.Investigate: hear++; break;
                        default: chase++; break;
                    }
                }

                t.Append("Wander ").Append(wander).Append("  hear ").Append(hear).Append("  chase ").Append(chase)
                    .Append("  calls ").Append(_horde.Perception.Calls).Append("  noise ").Append(_horde.Perception.FieldBuilds).Append('\n');
            }
            t.Append("Sim ").Append(_shownSimAvg.ToString("0.00")).Append('/').Append(_shownSimMax.ToString("0.00"))
                .Append("  flow ").Append(_horde.FlowMs.ToString("0.00"))
                .Append("  fov ").Append(_visibility.LastComputeMs.ToString("0.00")).Append(" ms\n");
            t.Append("Draw ").Append(_renderer.RenderMs.ToString("0.00")).Append("  weap ").Append(_weapons.TickMs.ToString("0.00"))
                .Append("  fx ").Append(_effects.RenderMs.ToString("0.00")).Append(" ms\n");
            t.Append("Bullets ").Append(_weapons.BulletCount).Append("  fire ").Append(_weapons.Fire != null ? _weapons.Fire.Count : 0)
                .Append("  calls ").Append(_renderer.DrawCalls).Append('\n');
            if (_drawCalls.Valid)
            {
                t.Append("DC ").Append(_drawCalls.LastValue).Append("  SetPass ").Append(_setPass.LastValue)
                    .Append("  tris ").Append((_triangles.LastValue / 1000).ToString()).Append("k  GC ")
                    .Append(_gcAlloc.LastValue).Append(" B\n");
            }

            t.Append("Cap ").Append(FrameRateCap).Append("  ");
            t.Append("Mesh ").Append(_renderer.MeshVertexCount).Append(" v (src ").Append(_renderer.SourceVertexCount)
                .Append(")  ").Append(Screen.width).Append('x').Append(Screen.height).Append('\n');

            if (BenchmarkRunning)
            {
                var config = _plan[_step];
                var left = _settings.BenchmarkWarmup + _settings.BenchmarkSample - _stepTime;
                t.Append("BENCH ").Append(_step + 1).Append('/').Append(_plan.Length).Append(' ').Append(config.Name)
                    .Append("  ").Append(Mathf.CeilToInt(left)).Append(" s\n");
            }
            else if (_results.Count > 0)
            {
                t.Append("Last bench: ").Append(_results.Count).Append(" configs (see log)\n");
                foreach (var r in _results)
                    t.Append(r.Config.Name).Append(": ").Append(r.AvgFps.ToString("0")).Append(" / ")
                        .Append(r.Low1Fps.ToString("0")).Append('\n');
            }

            return t;
        }

        private void LiveFps(out float fps, out float low)
        {
            if (_liveCount == 0)
            {
                fps = low = 0f;
                return;
            }

            double total = 0;
            for (var i = 0; i < _liveCount; i++)
            {
                _sorted[i] = _live[i];
                total += _live[i];
            }

            Array.Sort(_sorted, 0, _liveCount);
            fps = (float)(_liveCount / Math.Max(total, 1e-6));
            low = 1f / Mathf.Max(_sorted[Math.Min(_liveCount - 1, (int)(_liveCount * 0.99f))], 1e-6f);
        }

        private string DeviceLine() =>
            $"Device: {SystemInfo.deviceModel} | {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) | " +
            $"{UnityEngine.Application.platform} | screen {Screen.width}x{Screen.height} {MaxFrameRate} Hz cap {FrameRateCap} | level {_level.name} " +
            $"{_level.Geometry.Width}x{_level.Geometry.Height} | debug {Debug.isDebugBuild}";
    }
}
