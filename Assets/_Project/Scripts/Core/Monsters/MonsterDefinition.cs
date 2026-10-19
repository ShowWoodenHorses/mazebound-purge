using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Drops;
using Purge.Core.Effects;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Monsters
{
    /// <summary>ТЗ: ранги рядовые / офицеры / элита / боссы; the rank decides the drop and how the monster is drawn.</summary>
    public enum MonsterRank
    {
        /// <summary>Rank-and-file: flat arrays, baked animation + GPU instancing.</summary>
        Mass = 0,
        Officer = 1,
        Elite = 2,
        Boss = 3,
    }

    /// <summary>
    /// ТЗ: бродяга (не знает об игроке, идёт в общем направлении, реагирует на шум) / зовущий (зовёт других, заметив
    /// игрока) / охотник (сразу бежит на игрока). The horde's <c>HordePerception</c> (stage 5b).
    /// </summary>
    public enum MonsterPerception
    {
        Wanderer = 0,
        Caller = 1,
        Hunter = 2,
    }

    /// <summary>Where a monster lives (ТЗ: общие — на всех картах, биомные — своя модель в биоме).</summary>
    public enum MonsterBiome
    {
        Common = 0,
        Castle = 1,
        Forest = 2,
        Snow = 3,
        Desert = 4,
        Volcano = 5,
    }

    /// <summary>
    /// What a chasing monster does with a closed door (ТЗ: рядовые не открывают, офицеры открывают, Громила и боссы
    /// выбивают). Locked and frozen doors stop everyone.
    /// </summary>
    public enum MonsterDoorUse
    {
        /// <summary>By <see cref="MonsterRank"/>: Mass — <see cref="Never"/>, Officer and Elite — <see cref="Open"/>, Boss — <see cref="Break"/>.</summary>
        ByRank = 0,
        /// <summary>A closed door is a wall.</summary>
        Never = 1,
        /// <summary>Opens it at once; it stays open.</summary>
        Open = 2,
        /// <summary>Breaks it down blow by blow (<see cref="MonsterStats.DoorDamage"/>): it falls flat and stays open for good.</summary>
        Break = 3,
    }

    /// <summary>Body and fight numbers of a monster (drafts; balance — stage 9).</summary>
    [Serializable]
    public sealed class MonsterStats
    {
        [Min(1f)] public float MaxHp = 30f;
        [Tooltip("Cells per second.")]
        [Min(0.1f)] public float Speed = 2.2f;
        [Tooltip("Body radius in cells (crowd spacing, hits).")]
        [Range(0.1f, 0.5f)] public float Radius = 0.3f;
        [Tooltip("Attacks when the distance between centres is at most this.")]
        [Min(0.3f)] public float AttackReach = 0.8f;
        [Min(0f)] public int Damage = 5;
        [Min(0.1f)] public float AttackInterval = 1.2f;
        [Tooltip("Degrees per second.")]
        [Min(30f)] public float TurnSpeed = 540f;
        [Tooltip("Share of the overlap pushed apart per tick (0 = no crowd spacing).")]
        [Range(0f, 1f)] public float Separation = 0.5f;
        [Tooltip("Seconds from death until the corpse disappears (the death clip plays first, then holds).")]
        [Min(0f)] public float CorpseTime = 3f;
        [Tooltip("Seconds a new monster climbs out of the ground before it walks (it can be hit meanwhile); 0 = none.")]
        [Min(0f)] public float EmergeTime;

        [Header("Perception")]
        [Tooltip("Cells per second while wandering (the walk clip plays at 1× at 0.4).")]
        [Min(0.05f)] public float WanderSpeed = WalkClipSpeed;
        [Tooltip("Notices the player this close, cells — only within the player's view (× difficulty).")]
        [Min(0f)] public float SightRange = 7f;
        [Tooltip("Callers: how far the call carries along the passages, cells (× difficulty).")]
        [Min(0f)] public float CallRadius = 12f;

        [Header("Tactics (stage 5c)")]
        [Tooltip("Stops closing in this close to the player while it sees them (a ranged monster shoots from here); 0 = closes in.")]
        [Min(0f)] public float HoldRange;
        [Tooltip("Steps back while the player is closer than this (unless cornered: then it fights in melee); 0 = never.")]
        [Min(0f)] public float RetreatRange;

        [Header("Weight (stage 5d)")]
        [Tooltip("Body mass (the player's is 1); 0 = by rank: rank-and-file 0.15, officers 0.5, elite 4, bosses immovable. " +
                 "Who gives way when bodies meet (the player, the crowd), how far knockbacks, blasts and charges carry it.")]
        [Min(0f)] public float Mass;
        [Tooltip("Nothing moves it: neither the player, nor the crowd, nor knockbacks (bosses by rank).")]
        public bool Immovable;

        [Header("Doors (stage 5d)")]
        [Tooltip("What it does with a closed door on its way to the player. By rank: the rank-and-file stop, officers and elite open it, bosses break it.")]
        public MonsterDoorUse Doors;
        [Tooltip("Breaks: damage to the door per blow (one blow per attack interval; the door's strength — DoorData).")]
        [Min(0f)] public float DoorDamage = 20f;

        [Header("Armor (stage 5e)")]
        [Tooltip("Share of the damage taken off hits from the front (0.5 = half goes through; the Armadillo). Hits without " +
                 "a direction — zones, burning, poison — always go through in full.")]
        [Range(0f, 0.9f)] public float FrontArmor;
        [Tooltip("Width of the armored front, degrees around the facing.")]
        [Range(30f, 270f)] public float FrontArc = 120f;

        /// <summary>Ground speed of the packs' walk clips at 1× (they shuffle at 0.2–0.6 cells/s).</summary>
        public const float WalkClipSpeed = 0.4f;

        public MonsterStats Clone() => (MonsterStats)MemberwiseClone();

        /// <summary>
        /// × damage of a hit travelling along <paramref name="hitDirection"/> into a monster facing
        /// <paramref name="facing"/> (radians, 0 = North, clockwise): 1 − <see cref="FrontArmor"/> when it comes from the
        /// armored front, 1 otherwise or without a direction (zero vector).
        /// </summary>
        public float ArmorFactor(float facing, Vector2 hitDirection)
        {
            if (FrontArmor <= 0f || hitDirection.sqrMagnitude < 1e-8f) return 1f;
            var forward = new Vector2(Mathf.Sin(facing), Mathf.Cos(facing));
            // The hit comes from where it travels from.
            return Vector2.Angle(forward, -hitDirection) <= FrontArc * 0.5f ? 1f - FrontArmor : 1f;
        }
    }

    /// <summary>A state lasts × <see cref="DurationFactor"/> on this monster (0 = immune).</summary>
    [Serializable]
    public sealed class StatusResistance
    {
        [SerializeField] private StatusDefinition _status;
        [Tooltip("× the state's duration on this monster: 0 = immune, 0.5 = half as long, 1 = as usual.")]
        [SerializeField, Range(0f, 2f)] private float _durationFactor = 0.5f;

        public StatusDefinition Status => _status;
        public float DurationFactor => _durationFactor;

        public StatusResistance()
        {
        }

        public StatusResistance(StatusDefinition status, float durationFactor)
        {
            _status = status;
            _durationFactor = durationFactor;
        }
    }

    /// <summary>
    /// A monster kind (ТЗ: «Монстр — данные: ранг, статы, восприятие, способности, сопротивления, эффект смерти,
    /// размер, вид»). Gameplay only: the look (baked mass visual or Animator prefab, equipment) is defined separately
    /// by the monster's id (<c>HordeVisualDefinition.MonsterId</c>), never here — the content catalog loads every
    /// definition at start and must stay light. Abilities — <see cref="AbilityModule"/>s (stage 5c). Built by Purge → Dev → Build Monsters
    /// from the folders Art/Monster/&lt;Biome&gt;/&lt;Rank&gt;; the numbers are edited here and kept on rebuilds.
    /// </summary>
    [CreateAssetMenu(fileName = "Monster", menuName = "Purge/Definitions/Monster")]
    public sealed class MonsterDefinition : ContentDefinition
    {
        [SerializeField] private MonsterRank _rank;
        [SerializeField] private MonsterBiome _biome;
        [SerializeField] private MonsterPerception _perception = MonsterPerception.Hunter;
        [SerializeField] private MonsterStats _stats = new MonsterStats();

        [Tooltip("Points of a wave's budget it takes (stage 6); 0 = by rank: rank-and-file 1, officers 10, elite 30, bosses 100.")]
        [SerializeField, Min(0f)] private float _cost;

        [Tooltip("Money of the run for killing it (stage 6b, × the difficulty's money factor); 0 = by rank: rank-and-file 1, officers 5, elite 20, bosses 100.")]
        [SerializeField, Min(0f)] private float _money;

        [Tooltip("Experience of the run for killing it (stage 6c); 0 = by rank: rank-and-file 1, officers 4, elite 15, bosses 50.")]
        [SerializeField, Min(0f)] private float _experience;

        [Tooltip("Score of the run for killing it (stage 6d, × the difficulty's score factor); 0 = by rank: rank-and-file 10, officers 50, elite 200, bosses 2000.")]
        [SerializeField, Min(0f)] private float _score;

        [Tooltip("What it drops (ТЗ: ранг определяет дроп) — the rank's table; empty = the level's.")]
        [SerializeField] private DropTable _drop;

        [Tooltip("States that last shorter on this monster (or never take).")]
        [SerializeField] private List<StatusResistance> _resistances = new List<StatusResistance>();

        [Tooltip("What happens where it dies (an explosion, a zone…); its zones hurt the player too.")]
        [SerializeReference, ModuleList] private List<EffectModule> _deathEffects = new List<EffectModule>();

        [Tooltip("What it can do besides its plain attack (stage 5c): cast in list order when the conditions hold.")]
        [SerializeReference, ModuleList] private List<AbilityModule> _abilities = new List<AbilityModule>();

        public MonsterRank Rank => _rank;
        public MonsterBiome Biome => _biome;
        public MonsterPerception Perception => _perception;
        public MonsterStats Stats => _stats;
        public DropTable Drop => _drop;

        /// <summary>Points of a wave's budget it takes, the rank resolved (drafts; balance — stage 9).</summary>
        public float Cost => _cost > 0f ? _cost : CostOf(_rank);

        public static float CostOf(MonsterRank rank) => rank switch
        {
            MonsterRank.Mass => 1f,
            MonsterRank.Officer => 10f,
            MonsterRank.Elite => 30f,
            _ => 100f,
        };

        /// <summary>Money of the run for killing it, the rank resolved (drafts; balance — stage 9).</summary>
        public float Money => _money > 0f ? _money : MoneyOf(_rank);

        public static float MoneyOf(MonsterRank rank) => rank switch
        {
            MonsterRank.Mass => 1f,
            MonsterRank.Officer => 5f,
            MonsterRank.Elite => 20f,
            _ => 100f,
        };

        /// <summary>Experience of the run for killing it, the rank resolved (drafts; balance — stage 9).</summary>
        public float Experience => _experience > 0f ? _experience : ExperienceOf(_rank);

        public static float ExperienceOf(MonsterRank rank) => rank switch
        {
            MonsterRank.Mass => 1f,
            MonsterRank.Officer => 4f,
            MonsterRank.Elite => 15f,
            _ => 50f,
        };

        /// <summary>Score of the run for killing it, the rank resolved (ТЗ: 10 / 50 / 200 / 2000).</summary>
        public float Score => _score > 0f ? _score : ScoreOf(_rank);

        public static float ScoreOf(MonsterRank rank) => rank switch
        {
            MonsterRank.Mass => 10f,
            MonsterRank.Officer => 50f,
            MonsterRank.Elite => 200f,
            _ => 2000f,
        };

        public IReadOnlyList<StatusResistance> Resistances => _resistances;
        public IReadOnlyList<EffectModule> DeathEffects => _deathEffects;
        public IReadOnlyList<AbilityModule> Abilities => _abilities;

        /// <summary><see cref="MonsterStats.Doors"/> with <see cref="MonsterDoorUse.ByRank"/> resolved.</summary>
        public MonsterDoorUse DoorUse => DoorUseOf(_rank, _stats != null ? _stats.Doors : MonsterDoorUse.ByRank);

        /// <summary>Mass by rank when <see cref="MonsterStats.Mass"/> is 0 (drafts; the player's is 1).</summary>
        public const float RankAndFileMass = 0.15f, OfficerMass = 0.5f, EliteMass = 4f;

        /// <summary>The body's mass with the rank resolved; <see cref="float.PositiveInfinity"/> = immovable.</summary>
        public float BodyMass => MassOf(_rank, _stats);

        public static float MassOf(MonsterRank rank, MonsterStats stats)
        {
            if (stats != null && stats.Immovable) return float.PositiveInfinity;
            if (stats != null && stats.Mass > 0f) return stats.Mass;
            return rank switch
            {
                MonsterRank.Mass => RankAndFileMass,
                MonsterRank.Officer => OfficerMass,
                MonsterRank.Elite => EliteMass,
                _ => float.PositiveInfinity,
            };
        }

        public static MonsterDoorUse DoorUseOf(MonsterRank rank, MonsterDoorUse doors)
        {
            if (doors != MonsterDoorUse.ByRank) return doors;
            return rank switch
            {
                MonsterRank.Mass => MonsterDoorUse.Never,
                MonsterRank.Boss => MonsterDoorUse.Break,
                _ => MonsterDoorUse.Open,
            };
        }

        /// <summary>× a state's duration on this monster (1 = no resistance).</summary>
        public float DurationFactor(StatusDefinition status)
        {
            if (status == null) return 1f;
            for (var i = 0; i < _resistances.Count; i++)
                if (_resistances[i] != null && _resistances[i].Status == status)
                    return _resistances[i].DurationFactor;
            return 1f;
        }

        public void Validate(List<string> problems)
        {
            var owner = $"Monster '{name}'";
            Purge.Core.Modules.Modules.Validate(_deathEffects, owner, problems);
            Purge.Core.Modules.Modules.Validate(_abilities, owner, problems);
            if (_drop == null) problems.Add($"{owner}: no drop table (drops what the level drops).");
            for (var i = 0; i < _resistances.Count; i++)
                if (_resistances[i] == null || _resistances[i].Status == null)
                    problems.Add($"{owner}: resistance #{i + 1} has no state.");
        }

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, MonsterRank rank, MonsterBiome biome, MonsterStats stats, DropTable drop = null)
        {
            SetId(id);
            _rank = rank;
            _biome = biome;
            _stats = stats ?? new MonsterStats();
            _drop = drop;
        }

        /// <summary>For tests and tools.</summary>
        internal void ConfigureEffects(StatusResistance[] resistances, params EffectModule[] deathEffects)
        {
            _resistances = new List<StatusResistance>(resistances ?? Array.Empty<StatusResistance>());
            _deathEffects = new List<EffectModule>(deathEffects);
        }

        /// <summary>Build Monsters: rank and biome follow the folder.</summary>
        internal void SetPlace(MonsterRank rank, MonsterBiome biome)
        {
            _rank = rank;
            _biome = biome;
        }

        internal void SetDrop(DropTable drop) => _drop = drop;

        /// <summary>For tests and tools: 0 = by rank.</summary>
        internal void SetCost(float cost) => _cost = Mathf.Max(0f, cost);

        /// <summary>For tests and tools: 0 = by rank.</summary>
        internal void SetMoney(float money) => _money = Mathf.Max(0f, money);

        /// <summary>For tests and tools: 0 = by rank.</summary>
        internal void SetExperience(float experience) => _experience = Mathf.Max(0f, experience);

        /// <summary>For tests and tools.</summary>
        internal void SetAbilities(params AbilityModule[] abilities) => _abilities = new List<AbilityModule>(abilities);

        /// <summary>For tests and tools.</summary>
        internal void SetPerception(MonsterPerception perception) => _perception = perception;
    }
}
