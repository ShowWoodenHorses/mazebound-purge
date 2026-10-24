using System;
using Purge.Core.Grid;

namespace Purge.Gameplay.Grid
{
    /// <summary>
    /// Level objects standing in cells (stage 7в: barrels, chests) that block movement on top of the cell flags — one
    /// byte per cell of every floor, a mask, not an object per cell. <see cref="LevelPassability"/> reads it; the horde
    /// refreshes its walkability and flow on <see cref="Changed"/> (a barrel blew up).
    /// </summary>
    public sealed class CellBlockers
    {
        private readonly LevelGrid _grid;
        private readonly byte[] _count;

        public CellBlockers(LevelGrid grid)
        {
            _grid = grid;
            _count = new byte[grid.CellCount];
        }

        /// <summary>Grows on every change.</summary>
        public int Version { get; private set; }

        /// <summary>A cell got or lost its blocker.</summary>
        public event Action<GridPosition> Changed;

        public bool BlocksMovement(GridPosition cell) =>
            _grid.IsInside(cell) && _count[_grid.ToIndex(cell)] > 0;

        public void Add(GridPosition cell)
        {
            if (!_grid.IsInside(cell)) return;
            var index = _grid.ToIndex(cell);
            if (_count[index] == byte.MaxValue) return;
            _count[index]++;
            Version++;
            Changed?.Invoke(cell);
        }

        public void Remove(GridPosition cell)
        {
            if (!_grid.IsInside(cell)) return;
            var index = _grid.ToIndex(cell);
            if (_count[index] == 0) return;
            _count[index]--;
            Version++;
            Changed?.Invoke(cell);
        }
    }
}
