using UnityEngine;

namespace Purge.Core.Content
{
    /// <summary>
    /// Base of every content asset that saves, drop tables, the shop or other content refer to (weapons, monsters,
    /// obstacles, later throwables, cards…). Its <see cref="Id"/> is stable: made once from the file name when the
    /// asset is created (editor, <c>ContentIdAssigner</c>), never changed afterwards — renaming or moving the file
    /// keeps it, a copy of the asset gets its own. Saves keep purchases and upgrades by it.
    /// </summary>
    public abstract class ContentDefinition : ScriptableObject
    {
        [Tooltip("Stable id: made from the file name when the asset is created and never changed (saves keep purchases " +
                 "and upgrades by it). Renaming the file keeps it.")]
        [SerializeField, StableId] private string _id;

        public string Id => _id;

        /// <summary>Editor (id assignment) and tests only.</summary>
        internal void SetId(string id) => _id = id;
    }

    /// <summary>Marks the stable id field of <see cref="ContentDefinition"/>: shown read-only in the inspector.</summary>
    public sealed class StableIdAttribute : PropertyAttribute
    {
    }
}
