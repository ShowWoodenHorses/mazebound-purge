using Purge.Gameplay.Player;
using Purge.Gameplay.Run;
using Purge.Gameplay.Waves;
using Purge.Gameplay.Zombies;

namespace Purge.Gameplay.Level
{
    /// <summary>
    /// Outcome of one run of a level (stage 6d, ТЗ «итог» instead of the stars of Maze): won or not, cleared waves of
    /// all, kills, play time, money earned and score.
    /// </summary>
    public readonly struct LevelResult
    {
        public LevelResult(bool completed, int kills, float time = 0f, int waves = 0, int waveCount = 0, int money = 0,
            long score = 0, int revives = 0)
        {
            Completed = completed;
            Kills = kills;
            Time = time;
            Waves = waves;
            WaveCount = waveCount;
            Money = money;
            Score = score;
            Revives = revives;
        }

        public bool Completed { get; }

        /// <summary>Monsters killed (the horde's and the Maze zombies').</summary>
        public int Kills { get; }

        /// <summary>Seconds of play (pause, map and dialogs do not count); 0 = unknown.</summary>
        public float Time { get; }

        /// <summary>Waves cleared; with <see cref="WaveCount"/> 0 the level has no waves.</summary>
        public int Waves { get; }

        public int WaveCount { get; }

        /// <summary>Money earned in the run (before the rewarded ×2).</summary>
        public int Money { get; }

        public long Score { get; }

        public int Revives { get; }

        public override string ToString() =>
            $"{(Completed ? "completed" : "failed")}, waves {Waves}/{WaveCount}, kills {Kills}, money {Money}, score {Score}, " +
            $"revives {Revives}, time {Time:0.0} s";
    }

    /// <summary>
    /// Runtime progress of the current run (ТЗ §86): the play time (counted only while the level ticks, i.e. Running:
    /// pause, map and dialogs stop it) and the run's numbers from their systems for <see cref="GetResult"/>.
    /// Nothing here is saved: a retry or death starts from a fresh LevelScope.
    /// </summary>
    public sealed class LevelProgress : ILevelTickable
    {
        private readonly ZombieSystem _zombies;
        private readonly RunScore _score;
        private readonly RunWallet _wallet;
        private readonly WaveSystem _waves;
        private readonly PlayerRevival _revival;

        public LevelProgress(ZombieSystem zombies, RunScore score = null, RunWallet wallet = null, WaveSystem waves = null,
            PlayerRevival revival = null)
        {
            _zombies = zombies;
            _score = score;
            _wallet = wallet;
            _waves = waves;
            _revival = revival;
        }

        public int Kills => _zombies.KilledCount + (_score?.Kills ?? 0);

        /// <summary>Seconds played so far.</summary>
        public float Elapsed { get; private set; }

        public void Tick(float deltaTime) => Elapsed += deltaTime;

        public LevelResult GetResult(LevelOutcome outcome)
        {
            var completed = outcome == LevelOutcome.Completed;
            var waveCount = _waves != null && _waves.IsActive ? _waves.WaveCount : 0;
            var waves = waveCount > 0 ? _score?.WavesCleared ?? 0 : 0;
            return new LevelResult(completed, Kills, Elapsed, waves, waveCount, _wallet?.Money ?? 0, _score?.Score ?? 0,
                _revival?.RevivesUsed ?? 0);
        }
    }
}
