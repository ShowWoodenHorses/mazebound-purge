using System;

namespace Purge.Core.Horde
{
    /// <summary>
    /// Spatial hash of the horde: agents grouped by grid cell as intrusive linked lists (head per cell, next per
    /// agent). Rebuilt every tick in O(agents): only the cells touched last time are cleared. Neighbour searches,
    /// bullet hits and zone damage read a few cells instead of scanning every agent. Allocation-free.
    /// Covers every floor: cell index = (floor · Height + y) · Width + x, as in the grid.
    /// </summary>
    public sealed class CellBuckets
    {
        private readonly int[] _head;
        private readonly int[] _next;
        private readonly int[] _touched;
        private int _touchedCount;

        public CellBuckets(int width, int height, int capacity, int floors = 1)
        {
            if (width <= 0 || height <= 0 || floors <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), $"Invalid grid size {width}x{height}x{floors}.");

            Width = width;
            Height = height;
            Floors = floors;
            _head = new int[width * height * floors];
            _next = new int[capacity];
            _touched = new int[capacity];
            for (var i = 0; i < _head.Length; i++)
                _head[i] = -1;
        }

        public int Width { get; }
        public int Height { get; }
        public int Floors { get; }
        public int Capacity => _next.Length;

        public void Clear()
        {
            for (var i = 0; i < _touchedCount; i++)
                _head[_touched[i]] = -1;
            _touchedCount = 0;
        }

        /// <summary>Adds <paramref name="agent"/> to <paramref name="cellIndex"/>; outside the grid it is skipped.</summary>
        public void Insert(int agent, int cellIndex)
        {
            if ((uint)cellIndex >= (uint)_head.Length)
                return;

            if (_head[cellIndex] < 0)
                _touched[_touchedCount++] = cellIndex;
            _next[agent] = _head[cellIndex];
            _head[cellIndex] = agent;
        }

        /// <summary>First agent of the cell, -1 when empty or outside the grid.</summary>
        public int First(int x, int y, int floor = 0) =>
            x >= 0 && x < Width && y >= 0 && y < Height && floor >= 0 && floor < Floors
                ? _head[(floor * Height + y) * Width + x]
                : -1;

        public int First(int cellIndex) => (uint)cellIndex < (uint)_head.Length ? _head[cellIndex] : -1;

        /// <summary>The agent after <paramref name="agent"/> in its cell, -1 at the end.</summary>
        public int Next(int agent) => _next[agent];
    }
}
