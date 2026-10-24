using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Level;

namespace Purge.Core.Generation
{
    /// <summary>
    /// Runs a <see cref="BiomeRecipe"/> over floor 0 (stage 7). The result depends only on the settings, the recipe and
    /// the kept cells: cells edited by hand and locked areas (<see cref="AuthoringMarks"/>) are copied from the level and
    /// never changed; a start, spawn points and an arena standing on them are kept and the steps build around them.
    /// After the steps every walkable cell is made reachable from the start (<see cref="LevelLayout.Connect"/>).
    /// Upper floors of a level of the same size are copied as they are.
    /// </summary>
    public sealed class BiomeGenerator
    {
        public const int MinSize = 16;

        /// <summary>Generates from scratch (nothing kept).</summary>
        public GenerationResult Generate(LevelGenerationSettings settings, BiomeRecipe recipe) =>
            Generate(settings, recipe, null);

        /// <summary>Generates keeping the hand-edited cells and locked areas of <paramref name="existing"/> (null = none).</summary>
        public GenerationResult Generate(LevelGenerationSettings settings, BiomeRecipe recipe, LevelData existing)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (settings.Width < MinSize || settings.Height < MinSize)
                throw new ArgumentException($"Level size must be at least {MinSize}x{MinSize}, got {settings.Width}x{settings.Height}.");

            var width = settings.Width;
            var height = settings.Height;
            var old = existing != null ? existing.Geometry : null;
            var sameSize = old != null && old.IsConsistent && old.Width == width && old.Height == height;
            var kept = new bool[width * height];
            if (sameSize)
                for (var i = 0; i < kept.Length; i++)
                    kept[i] = existing.Marks.IsKept(old, old.ToPosition(i));

            var layout = new LevelLayout(width, height, settings.LayoutSeed, kept);
            if (sameSize) LoadKept(layout, existing);

            for (var i = 0; i < recipe.Steps.Count; i++)
            {
                var step = recipe.Steps[i];
                if (step == null) continue;
                var seed = (int)StableHash.Combine((ulong)(uint)settings.LayoutSeed, (ulong)(i + 1));
                step.Apply(layout, new DeterministicRandom(seed));
            }

            layout.Connect();
            var result = new GenerationResult(Build(layout, sameSize ? old : null),
                layout.HasStart && !layout.StartKept ? new[] { layout.Start } : Array.Empty<GridPosition>(),
                layout.Spawns.GetRange(layout.KeptSpawnCount, layout.Spawns.Count - layout.KeptSpawnCount),
                layout.Arena, layout.Problems)
            {
                Decor = layout.Decor,
            };
            return result;
        }

        /// <summary>Kept cells, and the start, spawn points and arena standing on them.</summary>
        private static void LoadKept(LevelLayout layout, LevelData level)
        {
            var geometry = level.Geometry;
            for (var i = 0; i < layout.CellCount; i++)
            {
                if (!layout.IsKept(i)) continue;
                var p = layout.Position(i);
                layout.Load(i, geometry.GetCell(p), geometry.GetSurface(p), geometry.GetObstacle(p));
            }

            foreach (var start in level.PlayerStarts)
                if (start.Position.Floor == 0 && layout.IsKept(layout.Index(start.Position)))
                {
                    layout.SetStart(start.Position);
                    layout.StartKept = true;
                    break;
                }

            foreach (var spawn in level.MonsterSpawns)
                if (spawn.Position.Floor == 0 && layout.IsKept(layout.Index(spawn.Position)))
                    layout.Spawns.Add(spawn.Position);
            layout.KeptSpawnCount = layout.Spawns.Count;

            var arena = level.Arena;
            if (arena.IsEmpty || arena.Floor != 0) return;
            var outer = arena.Outer;
            for (var y = outer.Y; y < outer.YMax; y++)
            for (var x = outer.X; x < outer.XMax; x++)
                if (layout.InBounds(x, y) && layout.IsKept(layout.Index(x, y)))
                {
                    layout.Arena = arena;
                    return;
                }
        }

        private static LevelGeometry Build(LevelLayout layout, LevelGeometry old)
        {
            var floors = old != null ? old.FloorCount : 1;
            var geometry = new LevelGeometry(layout.Width, layout.Height, CellType.Floor, floors);
            for (var i = 0; i < layout.CellCount; i++)
            {
                var p = layout.Position(i);
                geometry.SetCell(p, layout.Cell(i));
                if (layout.Surface(i) != CellSurface.None) geometry.SetSurface(p, layout.Surface(i));
                if (layout.Obstacle(i) != null) geometry.SetObstacle(p, layout.Obstacle(i));
            }

            for (var floor = 1; floor < floors; floor++)
            for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var p = new GridPosition(x, y, floor);
                geometry.SetCell(p, old.GetCell(p));
                if (old.GetSurface(p) != CellSurface.None) geometry.SetSurface(p, old.GetSurface(p));
                if (old.GetObstacle(p) != null) geometry.SetObstacle(p, old.GetObstacle(p));
            }

            return geometry;
        }

        /// <summary>Problems of the recipe's steps (editor checks).</summary>
        public static List<string> Validate(BiomeRecipe recipe)
        {
            var problems = new List<string>();
            recipe.Validate(problems);
            return problems;
        }
    }
}
