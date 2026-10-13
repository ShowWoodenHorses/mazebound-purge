using UnityEngine;

namespace Maze.Gameplay.Combat
{
    /// <summary>Directions the player's attacks (and the facing after stopping) snap to.</summary>
    public enum AimMode
    {
        Free = 0,
        Eight = 1,
        Four = 2,
    }

    /// <summary>Player aiming preferences (a player setting, read live).</summary>
    public interface IAimSettings
    {
        AimMode AimMode { get; }

        /// <summary>Attacks turn to the nearest visible zombie near the wanted direction.</summary>
        bool AimAssist { get; }
    }

    public static class Aim
    {
        /// <summary>Free aim, no assist (immutable): tests and tools.</summary>
        public static readonly IAimSettings FreeNoAssist = new FixedAimSettings(AimMode.Free, false);

        /// <summary>Unit direction snapped to the nearest allowed one (N/E/S/W, plus diagonals for Eight).</summary>
        public static Vector2 Snap(Vector2 direction, AimMode mode)
        {
            if (direction.sqrMagnitude <= 0f)
                return direction;
            if (mode == AimMode.Free)
                return direction.normalized;

            var step = mode == AimMode.Four ? 90f : 45f;
            var angle = Mathf.Round(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg / step) * step * Mathf.Deg2Rad;
            var x = Mathf.Cos(angle);
            var y = Mathf.Sin(angle);
            // Exact axes (cos 90° is not exactly 0).
            if (Mathf.Abs(x) < 1e-5f) x = 0f;
            if (Mathf.Abs(y) < 1e-5f) y = 0f;
            return new Vector2(x, y).normalized;
        }
    }

    /// <summary>Aim settings that never change (tests, tools).</summary>
    public sealed class FixedAimSettings : IAimSettings
    {
        public FixedAimSettings(AimMode aimMode, bool aimAssist)
        {
            AimMode = aimMode;
            AimAssist = aimAssist;
        }

        public AimMode AimMode { get; }
        public bool AimAssist { get; }
    }
}
