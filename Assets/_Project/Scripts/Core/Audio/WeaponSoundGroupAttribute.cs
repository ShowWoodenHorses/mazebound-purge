using UnityEngine;

namespace Purge.Core.Audio
{
    /// <summary>
    /// A string field naming a weapon sound group: the inspector shows a dropdown of the group folders
    /// (Sounds/Weapon/&lt;group&gt;) instead of a text box, so a name cannot be mistyped.
    /// </summary>
    public sealed class WeaponSoundGroupAttribute : PropertyAttribute
    {
    }

    /// <summary>
    /// A string field naming a shared weapon sound (<see cref="SoundPart"/>): a dropdown of the part folders
    /// (Sounds/Weapon/_Parts/&lt;part&gt;).
    /// </summary>
    public sealed class WeaponSoundPartAttribute : PropertyAttribute
    {
    }
}
