using Maze.Core.Grid;
using Maze.Gameplay.Doors;

namespace Maze.Gameplay.Grid
{
    /// <summary>
    /// Static walkability of cells (ТЗ §52): Floor and open Door are walkable; Wall, closed Door and everything
    /// outside the grid are not. Occupancy (player, zombies) is a separate question (<see cref="OccupancyMap"/>).
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
            switch (_grid.GetCellOrWall(cell))
            {
                case CellType.Floor: return true;
                case CellType.Door: return _doors.IsOpenAt(cell);
                default: return false;
            }
        }

        /// <summary>Deep snow in the cell (walkers there move slower).</summary>
        public bool IsSnowdrift(GridPosition cell) => _grid.IsSnowdrift(cell);
    }
}
