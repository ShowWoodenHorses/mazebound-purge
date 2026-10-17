using Purge.Core.Content;
using UnityEngine;

namespace Purge.Core.Definitions
{
    /// <summary>
    /// A medkit kind that monsters drop (ТЗ: до 5 типов — полное восстановление и фиксированные +N). Used on pickup only
    /// when the player is hurt; at full health it stays lying. Medkits placed in a level (Maze) heal to full. The look is
    /// defined separately (ItemVisualCatalog).
    /// </summary>
    [CreateAssetMenu(fileName = "Medkit", menuName = "Purge/Definitions/Medkit")]
    public sealed class MedkitDefinition : ContentDefinition
    {
        [Tooltip("Health restored; 0 = to full.")]
        [SerializeField, Min(0)] private int _heal;

        public int Heal => _heal;
        public bool HealsToFull => _heal <= 0;

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, int heal)
        {
            SetId(id);
            _heal = heal;
        }
    }
}
