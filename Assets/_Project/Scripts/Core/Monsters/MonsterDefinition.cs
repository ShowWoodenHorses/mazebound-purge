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

        /// <summary>Ground speed of the packs' walk clips at 1× (they shuffle at 0.2–0.6 cells/s).</summary>
        public const float WalkClipSpeed = 0.4f;

        public MonsterStats Clone() => (MonsterStats)MemberwiseClone();
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
        public IReadOnlyList<StatusResistance> Resistances => _resistances;
        public IReadOnlyList<EffectModule> DeathEffects => _deathEffects;
        public IReadOnlyList<AbilityModule> Abilities => _abilities;

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

        /// <summary>For tests and tools.</summary>
        internal void SetAbilities(params AbilityModule[] abilities) => _abilities = new List<AbilityModule>(abilities);

        /// <summary>For tests and tools.</summary>
        internal void SetPerception(MonsterPerception perception) => _perception = perception;
    }
}
