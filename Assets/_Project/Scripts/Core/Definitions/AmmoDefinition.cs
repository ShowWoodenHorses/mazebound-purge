using Purge.Core.Content;
using UnityEngine;

namespace Purge.Core.Definitions
{
    /// <summary>
    /// A type of ammunition (ТЗ: 9 мм, 5,45, 7,62, дробь, баллоны, батареи, заряды). The reserve of every type is
    /// unlimited; the type is a property of a concrete weapon (its <see cref="AmmoModule"/>), not of its class.
    /// The shot's noise comes from here (ТЗ «Шум от калибра»): 9 mm is quieter than 5.45, 5.45 than 7.62; a silencer
    /// multiplies it (<see cref="SilencerModule"/>), a weapon may override it.
    /// </summary>
    [CreateAssetMenu(fileName = "Ammo", menuName = "Purge/Definitions/Ammo")]
    public sealed class AmmoDefinition : ContentDefinition
    {
        [Tooltip("How far a shot with this ammo is heard by monsters, in cells.")]
        [SerializeField, Min(0f)] private float _soundRadius = 6f;

        [Tooltip("Folders of Art/Weapons/Purge (class or caliber folder names, e.g. \"9_mm\", \"Shotgun\") whose models " +
                 "get this ammo when Build Weapons makes their definition. The deepest matching folder wins.")]
        [SerializeField] private string[] _modelFolders = new string[0];

        public float SoundRadius => _soundRadius;
        public string[] ModelFolders => _modelFolders;

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, float soundRadius, params string[] modelFolders)
        {
            SetId(id);
            _soundRadius = soundRadius;
            _modelFolders = modelFolders;
        }
    }
}
