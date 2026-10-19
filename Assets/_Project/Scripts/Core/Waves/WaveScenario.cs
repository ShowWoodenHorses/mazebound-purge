using System;
using Purge.Core.Monsters;
using UnityEngine;

namespace Purge.Core.Waves
{
    /// <summary>A monster the wave's budget is spent on, and how often (weights are relative).</summary>
    [Serializable]
    public sealed class WaveMonster
    {
        [SerializeField] private MonsterDefinition _monster;
        [SerializeField, Min(0f)] private float _weight = 1f;

        public MonsterDefinition Monster => _monster;
        public float Weight => _weight;

        public WaveMonster()
        {
        }

        public WaveMonster(MonsterDefinition monster, float weight)
        {
            _monster = monster;
            _weight = weight;
        }
    }

    /// <summary>
    /// Monsters the wave always has, on top of its budget (ТЗ: «2 офицера на 40-й секунде», «Громила из точки B»):
    /// <see cref="Count"/> × the difficulty's count factor (at least one) at <see cref="Time"/> seconds after the wave starts.
    /// </summary>
    [Serializable]
    public sealed class WaveGroup
    {
        [SerializeField] private MonsterDefinition _monster;
        [SerializeField, Min(1)] private int _count = 1;
        [Tooltip("Seconds after the wave starts.")]
        [SerializeField, Min(0f)] private float _time;
        [Tooltip("Index of the level's monster spawn point (in its list); -1 = any the rules allow.")]
        [SerializeField, Min(-1)] private int _point = -1;

        public MonsterDefinition Monster => _monster;
        public int Count => _count;
        public float Time => _time;
        public int Point => _point;

        public WaveGroup()
        {
        }

        public WaveGroup(MonsterDefinition monster, int count, float time, int point = -1)
        {
            _monster = monster;
            _count = Math.Max(1, count);
            _time = Math.Max(0f, time);
            _point = Math.Max(-1, point);
        }
    }

    /// <summary>
    /// One wave: the budget (points, × difficulty) the generator spends on <see cref="Monsters"/> by weight (a monster
    /// costs <see cref="MonsterDefinition.Cost"/>), the fixed <see cref="Groups"/>, how long the budget's monsters keep
    /// appearing and how many may live at once. The wave ends when everything has appeared and everyone is dead.
    /// </summary>
    [Serializable]
    public sealed class WaveDefinition
    {
        [Tooltip("Points spent on the monsters below (× the difficulty's count factor).")]
        [SerializeField, Min(0f)] private float _budget = 40f;
        [SerializeField] private WaveMonster[] _monsters = Array.Empty<WaveMonster>();
        [SerializeField] private WaveGroup[] _groups = Array.Empty<WaveGroup>();
        [Tooltip("Seconds over which the budget's monsters appear (in packs).")]
        [SerializeField, Min(0f)] private float _spawnTime = 40f;
        [Tooltip("Monsters of the budget appear together this many at a time, from one point.")]
        [SerializeField, Range(1, 30)] private int _packSize = 6;
        [Tooltip("At most this many alive at once: the rest wait (ТЗ: до 200 рядовых, пики до 300).")]
        [SerializeField, Min(1)] private int _maxAlive = 150;
        [Tooltip("Money per second left of the pause before this wave for «Пропустить» (× the difficulty's money factor); " +
                 "0 = the wave's number (wave 2 — 2 a second).")]
        [SerializeField, Min(0f)] private float _skipRate;

        public float Budget => _budget;
        public WaveMonster[] Monsters => _monsters ??= Array.Empty<WaveMonster>();
        public WaveGroup[] Groups => _groups ??= Array.Empty<WaveGroup>();
        public float SpawnTime => _spawnTime;
        public int PackSize => Math.Max(1, _packSize);
        public int MaxAlive => Math.Max(1, _maxAlive);

        /// <summary>The set rate; 0 = by the wave's number (<see cref="WaveScenario.SkipRateOf"/>).</summary>
        public float SkipRate
        {
            get => _skipRate;
            internal set => _skipRate = Math.Max(0f, value);
        }

        public WaveDefinition()
        {
        }

        public WaveDefinition(float budget, WaveMonster[] monsters, WaveGroup[] groups = null, float spawnTime = 40f,
            int packSize = 6, int maxAlive = 150)
        {
            _budget = budget;
            _monsters = monsters ?? Array.Empty<WaveMonster>();
            _groups = groups ?? Array.Empty<WaveGroup>();
            _spawnTime = spawnTime;
            _packSize = packSize;
            _maxAlive = maxAlive;
        }
    }

    /// <summary>
    /// The waves of a level (ТЗ: «сложность — множители поверх одного сценария волн уровня»). Referenced by the level's
    /// <c>HordeDefinition.Waves</c>; the waves are generated from it by <see cref="WaveGenerator"/> (survival, stage 13,
    /// makes its wave definitions on the same generator).
    /// </summary>
    [CreateAssetMenu(fileName = "Waves", menuName = "Purge/Definitions/Wave Scenario")]
    public sealed class WaveScenario : ScriptableObject
    {
        [SerializeField] private WaveDefinition[] _waves = Array.Empty<WaveDefinition>();
        [Tooltip("Seconds of the pause between waves (ТЗ: отсчёт, «Пропустить +N» — the seconds left × the next wave's skip rate).")]
        [SerializeField, Min(0f)] private float _pauseTime = 20f;
        [Tooltip("Seconds before the first wave.")]
        [SerializeField, Min(0f)] private float _firstDelay = 3f;

        public WaveDefinition[] Waves => _waves ??= Array.Empty<WaveDefinition>();
        public int Count => Waves.Length;
        public float PauseTime => _pauseTime;
        public float FirstDelay => _firstDelay;

        /// <summary>Money per second of the pause skipped before wave <paramref name="index"/> (from 0), the default resolved.</summary>
        public float SkipRateOf(int index)
        {
            if (index < 0 || index >= Count) return 0f;
            var rate = Waves[index]?.SkipRate ?? 0f;
            return rate > 0f ? rate : index + 1;
        }

        /// <summary>For tests and tools.</summary>
        internal void Configure(WaveDefinition[] waves, float pauseTime, float firstDelay)
        {
            _waves = waves ?? Array.Empty<WaveDefinition>();
            _pauseTime = pauseTime;
            _firstDelay = firstDelay;
        }
    }
}
