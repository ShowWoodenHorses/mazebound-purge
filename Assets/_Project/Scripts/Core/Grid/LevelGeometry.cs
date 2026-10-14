using System;
using System.Collections.Generic;
using Purge.Core.Definitions;
using UnityEngine;

namespace Purge.Core.Grid
{
    /// <summary>
    /// Serialized cell layout of a level: structure (<see cref="CellType"/>), surfaces and obstacles of every cell of
    /// every floor. Floors are layers of the same width and height (ТЗ: «Этажи»): cells are stored floor by floor, row
    /// by row — index = (floor · Height + y) · Width + x, so floor 0 keeps the indexing of single-floor levels.
    /// </summary>
    [Serializable]
    public sealed class LevelGeometry
    {
        [SerializeField] private int _width;
        [SerializeField] private int _height;

        [Tooltip("Number of floors; 0 = 1 (levels made before floors).")]
        [SerializeField] private int _floorCount = 1;

        [SerializeField] private CellType[] _cells;

        [Tooltip("Ground of the Floor cells, same indexing as the cells; empty = no special ground (levels made before it).")]
        [SerializeField] private CellSurface[] _surfaces;

        [Tooltip("Obstacle kinds used by the level; cells refer to them by number.")]
        [SerializeField] private List<ObstacleDefinition> _obstacleTypes = new List<ObstacleDefinition>();

        [Tooltip("Obstacle of every cell: 0 = none, n = Obstacle Types[n − 1]; empty = no obstacles.")]
        [SerializeField] private byte[] _obstacles;

        public LevelGeometry(int width, int height, CellType fill = CellType.Wall, int floors = 1)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
            if (floors <= 0) throw new ArgumentOutOfRangeException(nameof(floors), floors, "A level has at least one floor.");

            _width = width;
            _height = height;
            _floorCount = floors;
            _cells = new CellType[width * height * floors];
            _surfaces = Array.Empty<CellSurface>();
            _obstacles = Array.Empty<byte>();
            Fill(fill);
        }

        public int Width => _width;
        public int Height => _height;
        public int FloorCount => Math.Max(1, _floorCount);

        /// <summary>Cells of one floor (Width · Height).</summary>
        public int FloorSize => _width * _height;

        /// <summary>Cells of all floors.</summary>
        public int CellCount => FloorSize * FloorCount;

        /// <summary>False if the serialized data is corrupted (e.g. cell array does not match the size).</summary>
        public bool IsConsistent => _width > 0 && _height > 0 && _cells != null && _cells.Length == CellCount;

        /// <summary>False if the surfaces do not match the size (empty — none set — is fine).</summary>
        public bool SurfacesConsistent => _surfaces == null || _surfaces.Length == 0 || _surfaces.Length == CellCount;

        /// <summary>False if the obstacles do not match the size or refer to a missing kind (empty is fine).</summary>
        public bool ObstaclesConsistent
        {
            get
            {
                if (_obstacles == null || _obstacles.Length == 0) return true;
                if (_obstacles.Length != CellCount) return false;
                foreach (var number in _obstacles)
                    if (number > 0 && (number > ObstacleTypeCount || _obstacleTypes[number - 1] == null))
                        return false;
                return true;
            }
        }

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

        /// <summary>Obstacle kinds referred to by cells (may hold unused ones until the next change).</summary>
        public IReadOnlyList<ObstacleDefinition> ObstacleTypes => _obstacleTypes;

        private int ObstacleTypeCount => _obstacleTypes?.Count ?? 0;

        public bool IsInside(GridPosition position) =>
            position.X >= 0 && position.X < _width && position.Y >= 0 && position.Y < _height &&
            position.Floor >= 0 && position.Floor < FloorCount;

        public CellType GetCell(GridPosition position) => _cells[ToIndex(position)];

        public CellSurface GetSurface(GridPosition position)
        {
            var index = ToIndex(position);
            return _surfaces != null && index < _surfaces.Length ? _surfaces[index] : CellSurface.None;
        }

        /// <summary>
        /// Static flags of the cell (structure + surface + obstacle; doors count as open) — what <see cref="LevelGrid"/>
        /// bakes at runtime. Outside the grid — a wall's.
        /// </summary>
        public CellFlags GetFlags(GridPosition position)
        {
            if (!IsInside(position)) return CellFlags.Solid;
            var index = ToIndex(position);
            var type = _cells[index];
            if (!CellRules.CanHaveContent(type)) return CellRules.StructureFlags(type);
            var obstacle = ObstacleAt(index);
            return CellRules.StructureFlags(type) |
                   CellRules.SurfaceFlags(_surfaces != null && index < _surfaces.Length ? _surfaces[index] : CellSurface.None) |
                   (obstacle != null ? obstacle.Flags : CellFlags.None);
        }

        /// <summary>The obstacle standing in the cell, null when there is none.</summary>
        public ObstacleDefinition GetObstacle(GridPosition position) => ObstacleAt(ToIndex(position));

        internal ObstacleDefinition ObstacleAt(int index)
        {
            if (_obstacles == null || index >= _obstacles.Length) return null;
            var number = _obstacles[index];
            return number > 0 && number <= ObstacleTypeCount ? _obstacleTypes[number - 1] : null;
        }

        public int ToIndex(GridPosition position)
        {
            if (!IsInside(position))
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    $"Outside of {_width}x{_height} grid with {FloorCount} floor(s).");

            return (position.Floor * _height + position.Y) * _width + position.X;
        }

        public GridPosition ToPosition(int index)
        {
            if (index < 0 || index >= CellCount)
                throw new ArgumentOutOfRangeException(nameof(index), index, null);

            var inFloor = index % FloorSize;
            return new GridPosition(inFloor % _width, inFloor / _width, index / FloorSize);
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

        /// <summary>Puts <paramref name="obstacle"/> in the cell (null removes it); unused kinds are dropped.</summary>
        internal void SetObstacle(GridPosition position, ObstacleDefinition obstacle)
        {
            var index = ToIndex(position);
            _obstacleTypes ??= new List<ObstacleDefinition>();
            if (_obstacles == null || _obstacles.Length != CellCount)
            {
                if (obstacle == null) return;
                _obstacles = new byte[CellCount];
            }

            var number = 0;
            if (obstacle != null)
            {
                number = _obstacleTypes.IndexOf(obstacle) + 1;
                if (number == 0)
                {
                    if (_obstacleTypes.Count >= byte.MaxValue)
                        throw new InvalidOperationException($"A level holds at most {byte.MaxValue} obstacle kinds.");
                    _obstacleTypes.Add(obstacle);
                    number = _obstacleTypes.Count;
                }
            }

            _obstacles[index] = (byte)number;
            DropUnusedObstacleTypes();
        }

        /// <summary>Adds an empty top floor (all <paramref name="fill"/>); returns its number.</summary>
        internal int AddFloor(CellType fill = CellType.Void)
        {
            var old = CellCount;
            _floorCount = FloorCount + 1;
            Array.Resize(ref _cells, CellCount);
            for (var i = old; i < _cells.Length; i++)
                _cells[i] = fill;
            if (_surfaces != null && _surfaces.Length == old)
                Array.Resize(ref _surfaces, CellCount);
            if (_obstacles != null && _obstacles.Length == old)
                Array.Resize(ref _obstacles, CellCount);
            return FloorCount - 1;
        }

        /// <summary>Removes the top floor (only the top one: no floor numbers change). The last floor stays.</summary>
        internal void RemoveTopFloor()
        {
            if (FloorCount <= 1) throw new InvalidOperationException("A level has at least one floor.");

            _floorCount = FloorCount - 1;
            Array.Resize(ref _cells, CellCount);
            if (_surfaces != null && _surfaces.Length > CellCount) Array.Resize(ref _surfaces, CellCount);
            if (_obstacles != null && _obstacles.Length > CellCount) Array.Resize(ref _obstacles, CellCount);
            DropUnusedObstacleTypes();
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

        /// <summary>The obstacle of every cell (null = none), <see cref="CellCount"/> long.</summary>
        internal ObstacleDefinition[] CopyObstacles()
        {
            var copy = new ObstacleDefinition[CellCount];
            if (_obstacles != null && _obstacles.Length == copy.Length)
                for (var i = 0; i < copy.Length; i++)
                    copy[i] = ObstacleAt(i);
            return copy;
        }

        /// <summary>Renumbers the cells so that only kinds still standing somewhere stay in the list.</summary>
        private void DropUnusedObstacleTypes()
        {
            if (_obstacleTypes == null || _obstacleTypes.Count == 0) return;
            var used = new bool[_obstacleTypes.Count + 1];
            if (_obstacles != null)
                foreach (var number in _obstacles)
                    if (number < used.Length)
                        used[number] = true;

            var remap = new byte[used.Length];
            var kept = new List<ObstacleDefinition>();
            for (var n = 1; n < used.Length; n++)
                if (used[n])
                {
                    kept.Add(_obstacleTypes[n - 1]);
                    remap[n] = (byte)kept.Count;
                }

            if (kept.Count == _obstacleTypes.Count) return;
            for (var i = 0; i < _obstacles.Length; i++)
                _obstacles[i] = _obstacles[i] < remap.Length ? remap[_obstacles[i]] : (byte)0;
            _obstacleTypes = kept;
        }
    }
}
