using Maze.Gameplay.Map;
using Maze.Gameplay.Zombies;

namespace Maze.Gameplay.Level
{
    /// <summary>
    /// Outcome of one run of a level and its stars (ТЗ §87): 1 — the level is completed, 2 — every zombie is killed,
    /// 3 — every map fragment is collected. Stars 2 and 3 count only together with completion. A level without
    /// zombies (fragments) gives the matching star for free: there is nothing left to do for it.
    /// </summary>
    public readonly struct LevelResult
    {
        public const int MaxStars = 3;

        public LevelResult(bool completed, int kills, int totalZombies, int fragments, int totalFragments, float time = 0f)
        {
            Time = time;
            Completed = completed;
            Kills = kills;
            TotalZombies = totalZombies;
            Fragments = fragments;
            TotalFragments = totalFragments;
        }

        public bool Completed { get; }
        public int Kills { get; }
        public int TotalZombies { get; }
        public int Fragments { get; }
        public int TotalFragments { get; }

        /// <summary>Seconds of play (pause, map and dialogs do not count); 0 = unknown.</summary>
        public float Time { get; }

        public bool AllZombiesKilled => Kills >= TotalZombies;
        public bool AllFragmentsCollected => Fragments >= TotalFragments;

        public int Stars => !Completed ? 0 : 1 + (AllZombiesKilled ? 1 : 0) + (AllFragmentsCollected ? 1 : 0);

        public override string ToString() =>
            $"{(Completed ? "completed" : "failed")}, stars {Stars}, kills {Kills}/{TotalZombies}, fragments {Fragments}/{TotalFragments}, time {Time:0.0} s";
    }

    /// <summary>
    /// Runtime progress of the current run (ТЗ §86): killed zombies, collected fragments and the play time (counted
    /// only while the level ticks, i.e. Running: pause, map and the exit dialog stop it). Door, player and weapon
    /// state live in their own systems. Nothing here is saved: a retry or death starts from a fresh LevelScope.
    /// </summary>
    public sealed class LevelProgress : ILevelTickable
    {
        private readonly ZombieSystem _zombies;
        private readonly MapSystem _map;

        public LevelProgress(ZombieSystem zombies, MapSystem map)
        {
            _zombies = zombies;
            _map = map;
        }

        public int Kills => _zombies.KilledCount;
        public int TotalZombies => _zombies.TotalCount;
        public int Fragments => _map.CollectedCount;
        public int TotalFragments => _map.TotalCount;

        /// <summary>Seconds played so far.</summary>
        public float Elapsed { get; private set; }

        public void Tick(float deltaTime) => Elapsed += deltaTime;

        public LevelResult GetResult(LevelOutcome outcome) =>
            new LevelResult(outcome == LevelOutcome.Completed, Kills, TotalZombies, Fragments, TotalFragments, Elapsed);
    }
}
