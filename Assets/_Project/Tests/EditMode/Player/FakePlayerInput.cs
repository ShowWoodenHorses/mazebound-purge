using System.Collections.Generic;
using Maze.Gameplay.Player;
using UnityEngine;

namespace Maze.Tests.EditMode.Player
{
    /// <summary>Scripted input: set <see cref="Move"/>, or <see cref="Press"/> a button for the next tick.</summary>
    public sealed class FakePlayerInput : IPlayerInput
    {
        private readonly HashSet<PlayerAction> _pressed = new HashSet<PlayerAction>();

        public Vector2 Move { get; set; }
        public Vector2 Look => Vector2.zero;
        public bool LookIsPointer => false;
        public bool AttackHeld { get; set; }

        public bool WasPressed(PlayerAction action) => _pressed.Contains(action);

        public void Press(PlayerAction action) => _pressed.Add(action);

        /// <summary>Ends the "frame": pressed buttons are released.</summary>
        public void EndFrame() => _pressed.Clear();
    }
}
