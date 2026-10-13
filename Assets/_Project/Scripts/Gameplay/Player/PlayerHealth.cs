using System;
using Maze.Core.Definitions;

namespace Maze.Gameplay.Player
{
    /// <summary>The player's HP (runtime state, ТЗ §86). Starts full; damage comes with combat, healing with medkits.</summary>
    public sealed class PlayerHealth
    {
        public PlayerHealth(PlayerDefinition definition)
        {
            Max = definition.MaxHealth;
            Current = Max;
        }

        public int Max { get; }
        public int Current { get; private set; }
        public bool IsFull => Current >= Max;
        public bool IsDead => Current <= 0;

        /// <summary>(current, max) after every change.</summary>
        public event Action<int, int> Changed;

        public void HealToFull() => Set(Max);

        public void Damage(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, null);
            Set(Math.Max(0, Current - amount));
        }

        private void Set(int value)
        {
            if (value == Current) return;
            Current = value;
            Changed?.Invoke(Current, Max);
        }
    }
}
