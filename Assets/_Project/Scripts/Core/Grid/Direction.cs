using System;

namespace Maze.Core.Grid
{
    /// <summary>Cardinal direction on the grid. North is +Y (world +Z).</summary>
    public enum Direction : byte
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    public static class DirectionExtensions
    {
        public static readonly Direction[] All =
        {
            Direction.North, Direction.East, Direction.South, Direction.West,
        };

        public static GridPosition ToOffset(this Direction direction)
        {
            switch (direction)
            {
                case Direction.North: return new GridPosition(0, 1);
                case Direction.East: return new GridPosition(1, 0);
                case Direction.South: return new GridPosition(0, -1);
                case Direction.West: return new GridPosition(-1, 0);
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        public static Direction Opposite(this Direction direction) => (Direction)(((int)direction + 2) % 4);

        public static Direction RotateClockwise(this Direction direction) => (Direction)(((int)direction + 1) % 4);
    }
}
