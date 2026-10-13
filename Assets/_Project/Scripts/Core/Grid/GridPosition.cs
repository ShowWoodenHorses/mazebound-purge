using System;
using UnityEngine;

namespace Maze.Core.Grid
{
    /// <summary>
    /// Logical cell coordinate. One cell is 1x1 Unity unit; cell (x, y) is centered at world (x, 0, y).
    /// </summary>
    [Serializable]
    public struct GridPosition : IEquatable<GridPosition>
    {
        [SerializeField] private int _x;
        [SerializeField] private int _y;

        public GridPosition(int x, int y)
        {
            _x = x;
            _y = y;
        }

        public int X => _x;
        public int Y => _y;

        public GridPosition Neighbour(Direction direction) => this + direction.ToOffset();

        public int ManhattanDistance(GridPosition other) => Math.Abs(_x - other._x) + Math.Abs(_y - other._y);

        public Vector3 ToWorld() => new Vector3(_x, 0f, _y);

        public static GridPosition FromWorld(Vector3 world) =>
            new GridPosition(Mathf.FloorToInt(world.x + 0.5f), Mathf.FloorToInt(world.z + 0.5f));

        public static GridPosition operator +(GridPosition a, GridPosition b) => new GridPosition(a._x + b._x, a._y + b._y);
        public static GridPosition operator -(GridPosition a, GridPosition b) => new GridPosition(a._x - b._x, a._y - b._y);
        public static bool operator ==(GridPosition a, GridPosition b) => a._x == b._x && a._y == b._y;
        public static bool operator !=(GridPosition a, GridPosition b) => !(a == b);

        public bool Equals(GridPosition other) => this == other;
        public override bool Equals(object obj) => obj is GridPosition other && this == other;
        public override int GetHashCode() => (_x * 397) ^ _y;
        public override string ToString() => $"({_x}, {_y})";
    }
}
