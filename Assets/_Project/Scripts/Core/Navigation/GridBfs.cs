using System;
using System.Collections.Generic;
using Maze.Core.Grid;

namespace Maze.Core.Navigation
{
    /// <summary>
    /// Breadth-first search over the grid (4 directions) from one cell: the path length to every reachable cell at once,
    /// and the parents to walk any of those paths back. Buffers are allocated once per grid size; a run allocates
    /// nothing. The start cell is always reached, whatever <typeparamref name="TPassability"/> says about it.
    /// </summary>
    public sealed class GridBfs
    {
        private static readonly int[] StepX = { 0, 1, 0, -1 };
        private static readonly int[] StepY = { 1, 0, -1, 0 };

        private readonly int _width;
        private readonly int _height;
        private readonly int[] _distance;
        private readonly int[] _parent;
        private readonly int[] _queue;

        public GridBfs(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "The grid is empty.");
            _width = width;
            _height = height;
            _distance = new int[width * height];
            _parent = new int[width * height];
            _queue = new int[width * height];
            Array.Fill(_distance, -1);
        }

        /// <summary>The cell the last run started from.</summary>
        public GridPosition Start { get; private set; }

        public void Run<TPassability>(GridPosition start, TPassability passability) where TPassability : IGridPassability
        {
            Array.Fill(_distance, -1);
            Start = start;
            if (!IsInside(start)) return;

            var head = 0;
            var tail = 0;
            var startIndex = start.Y * _width + start.X;
            _distance[startIndex] = 0;
            _parent[startIndex] = -1;
            _queue[tail++] = startIndex;

            while (head < tail)
            {
                var index = _queue[head++];
                var x = index % _width;
                var y = index / _width;
                var next = _distance[index] + 1;
                for (var d = 0; d < 4; d++)
                {
                    var nx = x + StepX[d];
                    var ny = y + StepY[d];
                    if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) continue;
                    var neighbour = ny * _width + nx;
                    if (_distance[neighbour] >= 0 || !passability.IsPassable(new GridPosition(nx, ny))) continue;
                    _distance[neighbour] = next;
                    _parent[neighbour] = index;
                    _queue[tail++] = neighbour;
                }
            }
        }

        /// <summary>Steps from the start of the last run to <paramref name="cell"/>; −1 when it was not reached.</summary>
        public int DistanceTo(GridPosition cell) => IsInside(cell) ? _distance[cell.Y * _width + cell.X] : -1;

        public bool IsReachable(GridPosition cell) => DistanceTo(cell) >= 0;

        /// <summary>The shortest path of the last run, start and <paramref name="goal"/> included; false when unreachable.</summary>
        public bool TryGetPath(GridPosition goal, List<GridPosition> path)
        {
            path.Clear();
            var length = DistanceTo(goal);
            if (length < 0) return false;

            for (var i = 0; i <= length; i++) path.Add(default);
            var index = goal.Y * _width + goal.X;
            for (var i = length; i >= 0; i--)
            {
                path[i] = new GridPosition(index % _width, index / _width);
                index = _parent[index];
            }

            return true;
        }

        private bool IsInside(GridPosition cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < _width && cell.Y < _height;
    }
}
