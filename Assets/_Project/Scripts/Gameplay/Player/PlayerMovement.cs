using Maze.Core.Grid;
using UnityEngine;

namespace Maze.Gameplay.Player
{
    /// <summary>What blocks the player.</summary>
    public interface IPlayerBlockers
    {
        /// <summary>Wall, closed door, outside the grid: the player's footprint may not overlap such a cell.</summary>
        bool IsWalkable(GridPosition cell);

        /// <summary>Occupancy (ТЗ §51): the player's centre may not enter a cell with zombies.</summary>
        bool CanEnter(GridPosition cell);
    }

    /// <summary>
    /// Smooth player movement on the grid without physics (ТЗ §65, §78). Positions are in grid units
    /// (x = East, y = North; cell centres at integers). The footprint is a square of half size h that collides with
    /// non-walkable cells; movement is resolved per axis, so the player slides along walls. When the player pushes
    /// straight into a wall next to a side passage, a corner assist nudges them toward the passage's lane.
    /// Long moves are split into small steps, so nothing is tunnelled through.
    /// </summary>
    public static class PlayerMovement
    {
        public const float MaxSubstep = 0.2f;
        private const float Skin = 0.001f;

        public static Vector2 Move(Vector2 position, Vector2 delta, float halfSize, float cornerAssist, IPlayerBlockers blockers)
        {
            var distance = delta.magnitude;
            if (distance <= 0f)
                return position;

            var steps = Mathf.Max(1, Mathf.CeilToInt(distance / MaxSubstep));
            var step = delta / steps;
            for (var i = 0; i < steps; i++)
                position = Step(position, step, halfSize, cornerAssist, blockers);
            return position;
        }

        public static GridPosition CellOf(Vector2 position) =>
            new GridPosition(Mathf.FloorToInt(position.x + 0.5f), Mathf.FloorToInt(position.y + 0.5f));

        /// <summary>True when the footprint at <paramref name="center"/> overlaps a non-walkable cell.</summary>
        public static bool Overlaps(Vector2 center, float halfSize, IPlayerBlockers blockers)
        {
            var minX = Mathf.FloorToInt(center.x - halfSize + Skin + 0.5f);
            var maxX = Mathf.FloorToInt(center.x + halfSize - Skin + 0.5f);
            var minY = Mathf.FloorToInt(center.y - halfSize + Skin + 0.5f);
            var maxY = Mathf.FloorToInt(center.y + halfSize - Skin + 0.5f);
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
                if (!blockers.IsWalkable(new GridPosition(x, y)))
                    return true;

            return false;
        }

        private static Vector2 Step(Vector2 position, Vector2 step, float halfSize, float cornerAssist, IPlayerBlockers blockers)
        {
            var movedX = MoveAxis(ref position, step.x, 0, halfSize, blockers);
            var movedY = MoveAxis(ref position, step.y, 1, halfSize, blockers);

            if (cornerAssist > 0f)
            {
                var absX = Mathf.Abs(step.x);
                var absY = Mathf.Abs(step.y);
                if (!movedX && absX > absY)
                    Assist(ref position, 0, Mathf.Sign(step.x), absX, halfSize, cornerAssist, blockers);
                else if (!movedY && absY > absX)
                    Assist(ref position, 1, Mathf.Sign(step.y), absY, halfSize, cornerAssist, blockers);
            }

            return position;
        }

        /// <summary>Moves along one axis; returns false when blocked (position is then clamped against the obstacle).</summary>
        private static bool MoveAxis(ref Vector2 position, float delta, int axis, float halfSize, IPlayerBlockers blockers)
        {
            if (delta == 0f)
                return true;

            var candidate = position;
            candidate[axis] += delta;
            var overlaps = Overlaps(candidate, halfSize, blockers);
            if (!overlaps && blockers.CanEnter(CellOf(candidate)))
            {
                position = candidate;
                return true;
            }

            float limit;
            if (overlaps)
            {
                // Stop right before the blocking row/column.
                var edge = candidate[axis] + Mathf.Sign(delta) * halfSize;
                var blocking = Mathf.Floor(edge + 0.5f);
                limit = delta > 0f ? blocking - 0.5f - halfSize - Skin : blocking + 0.5f + halfSize + Skin;
            }
            else
            {
                // Occupied cell ahead: the centre stays inside its current cell.
                var current = Mathf.Floor(position[axis] + 0.5f);
                limit = delta > 0f ? current + 0.5f - Skin : current - 0.5f + Skin;
            }

            if ((limit - position[axis]) * delta > 0f)
            {
                var clamped = position;
                clamped[axis] = limit;
                if (!Overlaps(clamped, halfSize, blockers) && blockers.CanEnter(CellOf(clamped)))
                    position = clamped;
            }

            return false;
        }

        /// <summary>
        /// Blocked on <paramref name="axis"/>: if the neighbouring cell straight ahead in the nearest lane is walkable
        /// and the lane centre is within <paramref name="maxOffset"/>, slide toward it by up to <paramref name="amount"/>.
        /// </summary>
        private static void Assist(ref Vector2 position, int axis, float direction, float amount, float halfSize,
            float maxOffset, IPlayerBlockers blockers)
        {
            var other = 1 - axis;
            var lane = Mathf.Round(position[other]);
            var offset = lane - position[other];
            if (Mathf.Abs(offset) < 1e-4f || Mathf.Abs(offset) > maxOffset)
                return;

            var ahead = Mathf.Round(position[axis]) + direction;
            var target = axis == 0
                ? new GridPosition((int)ahead, (int)lane)
                : new GridPosition((int)lane, (int)ahead);
            if (!blockers.IsWalkable(target))
                return;

            MoveAxis(ref position, Mathf.Sign(offset) * Mathf.Min(Mathf.Abs(offset), amount), other, halfSize, blockers);
        }
    }
}
