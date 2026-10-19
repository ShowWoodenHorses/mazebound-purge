using System;
using Purge.Core.Monsters;
using Purge.Core.Waves;
using UnityEngine;

namespace Purge.Core.Horde
{
    /// <summary>A monster the horde spawns and how often (weights are relative).</summary>
    [Serializable]
    public sealed class HordeSpawn
    {
        [SerializeField] private MonsterDefinition _monster;
        [SerializeField, Min(0f)] private float _weight = 1f;

        public MonsterDefinition Monster => _monster;
        public float Weight => _weight;

        public HordeSpawn()
        {
        }

        public HordeSpawn(MonsterDefinition monster, float weight)
        {
            _monster = monster;
            _weight = weight;
        }
    }

    /// <summary>
    /// The monsters of a level (<c>LevelData.Horde</c>, stage 6): which kinds the horde has and how they look, the
    /// champions, how many slots its arrays have, the difficulty by default, the drop and the waves. Any level with
    /// monsters has one; the dev levels' extras (stress weapons, autopilot, benchmark, sandbox) are in
    /// <see cref="StressTestDefinition"/>. Assets — Data/Horde; the looks are filled by Build Monsters.
    /// </summary>
    [CreateAssetMenu(fileName = "Horde", menuName = "Purge/Definitions/Horde")]
    public sealed class HordeDefinition : ScriptableObject
    {
        [Tooltip("Look of the built-in monster (used when no kind is set: the stress test's zombie).")]
        [SerializeField] private HordeVisualDefinition _visual;
        [Tooltip("Stats of the built-in monster (used when no kind is set).")]
        [SerializeField] private MonsterStats _monster = new MonsterStats();
        [Tooltip("Monsters the horde keeps up by weight without waves (stress test, sandbox).")]
        [SerializeField] private HordeSpawn[] _monsters = Array.Empty<HordeSpawn>();
        [Tooltip("Monsters that appear only when asked (the sandbox's officer buttons), never by weight.")]
        [SerializeField] private MonsterDefinition[] _onDemandMonsters = Array.Empty<MonsterDefinition>();
        [Tooltip("The waves (stage 6): with them the horde starts empty and the wave system spawns it at the level's spawn points.")]
        [SerializeField] private WaveScenario _waves;

        [Header("Looks (Build Monsters fills them)")]
        [Tooltip("Baked looks of the rank-and-file (by monster id).")]
        [SerializeField] private HordeVisualDefinition[] _monsterVisuals = Array.Empty<HordeVisualDefinition>();
        [Tooltip("Animator looks of the officers, elite and bosses (by monster id).")]
        [SerializeField] private MonsterViewDefinition[] _monsterViews = Array.Empty<MonsterViewDefinition>();

        [Header("Capacity")]
        [Tooltip("Most monsters alive at once (ТЗ: до 200 рядовых, пики до 300); a wave's own limit is within it.")]
        [SerializeField, Min(1)] private int _maxAlive = 300;
        [Tooltip("Slots above it for monsters called up on top (officer/champion buttons, raised dead).")]
        [SerializeField, Min(0)] private int _reserveSlots = 64;

        [Header("Perception")]
        [Tooltip("Difficulty when the launch does not choose one (empty = every factor 1).")]
        [SerializeField] private DifficultyDefinition _difficulty;
        [Tooltip("Wanderers walk in this many groups, each to its own point.")]
        [SerializeField, Range(1, 6)] private int _wanderGroups = 3;
        [Tooltip("Seconds between a group's new points.")]
        [SerializeField, Min(3f)] private float _wanderRetarget = 25f;

        [Header("Champions")]
        [Tooltip("Rank-and-file and officers with an affix: how often, which affixes, HP, size, the rank-up drop.")]
        [SerializeField] private ChampionRules _champions = new ChampionRules();

        [Header("Drop")]
        [Tooltip("What the monsters drop (empty = nothing).")]
        [SerializeField] private Drops.DropTable _drop;
        [Tooltip("Weapons that may drop (until the bought ones of stage 10).")]
        [SerializeField] private Definitions.WeaponDefinition[] _poolWeapons = Array.Empty<Definitions.WeaponDefinition>();
        [Tooltip("Throwables that may drop (until the bought ones of stage 10).")]
        [SerializeField] private Definitions.ThrowableDefinition[] _poolThrowables = Array.Empty<Definitions.ThrowableDefinition>();

        public HordeVisualDefinition Visual => _visual;
        public MonsterStats Monster => _monster ??= new MonsterStats();
        public HordeSpawn[] Monsters => _monsters ??= Array.Empty<HordeSpawn>();
        public MonsterDefinition[] OnDemandMonsters => _onDemandMonsters ??= Array.Empty<MonsterDefinition>();
        public WaveScenario Waves => _waves;
        public HordeVisualDefinition[] MonsterVisuals => _monsterVisuals ??= Array.Empty<HordeVisualDefinition>();
        public MonsterViewDefinition[] MonsterViews => _monsterViews ??= Array.Empty<MonsterViewDefinition>();
        public int MaxAlive => Math.Max(1, _maxAlive);
        public int ReserveSlots => Math.Max(0, _reserveSlots);
        public DifficultyDefinition Difficulty => _difficulty;
        public int WanderGroups => Math.Max(1, _wanderGroups);
        public float WanderRetarget => Math.Max(3f, _wanderRetarget);
        public ChampionRules Champions => _champions ??= new ChampionRules();
        public Drops.DropTable Drop => _drop;
        public Definitions.WeaponDefinition[] PoolWeapons => _poolWeapons ??= Array.Empty<Definitions.WeaponDefinition>();
        public Definitions.ThrowableDefinition[] PoolThrowables => _poolThrowables ??= Array.Empty<Definitions.ThrowableDefinition>();

        /// <summary>
        /// Every monster kind the horde may have, each once: <see cref="Monsters"/> by weight, then the waves' and
        /// <see cref="OnDemandMonsters"/> (weight 0: only when asked). Build Monsters gives looks to exactly these.
        /// </summary>
        public System.Collections.Generic.List<MonsterDefinition> AllMonsters()
        {
            var all = new System.Collections.Generic.List<MonsterDefinition>();
            foreach (var spawn in Monsters)
                if (spawn?.Monster != null && !all.Contains(spawn.Monster)) all.Add(spawn.Monster);
            if (_waves != null)
                foreach (var wave in _waves.Waves)
                {
                    if (wave == null) continue;
                    foreach (var entry in wave.Monsters)
                        if (entry?.Monster != null && !all.Contains(entry.Monster)) all.Add(entry.Monster);
                    foreach (var group in wave.Groups)
                        if (group?.Monster != null && !all.Contains(group.Monster)) all.Add(group.Monster);
                }

            foreach (var monster in OnDemandMonsters)
                if (monster != null && !all.Contains(monster)) all.Add(monster);
            return all;
        }

        /// <summary>Animator look of <paramref name="monster"/> among <see cref="MonsterViews"/> (null = none: baked or not drawn).</summary>
        public MonsterViewDefinition ViewOf(MonsterDefinition monster)
        {
            if (monster == null) return null;
            foreach (var view in MonsterViews)
                if (view != null && view.MonsterId == monster.Id)
                    return view;
            return null;
        }

        /// <summary>Look of <paramref name="monster"/> among <see cref="MonsterVisuals"/> (null = none).</summary>
        public HordeVisualDefinition VisualOf(MonsterDefinition monster)
        {
            if (monster == null) return _visual;
            foreach (var visual in MonsterVisuals)
                if (visual != null && visual.MonsterId == monster.Id)
                    return visual;
            return null;
        }

        internal void ConfigureMonsters(HordeSpawn[] monsters) => _monsters = monsters ?? Array.Empty<HordeSpawn>();

        internal void ConfigureCapacity(int maxAlive, int reserveSlots)
        {
            _maxAlive = maxAlive;
            _reserveSlots = reserveSlots;
        }

        internal void ConfigurePerception(DifficultyDefinition difficulty, int wanderGroups, float wanderRetarget)
        {
            _difficulty = difficulty;
            _wanderGroups = wanderGroups;
            _wanderRetarget = wanderRetarget;
        }

        internal void ConfigureDrops(Drops.DropTable drop, Definitions.WeaponDefinition[] weapons,
            params Definitions.ThrowableDefinition[] throwables)
        {
            _drop = drop;
            _poolWeapons = weapons ?? Array.Empty<Definitions.WeaponDefinition>();
            _poolThrowables = throwables ?? Array.Empty<Definitions.ThrowableDefinition>();
        }

        internal void SetWaves(WaveScenario waves) => _waves = waves;

        internal void SetVisual(HordeVisualDefinition visual) => _visual = visual;

        internal void SetMonsterVisuals(HordeVisualDefinition[] visuals) => _monsterVisuals = visuals ?? Array.Empty<HordeVisualDefinition>();

        internal void SetMonsterViews(MonsterViewDefinition[] views) => _monsterViews = views ?? Array.Empty<MonsterViewDefinition>();

        internal void SetOnDemandMonsters(MonsterDefinition[] monsters) => _onDemandMonsters = monsters ?? Array.Empty<MonsterDefinition>();
    }
}
