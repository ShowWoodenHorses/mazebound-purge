using Maze.Core.Grid;

namespace Maze.Core.Navigation
{
    /// <summary>
    /// Answers "can a walker enter this cell?". Implemented by structs and passed as a generic argument,
    /// so pathfinding neither boxes nor allocates.
    /// </summary>
    public interface IGridPassability
    {
        bool IsPassable(GridPosition position);
    }

    /// <summary>Passability backed by a per-cell mask (row-major, same indexing as the grid).</summary>
    public readonly struct CellMaskPassability : IGridPassability
    {
        private readonly bool[] _passable;
        private readonly int _width;
        private readonly int _height;

        public CellMaskPassability(int width, int height, bool[] passable)
        {
            _width = width;
            _height = height;
            _passable = passable;
        }

        public bool IsPassable(GridPosition position) =>
            position.X >= 0 && position.X < _width && position.Y >= 0 && position.Y < _height &&
            _passable[position.Y * _width + position.X];
    }
}
