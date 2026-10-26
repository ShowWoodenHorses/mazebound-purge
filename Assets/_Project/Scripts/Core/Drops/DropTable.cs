using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Definitions;
using UnityEngine;

namespace Purge.Core.Drops
{
    /// <summary>
    /// What a killed monster drops (ТЗ «Дроп»): first the kind by weight — nothing, ammo, a medkit, a throwable, a weapon;
    /// then the item. Weapons and throwables come from the drop pool (bought ones — stage 10; the sandbox's list now),
    /// weighted by rarity and capped by the monster's rank (<see cref="MaxRarity"/>). Ammo is smart: mostly for the
    /// weapons in the slots, more for the one in hands and for one that runs low. Stage 5 gives every rank its table.
    /// </summary>
    [CreateAssetMenu(fileName = "DropTable", menuName = "Purge/Definitions/Drop Table")]
    public sealed class DropTable : ContentDefinition
    {
        [Header("Kind (weights)")]
        [SerializeField, Min(0f)] private float _nothing = 60f;
        [SerializeField, Min(0f)] private float _ammo = 25f;
        [SerializeField, Min(0f)] private float _medkit = 7f;
        [SerializeField, Min(0f)] private float _throwable = 5f;
        [SerializeField, Min(0f)] private float _weapon = 3f;

        [Header("Ammo")]
        [Tooltip("Share of ammo drops that are for the weapons in the slots (the rest — any type the pool's weapons use).")]
        [SerializeField, Range(0f, 1f)] private float _heldShare = 0.75f;
        [Tooltip("Rounds dropped, × the magazine of a weapon using the type (random in the range).")]
        [SerializeField] private Vector2 _magazines = new Vector2(0.5f, 1.2f);
        [Tooltip("Rounds of a type no weapon in the slots uses.")]
        [SerializeField, Min(1)] private int _fallbackRounds = 10;

        [Header("Medkits")]
        [SerializeField] private List<MedkitDrop> _medkits = new List<MedkitDrop>();
        [Tooltip("Weight of the medkit kind while the player has full health, × (rarer: they would only lie there).")]
        [SerializeField, Range(0f, 1f)] private float _medkitAtFullHealth = 0.3f;

        [Header("Weapons and throwables")]
        [Tooltip("Weight by rarity: Common, Uncommon, Rare, Epic, Legendary.")]
        [SerializeField] private float[] _rarityWeights = { 100f, 45f, 18f, 7f, 3f };
        [Tooltip("Rarest item this table drops (the monster's rank caps it).")]
        [SerializeField] private Rarity _maxRarity = Rarity.Legendary;

        [Header("On the ground")]
        [Tooltip("Seconds a dropped item lies before it disappears.")]
        [SerializeField, Min(1f)] private float _lifetime = 30f;
        [Tooltip("Most dropped items lying at once: the oldest disappears for a new one.")]
        [SerializeField, Min(1)] private int _maxLying = 40;

        public float NothingWeight => _nothing;
        public float AmmoWeight => _ammo;
        public float MedkitWeight => _medkit;
        public float ThrowableWeight => _throwable;
        public float WeaponWeight => _weapon;
        public float HeldShare => _heldShare;
        public Vector2 Magazines => _magazines;
        public int FallbackRounds => _fallbackRounds;
        public IReadOnlyList<MedkitDrop> Medkits => _medkits;
        public float MedkitAtFullHealth => _medkitAtFullHealth;
        public Rarity MaxRarity => _maxRarity;
        public float Lifetime => _lifetime;
        public int MaxLying => _maxLying;

        public float RarityWeight(Rarity rarity)
        {
            if (rarity > _maxRarity) return 0f;
            var index = (int)rarity;
            return _rarityWeights != null && index < _rarityWeights.Length ? Mathf.Max(0f, _rarityWeights[index]) : 0f;
        }

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, float nothing, float ammo, float medkit, float throwable, float weapon)
        {
            SetId(id);
            _nothing = nothing;
            _ammo = ammo;
            _medkit = medkit;
            _throwable = throwable;
            _weapon = weapon;
        }

        internal void ConfigureAmmo(float heldShare, Vector2 magazines, int fallbackRounds)
        {
            _heldShare = heldShare;
            _magazines = magazines;
            _fallbackRounds = fallbackRounds;
        }

        /// <summary>For tests and tools: weights per rarity from Common up (a boss's drop — a step above the elite's).</summary>
        internal void SetRarityWeights(params float[] weights) => _rarityWeights = weights;

        internal void ConfigureGround(float lifetime, int maxLying)
        {
            _lifetime = lifetime;
            _maxLying = maxLying;
        }

        internal List<MedkitDrop> MutableMedkits => _medkits;
    }

    [Serializable]
    public sealed class MedkitDrop
    {
        [SerializeField] private MedkitDefinition _medkit;
        [SerializeField, Min(0f)] private float _weight = 1f;

        public MedkitDrop(MedkitDefinition medkit, float weight)
        {
            _medkit = medkit;
            _weight = weight;
        }

        public MedkitDefinition Medkit => _medkit;
        public float Weight => _weight;
    }
}
