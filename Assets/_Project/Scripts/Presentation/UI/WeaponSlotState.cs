using UnityEngine;

namespace Maze.Presentation.UI
{
    /// <summary>What a weapon slot shows (touch cluster, desktop HUD). Filled by the HUD presenter.</summary>
    public struct WeaponSlotState
    {
        /// <summary>A weapon is in the slot.</summary>
        public bool Equipped;

        /// <summary>The slot is the active one.</summary>
        public bool Active;

        /// <summary>Silhouette of the weapon; null → the generic slot icon.</summary>
        public Sprite Icon;

        /// <summary>Magazine size; 0 for melee (no ammo shown).</summary>
        public int Magazine;

        public int Ammo;

        /// <summary>0..1 share of the reload done while reloading, otherwise 0.</summary>
        public float Reload;

        public bool Reloading;

        public static WeaponSlotState Empty => default;

        public bool Same(in WeaponSlotState other) =>
            Equipped == other.Equipped && Active == other.Active && Icon == other.Icon && Magazine == other.Magazine &&
            Ammo == other.Ammo && Reloading == other.Reloading && Mathf.Abs(Reload - other.Reload) < 0.005f;
    }

    /// <summary>What the Use button would do now.</summary>
    public enum UseTarget
    {
        None = 0,
        PickUp = 1,
        Door = 2,
    }
}
