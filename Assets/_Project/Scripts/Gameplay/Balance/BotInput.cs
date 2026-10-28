using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Balance
{
    /// <summary>
    /// The player's input in a balance bot's run (stage 10a): what <see cref="BalanceBot"/> decided — movement, the
    /// aim stick (it fires past <see cref="Combat.PlayerCombat.StickFireFrom"/>, exactly where it points) and button
    /// presses. The bot ticks after the player's systems, so a press made in its tick is read by them in the next
    /// one and cleared by the bot's next tick. No mouse, no right stick, nothing held.
    /// </summary>
    public sealed class BotInput : IPlayerInput
    {
        private int _pressed;

        public Vector2 Move { get; set; }
        public Vector2 AimStick { get; set; }

        public Vector2 Look => Vector2.zero;
        public bool LookIsPointer => false;
        public bool AttackHeld => false;

        public void Press(PlayerAction action) => _pressed |= 1 << (int)action;

        /// <summary>The bot's tick begins: last tick's presses have been read.</summary>
        public void ClearPresses() => _pressed = 0;

        public bool WasPressed(PlayerAction action) => (_pressed & (1 << (int)action)) != 0;

        public bool IsHeld(PlayerAction action) => false;

        public bool TryGetGroundTap(out Vector2 screenPosition)
        {
            screenPosition = default;
            return false;
        }
    }
}
