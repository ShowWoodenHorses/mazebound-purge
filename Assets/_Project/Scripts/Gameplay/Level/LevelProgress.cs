using Purge.Gameplay.Zombies;

namespace Purge.Gameplay.Level
{
    /// <summary>
    /// Outcome of one run of a level and its stars (ТЗ §87, Maze rules until the run result is redone in plan stage 6):
    /// 1 — the level is completed, 2 — every zombie is killed, 3 — the map fragments star of Maze, now always given
    /// with completion. Stars 2 and 3 count only together with completion.
    /// </summary>
    public readonly struct LevelResult
    {
        public const int MaxStars = 3;

        public LevelResult(bool completed, int kills, int totalZombies, float time = 0f)
        {
            Time = time;
            Completed = completed;
            Kills = kills;
            TotalZombies = totalZombies;
        }

        public bool Completed { get; }
        public int Kills { get; }
        public int TotalZombies { get; }

        /// <summary>Seconds of play (pause, map and dialogs do not count); 0 = unknown.</summary>
        public float Time { get; }

        public bool AllZombiesKilled => Kills >= TotalZombies;

        public int Stars => !Completed ? 0 : 2 + (AllZombiesKilled ? 1 : 0);

        public override string ToString() =>
            $"{(Completed ? "completed" : "failed")}, stars {Stars}, kills {Kills}/{TotalZombies}, time {Time:0.0} s";
    }

    /// <summary>
    /// Runtime progress of the current run (ТЗ §86): killed zombies and the play time (counted only while the level
    /// ticks, i.e. Running: pause, map and dialogs stop it). Door, player and weapon state live in their own systems.
    /// Nothing here is saved: a retry or death starts from a fresh LevelScope.
    /// </summary>
    public sealed class LevelProgress : ILevelTickable
    {
        private readonly ZombieSystem _zombies;

        public LevelProgress(ZombieSystem zombies)
        {
            _zombies = zombies;
        }

        public int Kills => _zombies.KilledCount;
        public int TotalZombies => _zombies.TotalCount;

        /// <summary>Seconds played so far.</summary>
        public float Elapsed { get; private set; }

        public void Tick(float deltaTime) => Elapsed += deltaTime;

        public LevelResult GetResult(LevelOutcome outcome) =>
            new LevelResult(outcome == LevelOutcome.Completed, Kills, TotalZombies, Elapsed);
    }
}
