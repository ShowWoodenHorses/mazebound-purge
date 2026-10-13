using System;
using Purge.Core.Common;
using Purge.Gameplay.Zombies;

namespace Purge.Gameplay.Level
{
    /// <summary>
    /// Temporary win rule until the wave run (plan stage 6): the last zombie killed completes the level. A level
    /// without zombies is not completed by it.
    /// </summary>
    public sealed class AllKilledRule : IDisposable
    {
        private readonly ZombieSystem _zombies;
        private readonly LevelRuntime _runtime;

        public AllKilledRule(ZombieSystem zombies, LevelRuntime runtime)
        {
            _zombies = zombies;
            _runtime = runtime;
            _zombies.Died += OnZombieDied;
        }

        public void Dispose() => _zombies.Died -= OnZombieDied;

        private void OnZombieDied(ZombieRuntime zombie)
        {
            if (!_zombies.AllKilled)
                return;

            GameLog.Info(LogChannel.Gameplay, "Every zombie is killed: the level is completed.");
            _runtime.Finish(LevelOutcome.Completed);
        }
    }
}
