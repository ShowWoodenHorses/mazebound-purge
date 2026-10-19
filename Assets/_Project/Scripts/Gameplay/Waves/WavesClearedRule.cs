using System;
using Purge.Core.Common;
using Purge.Gameplay.Level;

namespace Purge.Gameplay.Waves
{
    /// <summary>
    /// Win rule of a wave level (stage 6a): the last wave cleared completes the level (the final boss — stage 7). A
    /// level without waves is not completed by it.
    /// </summary>
    public sealed class WavesClearedRule : IDisposable
    {
        private readonly WaveSystem _waves;
        private readonly LevelRuntime _runtime;

        public WavesClearedRule(WaveSystem waves, LevelRuntime runtime)
        {
            _waves = waves;
            _runtime = runtime;
            _waves.Completed += OnCompleted;
        }

        public void Dispose() => _waves.Completed -= OnCompleted;

        private void OnCompleted()
        {
            GameLog.Info(LogChannel.Gameplay, "Every wave is cleared: the level is completed.");
            _runtime.Finish(LevelOutcome.Completed);
        }
    }
}
