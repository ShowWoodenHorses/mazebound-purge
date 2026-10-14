using System;
using Purge.Core.Grid;

namespace Purge.Core.Navigation
{
    /// <summary>
    /// Shared path field towards one target cell for the whole horde (instead of A* per monster): Dijkstra over the
    /// grid in 8 directions (orthogonal step 2, diagonal 3 ≈ 2·1.41), a diagonal only when both orthogonal cells
    /// beside it are passable (no corner cutting). Every reached cell stores its distance and the neighbour to step
    /// to. Rebuilt on events (the target changed cell, a door changed), never per frame. Allocation-free after
    /// construction. Covers the floor of the target (<see cref="Floor"/>); indices are within that floor
    /// (y · Width + x).
    /// </summary>
    public sealed class FlowField
    {
        public const ushort Unreachable = ushort.MaxValue;
        public const int OrthogonalCost = 2;
        public const int DiagonalCost = 3;

        private static readonly int[] OffsetX = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] OffsetY = { 1, 0, -1, 0, 1, -1, -1, 1 };

        private readonly ushort[] _distance;
        private readonly int[] _next;

        // Bucket queue: costs are small integers, so a cyclic array of buckets replaces a heap.
        // Entries live in a pool: a cell may be queued again when reached cheaper (the old entry goes stale).
        private readonly int[] _bucketHead;
        private readonly int[] _entryCell;
        private readonly int[] _entryNext;
        private readonly bool[] _passable;
        private int _entryCount;

        public FlowField(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), $"Invalid grid size {width}x{height}.");

            Width = width;
            Height = height;
            var count = width * height;
            _distance = new ushort[count];
            _next = new int[count];
            // Each cell is queued at most once per incoming direction.
            _entryCell = new int[count * 8 + 1];
            _entryNext = new int[count * 8 + 1];
            _passable = new bool[count];
            _bucketHead = new int[DiagonalCost + 1];
            for (var i = 0; i < count; i++)
            {
                _distance[i] = Unreachable;
                _next[i] = -1;
            }
        }

        public int Width { get; }
        public int Height { get; }
        public GridPosition Target { get; private set; }
        public bool HasResult { get; private set; }

        /// <summary>Floor of the last target: the field leads walkers of that floor only.</summary>
        public int Floor => Target.Floor;

        /// <summary>Incremented by every <see cref="Build{TPass}"/>.</summary>
        public int Version { get; private set; }

        public bool IsInside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        /// <summary>Path cost to the target in half-steps (orthogonal step = 2), or <see cref="Unreachable"/>.</summary>
        public ushort DistanceAt(int index) => _distance[index];

        /// <summary>Index of the neighbour to step to from <paramref name="index"/>; -1 at the target or when unreachable.</summary>
        public int NextAt(int index) => _next[index];

        public void Build<TPass>(GridPosition target, in TPass passability) where TPass : struct, IGridPassability
        {
            var count = Width * Height;
            for (var i = 0; i < count; i++)
            {
                _distance[i] = Unreachable;
                _next[i] = -1;
                _passable[i] = passability.IsPassable(new GridPosition(i % Width, i / Width, target.Floor));
            }

            Target = target;
            HasResult = true;
            Version++;
            if (!IsInside(target.X, target.Y))
                return;

            for (var b = 0; b < _bucketHead.Length; b++)
                _bucketHead[b] = -1;
            _entryCount = 0;

            var start = target.Y * Width + target.X;
            _distance[start] = 0;
            Push(start, 0);
            var pending = 1;
            var current = 0;

            while (pending > 0)
            {
                var bucket = current % _bucketHead.Length;
                var entry = _bucketHead[bucket];
                if (entry < 0)
                {
                    current++;
                    continue;
                }

                _bucketHead[bucket] = _entryNext[entry];
                var index = _entryCell[entry];
                pending--;
                if (_distance[index] != current)
                    continue; // stale entry: the cell was reached cheaper later

                var x = index % Width;
                var y = index / Width;
                for (var d = 0; d < 8; d++)
                {
                    var nx = x + OffsetX[d];
                    var ny = y + OffsetY[d];
                    if (!IsInside(nx, ny))
                        continue;

                    var neighbour = ny * Width + nx;
                    if (!_passable[neighbour])
                        continue;

                    var diagonal = d >= 4;
                    if (diagonal && (!Passable(x + OffsetX[d], y) || !Passable(x, y + OffsetY[d])))
                        continue;

                    var cost = current + (diagonal ? DiagonalCost : OrthogonalCost);
                    if (cost >= Unreachable || cost >= _distance[neighbour])
                        continue;

                    _distance[neighbour] = (ushort)cost;
                    _next[neighbour] = index;
                    Push(neighbour, cost);
                    pending++;
                }
            }
        }

        private bool Passable(int x, int y) => IsInside(x, y) && _passable[y * Width + x];

        private void Push(int index, int cost)
        {
            var bucket = cost % _bucketHead.Length;
            var entry = _entryCount++;
            _entryCell[entry] = index;
            _entryNext[entry] = _bucketHead[bucket];
            _bucketHead[bucket] = entry;
        }
    }
}
