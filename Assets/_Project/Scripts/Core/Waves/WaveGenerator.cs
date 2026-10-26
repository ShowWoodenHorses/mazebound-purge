using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Monsters;

namespace Purge.Core.Waves
{
    /// <summary>One monster of a generated wave: when (seconds after the wave starts), what, and from where.</summary>
    public readonly struct WaveSpawn
    {
        public WaveSpawn(float time, MonsterDefinition monster, int pack, int point)
        {
            Time = time;
            Monster = monster;
            Pack = pack;
            Point = point;
        }

        public float Time { get; }
        public MonsterDefinition Monster { get; }

        /// <summary>Monsters of one pack appear together from one spawn point.</summary>
        public int Pack { get; }

        /// <summary>Index of the level's spawn point; -1 = any the rules allow (picked when the pack appears).</summary>
        public int Point { get; }
    }

    /// <summary>
    /// Spends a wave's budget (× <see cref="DifficultyDefinition.Count"/>) on its monsters by weight (officers, elite
    /// and bosses × <see cref="DifficultyDefinition.SeniorShare"/>), only on those that still fit what is left; packs of
    /// <see cref="WaveDefinition.PackSize"/> spread over <see cref="WaveDefinition.SpawnTime"/>; the fixed groups on top.
    /// Deterministic by <c>seed</c>: the same wave of the same run is the same (a wave restarted from the save).
    /// </summary>
    public static class WaveGenerator
    {
        /// <summary>A runaway budget (cheap monsters, huge points) stops here.</summary>
        public const int MaxSpawns = 2000;

        public static List<WaveSpawn> Generate(WaveDefinition wave, DifficultyDefinition difficulty, int seed)
        {
            var result = new List<WaveSpawn>();
            if (wave == null) return result;

            var random = new DeterministicRandom(seed);
            var countFactor = difficulty != null ? difficulty.Count : 1f;
            var senior = difficulty != null ? difficulty.SeniorShare : 1f;

            var picks = new List<MonsterDefinition>();
            var pool = new List<(MonsterDefinition Monster, float Weight, float Cost)>();
            foreach (var entry in wave.Monsters)
                if (entry != null && entry.Monster != null && entry.Weight > 0f && entry.Monster.Cost > 0f)
                    pool.Add((entry.Monster, entry.Weight * (entry.Monster.Rank != MonsterRank.Mass ? senior : 1f), entry.Monster.Cost));

            var left = wave.Budget * countFactor;
            while (pool.Count > 0 && picks.Count < MaxSpawns)
            {
                var total = 0f;
                foreach (var entry in pool)
                    if (entry.Cost <= left + 1e-4f) total += entry.Weight;
                if (total <= 0f) break;

                var roll = (float)random.NextDouble() * total;
                var picked = -1;
                for (var i = 0; i < pool.Count; i++)
                {
                    if (pool[i].Cost > left + 1e-4f) continue;
                    picked = i;
                    roll -= pool[i].Weight;
                    if (roll < 0f) break;
                }

                picks.Add(pool[picked].Monster);
                left -= pool[picked].Cost;
            }

            var packSize = wave.PackSize;
            var packs = (picks.Count + packSize - 1) / packSize;
            var interval = packs > 1 ? wave.SpawnTime / packs : 0f;
            for (var pack = 0; pack < packs; pack++)
            {
                // The first pack comes at once; the others spread with a little jitter.
                var time = pack == 0 ? 0f : interval * (pack + ((float)random.NextDouble() - 0.5f) * 0.5f);
                for (var i = pack * packSize; i < Math.Min(picks.Count, (pack + 1) * packSize); i++)
                    result.Add(new WaveSpawn(time, picks[i], pack, -1));
            }

            var groupPack = packs;
            foreach (var group in wave.Groups)
            {
                if (group == null || group.Monster == null) continue;
                var count = Math.Max(1, (int)Math.Round(group.Count * countFactor));
                for (var i = 0; i < count && result.Count < MaxSpawns; i++)
                    result.Add(new WaveSpawn(group.Time, group.Monster, groupPack, group.Point));
                groupPack++;
            }

            SortByTime(result);
            return result;
        }

        /// <summary>
        /// The escort of a boss wave (stage 8a): each group × the difficulty's count (at least one) at its time, a pack per
        /// group (the point — chosen when it appears: one of those nearest the arena).
        /// </summary>
        public static List<WaveSpawn> Escort(WaveBoss boss, DifficultyDefinition difficulty)
        {
            var result = new List<WaveSpawn>();
            if (boss == null) return result;
            var countFactor = difficulty != null ? difficulty.Count : 1f;
            var pack = 0;
            foreach (var group in boss.Escort)
            {
                if (group == null || group.Monster == null) continue;
                var count = Math.Max(1, (int)Math.Round(group.Count * countFactor));
                for (var i = 0; i < count && result.Count < MaxSpawns; i++)
                    result.Add(new WaveSpawn(group.Time, group.Monster, pack, group.Point));
                pack++;
            }

            SortByTime(result);
            return result;
        }

        /// <summary>The seed of wave <paramref name="index"/> of a run.</summary>
        public static int SeedOf(int runSeed, int index) => (int)StableHash.Of($"wave/{runSeed}/{index}");

        private static void SortByTime(List<WaveSpawn> list)
        {
            // Insertion sort: stable (equal times keep their order — a pack stays together), and the list is nearly sorted.
            for (var i = 1; i < list.Count; i++)
            {
                var item = list[i];
                var j = i - 1;
                while (j >= 0 && list[j].Time > item.Time)
                {
                    list[j + 1] = list[j];
                    j--;
                }

                list[j + 1] = item;
            }
        }
    }
}
