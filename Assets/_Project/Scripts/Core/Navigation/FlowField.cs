using System;
using Purge.Core.Grid;

namespace Purge.Core.Navigation
{
    /// <summary>
    /// Shared path field towards one target cell for the whole horde (instead of A* per monster): Dijkstra over the
    /// grid in 8 directions (orthogonal step 2, diagonal 3 ≈ 2·1.41), a diagonal only when both orthogonal cells
    /// beside it are passable (no corner cutting). Every reached cell stores its distance and the neighbour to step
    /// to. Rebuilt on events (the target changed cell, a door changed), never per frame. Allocation-free after
    /// construction. A level grid adds stairs as edges between floors. Integer indices refer to the target's floor;
    /// use grid positions to query other floors.
    /// Inside, the grid has a one-cell border that is never passable (stage 7г): the search needs no bounds checks
    /// and no division — a rebuild of 100×100 from a cached mask (<see cref="Build(GridPosition, bool[])"/>) is the
    /// hot path, on every step of the player.
    /// </summary>
    public sealed class FlowField
    {
        public const ushort Unreachable = ushort.MaxValue;
        public const int OrthogonalCost = 2;
        public const int DiagonalCost = 3;

        private readonly int _stride; // padded row: Width + 2
        private readonly int _paddedFloorSize;
        private readonly int _floors;
        private readonly int[] _stairs;
        private readonly ushort[] _distance; // padded
        private readonly int[] _next; // padded index of the next cell, -1
        private readonly int[] _offset = new int[8];
        private readonly int[] _sideA = new int[8];
        private readonly int[] _sideB = new int[8];

        // Bucket queue: costs are small integers, so a cyclic array of buckets replaces a heap.
        // Entries live in a pool: a cell may be queued again when reached cheaper (the old entry goes stale).
        private readonly int[] _bucketHead;
        private readonly int[] _entryCell;
        private readonly int[] _entryNext;
        private readonly bool[] _passable; // padded, the border false
        private int _entryCount;

        public FlowField(int width, int height) : this(width, height, null) { }

        public FlowField(LevelGrid grid) : this(grid.Width, grid.Height, grid) { }

        private FlowField(int width, int height, LevelGrid grid)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), $"Invalid grid size {width}x{height}.");

            Width = width;
            Height = height;
            _stride = width + 2;
            _paddedFloorSize = _stride * (height + 2);
            _floors = grid?.FloorCount ?? 1;
            var count = _paddedFloorSize * _floors;
            if (grid != null && grid.HasStairs)
            {
                _stairs = new int[count];
                Array.Fill(_stairs, -1);
                for (var i = 0; i < grid.CellCount; i++)
                {
                    var cell = grid.ToPosition(i);
                    if (grid.TryGetStairDestination(cell, out var landing))
                        _stairs[Padded(cell)] = Padded(landing);
                }
            }
            _distance = new ushort[count];
            _next = new int[count];
            // Each cell is queued at most once per incoming direction.
            _entryCell = new int[width * height * _floors * (_stairs == null ? 8 : 9) + 1];
            _entryNext = new int[_entryCell.Length];
            _passable = new bool[count];
            _bucketHead = new int[DiagonalCost + 1];
            Array.Fill(_distance, Unreachable);
            Array.Fill(_next, -1);

            int[] dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
            int[] dy = { 1, 0, -1, 0, 1, -1, -1, 1 };
            for (var d = 0; d < 8; d++)
            {
                _offset[d] = dy[d] * _stride + dx[d];
                _sideA[d] = dx[d];
                _sideB[d] = dy[d] * _stride;
            }
        }

        public int Width { get; }
        public int Height { get; }
        public GridPosition Target { get; private set; }
        public bool HasResult { get; private set; }

        /// <summary>Floor of the last target: the field leads walkers of that floor only.</summary>
        public int Floor => Target.Floor;

        /// <summary>Incremented by every build.</summary>
        public int Version { get; private set; }

        public bool IsInside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        /// <summary>Path cost to the target in half-steps (orthogonal step = 2), or <see cref="Unreachable"/>.</summary>
        public ushort DistanceAt(int index) => _distance[Padded(Target.Floor, index)];

        public ushort DistanceAt(GridPosition cell) =>
            IsInside(cell.X, cell.Y) && IsFloorInField(cell.Floor) ? _distance[Padded(cell)] : Unreachable;

        /// <summary>Index of the neighbour to step to from <paramref name="index"/>; -1 at the target or when unreachable.</summary>
        public int NextAt(int index)
        {
            var next = _next[Padded(Target.Floor, index)];
            if (next < 0 || StorageFloor(Target.Floor) != next / _paddedFloorSize) return -1;
            var local = next % _paddedFloorSize;
            return (local / _stride - 1) * Width + local % _stride - 1;
        }

        public bool TryNext(GridPosition cell, out GridPosition nextCell)
        {
            nextCell = default;
            if (!IsFloorInField(cell.Floor) || !IsInside(cell.X, cell.Y)) return false;
            var next = _next[Padded(cell)];
            if (next < 0) return false;
            var local = next % _paddedFloorSize;
            nextCell = new GridPosition(local % _stride - 1, local / _stride - 1,
                _floors == 1 ? cell.Floor : next / _paddedFloorSize);
            return true;
        }

        public void Build<TPass>(GridPosition target, in TPass passability) where TPass : struct, IGridPassability
        {
            for (var layer = 0; layer < _floors; layer++)
            {
                var floor = _floors == 1 ? target.Floor : layer;
                for (var y = 0; y < Height; y++)
                {
                    var row = layer * _paddedFloorSize + (y + 1) * _stride + 1;
                    for (var x = 0; x < Width; x++)
                        _passable[row + x] = passability.IsPassable(new GridPosition(x, y, floor));
                }
            }

            Search(target);
        }

        /// <summary>
        /// Builds from a cached walkability mask of the target's floor (y · Width + x; stage 7г): a block copy per row
        /// instead of a passability query per cell — the cheap path for rebuilds on every player step.
        /// </summary>
        public void Build(GridPosition target, bool[] floorMask)
        {
            var count = Width * Height * _floors;
            if (floorMask == null || floorMask.Length < count)
                throw new ArgumentException($"Expected a mask of {count} cells.", nameof(floorMask));
            for (var floor = 0; floor < _floors; floor++)
            for (var y = 0; y < Height; y++)
                Array.Copy(floorMask, (floor * Height + y) * Width, _passable,
                    floor * _paddedFloorSize + (y + 1) * _stride + 1, Width);
            Search(target);
        }

        private int StorageFloor(int floor) => _floors == 1 ? 0 : floor;

        private bool IsFloorInField(int floor) => _floors == 1 ? floor == Floor : (uint)floor < (uint)_floors;

        private int Padded(int floor, int index) =>
            StorageFloor(floor) * _paddedFloorSize + (index / Width + 1) * _stride + index % Width + 1;

        private int Padded(GridPosition cell) => Padded(cell.Floor, cell.Y * Width + cell.X);

        private void Search(GridPosition target)
        {
            Array.Fill(_distance, Unreachable);
            Array.Fill(_next, -1);

            Target = target;
            HasResult = true;
            Version++;
            if (!IsInside(target.X, target.Y) || !IsFloorInField(target.Floor))
                return;

            for (var b = 0; b < _bucketHead.Length; b++)
                _bucketHead[b] = -1;
            _entryCount = 0;

            var distance = _distance;
            var passable = _passable;
            var next = _next;
            var start = Padded(target);
            distance[start] = 0;
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
                if (distance[index] != current)
                    continue; // stale entry: the cell was reached cheaper later

                for (var d = 0; d < 8; d++)
                {
                    var neighbour = index + _offset[d];
                    if (!passable[neighbour])
                        continue; // the border is never passable: no bounds checks

                    int cost;
                    if (d < 4)
                        cost = current + OrthogonalCost;
                    else if (passable[index + _sideA[d]] && passable[index + _sideB[d]])
                        cost = current + DiagonalCost;
                    else
                        continue;

                    if (cost >= Unreachable || cost >= distance[neighbour])
                        continue;

                    distance[neighbour] = (ushort)cost;
                    next[neighbour] = index;
                    Push(neighbour, cost);
                    pending++;
                }

                if (_stairs == null) continue;
                var stair = _stairs[index];
                var stairCost = current + DiagonalCost;
                if (stair < 0 || !passable[stair] || stairCost >= distance[stair]) continue;
                distance[stair] = (ushort)stairCost;
                next[stair] = index;
                Push(stair, stairCost);
                pending++;
            }
        }

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
