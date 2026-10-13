using Maze.Core.Grid;
using Maze.Core.Level;
using UnityEngine;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Where the fixture of a light source (a torch) hangs: on the wall face of the light's cell, on the side the light
    /// is shifted to (otherwise the first wall around the cell, N → E → S → W), keeping the shift along the wall.
    /// A light without a wall next to it has no fixture. Fixture prefabs are authored with the mount plate at the
    /// origin, the wall behind it (−Z) and the torch pointing North (+Z). Pure lookup: runtime and preview use it.
    /// </summary>
    public static class LightFixtures
    {
        /// <summary>Height of the mount above the floor, m (walls are 1.5 m high).</summary>
        public const float MountHeight = 0.95f;

        /// <summary>Saved fixture variant, otherwise the Light set's default; None without a set.</summary>
        public static VisualChoice Resolve(LevelData level, LightSourceData light)
        {
            if (!light.Visual.IsEmpty)
                return light.Visual;

            var theme = level.VisualTheme;
            var set = theme != null ? theme.GetSet(VisualKind.Light) : null;
            return set != null && !string.IsNullOrEmpty(set.DefaultVariantId) ? new VisualChoice(set.DefaultVariantId) : VisualChoice.None;
        }

        /// <summary>
        /// Pose relative to the centre of the light's cell (x = East, y = up, z = North) and turn in degrees clockwise
        /// from above. False when no wall borders the cell.
        /// </summary>
        public static bool TryGetPose(LevelGeometry geometry, LightSourceData light, out Vector3 offset, out float yaw)
        {
            offset = Vector3.zero;
            yaw = 0f;
            if (!TryGetWallSide(geometry, light, out var side))
                return false;

            var step = side.ToOffset();
            var along = step.X != 0
                ? new Vector2(0f, Mathf.Clamp(light.Offset.y, -0.4f, 0.4f))
                : new Vector2(Mathf.Clamp(light.Offset.x, -0.4f, 0.4f), 0f);
            offset = new Vector3(step.X * 0.5f + along.x, MountHeight, step.Y * 0.5f + along.y);
            yaw = 90f * (int)side.Opposite();
            return true;
        }

        /// <summary>The wall the fixture hangs on: towards the light's shift, otherwise the first wall around.</summary>
        public static bool TryGetWallSide(LevelGeometry geometry, LightSourceData light, out Direction side)
        {
            var shift = light.Offset;
            if (Mathf.Abs(shift.x) > 1e-3f || Mathf.Abs(shift.y) > 1e-3f)
            {
                side = Mathf.Abs(shift.x) > Mathf.Abs(shift.y)
                    ? (shift.x > 0f ? Direction.East : Direction.West)
                    : (shift.y > 0f ? Direction.North : Direction.South);
                if (IsWall(geometry, light.Cell + side.ToOffset()))
                    return true;
            }

            foreach (var direction in DirectionExtensions.All)
                if (IsWall(geometry, light.Cell + direction.ToOffset()))
                {
                    side = direction;
                    return true;
                }

            side = default;
            return false;
        }

        // Outside the grid counts as wall.
        private static bool IsWall(LevelGeometry geometry, GridPosition cell) =>
            !geometry.IsInside(cell) || geometry.GetCell(cell) == CellType.Wall;
    }
}
