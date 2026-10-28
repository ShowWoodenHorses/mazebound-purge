using System;
using System.Collections.Generic;
using Purge.Core.Grid;
using Purge.Core.Level;

namespace Purge.Core.Generation
{
    /// <summary>
    /// Output of a level generator: logical geometry plus the generated player starts, monster spawn points and the
    /// arena. Starts and spawns kept on hand-edited cells are not repeated here.
    /// </summary>
    public sealed class GenerationResult
    {
        public GenerationResult(LevelGeometry geometry, IReadOnlyList<GridPosition> playerStarts,
            IReadOnlyList<GridPosition> monsterSpawns = null, ArenaData arena = null, IReadOnlyList<string> problems = null)
        {
            Geometry = geometry;
            PlayerStarts = playerStarts;
            MonsterSpawns = monsterSpawns ?? Array.Empty<GridPosition>();
            Arena = arena;
            Problems = problems ?? Array.Empty<string>();
        }

        public LevelGeometry Geometry { get; }
        public IReadOnlyList<GridPosition> PlayerStarts { get; }
        public IReadOnlyList<GridPosition> MonsterSpawns { get; }

        /// <summary>The arena; null = none.</summary>
        public ArenaData Arena { get; }

        /// <summary>What the steps could not do (e.g. the arena did not fit), in English; empty = all done.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>Decor settings set by the recipe's decor step; null = keep the level's.</summary>
        public DecorSettings Decor { get; internal set; }

        /// <summary>Generated barrels and chests (stage 7в); ones kept on hand-edited cells are not repeated.</summary>
        public IReadOnlyList<LayoutObject<BarrelKind>> Barrels { get; internal set; } = Array.Empty<LayoutObject<BarrelKind>>();
        public IReadOnlyList<LayoutObject<ChestKind>> Chests { get; internal set; } = Array.Empty<LayoutObject<ChestKind>>();

        /// <summary>Generated vehicles (stage 9в); ones kept on hand-edited cells are not repeated.</summary>
        public IReadOnlyList<LayoutVehicle> Vehicles { get; internal set; } = Array.Empty<LayoutVehicle>();
    }

    /// <summary>Auto decor settings a recipe writes into <see cref="LevelGenerationSettings"/>.</summary>
    public sealed class DecorSettings
    {
        public float Density;
        public float MaxHeight;
        public float GrassDensity;
    }
}
