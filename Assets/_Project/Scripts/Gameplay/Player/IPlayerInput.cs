using UnityEngine;

namespace Purge.Gameplay.Player
{
    /// <summary>Button actions of the input abstraction (ТЗ §64).</summary>
    public enum PlayerAction
    {
        Attack = 0,
        Interact = 1,
        /// <summary>Choose weapon slot 1 (again — reload; hold — drop the weapon).</summary>
        Slot1 = 2,
        /// <summary>Choose weapon slot 2 (again — reload; hold — drop the weapon).</summary>
        Slot2 = 3,
        OpenMap = 4,

        /// <summary>Reload the ranged weapon in hands before its magazine is empty.</summary>
        Reload = 5,

        /// <summary>The throwable: press — aim (hold and release — throw; a quick press — aim until a tap or click).</summary>
        Throw = 6,

        /// <summary>A vehicle's throttle, held (stage 9a; W, RT, the HUD's Gas button). The wheel is the stick's x.</summary>
        Gas = 7,

        /// <summary>A vehicle's brake, at a stop — reverse, held (stage 9a; S, LT, the HUD's Brake button).</summary>
        Brake = 8,
    }

    /// <summary>
    /// Gameplay input abstraction (ТЗ §64). Implemented by the application's input service; gameplay never reads
    /// devices directly. The camera does not rotate, so screen up is always grid North.
    /// Buttons are polled from level ticks (<see cref="WasPressed"/>), so presses made while the level is paused
    /// are never applied later.
    /// </summary>
    public interface IPlayerInput
    {
        /// <summary>Desired movement: x = East, y = North, magnitude 0..1.</summary>
        Vector2 Move { get; }

        /// <summary>Pointer position in screen pixels when <see cref="LookIsPointer"/>, otherwise a stick direction.</summary>
        Vector2 Look { get; }

        bool LookIsPointer { get; }

        /// <summary>
        /// The on-screen aim stick of the two-stick scheme (a phone): its deflection, x = East, y = North, magnitude
        /// 0..1, zero inside its dead zone and when not touched. Fires while deflected (decision 2026-10-19); a real
        /// gamepad's right stick is <see cref="Look"/>, never this.
        /// </summary>
        Vector2 AimStick { get; }

        /// <summary>True while the attack control is held (automatic weapons).</summary>
        bool AttackHeld { get; }

        /// <summary>True during the frame the button was pressed.</summary>
        bool WasPressed(PlayerAction action);

        /// <summary>True while the button is held.</summary>
        bool IsHeld(PlayerAction action);

        /// <summary>
        /// A touch that began this frame on the game itself (not on the on-screen controls or any UI): its screen
        /// position. False without one.
        /// </summary>
        bool TryGetGroundTap(out Vector2 screenPosition);
    }
}
