using System;
using System.Collections.Generic;
using Maze.Core.Definitions;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Look of one weapon in the player's hands (ТЗ §39: WeaponDefinition → WeaponVisualDefinition → prefab). The
    /// held prefab is display only: pivot at the grip, barrel or blade along +Z, up +Y. How it looks lying on the
    /// floor is the theme's Weapon set (a variant per definition), not this.
    /// </summary>
    [Serializable]
    public sealed class WeaponVisualDefinition
    {
        [SerializeField] private WeaponDefinition _definition;
        [SerializeField] private AssetReferenceGameObject _heldPrefab;

        [Tooltip("White silhouette for the HUD weapon slots (Maze → Dev → Build UI Icons).")]
        [SerializeField] private Sprite _icon;

        public WeaponVisualDefinition(WeaponDefinition definition, AssetReferenceGameObject heldPrefab, Sprite icon = null)
        {
            _definition = definition;
            _heldPrefab = heldPrefab;
            _icon = icon;
        }

        public WeaponDefinition Definition => _definition;
        public AssetReferenceGameObject HeldPrefab => _heldPrefab;

        /// <summary>Silhouette for the HUD; null until icons are built.</summary>
        public Sprite Icon
        {
            get => _icon;
            internal set => _icon = value;
        }
    }

    /// <summary>Held looks of all weapons, shared by every level, Addressable at <see cref="Address"/>.</summary>
    [CreateAssetMenu(fileName = "WeaponVisuals", menuName = "Maze/Visual/Weapon Visuals")]
    public sealed class WeaponVisualCatalog : ScriptableObject
    {
        public const string Address = "Weapons/Visual";

        [SerializeField] private List<WeaponVisualDefinition> _weapons = new List<WeaponVisualDefinition>();

        public IReadOnlyList<WeaponVisualDefinition> Weapons => _weapons;

        internal List<WeaponVisualDefinition> MutableWeapons => _weapons;

        /// <summary>
        /// The look of <paramref name="definition"/>, or null. Matched by reference, then by id: in a player build a
        /// definition that is not Addressable itself is copied into every bundle referencing it, so the level's and
        /// this catalog's copies may be different objects.
        /// </summary>
        public WeaponVisualDefinition Find(WeaponDefinition definition)
        {
            if (definition == null) return null;
            foreach (var weapon in _weapons)
                if (weapon != null && weapon.Definition == definition)
                    return weapon;

            if (string.IsNullOrEmpty(definition.Id)) return null;
            foreach (var weapon in _weapons)
                if (weapon?.Definition != null && weapon.Definition.Id == definition.Id)
                    return weapon;
            return null;
        }
    }
}
