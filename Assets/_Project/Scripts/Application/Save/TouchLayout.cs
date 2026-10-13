using System;
using UnityEngine;

namespace Maze.Application.Save
{
    /// <summary>On-screen controls the player can move and resize (part of <see cref="TouchLayout"/>).</summary>
    public enum TouchElement
    {
        Stick,
        Attack,
        Use,
        Melee,
        Ranged,
    }

    /// <summary>
    /// Where one on-screen control is: either the built-in place (<see cref="Moved"/> = false) or a centre point
    /// normalized to the safe area (0..1, x = right, y = up) of the right-handed layout; the left-handed layout mirrors it.
    /// </summary>
    [Serializable]
    public struct TouchPlacement : IEquatable<TouchPlacement>
    {
        public const float MinScale = 0.6f;
        public const float MaxScale = 1.6f;

        public bool Moved;
        public Vector2 Position;

        /// <summary>Multiplies the control's size (on top of <see cref="ControlsSettings.Size"/>).</summary>
        public float Scale;

        public static TouchPlacement Default => new TouchPlacement { Scale = 1f };

        public TouchPlacement Clamped()
        {
            var result = this;
            // Missing in old saves: zero.
            result.Scale = Scale > 0f ? Mathf.Clamp(Scale, MinScale, MaxScale) : 1f;
            if (float.IsNaN(Position.x) || float.IsNaN(Position.y))
            {
                result.Moved = false;
                result.Position = Vector2.zero;
            }
            else
            {
                result.Position = new Vector2(Mathf.Clamp01(Position.x), Mathf.Clamp01(Position.y));
            }

            if (!result.Moved) result.Position = Vector2.zero;
            return result;
        }

        public bool Equals(TouchPlacement other) =>
            Moved == other.Moved && Mathf.Approximately(Scale, other.Scale) &&
            (!Moved || (Mathf.Approximately(Position.x, other.Position.x) && Mathf.Approximately(Position.y, other.Position.y)));

        public override bool Equals(object obj) => obj is TouchPlacement other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Moved, Scale, Moved ? Position : Vector2.zero);
    }

    /// <summary>
    /// The player's arrangement of the on-screen controls (ТЗ §64), edited by dragging them in the settings.
    /// One field per <see cref="TouchElement"/> (JsonUtility-friendly).
    /// </summary>
    [Serializable]
    public struct TouchLayout : IEquatable<TouchLayout>
    {
        public TouchPlacement Stick;
        public TouchPlacement Attack;
        public TouchPlacement Use;
        public TouchPlacement Melee;
        public TouchPlacement Ranged;

        public const int Count = 5;

        public static TouchLayout Default => new TouchLayout
        {
            Stick = TouchPlacement.Default,
            Attack = TouchPlacement.Default,
            Use = TouchPlacement.Default,
            Melee = TouchPlacement.Default,
            Ranged = TouchPlacement.Default,
        };

        public TouchPlacement this[TouchElement element]
        {
            get => element switch
            {
                TouchElement.Stick => Stick,
                TouchElement.Attack => Attack,
                TouchElement.Use => Use,
                TouchElement.Melee => Melee,
                TouchElement.Ranged => Ranged,
                _ => throw new ArgumentOutOfRangeException(nameof(element)),
            };
            set
            {
                switch (element)
                {
                    case TouchElement.Stick: Stick = value; break;
                    case TouchElement.Attack: Attack = value; break;
                    case TouchElement.Use: Use = value; break;
                    case TouchElement.Melee: Melee = value; break;
                    case TouchElement.Ranged: Ranged = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(element));
                }
            }
        }

        public TouchLayout Clamped() => new TouchLayout
        {
            Stick = Stick.Clamped(),
            Attack = Attack.Clamped(),
            Use = Use.Clamped(),
            Melee = Melee.Clamped(),
            Ranged = Ranged.Clamped(),
        };

        public bool Equals(TouchLayout other) =>
            Stick.Equals(other.Stick) && Attack.Equals(other.Attack) && Use.Equals(other.Use) &&
            Melee.Equals(other.Melee) && Ranged.Equals(other.Ranged);

        public override bool Equals(object obj) => obj is TouchLayout other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Stick, Attack, Use, Melee, Ranged);
    }
}
