using System;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Level;

namespace Purge.Core.Generation
{
    /// <summary>
    /// Placeholder generator for "Generate New" until the biome recipes: an open field with a border wall and one
    /// player start in the centre. <see cref="LevelGenerationSettings.ObstacleDensity"/> turns some inner cells with
    /// both coordinates even into single-cell pillars; such pillars never touch each other by a side, so every
    /// floor cell stays reachable. The result depends only on the settings.
    /// </summary>
    public sealed class OpenFieldGenerator
    {
        public const int MinSize = 5;

        public GenerationResult Generate(LevelGenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.Width < MinSize || settings.Height < MinSize)
                throw new ArgumentException($"Level size must be at least {MinSize}x{MinSize}, got {settings.Width}x{settings.Height}.");

            var width = settings.Width;
            var height = settings.Height;
            var geometry = new LevelGeometry(width, height, CellType.Floor);
            for (var x = 0; x < width; x++)
            {
                geometry.SetCell(new GridPosition(x, 0), CellType.Wall);
                geometry.SetCell(new GridPosition(x, height - 1), CellType.Wall);
            }

            for (var y = 0; y < height; y++)
            {
                geometry.SetCell(new GridPosition(0, y), CellType.Wall);
                geometry.SetCell(new GridPosition(width - 1, y), CellType.Wall);
            }

            var start = new GridPosition(width / 2, height / 2);
            if (settings.ObstacleDensity > 0f)
            {
                var random = new DeterministicRandom(settings.LayoutSeed);
                for (var y = 2; y < height - 2; y += 2)
                for (var x = 2; x < width - 2; x += 2)
                {
                    var cell = new GridPosition(x, y);
                    if (random.NextDouble() < settings.ObstacleDensity && !IsNear(cell, start))
                        geometry.SetCell(cell, CellType.Wall);
                }
            }

            return new GenerationResult(geometry, new[] { start });
        }

        private static bool IsNear(GridPosition a, GridPosition b) =>
            Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;
    }
}
