using System;
using System.Collections.Generic;

namespace Maze.Core.Grid
{
    /// <summary>
    /// Runtime read-only cell layout built from <see cref="LevelGeometry"/>.
    /// Holds a private copy, so the level asset is never touched at runtime.
    /// Door open/closed state is not stored here: it is runtime state owned by DoorSystem.
    /// </summary>
    public sealed class LevelGrid
    {
        private readonly CellType[] _cells;
        private readonly CellSurface[] _surfaces;

        public LevelGrid(LevelGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (!geometry.IsConsistent) throw new ArgumentException("Level geometry is corrupted.", nameof(geometry));

            Width = geometry.Width;
            Height = geometry.Height;
            _cells = geometry.CopyCells();
            _surfaces = geometry.CopySurfaces();
        }

        public int Width { get; }
        public int Height { get; }
        public int CellCount => _cells.Length;

        public bool IsInside(GridPosition position) =>
            position.X >= 0 && position.X < Width && position.Y >= 0 && position.Y < Height;

        public CellType GetCell(GridPosition position) => _cells[ToIndex(position)];

        /// <summary>Ground of the cell; None outside the grid and for non-Floor cells.</summary>
        public CellSurface GetSurface(GridPosition position) =>
            IsInside(position) && _cells[ToIndex(position)] == CellType.Floor ? _surfaces[ToIndex(position)] : CellSurface.None;

        public bool IsSnowdrift(GridPosition position) => GetSurface(position) == CellSurface.Snowdrift;

        /// <summary>Returns Wall for positions outside the grid, which simplifies neighbour checks.</summary>
        public CellType GetCellOrWall(GridPosition position) => IsInside(position) ? GetCell(position) : CellType.Wall;

        public int ToIndex(GridPosition position)
        {
            if (!IsInside(position))
                throw new ArgumentOutOfRangeException(nameof(position), position, $"Outside of {Width}x{Height} grid.");

            return position.Y * Width + position.X;
        }

        public GridPosition ToPosition(int index)
        {
            if (index < 0 || index >= _cells.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, null);

            return new GridPosition(index % Width, index / Width);
        }

        /// <summary>Adds in-bounds cardinal neighbours to <paramref name="result"/> without allocating.</summary>
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
