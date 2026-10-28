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

    /// <summary>
    /// A string field naming a monster sound (<see cref="AudioCatalog.MonsterSound"/>): a dropdown of the folders
    /// Sounds/Monster/&lt;sound&gt; (stage 6e2: casts of abilities).
    /// </summary>
    public sealed class MonsterSoundAttribute : PropertyAttribute
    {
    }

    /// <summary>
    /// A string field naming a vehicle sound (<see cref="AudioCatalog.VehicleSound"/>): a dropdown of the folders
    /// Sounds/Vehicle/&lt;sound&gt; (stage 9a: engines).
    /// </summary>
    public sealed class VehicleSoundAttribute : PropertyAttribute
    {
    }
}
