using System;
using Maze.Core.Common;
using Maze.Gameplay.Player;

namespace Maze.Gameplay.Level
{
    /// <summary>The player's HP reaching 0 fails the level (ТЗ §88); runtime progress is dropped with the level.</summary>
    public sealed class PlayerDeathRule : IDisposable
    {
        private readonly PlayerHealth _health;
        private readonly LevelRuntime _runtime;

        public PlayerDeathRule(PlayerHealth health, LevelRuntime runtime)
        {
            _health = health;
            _runtime = runtime;
            _health.Changed += OnHealthChanged;
        }

        public void Dispose() => _health.Changed -= OnHealthChanged;

        private void OnHealthChanged(int current, int max)
        {
            if (current > 0)
                return;

            GameLog.Info(LogChannel.Gameplay, "The player died.");
            _runtime.Finish(LevelOutcome.Failed);
        }
    }
}
