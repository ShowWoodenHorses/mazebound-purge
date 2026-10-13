using System.Collections.Generic;
using Maze.Core.Grid;

namespace Maze.Core.Generation
{
    /// <summary>Output of <see cref="MazeGenerator"/>: logical geometry plus initial player starts and exits.</summary>
    public sealed class MazeGenerationResult
    {
        public MazeGenerationResult(LevelGeometry geometry, IReadOnlyList<GridPosition> playerStarts,
            IReadOnlyList<GridPosition> exits)
        {
            Geometry = geometry;
            PlayerStarts = playerStarts;
            Exits = exits;
        }

        public LevelGeometry Geometry { get; }
        public IReadOnlyList<GridPosition> PlayerStarts { get; }
        public IReadOnlyList<GridPosition> Exits { get; }
    }
}
