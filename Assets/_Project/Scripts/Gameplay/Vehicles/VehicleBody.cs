using System.Collections.Generic;
using Purge.Core.Grid;
using Purge.Core.Vehicles;
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
        public static Vector2 Forward(float heading) => VehicleFootprint.Forward(heading);

        /// <summary>Unit direction of the right side for the front <paramref name="forward"/>.</summary>
        public static Vector2 Right(Vector2 forward) => VehicleFootprint.Right(forward);

        /// <summary>The rectangle overlaps the cell's square (separating axes: the grid's two and the body's two).</summary>
        public static bool OverlapsCell(Vector2 center, Vector2 forward, float halfLength, float halfWidth, int x, int y) =>
            OverlapsSquare(center, forward, halfLength, halfWidth, new Vector2(x, y), 0.5f);

        /// <summary>The rectangle overlaps the axis-aligned square of half size <paramref name="half"/> at <paramref name="square"/>.</summary>
        public static bool OverlapsSquare(Vector2 center, Vector2 forward, float halfLength, float halfWidth, Vector2 square,
            float half) =>
            VehicleFootprint.OverlapsSquare(center, forward, halfLength, halfWidth, square, half);

        /// <summary>Two rectangles overlap (separating axes: the two of each).</summary>
        public static bool OverlapsBody(Vector2 centerA, Vector2 forwardA, float halfLengthA, float halfWidthA,
            Vector2 centerB, Vector2 forwardB, float halfLengthB, float halfWidthB)
        {
            var d = centerB - centerA;
            var rightA = Right(forwardA);
            var rightB = Right(forwardB);
            return !Separates(forwardA, d, halfLengthA, forwardB, rightB, halfLengthB, halfWidthB) &&
                   !Separates(rightA, d, halfWidthA, forwardB, rightB, halfLengthB, halfWidthB) &&
                   !Separates(forwardB, d, halfLengthB, forwardA, rightA, halfLengthA, halfWidthA) &&
                   !Separates(rightB, d, halfWidthB, forwardA, rightA, halfLengthA, halfWidthA);
        }

        private static bool Separates(Vector2 axis, Vector2 d, float own, Vector2 forward, Vector2 right, float halfLength,
            float halfWidth) =>
            Mathf.Abs(Vector2.Dot(d, axis)) >=
            own + halfLength * Mathf.Abs(Vector2.Dot(forward, axis)) + halfWidth * Mathf.Abs(Vector2.Dot(right, axis));

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

        /// <summary>Cells the body stands on (<see cref="VehicleFootprint.Cells(Vector2, float, float, float, int, float, List{GridPosition})"/>).</summary>
        public static void Cells(Vector2 center, float heading, float halfLength, float halfWidth, int floor, float margin,
            List<GridPosition> result) =>
            VehicleFootprint.Cells(center, heading, halfLength, halfWidth, floor, margin, result);

        /// <summary>Distance from <paramref name="point"/> to the rectangle (0 inside).</summary>
        public static float Distance(Vector2 point, Vector2 center, float heading, float halfLength, float halfWidth)
        {
            var forward = Forward(heading);
            var d = point - center;
            var along = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, forward)) - halfLength);
            var side = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, Right(forward))) - halfWidth);
            return Mathf.Sqrt(along * along + side * side);
        }

        /// <summary>
        /// The step that puts a circle (<paramref name="radius"/>) at <paramref name="point"/> just out of the rectangle
        /// (stage 9b: monsters out of a driven vehicle): along the nearest normal; false when they do not overlap.
        /// </summary>
        public static bool PushOut(Vector2 point, float radius, Vector2 center, float heading, float halfLength, float halfWidth,
            out Vector2 step)
        {
            step = Vector2.zero;
            var forward = Forward(heading);
            var right = Right(forward);
            var d = point - center;
            var a = Vector2.Dot(d, forward);
            var s = Vector2.Dot(d, right);
            var outA = Mathf.Abs(a) - halfLength;
            var outS = Mathf.Abs(s) - halfWidth;
            if (outA > 0f && outS > 0f)
            {
                // By a corner: away from it.
                var corner = forward * (Mathf.Sign(a) * halfLength) + right * (Mathf.Sign(s) * halfWidth);
                var away = d - corner;
                var distance = away.magnitude;
                if (distance >= radius || distance < 1e-5f) return false;
                step = away / distance * (radius - distance);
                return true;
            }

            if (outA >= radius || outS >= radius) return false;
            // Beside a side (or inside): out through the nearer side.
            if (outA > outS) step = forward * (Mathf.Sign(a == 0f ? 1f : a) * (radius - outA));
            else step = right * (Mathf.Sign(s == 0f ? 1f : s) * (radius - outS));
            return true;
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
            out int minX, out int maxX, out int minY, out int maxY) =>
            VehicleFootprint.Bounds(center, forward, halfLength, halfWidth, out minX, out maxX, out minY, out maxY);
    }
}
