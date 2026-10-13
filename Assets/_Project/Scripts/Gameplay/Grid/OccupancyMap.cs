using System;
using Maze.Core.Grid;

namespace Maze.Gameplay.Grid
{
    /// <summary>
    /// Who stands where (ТЗ §51): at most one player per cell, 0..N zombies per cell, never the player and a zombie
    /// in the same cell. Pickups do not occupy cells. Pure bookkeeping, no allocations after construction.
    /// </summary>
    public sealed class OccupancyMap
    {
        private readonly LevelGrid _grid;
        private readonly int[] _zombies;

        public OccupancyMap(LevelGrid grid)
        {
            _grid = grid;
            _zombies = new int[grid.CellCount];
        }

        public bool HasPlayer { get; private set; }
        public GridPosition PlayerCell { get; private set; }

        public int ZombieCount(GridPosition cell) => _grid.IsInside(cell) ? _zombies[_grid.ToIndex(cell)] : 0;

        public bool CanPlayerEnter(GridPosition cell) => _grid.IsInside(cell) && _zombies[_grid.ToIndex(cell)] == 0;

        public bool CanZombieEnter(GridPosition cell) => _grid.IsInside(cell) && !(HasPlayer && PlayerCell == cell);

        public void SetPlayer(GridPosition cell)
        {
            if (!CanPlayerEnter(cell))
                throw new InvalidOperationException($"Player cannot occupy {cell}: it is outside the grid or has zombies.");

            PlayerCell = cell;
            HasPlayer = true;
        }

        public void ClearPlayer() => HasPlayer = false;

        public void AddZombie(GridPosition cell)
        {
            if (!CanZombieEnter(cell))
                throw new InvalidOperationException($"Zombie cannot occupy {cell}: it is outside the grid or has the player.");
            _zombies[_grid.ToIndex(cell)]++;
        }

        public void RemoveZombie(GridPosition cell)
        {
            var index = _grid.ToIndex(cell);
            if (_zombies[index] == 0)
                throw new InvalidOperationException($"No zombie to remove at {cell}.");
            _zombies[index]--;
        }

        public void MoveZombie(GridPosition from, GridPosition to)
        {
            if (from == to) return;
            if (!CanZombieEnter(to))
                throw new InvalidOperationException($"Zombie cannot move to {to}.");
            RemoveZombie(from);
            _zombies[_grid.ToIndex(to)]++;
        }
    }
}
