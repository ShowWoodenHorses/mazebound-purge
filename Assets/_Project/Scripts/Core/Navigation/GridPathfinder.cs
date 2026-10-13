using System;
using System.Collections.Generic;
using Maze.Core.Grid;

namespace Maze.Core.Navigation
{
    /// <summary>
    /// 4-directional A* on the level grid (ТЗ §52). All buffers are allocated once per grid size and reused,
    /// so a search does not allocate. Never call it every frame: only when target, doors or AI task change.
    /// Not thread-safe: one instance per caller.
    /// </summary>
    public sealed class GridPathfinder
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int[] _gScore;
        private readonly int[] _cameFrom;
        private readonly int[] _openStamp;
        private readonly int[] _closedStamp;
        private readonly MinHeap _open;
        private int _stamp;

        public GridPathfinder(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            _width = width;
            _height = height;
            var count = width * height;
            _gScore = new int[count];
            _cameFrom = new int[count];
            _openStamp = new int[count];
            _closedStamp = new int[count];
            _open = new MinHeap(count * 4);
        }

        /// <summary>
        /// Finds a shortest path by steps. On success <paramref name="path"/> holds cells from start to goal inclusive.
        /// The start cell itself does not need to be passable (the walker already stands there).
        /// </summary>
        public bool TryFindPath<TPassability>(GridPosition start, GridPosition goal, TPassability passability,
            List<GridPosition> path) where TPassability : IGridPassability =>
            TryFindPath(start, goal, passability, new UniformCost(), path);

        /// <summary>
        /// Finds a cheapest path by <paramref name="cost"/> of the cells stepped into (at least 1 each, e.g. slow cells
        /// cost more). On success <paramref name="path"/> holds cells from start to goal inclusive.
        /// </summary>
        public bool TryFindPath<TPassability, TCost>(GridPosition start, GridPosition goal, TPassability passability,
            TCost cost, List<GridPosition> path) where TPassability : IGridPassability where TCost : IGridCost
        {
            path.Clear();
            if (!IsInside(start) || !IsInside(goal) || !passability.IsPassable(goal))
                return false;

            if (start == goal)
            {
                path.Add(start);
                return true;
            }

            NextStamp();
            _open.Clear();

            var startIndex = ToIndex(start);
            var goalIndex = ToIndex(goal);
            _gScore[startIndex] = 0;
            _cameFrom[startIndex] = -1;
            _openStamp[startIndex] = _stamp;
            _open.Push(startIndex, start.ManhattanDistance(goal), start.ManhattanDistance(goal));

            while (_open.TryPop(out var current))
            {
                if (_closedStamp[current] == _stamp)
                    continue;

                if (current == goalIndex)
                {
                    BuildPath(goalIndex, path);
                    return true;
                }

                _closedStamp[current] = _stamp;
                var position = ToPosition(current);

                foreach (var direction in DirectionExtensions.All)
                {
                    var neighbour = position.Neighbour(direction);
                    if (!IsInside(neighbour) || !passability.IsPassable(neighbour))
                        continue;

                    var index = ToIndex(neighbour);
                    if (_closedStamp[index] == _stamp)
                        continue;

                    var nextG = _gScore[current] + Math.Max(cost.StepCost(neighbour), 1);
                    if (_openStamp[index] == _stamp && _gScore[index] <= nextG)
                        continue;

                    _openStamp[index] = _stamp;
                    _gScore[index] = nextG;
                    _cameFrom[index] = current;
                    var h = neighbour.ManhattanDistance(goal);
                    _open.Push(index, nextG + h, h);
                }
            }

            return false;
        }

        private void BuildPath(int goalIndex, List<GridPosition> path)
        {
            for (var index = goalIndex; index >= 0; index = _cameFrom[index])
                path.Add(ToPosition(index));

            path.Reverse();
        }

        private void NextStamp()
        {
            if (++_stamp != int.MaxValue)
                return;

            Array.Clear(_openStamp, 0, _openStamp.Length);
            Array.Clear(_closedStamp, 0, _closedStamp.Length);
            _stamp = 1;
        }

        private bool IsInside(GridPosition p) => p.X >= 0 && p.X < _width && p.Y >= 0 && p.Y < _height;
        private int ToIndex(GridPosition p) => p.Y * _width + p.X;
        private GridPosition ToPosition(int index) => new GridPosition(index % _width, index / _width);

        /// <summary>Binary min-heap ordered by (f, h); duplicates allowed, stale entries skipped by the caller.</summary>
        private sealed class MinHeap
        {
            private int[] _items;
            private long[] _keys;
            private int _count;

            public MinHeap(int capacity)
            {
                _items = new int[capacity];
                _keys = new long[capacity];
            }

            public void Clear() => _count = 0;

            public void Push(int item, int f, int h)
            {
                if (_count == _items.Length)
                {
                    Array.Resize(ref _items, _count * 2);
                    Array.Resize(ref _keys, _count * 2);
                }

                var key = ((long)f << 32) | (uint)h;
                var i = _count++;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (_keys[parent] <= key)
                        break;

                    _items[i] = _items[parent];
                    _keys[i] = _keys[parent];
                    i = parent;
                }

                _items[i] = item;
                _keys[i] = key;
            }

            public bool TryPop(out int item)
            {
                if (_count == 0)
                {
                    item = -1;
                    return false;
                }

                item = _items[0];
                var lastItem = _items[--_count];
                var lastKey = _keys[_count];
                var i = 0;
                while (true)
                {
                    var child = i * 2 + 1;
                    if (child >= _count)
                        break;

                    if (child + 1 < _count && _keys[child + 1] < _keys[child])
                        child++;

                    if (_keys[child] >= lastKey)
                        break;

                    _items[i] = _items[child];
                    _keys[i] = _keys[child];
                    i = child;
                }

                _items[i] = lastItem;
                _keys[i] = lastKey;
                return true;
            }
        }
    }
}
