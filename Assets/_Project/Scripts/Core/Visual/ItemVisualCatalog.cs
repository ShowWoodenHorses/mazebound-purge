using System;
using System.Collections.Generic;
using Purge.Core.Content;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Core.Visual
{
    /// <summary>
    /// Look of one item that can lie on the ground without being placed in a level: what monsters drop (weapons, ammo,
    /// medkits, throwables) and a throwable in flight. Display only.
    /// </summary>
    [Serializable]
    public sealed class ItemVisualDefinition
    {
        [SerializeField] private ContentDefinition _item;

        [Tooltip("Lying in a cell: pivot on the floor at the cell centre.")]
        [SerializeField] private AssetReferenceGameObject _pickup;

        [Tooltip("In flight (throwables): pivot at its centre. Empty = the pickup.")]
        [SerializeField] private AssetReferenceGameObject _flying;

        [Tooltip("White silhouette for the HUD (throwables).")]
        [SerializeField] private Sprite _icon;

        public ItemVisualDefinition(ContentDefinition item, AssetReferenceGameObject pickup, AssetReferenceGameObject flying = null,
            Sprite icon = null)
        {
            _item = item;
            _pickup = pickup;
            _flying = flying;
            _icon = icon;
        }

        public ContentDefinition Item => _item;
        public AssetReferenceGameObject Pickup => _pickup;
        public AssetReferenceGameObject Flying => _flying != null && _flying.RuntimeKeyIsValid() ? _flying : _pickup;

        public Sprite Icon
        {
            get => _icon;
            internal set => _icon = value;
        }
    }

    /// <summary>
    /// Looks of the items that appear during play (drops, thrown items), shared by every level, Addressable at
    /// <see cref="Address"/>. The prefabs themselves are separate Addressables: a level loads only those its drop pool
    /// can produce. Made by Purge → Dev → Build Items.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemVisuals", menuName = "Purge/Visual/Item Visuals")]
    public sealed class ItemVisualCatalog : ScriptableObject
    {
        public const string Address = "Items/Visual";

        [SerializeField] private List<ItemVisualDefinition> _items = new List<ItemVisualDefinition>();

        public IReadOnlyList<ItemVisualDefinition> Items => _items;

        internal List<ItemVisualDefinition> MutableItems => _items;

        /// <summary>
        /// The look of <paramref name="item"/>, or null. Matched by reference, then by id: in a player build a definition
        /// that is not Addressable itself is copied into every bundle referencing it.
        /// </summary>
        public ItemVisualDefinition Find(ContentDefinition item)
        {
            if (item == null) return null;
            foreach (var visual in _items)
                if (visual != null && visual.Item == item)
                    return visual;

            if (string.IsNullOrEmpty(item.Id)) return null;
            foreach (var visual in _items)
                if (visual?.Item != null && visual.Item.Id == item.Id && visual.Item.GetType() == item.GetType())
                    return visual;
            return null;
        }
    }
}
