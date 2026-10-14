using System;
using System.Collections.Generic;
using UnityEngine;

namespace Purge.Core.Content
{
    /// <summary>
    /// Every content asset by its stable id (Addressable <see cref="Address"/>): resolves the ids kept in saves, drop
    /// tables and the shop. Filled by the editor from the content folders (Purge → Dev → Build Content Catalog, also
    /// Build / Sync), never by hand.
    /// </summary>
    [CreateAssetMenu(fileName = "ContentCatalog", menuName = "Purge/Content Catalog")]
    public sealed class ContentCatalog : ScriptableObject
    {
        public const string Address = "Content/Catalog";

        [SerializeField] private List<ContentDefinition> _items = new List<ContentDefinition>();

        private Dictionary<string, ContentDefinition> _byId;

        public IReadOnlyList<ContentDefinition> Items => _items;

        internal List<ContentDefinition> MutableItems
        {
            get
            {
                _byId = null;
                return _items;
            }
        }

        /// <summary>The content with this id, null when there is none (e.g. removed from the game after a save).</summary>
        public ContentDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_byId == null)
            {
                _byId = new Dictionary<string, ContentDefinition>(_items.Count, StringComparer.Ordinal);
                foreach (var item in _items)
                    if (item != null && !string.IsNullOrEmpty(item.Id))
                        _byId[item.Id] = item;
            }

            return _byId.TryGetValue(id, out var found) ? found : null;
        }

        /// <summary>The content of type <typeparamref name="T"/> with this id, null when there is none.</summary>
        public T Find<T>(string id) where T : ContentDefinition => Find(id) as T;

        /// <summary>All content of type <typeparamref name="T"/>, in catalog order.</summary>
        public void GetAll<T>(List<T> result) where T : ContentDefinition
        {
            result.Clear();
            foreach (var item in _items)
                if (item is T typed)
                    result.Add(typed);
        }

        /// <summary>Problems of the list: empty slots, invalid ids, the same id twice (editor checks and tests).</summary>
        public void Validate(List<string> problems)
        {
            var seen = new Dictionary<string, ContentDefinition>(StringComparer.Ordinal);
            foreach (var item in _items)
            {
                if (item == null)
                {
                    problems.Add("Empty slot in the content catalog.");
                    continue;
                }

                if (!ContentIds.IsValid(item.Id))
                    problems.Add($"'{item.name}' has an invalid id '{item.Id}'.");
                else if (seen.TryGetValue(item.Id, out var other))
                    problems.Add($"'{item.name}' and '{other.name}' have the same id '{item.Id}'.");
                else
                    seen.Add(item.Id, item);
            }
        }
    }

    /// <summary>
    /// A string field holding the stable id of a content asset of <see cref="Type"/>: the inspector shows a dropdown
    /// of those assets instead of a text box (a mistyped id silently breaks).
    /// </summary>
    public sealed class ContentRefAttribute : PropertyAttribute
    {
        public ContentRefAttribute(Type type) => Type = type;

        public Type Type { get; }
    }
}
