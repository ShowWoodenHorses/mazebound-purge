using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Level;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Waves;
using Purge.Presentation.Visual.Horde;
using Unity.Profiling;
using UnityEngine;

namespace Purge.Presentation.Stress
{
    /// <summary>
    /// Stage 6e1 measure of a real run (dev builds only, levels with waves and no stress test): while a wave runs,
    /// logs to the Performance channel a line every <see cref="WindowSeconds"/> and a summary when the wave ends —
    /// frames (avg FPS, 1% low, median), the horde (alive, drawn), the Animator monsters (held views, shown, left
    /// without a view), simulation and draw calls. Read from logcat / the browser console.
    /// Any single frame longer than <see cref="SpikeSeconds"/> while the level ticks is logged at once (`WAVES spike`,
    /// with the wave's phase): freezes outside the measured windows (wave end, pause) show up too.
    /// </summary>
    public sealed class WavePerformanceLog : ILevelLoadStep, ILevelTickable, IDisposable
    {
        public const float WindowSeconds = 10f;
        private const int MaxSamples = 1 << 16;
        private const float SpikeSeconds = 0.08f;

        private readonly WaveSystem _waves;
        private readonly HordeSystem _horde;
        private readonly HordeRenderer _renderer;
        private readonly MonsterViewPresenter _monsterViews;
        private readonly LevelData _level;
        private readonly float[] _sorted = new float[MaxSamples];
        private readonly StringBuilder _text = new StringBuilder(256);
        private readonly Stats _wave = new Stats(MaxSamples);
        private readonly Stats _window = new Stats(MaxSamples / 8);
        private ProfilerRecorder _drawCalls;
        private bool _active;
        private int _waveIndex = -1;

        public WavePerformanceLog(WaveSystem waves, HordeSystem horde, HordeRenderer renderer,
            MonsterViewPresenter monsterViews, LevelData level)
        {
            _waves = waves;
            _horde = horde;
            _renderer = renderer;
            _monsterViews = monsterViews;
            _level = level;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_waves.IsActive || _level.StressTest != null) return UniTask.CompletedTask;
            _active = true;
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _waves.WaveStarted += OnWaveStarted;
            _waves.WaveCleared += OnWaveEnded;
            _waves.Skipped += OnWaveEnded;
            GameLog.Info(LogChannel.Performance,
                $"WAVES perf on. {SystemInfo.deviceModel} | {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) | " +
                $"screen {Screen.width}x{Screen.height} cap {UnityEngine.Application.targetFrameRate} | level {_level.name}\n" +
                "WAVES name | sec | avgFPS 1%low medMs | alive avg/max drawn | anim held avg/max shown max missing max | " +
                "simMs avg/max | drawCalls");
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            if (!_active) return;
            var frame = Time.unscaledDeltaTime;
            if (frame > SpikeSeconds)
                GameLog.Info(LogChannel.Performance,
                    $"WAVES spike {frame * 1000f:0} ms | wave {_waves.WaveIndex + 1} {_waves.Phase} | t {Time.realtimeSinceStartup:0.0}");
            if (_waveIndex < 0 || _waves.Phase != WavePhase.Running) return;
            var drawCalls = _drawCalls.Valid ? _drawCalls.LastValue : -1;
            _wave.Add(frame, _horde, _renderer, _monsterViews, drawCalls);
            _window.Add(frame, _horde, _renderer, _monsterViews, drawCalls);
            if (_window.Seconds < WindowSeconds) return;
            Log("W" + (_waveIndex + 1) + "@" + _wave.Seconds.ToString("0"), _window);
            _window.Clear();
        }

        public void Dispose()
        {
            if (!_active) return;
            Finish();
            _waves.WaveStarted -= OnWaveStarted;
            _waves.WaveCleared -= OnWaveEnded;
            _waves.Skipped -= OnWaveEnded;
            _drawCalls.Dispose();
            _active = false;
        }

        private void OnWaveStarted(int index)
        {
            Finish();
            _waveIndex = index;
            _wave.Clear();
            _window.Clear();
        }

        private void OnWaveEnded(int index) => Finish();

        /// <summary>The summary of the wave being measured (cleared, skipped, the next one started or the level left).</summary>
        private void Finish()
        {
            if (_waveIndex < 0) return;
            if (_wave.Count > 0) Log("W" + (_waveIndex + 1) + " total", _wave);
            _waveIndex = -1;
        }

        private void Log(string name, Stats stats)
        {
            var n = stats.Count;
            if (n == 0) return;
            Array.Copy(stats.Frames, _sorted, n);
            Array.Sort(_sorted, 0, n);
            var low = 1f / Mathf.Max(_sorted[Math.Min(n - 1, (int)(n * 0.99f))], 1e-6f);
            var median = _sorted[n / 2] * 1000f;
            var t = _text;
            t.Clear();
            t.Append("WAVES ").Append(name).Append(" | ").Append(stats.Seconds.ToString("0")).Append(" | ")
                .Append((n / Mathf.Max(stats.Seconds, 1e-6f)).ToString("0.0")).Append(' ').Append(low.ToString("0.0")).Append(' ')
                .Append(median.ToString("0.0")).Append(" | ")
                .Append((stats.Alive / n).ToString("0")).Append('/').Append(stats.AliveMax).Append(' ')
                .Append((stats.Drawn / n).ToString("0")).Append(" | ")
                .Append((stats.Held / n).ToString("0.0")).Append('/').Append(stats.HeldMax).Append(' ')
                .Append(stats.ShownMax).Append(' ').Append(stats.MissingMax).Append(" | ")
                .Append((stats.Sim / n).ToString("0.00")).Append('/').Append(stats.SimMax.ToString("0.0")).Append(" | ")
                .Append(stats.DrawCalls >= 0 ? (stats.DrawCalls / n).ToString("0") : "-").Append(" | flow ")
                .Append(stats.FlowCount > 0 ? (stats.Flow / stats.FlowCount).ToString("0.00") : "-").Append('/')
                .Append(stats.FlowMax.ToString("0.00")).Append(" x").Append(stats.FlowCount);
            GameLog.Info(LogChannel.Performance, t.ToString());
        }

        private sealed class Stats
        {
            public readonly float[] Frames;
            public int Count;
            public float Seconds;
            public double Alive, Drawn, Held, Sim, DrawCalls;
            public int AliveMax, HeldMax, ShownMax, MissingMax;
            public float SimMax;

            // Path field rebuilds (stage 7г: target ≤ 2 ms on A16): time of each rebuild seen in the frame.
            public double Flow;
            public int FlowCount, LastFlowBuilds = -1;
            public float FlowMax;

            public Stats(int capacity) => Frames = new float[capacity];

            public void Add(float frame, HordeSystem horde, HordeRenderer renderer, MonsterViewPresenter views, long drawCalls)
            {
                if (Count >= Frames.Length) return;
                Frames[Count++] = frame;
                Seconds += frame;
                Alive += horde.AliveCount;
                AliveMax = Math.Max(AliveMax, horde.AliveCount);
                Drawn += renderer.DrawnCount;
                Held += views.HeldCount;
                HeldMax = Math.Max(HeldMax, views.HeldCount);
                ShownMax = Math.Max(ShownMax, views.ShownCount);
                MissingMax = Math.Max(MissingMax, views.MissingCount);
                Sim += horde.SimulationMs;
                SimMax = Mathf.Max(SimMax, horde.SimulationMs);
                if (LastFlowBuilds >= 0 && horde.FlowBuilds != LastFlowBuilds)
                {
                    Flow += horde.FlowMs;
                    FlowCount++;
                    FlowMax = Mathf.Max(FlowMax, horde.FlowMs);
                }
                LastFlowBuilds = horde.FlowBuilds;
                if (drawCalls >= 0 && DrawCalls >= 0) DrawCalls += drawCalls;
                else DrawCalls = -1;
            }

            public void Clear()
            {
                Count = 0;
                Seconds = 0f;
                Alive = Drawn = Held = Sim = DrawCalls = 0;
                AliveMax = HeldMax = ShownMax = MissingMax = 0;
                SimMax = 0f;
                Flow = 0;
                FlowCount = 0;
                FlowMax = 0f;
                LastFlowBuilds = -1;
            }
        }
    }
}
