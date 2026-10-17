using System;
using UnityEngine;

namespace Purge.Gameplay.Sound
{
    public enum SoundType
    {
        Step = 0,
        Pickup = 1,
        Door = 2,
        Melee = 3,
        Ranged = 4,

        /// <summary>A blast (grenade, barrel).</summary>
        Explosion = 5,

        /// <summary>Something thrown (a grenade leaving the hand).</summary>
        Throw = 6,

        /// <summary>A caller's call (stage 5b): monsters that hear it chase the player.</summary>
        Call = 7,
    }

    /// <summary>
    /// A gameplay sound (ТЗ §71): <see cref="Radius"/> cells. The horde hears it along the passages (walls stop it, a closed
    /// door muffles — <c>HordePerception</c>); Maze zombies (<c>ZombieSystem</c>) — by straight distance.
    /// </summary>
    public readonly struct SoundEvent
    {
        public SoundEvent(SoundType type, Vector2 position, float radius, int floor = 0)
        {
            Type = type;
            Position = position;
            Radius = radius;
            Floor = floor;
        }

        public SoundType Type { get; }

        /// <summary>Grid units (x = East, y = North).</summary>
        public Vector2 Position { get; }

        public float Radius { get; }

        /// <summary>Floor it sounds on (not heard on others).</summary>
        public int Floor { get; }

        public bool IsHeardAt(Vector2 point) => (point - Position).sqrMagnitude <= Radius * Radius;
    }

    /// <summary>
    /// Gameplay sounds of the level. Monster hearing, noise rings and audio playback listen to <see cref="Emitted"/>;
    /// nothing is accumulated (no noise meter, ТЗ §71).
    /// </summary>
    public sealed class SoundEventBus
    {
        public event Action<SoundEvent> Emitted;

        public void Emit(SoundType type, Vector2 position, float radius, int floor = 0)
        {
            if (radius <= 0f) return;
            Emitted?.Invoke(new SoundEvent(type, position, radius, floor));
        }
    }
}
