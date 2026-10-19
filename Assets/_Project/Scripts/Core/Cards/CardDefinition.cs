using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Cards
{
    /// <summary>
    /// A card of the run (ТЗ «Прогрессия»: at a level-up the player picks 1 of 3; it lasts until the run's end), assembled
    /// from <see cref="CardModule"/>s. Every pick of the same card adds a level (up to <see cref="MaxLevel"/>; a card at
    /// its last level leaves the choice), each level adds the modules' numbers once more. The rarity is fixed (as a
    /// weapon's): a higher one is stronger and comes up less often (<see cref="CardDeck.WeightOf"/>).
    /// Texts: <c>card.&lt;id&gt;</c> (name), <c>card.&lt;id&gt;.text</c> (what a level gives, <c>{0}</c>, <c>{1}</c>… — the
    /// modules' <see cref="CardModule.ShownValue"/> in order).
    /// </summary>
    [CreateAssetMenu(fileName = "Card", menuName = "Purge/Definitions/Card")]
    public sealed class CardDefinition : ContentDefinition
    {
        [SerializeField] private Rarity _rarity;

        [Tooltip("Times it can be picked in a run (1 = once).")]
        [SerializeField, Min(1)] private int _maxLevel = 5;

        [Tooltip("Line icon of the card (Purge → Dev → Build UI Icons draws the first set's).")]
        [SerializeField] private Sprite _icon;

        [SerializeReference, ModuleList] private List<CardModule> _modules = new List<CardModule>();

        public Rarity Rarity => _rarity;
        public int MaxLevel => Mathf.Max(1, _maxLevel);
        public Sprite Icon { get => _icon; internal set => _icon = value; }
        public IReadOnlyList<CardModule> Modules => _modules;

        public void Validate(List<string> problems)
        {
            var owner = $"Card '{name}'";
            Purge.Core.Modules.Modules.Validate(_modules, owner, problems);
            if (_modules.Count == 0) problems.Add($"{owner}: has no modules (does nothing).");
        }

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, Rarity rarity, int maxLevel, params CardModule[] modules)
        {
            SetId(id);
            _rarity = rarity;
            _maxLevel = Mathf.Max(1, maxLevel);
            _modules = new List<CardModule>(modules);
        }
    }

    /// <summary>A module of a <see cref="CardDefinition"/>: what one level of the card gives.</summary>
    [Serializable]
    public abstract class CardModule : ContentModule
    {
        /// <summary>The number its text shows for one level (percent stats in percent).</summary>
        public abstract float ShownValue { get; }
    }

    /// <summary>A stat of the run a card changes (<see cref="StatCard"/>); every level adds its number.</summary>
    public enum RunStat
    {
        /// <summary>+share of the weapons' damage (bullets, streams, chains, melee; not grenades or zones).</summary>
        WeaponDamage = 0,

        /// <summary>+share of the attacks per second.</summary>
        FireRate = 1,

        /// <summary>−share of the reload time.</summary>
        ReloadTime = 2,

        /// <summary>+share of the magazine.</summary>
        Magazine = 3,

        /// <summary>+share of the move speed.</summary>
        MoveSpeed = 4,

        /// <summary>+HP of the maximum (and heals as much at once).</summary>
        MaxHealth = 5,

        /// <summary>+cells: ammo, throwables and medkits that close fly to the player.</summary>
        PickupRadius = 6,

        /// <summary>+HP for every monster killed.</summary>
        HealOnKill = 7,

        /// <summary>+share of the damage of the player's explosions (grenades, launchers).</summary>
        ExplosionDamage = 8,

        /// <summary>+share of the radius of the player's explosions.</summary>
        ExplosionRadius = 9,

        /// <summary>+chance (share) of a critical hit of bullets and melee: × <c>RunStats.CriticalFactor</c>.</summary>
        CritChance = 10,
    }

    /// <summary>Adds to a stat of the run every level.</summary>
    [Serializable, ModuleInfo("Stat", Category = "Card", AllowMultiple = true)]
    public sealed class StatCard : CardModule
    {
        [SerializeField] private RunStat _stat;

        [Tooltip("Per level: a share for percent stats (0.1 = +10 %), HP or cells for the others.")]
        [SerializeField] private float _perLevel = 0.1f;

        public RunStat Stat { get => _stat; internal set => _stat = value; }
        public float PerLevel { get => _perLevel; internal set => _perLevel = value; }

        public override float ShownValue => IsPercent(_stat) ? Mathf.Round(_perLevel * 1000f) / 10f : _perLevel;

        /// <summary>The stat is a share (shown in percent).</summary>
        public static bool IsPercent(RunStat stat) =>
            stat != RunStat.MaxHealth && stat != RunStat.PickupRadius && stat != RunStat.HealOnKill;

        public override void Validate(List<string> problems)
        {
            if (_perLevel <= 0f) problems.Add($"Stat '{_stat}': the number per level must be above 0.");
        }
    }

    /// <summary>More damage of the player's weapons and explosions to monsters with a state (burning, frozen…).</summary>
    [Serializable, ModuleInfo("Damage vs state", Category = "Card", AllowMultiple = true)]
    public sealed class StatusDamageCard : CardModule
    {
        [SerializeField] private StatusDefinition _status;

        [Tooltip("Per level: +share of the damage (0.25 = +25 %).")]
        [SerializeField, Min(0f)] private float _perLevel = 0.25f;

        public StatusDefinition Status { get => _status; internal set => _status = value; }
        public float PerLevel { get => _perLevel; internal set => _perLevel = value; }

        public override float ShownValue => Mathf.Round(_perLevel * 1000f) / 10f;

        public override void Validate(List<string> problems)
        {
            if (_status == null) problems.Add("Damage vs state: no state.");
        }
    }

    /// <summary>Throwables into the slot at once: the kind in it, or <see cref="Fallback"/> when it is empty.</summary>
    [Serializable, ModuleInfo("Throwables", Category = "Card")]
    public sealed class ThrowablesCard : CardModule
    {
        [SerializeField, Min(1)] private int _count = 2;

        [Tooltip("What the empty slot gets (a grenade).")]
        [SerializeField] private ThrowableDefinition _fallback;

        public int Count { get => _count; internal set => _count = value; }
        public ThrowableDefinition Fallback { get => _fallback; internal set => _fallback = value; }

        public override float ShownValue => _count;

        public override void Validate(List<string> problems)
        {
            if (_fallback == null) problems.Add("Throwables: no kind for an empty slot.");
        }
    }
}
