using System;
using Purge.Gameplay.Player;

namespace Purge.Gameplay.Level
{
    /// <summary>
    /// The player dead for good (ТЗ §88; <see cref="PlayerRevival.Died"/>: no revive left or given up) fails the level;
    /// runtime progress is dropped with the level.
    /// </summary>
    public sealed class PlayerDeathRule : IDisposable
    {
        private readonly PlayerRevival _revival;
        private readonly LevelRuntime _runtime;

        public PlayerDeathRule(PlayerRevival revival, LevelRuntime runtime)
        {
            _revival = revival;
            _runtime = runtime;
            _revival.Died += OnDied;
        }

        public void Dispose() => _revival.Died -= OnDied;

        private void OnDied() => _runtime.Finish(LevelOutcome.Failed);
    }
}
