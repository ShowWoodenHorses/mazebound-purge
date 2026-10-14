using Purge.Core.Grid;
using Purge.Gameplay.Doors;

namespace Purge.Gameplay.Grid
{
    /// <summary>
    /// Walkability of cells (ТЗ §52): a cell whose flags do not block movement (floor, a walkable obstacle), a door only
    /// while open; walls, void, blocking obstacles and everything outside the grid are not. Occupancy (player, zombies)
    /// is a separate question (<see cref="OccupancyMap"/>).
    /// </summary>
    public sealed class LevelPassability
    {
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;

        public LevelPassability(LevelGrid grid, DoorSystem doors)
        {
            _grid = grid;
            _doors = doors;
        }

        public bool IsWalkable(GridPosition cell)
        {
            if (_grid.Has(cell, CellFlags.BlocksMovement)) return false; // outside the grid too
            return _grid.GetCell(cell) != CellType.Door || _doors.IsOpenAt(cell);
        }

        /// <summary>Deep snow in the cell (walkers there move slower).</summary>
        public bool IsSnowdrift(GridPosition cell) => _grid.IsSnowdrift(cell);

        /// <summary>Share of the speed kept in the cell because of its obstacle (1 = none); surfaces are per walker.</summary>
        public float ObstacleSpeedFactor(GridPosition cell) => _grid.ObstacleSpeedFactor(cell);
    }
}
