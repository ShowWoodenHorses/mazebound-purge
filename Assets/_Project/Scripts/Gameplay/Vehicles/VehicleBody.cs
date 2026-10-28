using System.Collections.Generic;
using Purge.Core.Grid;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Vehicles
{
    /// <summary>
    /// The body of a vehicle on the grid (stage 9a): a rectangle turned by its heading, without physics. Positions are in
    /// grid units (cell centres at integers, x = East, y = North); a heading is in radians clockwise from North, so the
    /// front is (sin h, cos h) — the same turn as a model's yaw.
    /// </summary>
    public static class VehicleBody
    {
        /// <summary>Kept off the cells it touches, so a body resting against a wall does not overlap it.</summary>
        public const float Skin = 0.01f;

        /// <summary>Unit direction of the front at <paramref name="heading"/> radians.</summary>
        public static Vector2 Forward(float heading) => new Vector2(Mathf.Sin(heading), Mathf.Cos(heading));

        /// <summary>Unit direction of the right side for the front <paramref name="forward"/>.</summary>
        public static Vector2 Right(Vector2 forward) => new Vector2(forward.y, -forward.x);

        /// <summary>The rectangle overlaps the cell's square (separating axes: the grid's two and the body's two).</summary>
        public static bool OverlapsCell(Vector2 center, Vector2 forward, float halfLength, float halfWidth, int x, int y)
        {
            var right = Right(forward);
            var d = new Vector2(x - center.x, y - center.y);
            if (Mathf.Abs(d.x) >= 0.5f + halfLength * Mathf.Abs(forward.x) + halfWidth * Mathf.Abs(right.x)) return false;
            if (Mathf.Abs(d.y) >= 0.5f + halfLength * Mathf.Abs(forward.y) + halfWidth * Mathf.Abs(right.y)) return false;
            if (Mathf.Abs(Vector2.Dot(d, forward)) >= halfLength + 0.5f * (Mathf.Abs(forward.x) + Mathf.Abs(forward.y))) return false;
            return Mathf.Abs(Vector2.Dot(d, right)) < halfWidth + 0.5f * (Mathf.Abs(right.x) + Mathf.Abs(right.y));
        }

        /// <summary>The rectangle overlaps a cell the walker may not be in (walls, bushes, closed doors, parked vehicles).</summary>
        public static bool Overlaps(Vector2 center, float heading, float halfLength, float halfWidth, IPlayerBlockers blockers)
        {
            var forward = Forward(heading);
            halfLength -= Skin;
            halfWidth -= Skin;
            Bounds(center, forward, halfLength, halfWidth, out var minX, out var maxX, out var minY, out var maxY);
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
                if (!blockers.IsWalkable(new GridPosition(x, y, blockers.Floor)) &&
                    OverlapsCell(center, forward, halfLength, halfWidth, x, y))
                    return true;
            return false;
        }

        /// <summary>
        /// Cells the body stands on (a parked vehicle blocks them): those it overlaps by more than
        /// <paramref name="margin"/> cells, so a corner just touching a cell leaves it free.
        /// </summary>
        public static void Cells(Vector2 center, float heading, float halfLength, float halfWidth, int floor, float margin,
            List<GridPosition> result)
        {
            result.Clear();
            var forward = Forward(heading);
            halfLength = Mathf.Max(0.05f, halfLength - margin);
            halfWidth = Mathf.Max(0.05f, halfWidth - margin);
            Bounds(center, forward, halfLength, halfWidth, out var minX, out var maxX, out var minY, out var maxY);
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
                if (OverlapsCell(center, forward, halfLength, halfWidth, x, y))
                    result.Add(new GridPosition(x, y, floor));
        }

        /// <summary>Distance from <paramref name="point"/> to the rectangle (0 inside).</summary>
        public static float Distance(Vector2 point, Vector2 center, float heading, float halfLength, float halfWidth)
        {
            var forward = Forward(heading);
            var d = point - center;
            var along = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, forward)) - halfLength);
            var side = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, Right(forward))) - halfWidth);
            return Mathf.Sqrt(along * along + side * side);
        }

        /// <summary>First fraction 0..1 of the segment inside the rectangle (0 when it starts inside); false when it misses.</summary>
        public static bool Segment(Vector2 from, Vector2 to, Vector2 center, float heading, float halfLength, float halfWidth,
            out float t)
        {
            var forward = Forward(heading);
            var right = Right(forward);
            var start = from - center;
            var delta = to - from;
            // In the body's frame the rectangle is axis-aligned: clip the segment by its two slabs.
            var enter = 0f;
            var leave = 1f;
            t = 0f;
            if (!Slab(Vector2.Dot(start, forward), Vector2.Dot(delta, forward), halfLength, ref enter, ref leave)) return false;
            if (!Slab(Vector2.Dot(start, right), Vector2.Dot(delta, right), halfWidth, ref enter, ref leave)) return false;
            t = enter;
            return true;
        }

        private static bool Slab(float start, float delta, float half, ref float enter, ref float leave)
        {
            if (Mathf.Abs(delta) < 1e-8f) return start > -half && start < half;
            var a = (-half - start) / delta;
            var b = (half - start) / delta;
            if (a > b) (a, b) = (b, a);
            enter = Mathf.Max(enter, a);
            leave = Mathf.Min(leave, b);
            return enter <= leave;
        }

        private static void Bounds(Vector2 center, Vector2 forward, float halfLength, float halfWidth,
            out int minX, out int maxX, out int minY, out int maxY)
        {
            var right = Right(forward);
            var extentX = halfLength * Mathf.Abs(forward.x) + halfWidth * Mathf.Abs(right.x);
            var extentY = halfLength * Mathf.Abs(forward.y) + halfWidth * Mathf.Abs(right.y);
            minX = Mathf.FloorToInt(center.x - extentX + 0.5f);
            maxX = Mathf.FloorToInt(center.x + extentX + 0.5f);
            minY = Mathf.FloorToInt(center.y - extentY + 0.5f);
            maxY = Mathf.FloorToInt(center.y + extentY + 0.5f);
        }
    }
}
