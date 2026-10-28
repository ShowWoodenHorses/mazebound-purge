using System.Collections.Generic;
using Purge.Core.Grid;
using UnityEngine;

namespace Purge.Core.Vehicles
{
    /// <summary>
    /// The cells a vehicle's body takes (stage 9): a rectangle turned by its heading over the grid. Shared by the game
    /// (a parked body blocks them for the monsters) and the generator, the validator and the editor (where it fits).
    /// Positions are in grid units (cell centres at integers, x = East, y = North); a heading is in radians clockwise from
    /// North, so the front is (sin h, cos h).
    /// </summary>
    public static class VehicleFootprint
    {
        /// <summary>A parked body blocks the cells it overlaps deeper than this (a corner touching a cell leaves it free).</summary>
        public const float BlockMargin = 0.2f;

        /// <summary>Unit direction of the front at <paramref name="heading"/> radians.</summary>
        public static Vector2 Forward(float heading) => new Vector2(Mathf.Sin(heading), Mathf.Cos(heading));

        /// <summary>Unit direction of the right side for the front <paramref name="forward"/>.</summary>
        public static Vector2 Right(Vector2 forward) => new Vector2(forward.y, -forward.x);

        /// <summary>The rectangle overlaps the axis-aligned square of half size <paramref name="half"/> at <paramref name="square"/>.</summary>
        public static bool OverlapsSquare(Vector2 center, Vector2 forward, float halfLength, float halfWidth, Vector2 square,
            float half)
        {
            var right = Right(forward);
            var d = square - center;
            if (Mathf.Abs(d.x) >= half + halfLength * Mathf.Abs(forward.x) + halfWidth * Mathf.Abs(right.x)) return false;
            if (Mathf.Abs(d.y) >= half + halfLength * Mathf.Abs(forward.y) + halfWidth * Mathf.Abs(right.y)) return false;
            if (Mathf.Abs(Vector2.Dot(d, forward)) >= halfLength + half * (Mathf.Abs(forward.x) + Mathf.Abs(forward.y))) return false;
            return Mathf.Abs(Vector2.Dot(d, right)) < halfWidth + half * (Mathf.Abs(right.x) + Mathf.Abs(right.y));
        }

        /// <summary>
        /// Cells the body stands on: those it overlaps by more than <paramref name="margin"/> cells, so a corner just
        /// touching a cell leaves it free.
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
                if (OverlapsSquare(center, forward, halfLength, halfWidth, new Vector2(x, y), 0.5f))
                    result.Add(new GridPosition(x, y, floor));
        }

        /// <summary>The parked body's blocked cells (<see cref="BlockMargin"/>) of a vehicle of the level.</summary>
        public static void Cells(Level.VehicleData vehicle, List<GridPosition> result)
        {
            result.Clear();
            if (vehicle?.Definition == null) return;
            var hull = vehicle.Definition.Hull;
            Cells(new Vector2(vehicle.Position.X, vehicle.Position.Y), vehicle.Heading * Mathf.Deg2Rad, hull.Length * 0.5f,
                hull.Width * 0.5f, vehicle.Position.Floor, BlockMargin, result);
        }

        /// <summary>Cells the rectangle may touch (its bounding box).</summary>
        public static void Bounds(Vector2 center, Vector2 forward, float halfLength, float halfWidth,
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
