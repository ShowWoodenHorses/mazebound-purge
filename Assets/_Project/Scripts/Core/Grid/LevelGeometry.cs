using System;
using UnityEngine;

namespace Maze.Core.Grid
{
    /// <summary>
    /// Serialized cell layout of a level. Cells are stored row by row: index = y * Width + x.
    /// </summary>
    [Serializable]
    public sealed class LevelGeometry
    {
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private CellType[] _cells;

        [Tooltip("Ground of the Floor cells, same indexing as the cells; empty = no special ground (levels made before it).")]
        [SerializeField] private CellSurface[] _surfaces;

        public LevelGeometry(int width, int height, CellType fill = CellType.Wall)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");

            _width = width;
            _height = height;
            _cells = new CellType[width * height];
            _surfaces = Array.Empty<CellSurface>();
            Fill(fill);
        }

        public int Width => _width;
        public int Height => _height;
        public int CellCount => _width * _height;

        /// <summary>False if the serialized data is corrupted (e.g. cell array does not match the size).</summary>
        public bool IsConsistent => _width > 0 && _height > 0 && _cells != null && _cells.Length == _width * _height;

        /// <summary>False if the surfaces do not match the size (empty — none set — is fine).</summary>
        public bool SurfacesConsistent => _surfaces == null || _surfaces.Length == 0 || _surfaces.Length == CellCount;

        /// <summary>True if any cell has a surface.</summary>
        public bool HasSurfaces
        {
            get
            {
                if (_surfaces == null) return false;
                foreach (var surface in _surfaces)
                    if (surface != CellSurface.None)
                        return true;
                return false;
            }
        }

        public bool IsInside(GridPosition position) =>
            position.X >= 0 && position.X < _width && position.Y >= 0 && position.Y < _height;

        public CellType GetCell(GridPosition position) => _cells[ToIndex(position)];

        public CellSurface GetSurface(GridPosition position)
        {
            var index = ToIndex(position);
            return _surfaces != null && index < _surfaces.Length ? _surfaces[index] : CellSurface.None;
        }

        public int ToIndex(GridPosition position)
        {
            if (!IsInside(position))
                throw new ArgumentOutOfRangeException(nameof(position), position, $"Outside of {_width}x{_height} grid.");

            return position.Y * _width + position.X;
        }

        public GridPosition ToPosition(int index)
        {
            if (index < 0 || index >= CellCount)
                throw new ArgumentOutOfRangeException(nameof(index), index, null);

            return new GridPosition(index % _width, index / _width);
        }

        internal void SetCell(GridPosition position, CellType type) => _cells[ToIndex(position)] = type;

        /// <summary>The array is created on the first surface (older levels have none).</summary>
        internal void SetSurface(GridPosition position, CellSurface surface)
        {
            var index = ToIndex(position);
            if (_surfaces == null || _surfaces.Length != CellCount)
            {
                if (surface == CellSurface.None) return;
                _surfaces = new CellSurface[CellCount];
            }

            _surfaces[index] = surface;
        }

        internal void Fill(CellType type)
        {
            for (var i = 0; i < _cells.Length; i++)
                _cells[i] = type;
        }

        internal CellType[] CopyCells() => (CellType[])_cells.Clone();

        /// <summary>A copy of the surfaces, always <see cref="CellCount"/> long.</summary>
        internal CellSurface[] CopySurfaces()
        {
            var copy = new CellSurface[CellCount];
            if (_surfaces != null && _surfaces.Length == copy.Length)
                Array.Copy(_surfaces, copy, copy.Length);
            return copy;
        }
    }
}
