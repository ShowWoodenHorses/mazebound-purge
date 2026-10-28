using System;
using Purge.Core.Grid;

namespace Purge.Gameplay.Grid
{
    /// <summary>
    /// Level objects standing in cells (stage 7в: barrels, chests) that block movement on top of the cell flags — one
    /// byte per cell of every floor, a mask, not an object per cell. <see cref="LevelPassability"/> reads it; the horde
    /// refreshes its walkability and flow on <see cref="Changed"/> (a barrel blew up).
    /// <para>A body (a parked vehicle) blocks its cells for the monsters and their flow, while the player collides with
    /// its exact shape (<see cref="Player.IPlayerBodies"/>): <c>BlocksMovement(cell, bodies: false)</c> skips it.</para>
    /// </summary>
    public sealed class CellBlockers
    {
        private readonly LevelGrid _grid;
        private readonly byte[] _count;
        private readonly byte[] _bodies;

        public CellBlockers(LevelGrid grid)
        {
            _grid = grid;
            _count = new byte[grid.CellCount];
            _bodies = new byte[grid.CellCount];
        }

        /// <summary>Grows on every change.</summary>
        public int Version { get; private set; }

        /// <summary>A cell got or lost its blocker.</summary>
        public event Action<GridPosition> Changed;

        /// <summary>Something stands in the cell; with <paramref name="bodies"/> false bodies do not count.</summary>
        public bool BlocksMovement(GridPosition cell, bool bodies = true)
        {
            if (!_grid.IsInside(cell)) return false;
            var index = _grid.ToIndex(cell);
            return bodies ? _count[index] > 0 : _count[index] > _bodies[index];
        }

        /// <summary>A blocker in the cell; <paramref name="body"/> — of a body with its own shape (a parked vehicle).</summary>
        public void Add(GridPosition cell, bool body = false)
        {
            if (!_grid.IsInside(cell)) return;
            var index = _grid.ToIndex(cell);
            if (_count[index] == byte.MaxValue) return;
            _count[index]++;
            if (body) _bodies[index]++;
            Version++;
            Changed?.Invoke(cell);
        }

        public void Remove(GridPosition cell, bool body = false)
        {
            if (!_grid.IsInside(cell)) return;
            var index = _grid.ToIndex(cell);
            if (_count[index] == 0) return;
            _count[index]--;
            if (body && _bodies[index] > 0) _bodies[index]--;
            Version++;
            Changed?.Invoke(cell);
        }
    }
}
