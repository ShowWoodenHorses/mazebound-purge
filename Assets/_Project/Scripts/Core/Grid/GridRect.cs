using System;
using UnityEngine;

namespace Maze.Core.Grid
{
    /// <summary>Axis-aligned rectangle of cells. Min is inclusive, Max is exclusive.</summary>
    [Serializable]
    public struct GridRect : IEquatable<GridRect>
    {
        [SerializeField] private int _x;
        [SerializeField] private int _y;
        [SerializeField] private int _width;
        [SerializeField] private int _height;

        public GridRect(int x, int y, int width, int height)
        {
            _x = x;
            _y = y;
            _width = width;
            _height = height;
        }

        public static GridRect FromCorners(GridPosition a, GridPosition b)
        {
            var minX = Math.Min(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            return new GridRect(minX, minY, Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
        }

        public int X => _x;
        public int Y => _y;
        public int Width => _width;
        public int Height => _height;
        public int XMax => _x + _width;
        public int YMax => _y + _height;
        public GridPosition Min => new GridPosition(_x, _y);
        public bool IsEmpty => _width <= 0 || _height <= 0;
        public int CellCount => IsEmpty ? 0 : _width * _height;

        public bool Contains(GridPosition position) =>
            position.X >= _x && position.X < XMax && position.Y >= _y && position.Y < YMax;

        public bool Overlaps(GridRect other) =>
            !IsEmpty && !other.IsEmpty &&
            _x < other.XMax && other._x < XMax &&
            _y < other.YMax && other._y < YMax;

        public static bool operator ==(GridRect a, GridRect b) =>
            a._x == b._x && a._y == b._y && a._width == b._width && a._height == b._height;

        public static bool operator !=(GridRect a, GridRect b) => !(a == b);

        public bool Equals(GridRect other) => this == other;
        public override bool Equals(object obj) => obj is GridRect other && this == other;
        public override int GetHashCode() => (((_x * 397) ^ _y) * 397 ^ _width) * 397 ^ _height;
        public override string ToString() => $"[{_x}, {_y}, {_width}x{_height}]";
    }
}
