using System;
using Purge.Core.Definitions;
using UnityEngine;

namespace Purge.Gameplay.Player
{
    /// <summary>The player's HP (runtime state, ТЗ §86). Starts full; damage comes with combat, healing with medkits.</summary>
    public sealed class PlayerHealth
    {
        public PlayerHealth(PlayerDefinition definition)
        {
            Max = definition.MaxHealth;
            Current = Max;
        }

        public int Max { get; private set; }
        public int Current { get; private set; }
        public bool IsFull => Current >= Max;
        public bool IsDead => Current <= 0;

        /// <summary>(current, max) after every change.</summary>
        public event Action<int, int> Changed;

        /// <summary>(damage, where the blow came from) after a hit with a direction (the HUD's damage arc).</summary>
        public event Action<int, Vector2> Hurt;

        public void HealToFull() => Set(Max);

        /// <summary>A new maximum (the run's cards); the current HP stays, cut to it when above.</summary>
        public void SetMax(int max)
        {
            max = Math.Max(1, max);
            if (max == Max) return;
            Max = max;
            if (Current > Max) Current = Max;
            Changed?.Invoke(Current, Max);
        }

        /// <summary>Restores <paramref name="amount"/> (0 or less — to full).</summary>
        public void Heal(int amount) => Set(amount <= 0 ? Max : Math.Min(Max, Current + amount));

        public void Damage(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, null);
            Set(Math.Max(0, Current - amount));
        }

        /// <summary>Damage of a blow from <paramref name="source"/> (grid units): <see cref="Hurt"/> shows where it came from.</summary>
        public void Damage(int amount, Vector2 source)
        {
            if (amount <= 0 || IsDead) return;
            Damage(amount);
            Hurt?.Invoke(amount, source);
        }

        private void Set(int value)
        {
            if (value == Current) return;
            Current = value;
            Changed?.Invoke(Current, Max);
        }
    }
}
