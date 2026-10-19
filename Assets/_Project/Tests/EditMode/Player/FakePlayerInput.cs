using System.Collections.Generic;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Tests.EditMode.Player
{
    /// <summary>Scripted input: set <see cref="Move"/>, or <see cref="Press"/> a button for the next tick.</summary>
    public sealed class FakePlayerInput : IPlayerInput
    {
        private readonly HashSet<PlayerAction> _pressed = new HashSet<PlayerAction>();
        private readonly HashSet<PlayerAction> _held = new HashSet<PlayerAction>();

        public Vector2 Move { get; set; }
        public Vector2 Look { get; set; }
        public bool LookIsPointer { get; set; }
        public Vector2 AimStick { get; set; }
        public bool AttackHeld { get; set; }

        public bool WasPressed(PlayerAction action) => _pressed.Contains(action);

        public bool IsHeld(PlayerAction action) => action == PlayerAction.Attack ? AttackHeld : _held.Contains(action);

        public void Press(PlayerAction action) => _pressed.Add(action);

        /// <summary>A touch on the game this "frame" (<see cref="TryGetGroundTap"/>).</summary>
        public Vector2? Tap { get; set; }

        public bool TryGetGroundTap(out Vector2 screenPosition)
        {
            screenPosition = Tap ?? default;
            return Tap.HasValue;
        }

        /// <summary>Holds (true) or releases a button until changed.</summary>
        public void Hold(PlayerAction action, bool held)
        {
            if (held) _held.Add(action);
            else _held.Remove(action);
        }

        /// <summary>Ends the "frame": pressed buttons are released.</summary>
        public void EndFrame()
        {
            _pressed.Clear();
            Tap = null;
        }
    }
}
