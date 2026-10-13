using System.Collections.Generic;
using Purge.Core.Grid;

namespace Purge.Core.Generation
{
    /// <summary>Output of a level generator: logical geometry plus initial player starts.</summary>
    public sealed class GenerationResult
    {
        public GenerationResult(LevelGeometry geometry, IReadOnlyList<GridPosition> playerStarts)
        {
            Geometry = geometry;
            PlayerStarts = playerStarts;
        }

        public LevelGeometry Geometry { get; }
        public IReadOnlyList<GridPosition> PlayerStarts { get; }
    }
}
