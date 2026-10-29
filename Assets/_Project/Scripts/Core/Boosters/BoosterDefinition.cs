using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Level;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Boosters
{
    /// <summary>When a booster can be bought in the preparation (ТЗ 11д: базовые — после уровня 1, особые — узлами дерева).</summary>
    public enum BoosterUnlock
    {
        /// <summary>Once the first catalog level is completed on any difficulty.</summary>
        FirstLevel = 0,

        /// <summary>Opened by a node of the character tree (stage 11д: <c>SaveData.Boosters</c>).</summary>
        Tree = 1,
    }

    /// <summary>A booster's prices on one location: by difficulty, 0 = easy … 3 = unreal (the model's boosters.csv).</summary>
    [Serializable]
    public sealed class BoosterPrice
    {
        [SerializeField] private LevelLocation _location;
        [SerializeField] private int[] _byDifficulty = new int[4];

        public BoosterPrice(LevelLocation location, params int[] byDifficulty)
        {
            _location = location;
            _byDifficulty = byDifficulty;
        }

        public LevelLocation Location => _location;

        /// <summary>The price on a difficulty; 0 when there is none.</summary>
        public int On(int difficulty) =>
            _byDifficulty != null && difficulty >= 0 && difficulty < _byDifficulty.Length ? Mathf.Max(0, _byDifficulty[difficulty]) : 0;
    }

    /// <summary>
    /// A booster of the preparation (ТЗ А.5: каждый раз за забег, списание на старте, цена растёт с уровнем и сложностью),
    /// assembled from <see cref="BoosterModule"/>s; the run applies them at its start (stage 11б). Several can be taken at
    /// once. Texts: <c>booster.&lt;id&gt;</c> (name), <c>booster.&lt;id&gt;.text</c> (what it gives).
    /// </summary>
    [CreateAssetMenu(fileName = "Booster", menuName = "Purge/Definitions/Booster")]
    public sealed class BoosterDefinition : ContentDefinition
    {
        [SerializeField] private BoosterUnlock _unlock;

        [Tooltip("Line icon of the preparation (Purge → Dev → Build Boosters takes it from the UI icons).")]
        [SerializeField] private Sprite _icon;

        [Tooltip("Price by location and difficulty (the economy model); no price there = not offered there.")]
        [SerializeField] private List<BoosterPrice> _prices = new List<BoosterPrice>();

        [SerializeReference, ModuleList] private List<BoosterModule> _modules = new List<BoosterModule>();

        public BoosterUnlock Unlock => _unlock;
        public Sprite Icon { get => _icon; internal set => _icon = value; }
        public IReadOnlyList<BoosterPrice> Prices => _prices;
        public IReadOnlyList<BoosterModule> Modules => _modules;

        /// <summary>The price of one run on the location and difficulty; 0 = not offered there.</summary>
        public int PriceOn(LevelLocation location, int difficulty)
        {
            foreach (var price in _prices)
                if (price != null && price.Location == location)
                    return price.On(difficulty);
            return 0;
        }

        public bool Has<T>() where T : BoosterModule => Purge.Core.Modules.Modules.Has<T>(_modules);

        public bool TryGet<T>(out T module) where T : BoosterModule => Purge.Core.Modules.Modules.TryGet(_modules, out module);

        public void Validate(List<string> problems)
        {
            var owner = $"Booster '{name}'";
            Purge.Core.Modules.Modules.Validate(_modules, owner, problems);
            if (_modules.Count == 0) problems.Add($"{owner}: has no modules (does nothing).");
            if (_prices.Count == 0) problems.Add($"{owner}: has no prices (offered nowhere).");
        }

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, BoosterUnlock unlock, IEnumerable<BoosterPrice> prices, params BoosterModule[] modules)
        {
            SetId(id);
            _unlock = unlock;
            _prices = new List<BoosterPrice>(prices ?? Array.Empty<BoosterPrice>());
            _modules = new List<BoosterModule>(modules);
        }
    }

    /// <summary>A module of a <see cref="BoosterDefinition"/>: what the booster gives at the run's start.</summary>
    [Serializable]
    public abstract class BoosterModule : ContentModule
    {
    }

    /// <summary>
    /// More throwables at the start: added to the chosen throwable's stack (it may grow past the slot's stack then); no
    /// throwable chosen — <see cref="Fallback"/> (decision of the user, 2026-10-30).
    /// </summary>
    [Serializable, ModuleInfo("Throwables")]
    public sealed class ThrowablesBoost : BoosterModule
    {
        [SerializeField, Min(1)] private int _count = 3;

        [Tooltip("Given when the preparation has no throwable.")]
        [SerializeField] private ThrowableDefinition _fallback;

        public ThrowablesBoost(int count = 3, ThrowableDefinition fallback = null)
        {
            _count = count;
            _fallback = fallback;
        }

        public int Count => Mathf.Max(1, _count);
        public ThrowableDefinition Fallback => _fallback;

        public override void Validate(List<string> problems)
        {
            if (_fallback == null) problems.Add("Throwables: no fallback throwable.");
        }
    }

    /// <summary>A card to choose right at the start (one more level-up's offer).</summary>
    [Serializable, ModuleInfo("Start card")]
    public sealed class StartCardBoost : BoosterModule
    {
        [SerializeField, Min(1)] private int _cards = 1;

        public StartCardBoost(int cards = 1) => _cards = cards;

        public int Cards => Mathf.Max(1, _cards);
    }

    /// <summary>A medkit lying next to the start for the whole run (decision of the user, 2026-10-30).</summary>
    [Serializable, ModuleInfo("Medkit")]
    public sealed class MedkitBoost : BoosterModule
    {
        [SerializeField] private MedkitDefinition _medkit;

        public MedkitBoost(MedkitDefinition medkit = null) => _medkit = medkit;

        public MedkitDefinition Medkit => _medkit;

        public override void Validate(List<string> problems)
        {
            if (_medkit == null) problems.Add("Medkit: no medkit.");
        }
    }

    /// <summary>Free card rerolls for the run (without the rewarded ad).</summary>
    [Serializable, ModuleInfo("Rerolls")]
    public sealed class RerollBoost : BoosterModule
    {
        [SerializeField, Min(1)] private int _rerolls = 1;

        public RerollBoost(int rerolls = 1) => _rerolls = rerolls;

        public int Rerolls => Mathf.Max(1, _rerolls);
    }

    /// <summary>Scouting: the level's map is explored from the start.</summary>
    [Serializable, ModuleInfo("Scouting")]
    public sealed class ScoutingBoost : BoosterModule
    {
    }
}
