using System;
using System.Collections.Generic;
using Purge.Core.Definitions;

namespace Purge.Core.Grid
{
    /// <summary>
    /// Runtime read-only cell layout built from <see cref="LevelGeometry"/>: structure, surfaces, obstacles and the
    /// baked <see cref="CellFlags"/> of every cell of every floor (same indexing as the geometry).
    /// Holds private copies, so the level asset is never touched at runtime.
    /// Door open/closed state is not stored here: it is runtime state owned by DoorSystem.
    /// </summary>
    public sealed class LevelGrid
    {
        private readonly CellType[] _cells;
        private readonly CellSurface[] _surfaces;
        private bool[] _temporaryIce;
        private readonly ObstacleDefinition[] _obstacles;
        private readonly CellFlags[] _flags;

        private readonly Dictionary<int, StairLink> _stairs = new Dictionary<int, StairLink>();

        public LevelGrid(LevelGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (!geometry.IsConsistent) throw new ArgumentException("Level geometry is corrupted.", nameof(geometry));

            Width = geometry.Width;
            Height = geometry.Height;
            FloorCount = geometry.FloorCount;
            _cells = geometry.CopyCells();
            _surfaces = geometry.CopySurfaces();
            _obstacles = geometry.CopyObstacles();
            _flags = new CellFlags[_cells.Length];
            for (var i = 0; i < _cells.Length; i++)
            {
                var type = _cells[i];
                if (!CellRules.CanHaveContent(type))
                {
                    _surfaces[i] = CellSurface.None;
                    _obstacles[i] = null;
                }

                _flags[i] = CellRules.StructureFlags(type) | CellRules.SurfaceFlags(_surfaces[i]) |
                            (_obstacles[i] != null ? _obstacles[i].Flags : CellFlags.None);
            }

            foreach (var stair in geometry.Stairs)
            {
                if (!stair.Fits(geometry)) continue;
                var lower = ToIndex(stair.LowerLanding);
                var upper = ToIndex(stair.UpperLanding);
                if (_stairs.ContainsKey(lower) || _stairs.ContainsKey(upper) ||
                    Has(stair.LowerLanding, CellFlags.BlocksMovement) || Has(stair.UpperLanding, CellFlags.BlocksMovement) ||
                    _stairs.ContainsKey(ToIndex(stair.First)) || _stairs.ContainsKey(ToIndex(stair.Second)) ||
                    _stairs.ContainsKey(ToIndex(stair.First.OnFloor(stair.UpperFloor))) ||
                    _stairs.ContainsKey(ToIndex(stair.Second.OnFloor(stair.UpperFloor)))) continue;
                _stairs.Add(lower, stair);
                _stairs.Add(upper, stair);
                ReserveFlight(stair.First, stair.UpperFloor);
                ReserveFlight(stair.Second, stair.UpperFloor);
            }
        }

        private void ReserveFlight(GridPosition lower, int upperFloor)
        {
            _flags[ToIndex(lower)] |= CellFlags.BlocksMovement;
            var upper = ToIndex(lower.OnFloor(upperFloor));
            _cells[upper] = CellType.Void;
            _flags[upper] = CellRules.StructureFlags(CellType.Void);
            _surfaces[upper] = CellSurface.None;
            _obstacles[upper] = null;
        }

        public bool HasStairs => _stairs.Count != 0;

        public bool TryGetStair(GridPosition entrance, out StairLink stair)
        {
            stair = default;
            return HasStairs && IsInside(entrance) && _stairs.TryGetValue(ToIndex(entrance), out stair);
        }

        public bool TryGetStairDestination(GridPosition cell, out GridPosition destination)
        {
            if (TryGetStair(cell, out var stair))
            {
                destination = stair.ExitFrom(cell);
                return true;
            }

            destination = default;
            return false;
        }

        public int Width { get; }
        public int Height { get; }
        public int FloorCount { get; }

        /// <summary>Cells of one floor (Width · Height).</summary>
        public int FloorSize => Width * Height;

        /// <summary>Cells of all floors.</summary>
        public int CellCount => _cells.Length;

        public bool IsInside(GridPosition position) =>
            position.X >= 0 && position.X < Width && position.Y >= 0 && position.Y < Height &&
            position.Floor >= 0 && position.Floor < FloorCount;

        public CellType GetCell(GridPosition position) => _cells[ToIndex(position)];

        /// <summary>Returns Wall for positions outside the grid, which simplifies neighbour checks.</summary>
        public CellType GetCellOrWall(GridPosition position) => IsInside(position) ? GetCell(position) : CellType.Wall;

        /// <summary>Flags of the cell; outside the grid — a wall's.</summary>
        public CellFlags GetFlags(GridPosition position) => IsInside(position) ? _flags[ToIndex(position)] : CellFlags.Solid;

        /// <summary>True when the cell has any of <paramref name="flags"/> (outside the grid has all blocking ones).</summary>
        public bool Has(GridPosition position, CellFlags flags) => (GetFlags(position) & flags) != 0;

        /// <summary>Ground of the cell; None outside the grid and for non-Floor cells.</summary>
        public CellSurface GetSurface(GridPosition position) => IsInside(position) ? _surfaces[ToIndex(position)] : CellSurface.None;

        public bool IsIce(GridPosition position) =>
            IsInside(position) && (_surfaces[ToIndex(position)] == CellSurface.Ice ||
                (_temporaryIce != null && _temporaryIce[ToIndex(position)]));

        public void SetTemporaryIce(GridPosition position, bool ice)
        {
            if (!IsInside(position)) return;
            if (ice) _temporaryIce ??= new bool[CellCount];
            if (_temporaryIce != null) _temporaryIce[ToIndex(position)] = ice;
        }

        public bool IsSnowdrift(GridPosition position) => GetSurface(position) == CellSurface.Snowdrift;

        /// <summary>The obstacle standing in the cell; null outside the grid and when there is none.</summary>
        public ObstacleDefinition GetObstacle(GridPosition position) => IsInside(position) ? _obstacles[ToIndex(position)] : null;

        /// <summary>
        /// Share of the speed a walker keeps in the cell because of its obstacle (1 = none). Surfaces are per walker
        /// (e.g. <see cref="PlayerDefinition.SnowdriftSpeed"/>) and multiply on top.
        /// </summary>
        public float ObstacleSpeedFactor(GridPosition position) => GetObstacle(position)?.SpeedFactor ?? 1f;

        public int ToIndex(GridPosition position)
        {
            if (!IsInside(position))
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    $"Outside of {Width}x{Height} grid with {FloorCount} floor(s).");

            return (position.Floor * Height + position.Y) * Width + position.X;
        }

        public GridPosition ToPosition(int index)
        {
            if (index < 0 || index >= _cells.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, null);

            var inFloor = index % FloorSize;
            return new GridPosition(inFloor % Width, inFloor / Width, index / FloorSize);
        }

        /// <summary>Adds in-bounds cardinal neighbours (same floor) to <paramref name="result"/> without allocating.</summary>
        public void GetNeighbours(GridPosition position, List<GridPosition> result)
        {
            result.Clear();
            foreach (var direction in DirectionExtensions.All)
            {
                var neighbour = position.Neighbour(direction);
                if (IsInside(neighbour))
                    result.Add(neighbour);
            }
        }
    }
}
