using System;
using UnityEngine;

namespace Purge.Core.Grid
{
    /// <summary>
    /// Logical cell coordinate. One cell is 1x1 Unity unit; cell (x, y) of floor f is centered at world
    /// (x, f · <see cref="FloorHeight"/>, y). Floors are separate layers of the same grid (ТЗ: «Этажи»): they never
    /// interact, so neighbours, distances and lines stay on the floor of the cell they start from.
    /// </summary>
    [Serializable]
    public struct GridPosition : IEquatable<GridPosition>
    {
        /// <summary>World height between floors, metres (display only; floors do not interact).</summary>
        public const float FloorHeight = 3f;

        [SerializeField] private int _x;
        [SerializeField] private int _y;

        [Tooltip("Floor of the level (0 = ground; levels made before floors have 0).")]
        [SerializeField] private int _floor;

        public GridPosition(int x, int y, int floor = 0)
        {
            _x = x;
            _y = y;
            _floor = floor;
        }

        public int X => _x;
        public int Y => _y;
        public int Floor => _floor;

        /// <summary>The same x, y on another floor.</summary>
        public GridPosition OnFloor(int floor) => new GridPosition(_x, _y, floor);

        public GridPosition Neighbour(Direction direction) => this + direction.ToOffset();

        /// <summary>Distance on the floor (x, y only).</summary>
        public int ManhattanDistance(GridPosition other) => Math.Abs(_x - other._x) + Math.Abs(_y - other._y);

        public Vector3 ToWorld() => new Vector3(_x, _floor * FloorHeight, _y);

        public static GridPosition FromWorld(Vector3 world) =>
            new GridPosition(Mathf.FloorToInt(world.x + 0.5f), Mathf.FloorToInt(world.z + 0.5f),
                Mathf.FloorToInt(world.y / FloorHeight + 0.5f));

        /// <summary>Adds the coordinates (offsets have floor 0, so a neighbour stays on the floor).</summary>
        public static GridPosition operator +(GridPosition a, GridPosition b) =>
            new GridPosition(a._x + b._x, a._y + b._y, a._floor + b._floor);

        public static GridPosition operator -(GridPosition a, GridPosition b) =>
            new GridPosition(a._x - b._x, a._y - b._y, a._floor - b._floor);

        public static bool operator ==(GridPosition a, GridPosition b) => a._x == b._x && a._y == b._y && a._floor == b._floor;
        public static bool operator !=(GridPosition a, GridPosition b) => !(a == b);

        public bool Equals(GridPosition other) => this == other;
        public override bool Equals(object obj) => obj is GridPosition other && this == other;
        public override int GetHashCode() => ((_x * 397) ^ _y) * 397 ^ _floor;
        public override string ToString() => _floor == 0 ? $"({_x}, {_y})" : $"({_x}, {_y}, floor {_floor})";
    }
}
