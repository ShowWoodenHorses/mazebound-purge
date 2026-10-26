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
    /// The boss of a wave (stage 8a, ТЗ «босс выходит со свитой, обычная волна ждёт»): it climbs out in the centre of
    /// the level's arena when the wave starts; its <see cref="Escort"/> — groups from the spawn points nearest the arena
    /// (their time — seconds after the wave starts); the wave's budget and groups begin only once the boss is dead.
    /// </summary>
    [Serializable]
    public sealed class WaveBoss
    {
        [SerializeField] private MonsterDefinition _monster;
        [Tooltip("Comes with the boss: × the difficulty's count factor (at least one), from the spawn points nearest the arena.")]
        [SerializeField] private WaveGroup[] _escort = Array.Empty<WaveGroup>();

        public MonsterDefinition Monster => _monster;
        public WaveGroup[] Escort => _escort ??= Array.Empty<WaveGroup>();

        public WaveBoss()
        {
        }

        public WaveBoss(MonsterDefinition monster, params WaveGroup[] escort)
        {
            _monster = monster;
            _escort = escort ?? Array.Empty<WaveGroup>();
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
        [Tooltip("Money for clearing this wave (stage 6d, × the difficulty's money factor); 0 = 5 × the wave's number.")]
        [SerializeField, Min(0f)] private float _moneyBonus;
        [Tooltip("Score for clearing this wave (stage 6d, × the difficulty's score factor); 0 = 100 × the wave's number.")]
        [SerializeField, Min(0f)] private float _scoreBonus;
        [Tooltip("A boss wave (stage 8a): the boss with its escort first; the budget and the groups above wait for its death. " +
                 "Empty monster = an ordinary wave.")]
        [SerializeField] private WaveBoss _boss = new WaveBoss();

        /// <summary>The wave's boss; null for an ordinary wave.</summary>
        public WaveBoss Boss => _boss != null && _boss.Monster != null ? _boss : null;

        public bool IsBossWave => Boss != null;

        /// <summary>For tests and tools.</summary>
        internal void SetBoss(WaveBoss boss) => _boss = boss ?? new WaveBoss();

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

        /// <summary>The set money bonus; 0 = by the wave's number (<see cref="WaveScenario.MoneyBonusOf"/>).</summary>
        public float MoneyBonus
        {
            get => _moneyBonus;
            internal set => _moneyBonus = Math.Max(0f, value);
        }

        /// <summary>The set score bonus; 0 = by the wave's number (<see cref="WaveScenario.ScoreBonusOf"/>).</summary>
        public float ScoreBonus
        {
            get => _scoreBonus;
            internal set => _scoreBonus = Math.Max(0f, value);
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

        /// <summary>Money for clearing wave <paramref name="index"/> (from 0), the default (5 × its number) resolved.</summary>
        public float MoneyBonusOf(int index)
        {
            if (index < 0 || index >= Count) return 0f;
            var bonus = Waves[index]?.MoneyBonus ?? 0f;
            return bonus > 0f ? bonus : 5f * (index + 1);
        }

        /// <summary>Score for clearing wave <paramref name="index"/> (from 0), the default (100 × its number) resolved.</summary>
        public float ScoreBonusOf(int index)
        {
            if (index < 0 || index >= Count) return 0f;
            var bonus = Waves[index]?.ScoreBonus ?? 0f;
            return bonus > 0f ? bonus : 100f * (index + 1);
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
