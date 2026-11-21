using System;
using System.Collections.Generic;
using Purge.Core.Monsters;

namespace Purge.Core.Waves
{
    /// <summary>Generates one survival wave at a time from a location's campaign scenario.</summary>
    public sealed class SurvivalWaves
    {
        public const int MaxAlive = 150;
        private const double Growth = 1.12;
        private static readonly int[] WaveSizes =
        {
            10, 16, 23, 33, 46, 54, 64, 75, 85, 98, 108, 117, 126, 134, 141, 146, MaxAlive
        };
        private static int CapacityIndex => WaveSizes.Length - 1;

        private readonly WaveScenario _scenario;
        private readonly DifficultyDefinition _difficulty;
        private readonly int _seed;
        private readonly WaveMonster[] _pool;
        private readonly WaveBoss _boss;
        private readonly float _fullBudget;

        public SurvivalWaves(WaveScenario scenario, DifficultyDefinition difficulty, int seed)
        {
            _scenario = scenario;
            _difficulty = difficulty;
            _seed = seed;
            var pool = new List<WaveMonster>();
            var seen = new HashSet<MonsterDefinition>();
            float maxCost = 0;
            foreach (var wave in scenario.Waves)
            {
                _boss ??= wave.Boss;
                foreach (var entry in wave.Monsters)
                {
                    if (entry?.Monster == null || entry.Weight <= 0 || entry.Monster.IsBoss || !seen.Add(entry.Monster))
                        continue;
                    pool.Add(entry);
                    maxCost = Math.Max(maxCost, entry.Monster.Cost);
                }
                foreach (var group in wave.Groups)
                {
                    if (group?.Monster == null || group.Monster.IsBoss || !seen.Add(group.Monster)) continue;
                    pool.Add(new WaveMonster(group.Monster, 1f));
                    maxCost = Math.Max(maxCost, group.Monster.Cost);
                }
            }
            _pool = pool.ToArray();
            _fullBudget = (MaxAlive * maxCost + 1f) / Math.Max(0.01f, difficulty?.Count ?? 1f);
        }

        public static int TargetCount(int index) => index < 0 ? 0 : WaveSizes[Math.Min(index, CapacityIndex)];

        public WaveBoss Boss(int index) => index % 10 == 9 ? _boss : null;

        public List<WaveSpawn> Generate(WaveDefinition wave, int index, int limit) =>
            WaveGenerator.Generate(wave, _difficulty, WaveGenerator.SeedOf(_seed, index), Math.Min(limit, TargetCount(index)));

        public float HealthFactor(int index) => (float)Math.Pow(Growth, Math.Min(100, BeyondCapacity(index)));
        public float DamageFactor(int index) => (float)Math.Pow(1.05, Math.Min(100, BeyondCapacity(index)));
        public static float ChampionChance(int index) => Math.Min(29, index) * (0.30f / 29);

        public float MoneyBonus(int index)
        {
            int exponential = Math.Min(index, CapacityIndex);
            return (float)Math.Min(int.MaxValue, _scenario.MoneyBonusOf(0) * Math.Pow(Growth, exponential) *
                (1 + BeyondCapacity(index) * (Growth - 1)));
        }

        private static int BeyondCapacity(int index) => Math.Max(0, index - CapacityIndex);

        public WaveDefinition Definition(int index)
        {
            var first = _scenario.Waves[0];
            var pool = new List<WaveMonster>(_pool.Length);
            int extra = BeyondCapacity(index);
            var senior = 1f + extra * 0.05f;
            foreach (var entry in _pool)
            {
                var rank = entry.Monster.Rank;
                if (rank == MonsterRank.Officer && index < 2 || rank == MonsterRank.Elite && index < 5) continue;
                pool.Add(rank == MonsterRank.Mass || extra == 0
                    ? entry
                    : new WaveMonster(entry.Monster, entry.Weight * senior));
            }
            return new WaveDefinition(_fullBudget, pool.ToArray(), spawnTime: first.SpawnTime,
                packSize: first.PackSize, maxAlive: MaxAlive);
        }
    }
}
