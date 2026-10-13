using UnityEngine;

namespace Maze.Core.Audio
{
    /// <summary>
    /// A string field naming a weapon sound group: the inspector shows a dropdown of the group folders
    /// (Sounds/Weapon/&lt;group&gt;) instead of a text box, so a name cannot be mistyped.
    /// </summary>
    public sealed class WeaponSoundGroupAttribute : PropertyAttribute
    {
    }
}
