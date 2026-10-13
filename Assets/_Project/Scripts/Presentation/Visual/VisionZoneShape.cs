using Maze.Core.Grid;
using Maze.Core.Visibility;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Outline of what a zombie can see (display only, allocation-free): a fan of rays from its position cut where the
    /// rays enter an opaque cell (<see cref="GridRaycast"/> — walls, closed doors, as gameplay's line of sight). A cone
    /// of <c>angle</c> degrees around the facing, or a full circle at 360°. Points go counter-clockwise (x = East,
    /// y = North); for a cone the outline is the origin followed by the points, for a circle the points alone.
    /// </summary>
    public static class VisionZoneShape
    {
        /// <summary>Rays of a zone: one per <paramref name="rayStep"/> degrees (a cone has one more, both edges).</summary>
        public static int RayCount(float angle, float rayStep)
        {
            var step = Mathf.Max(rayStep, 0.5f);
            return IsCircle(angle)
                ? Mathf.Max(8, Mathf.CeilToInt(360f / step))
                : Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(angle, 1f) / step)) + 1;
        }

        public static bool IsCircle(float angle) => angle >= 359.5f;

        /// <summary>
        /// Fills <paramref name="points"/> (at least <see cref="RayCount"/> long) with the ray ends; returns their number.
        /// </summary>
        public static int Cast<T>(Vector2 origin, Vector2 facing, float angle, float range, float rayStep, in T opacity,
            Vector2[] points) where T : IGridOpacity
        {
            var count = RayCount(angle, rayStep);
            var circle = IsCircle(angle);
            var facingAngle = facing.sqrMagnitude > 1e-8f ? Mathf.Atan2(facing.y, facing.x) : Mathf.PI * 0.5f;
            var start = circle ? facingAngle : facingAngle - Mathf.Max(angle, 1f) * 0.5f * Mathf.Deg2Rad;
            var span = circle ? Mathf.PI * 2f : Mathf.Max(angle, 1f) * Mathf.Deg2Rad;
            var divisions = circle ? count : count - 1;

            for (var i = 0; i < count; i++)
            {
                var a = start + span * i / divisions;
                var direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var end = origin + direction * range;
                GridRaycast.Cast(origin, end, opacity, out var t, out GridPosition _);
                points[i] = origin + direction * (range * t);
            }

            return count;
        }
    }
}
