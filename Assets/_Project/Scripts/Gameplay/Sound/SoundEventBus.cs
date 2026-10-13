using System;
using UnityEngine;

namespace Maze.Gameplay.Sound
{
    public enum SoundType
    {
        Step = 0,
        Pickup = 1,
        Door = 2,
        Melee = 3,
        Ranged = 4,
    }

    /// <summary>A gameplay sound (ТЗ §71): heard within <see cref="Radius"/> cells, not blocked by walls or doors.</summary>
    public readonly struct SoundEvent
    {
        public SoundEvent(SoundType type, Vector2 position, float radius)
        {
            Type = type;
            Position = position;
            Radius = radius;
        }

        public SoundType Type { get; }

        /// <summary>Grid units (x = East, y = North).</summary>
        public Vector2 Position { get; }

        public float Radius { get; }

        public bool IsHeardAt(Vector2 point) => (point - Position).sqrMagnitude <= Radius * Radius;
    }

    /// <summary>
    /// Gameplay sounds of the level. Zombie hearing and audio playback listen to <see cref="Emitted"/>; nothing is
    /// accumulated (no noise meter, ТЗ §71).
    /// </summary>
    public sealed class SoundEventBus
    {
        public event Action<SoundEvent> Emitted;

        public void Emit(SoundType type, Vector2 position, float radius)
        {
            if (radius <= 0f) return;
            Emitted?.Invoke(new SoundEvent(type, position, radius));
        }
    }
}
