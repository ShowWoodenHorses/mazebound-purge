using UnityEngine;

namespace Maze.Gameplay.Player
{
    /// <summary>Button actions of the input abstraction (ТЗ §64).</summary>
    public enum PlayerAction
    {
        Attack = 0,
        Interact = 1,
        SwitchMelee = 2,
        SwitchRanged = 3,
        OpenMap = 4,

        /// <summary>Reload the ranged weapon in hands before its magazine is empty.</summary>
        Reload = 5,
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

        /// <summary>True while the attack control is held (automatic weapons).</summary>
        bool AttackHeld { get; }

        /// <summary>True during the frame the button was pressed.</summary>
        bool WasPressed(PlayerAction action);
    }
}
