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

        public StressController(LevelData level, HordeSystem horde, HordeRenderer renderer, StressWeapons weapons,
            StressEffectsView effects, VisibilitySystem visibility, TopDownCamera camera, IPlayerInput input,
            PlayerSystem player, CombatSandbox sandbox)
        {
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

        public void SetCamera(float halfExtent)
        {
            if (_camera != null) _camera.FollowHalfExtent = halfExtent;
        }

        /// <summary>The display's refresh rate (the highest cap that makes sense).</summary>
        public static int MaxFrameRate => Mathf.Max(30, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));

        public int FrameRateCap => UnityEngine.Application.targetFrameRate;

        public void SetFrameRateCap(int fps) => UnityEngine.Application.targetFrameRate = fps;

        public void SetResolutionScale(float scale)
        {
            _resolutionScale = scale;
            WebPage.SetResolutionScale(scale);
        }

        public void StartBenchmark()
        {
            if (!IsActive || BenchmarkRunning) return;
            _plan = BenchmarkPlan.Create(_settings);
            _before = Capture();
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
            if (!Mathf.Approximately(_resolutionScale, 1f))
                WebPage.SetResolutionScale(1f);
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
            Autopilot = true;
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
