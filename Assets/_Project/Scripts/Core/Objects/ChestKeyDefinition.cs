using Purge.Core.Content;
using UnityEngine;

namespace Purge.Core.Objects
{
    /// <summary>
    /// The key of the locked chest (ТЗ 7в): drops from the first elite killed, picked up by walking over it, opens the
    /// locked chest. Not lost when a wave changes. The look is in ItemVisualCatalog (Build Items).
    /// </summary>
    [CreateAssetMenu(fileName = "ChestKey", menuName = "Purge/Definitions/Chest Key")]
    public sealed class ChestKeyDefinition : ContentDefinition
    {
        /// <summary>For tests and tools.</summary>
        internal void Configure(string id) => SetId(id);
    }
}
