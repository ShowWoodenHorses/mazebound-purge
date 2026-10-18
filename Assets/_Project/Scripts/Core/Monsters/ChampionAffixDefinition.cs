using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Drops;
using Purge.Core.Effects;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Monsters
{
    /// <summary>
    /// A champion's affix (ТЗ «Чемпионы: рядовые и офицеры с аффиксом — быстрый, огненный, бронированный, взрывной,
    /// вампир; свечение, дроп на ранг выше»), assembled from <see cref="AffixModule"/>s: a champion is an ordinary monster
    /// of its kind with one affix on its slot (not a kind of its own). Every champion also gets the common
    /// <see cref="ChampionRules"/> (more HP, a bigger body, the rank-up drop) and glows in <see cref="Glow"/>.
    /// Built by Build Monsters into Data/Monsters/Champions (numbers are kept on rebuilds); a new affix = a new asset there.
    /// </summary>
    [CreateAssetMenu(fileName = "Affix", menuName = "Purge/Definitions/Champion Affix")]
    public sealed class ChampionAffixDefinition : ContentDefinition
    {
        [Tooltip("Colour of the champion's glow (alpha = strength).")]
        [SerializeField] private Color _glow = new Color(1f, 0.8f, 0.2f, 0.8f);

        [SerializeReference, ModuleList] private List<AffixModule> _modules = new List<AffixModule>();

        [NonSerialized] private bool _cached;
        [NonSerialized] private float _speedFactor, _attackRateFactor, _damageTaken, _lifesteal;
        [NonSerialized] private HitStatusAffix _hitStatus;
        [NonSerialized] private TrailAffix _trail;
        [NonSerialized] private DeathBlastAffix _deathBlast;
        [NonSerialized] private ResistAffix _resist;

        public Color Glow => _glow;
        public IReadOnlyList<AffixModule> Modules => _modules;

        /// <summary>× walking speed (1 = as its kind).</summary>
        public float SpeedFactor => Cache()._speedFactor;

        /// <summary>× how often it attacks (1 = as its kind; the attack interval is divided by it).</summary>
        public float AttackRateFactor => Cache()._attackRateFactor;

        /// <summary>× damage of hits with a direction (weapons, blasts); zones and states always go through in full.</summary>
        public float DamageTaken => Cache()._damageTaken;

        /// <summary>Share of the damage it deals to the player it heals (0 = none).</summary>
        public float Lifesteal => Cache()._lifesteal;

        public HitStatusAffix HitStatus => Cache()._hitStatus;
        public TrailAffix Trail => Cache()._trail;
        public DeathBlastAffix DeathBlast => Cache()._deathBlast;

        /// <summary>× a state's duration on the champion (1 = as its kind; 0 = immune).</summary>
        public float DurationFactor(StatusDefinition status) => Cache()._resist != null ? _resist.DurationFactor(status) : 1f;

        public void Validate(List<string> problems)
        {
            var owner = $"Champion affix '{name}'";
            Purge.Core.Modules.Modules.Validate(_modules, owner, problems);
            if (_modules.Count == 0) problems.Add($"{owner}: has no modules (only the common champion bonus).");
        }

        private void OnValidate() => _cached = false;

        private ChampionAffixDefinition Cache()
        {
            if (_cached) return this;
            _speedFactor = 1f;
            _attackRateFactor = 1f;
            _damageTaken = 1f;
            _lifesteal = 0f;
            _hitStatus = null;
            _trail = null;
            _deathBlast = null;
            _resist = null;
            foreach (var module in _modules)
                switch (module)
                {
                    case SpeedAffix speed:
                        _speedFactor *= speed.SpeedFactor;
                        _attackRateFactor *= speed.AttackRateFactor;
                        break;
                    case ArmorAffix armor: _damageTaken *= armor.DamageTaken; break;
                    case LifestealAffix lifesteal: _lifesteal += lifesteal.Share; break;
                    case HitStatusAffix hit: _hitStatus = hit; break;
                    case TrailAffix trail: _trail = trail; break;
                    case DeathBlastAffix blast: _deathBlast = blast; break;
                    case ResistAffix resist: _resist = resist; break;
                }

            _cached = true;
            return this;
        }

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, Color glow, params AffixModule[] modules)
        {
            SetId(id);
            _glow = glow;
            _modules = new List<AffixModule>(modules);
            _cached = false;
        }
    }

    /// <summary>
    /// What champions have in common (ТЗ: чемпионы появляются нечасто; в выживании доля растёт): which affixes there are,
    /// how often a new monster is a champion (rank-and-file and officers only), how much HP and size they gain, and the
    /// rank-up drop. Until the waves of stage 6 — on the horde's settings.
    /// </summary>
    [Serializable]
    public sealed class ChampionRules
    {
        [Tooltip("Chance a monster of the waves (rank-and-file; officers too when they come in waves) is a champion.")]
        [SerializeField, Range(0f, 1f)] private float _chance = 0.04f;
        [Tooltip("Affixes a champion gets one of, evenly (Build Monsters fills it from Data/Monsters/Champions).")]
        [SerializeField] private ChampionAffixDefinition[] _affixes = Array.Empty<ChampionAffixDefinition>();
        [Tooltip("× the monster's HP.")]
        [SerializeField, Min(1f)] private float _healthFactor = 2f;
        [Tooltip("× the size of its look (the body in the gameplay stays its kind's).")]
        [SerializeField, Range(1f, 1.6f)] private float _scale = 1.15f;
        [Tooltip("Drop of a champion of the rank-and-file (a rank higher: the officers' table).")]
        [SerializeField] private DropTable _massDrop;
        [Tooltip("Drop of a champion officer (a rank higher: the elite's table).")]
        [SerializeField] private DropTable _officerDrop;

        public float Chance => _chance;
        public ChampionAffixDefinition[] Affixes => _affixes ??= Array.Empty<ChampionAffixDefinition>();
        public float HealthFactor => _healthFactor;
        public float Scale => _scale;

        /// <summary>The rank-up table for a champion of <paramref name="rank"/> (null = its own).</summary>
        public DropTable DropOf(MonsterRank rank) => rank == MonsterRank.Mass ? _massDrop : rank == MonsterRank.Officer ? _officerDrop : null;

        /// <summary>Only the rank-and-file and officers become champions.</summary>
        public static bool CanBe(MonsterRank rank) => rank == MonsterRank.Mass || rank == MonsterRank.Officer;

        /// <summary>For tests and tools.</summary>
        internal void Configure(float chance, ChampionAffixDefinition[] affixes, float healthFactor, float scale,
            DropTable massDrop, DropTable officerDrop)
        {
            _chance = chance;
            _affixes = affixes ?? Array.Empty<ChampionAffixDefinition>();
            _healthFactor = healthFactor;
            _scale = scale;
            _massDrop = massDrop;
            _officerDrop = officerDrop;
        }

        /// <summary>Build Monsters: the affixes of the folder; drops only when not set by hand.</summary>
        internal void SetAffixes(ChampionAffixDefinition[] affixes, DropTable massDrop, DropTable officerDrop)
        {
            _affixes = affixes ?? Array.Empty<ChampionAffixDefinition>();
            if (_massDrop == null) _massDrop = massDrop;
            if (_officerDrop == null) _officerDrop = officerDrop;
        }
    }

    /// <summary>A module of a <see cref="ChampionAffixDefinition"/>.</summary>
    [Serializable]
    public abstract class AffixModule : ContentModule
    {
    }

    /// <summary>Quicker: walks and attacks faster (the fast).</summary>
    [Serializable, ModuleInfo("Speed", Category = "Affix")]
    public sealed class SpeedAffix : AffixModule
    {
        [Tooltip("× walking speed.")]
        [SerializeField, Range(1f, 3f)] private float _speedFactor = 1.5f;
        [Tooltip("× how often it attacks.")]
        [SerializeField, Range(1f, 3f)] private float _attackRateFactor = 1.3f;

        public float SpeedFactor { get => _speedFactor; internal set => _speedFactor = value; }
        public float AttackRateFactor { get => _attackRateFactor; internal set => _attackRateFactor = value; }
    }

    /// <summary>Armor all round (the armored): hits with a direction lose a share; zones and states go through in full.</summary>
    [Serializable, ModuleInfo("Armor", Category = "Affix")]
    public sealed class ArmorAffix : AffixModule
    {
        [Tooltip("× damage of weapons and blasts from any side (0.5 = half goes through).")]
        [SerializeField, Range(0.1f, 1f)] private float _damageTaken = 0.5f;

        public float DamageTaken { get => _damageTaken; internal set => _damageTaken = value; }
    }

    /// <summary>A state on the player with every hit it lands (the fiery: sets on fire).</summary>
    [Serializable, ModuleInfo("Status on hit", Category = "Affix")]
    public sealed class HitStatusAffix : AffixModule
    {
        [SerializeField] private StatusDefinition _status;
        [Tooltip("Seconds.")]
        [SerializeField, Min(0.1f)] private float _duration = 3f;

        public StatusDefinition Status { get => _status; internal set => _status = value; }
        public float Duration { get => _duration; internal set => _duration = value; }

        public override void Validate(List<string> problems)
        {
            if (_status == null) problems.Add("no status.");
        }
    }

    /// <summary>
    /// Leaves a zone on the cells it walks off (the fiery: a burning trail) — the monsters' zone (it hurts the player and
    /// the monsters in it); the champion itself takes nothing from this zone.
    /// </summary>
    [Serializable, ModuleInfo("Trail", Category = "Affix")]
    public sealed class TrailAffix : AffixModule
    {
        [SerializeField] private ZoneDefinition _zone;
        [Tooltip("Seconds a cell of the trail lasts.")]
        [SerializeField, Min(0.2f)] private float _duration = 3f;
        [Tooltip("Multiplier of the zone's damage.")]
        [SerializeField, Min(0f)] private float _strength = 1f;

        public ZoneDefinition Zone { get => _zone; internal set => _zone = value; }
        public float Duration { get => _duration; internal set => _duration = value; }
        public float Strength { get => _strength; internal set => _strength = value; }

        public override void Validate(List<string> problems)
        {
            if (_zone == null) problems.Add("no zone.");
        }
    }

    /// <summary>
    /// Goes off where it died after a delay with a warning circle (the explosive): its effects are the monsters' (an
    /// explosion hurts the player and the monsters around).
    /// </summary>
    [Serializable, ModuleInfo("Death blast", Category = "Affix")]
    public sealed class DeathBlastAffix : AffixModule
    {
        [Tooltip("Seconds from the death to the blast (the warning circle fills meanwhile).")]
        [SerializeField, Range(0.1f, 3f)] private float _delay = 0.6f;
        [SerializeReference, ModuleList] private List<EffectModule> _effects = new List<EffectModule>();

        public float Delay { get => _delay; internal set => _delay = value; }
        public IReadOnlyList<EffectModule> Effects => _effects;

        /// <summary>The widest of its effects (the warning circle).</summary>
        public float Radius
        {
            get
            {
                var radius = 0f;
                foreach (var effect in _effects)
                    if (effect != null)
                        radius = Mathf.Max(radius, effect.Radius);
                return radius;
            }
        }

        internal void SetEffects(params EffectModule[] effects) => _effects = new List<EffectModule>(effects);

        public override void Validate(List<string> problems)
        {
            if (_effects.Count == 0) problems.Add("no effects.");
            Purge.Core.Modules.Modules.Validate(_effects, "Death blast", problems);
        }
    }

    /// <summary>Heals a share of the damage it deals to the player (the vampire).</summary>
    [Serializable, ModuleInfo("Lifesteal", Category = "Affix")]
    public sealed class LifestealAffix : AffixModule
    {
        [Tooltip("Share of the damage dealt to the player it heals (1 = all of it), up to its full HP.")]
        [SerializeField, Range(0f, 3f)] private float _share = 1f;

        public float Share { get => _share; internal set => _share = value; }
    }

    /// <summary>States that last shorter on the champion (or never take; the fiery does not burn).</summary>
    [Serializable, ModuleInfo("Resist", Category = "Affix")]
    public sealed class ResistAffix : AffixModule
    {
        [SerializeField] private List<StatusResistance> _resistances = new List<StatusResistance>();

        public IReadOnlyList<StatusResistance> Resistances => _resistances;

        public float DurationFactor(StatusDefinition status)
        {
            if (status == null) return 1f;
            foreach (var resistance in _resistances)
                if (resistance != null && resistance.Status == status)
                    return resistance.DurationFactor;
            return 1f;
        }

        internal void SetResistances(params StatusResistance[] resistances) =>
            _resistances = new List<StatusResistance>(resistances);

        public override void Validate(List<string> problems)
        {
            for (var i = 0; i < _resistances.Count; i++)
                if (_resistances[i] == null || _resistances[i].Status == null)
                    problems.Add($"resistance #{i + 1} has no state.");
        }
    }
}
