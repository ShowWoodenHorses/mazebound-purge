using System;
using System.Collections.Generic;
using System.Linq;
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
            var oldGround = sameSize ? old.GroundFloor : 0;
            var kept = new bool[width * height];
            if (sameSize)
            {
                for (var i = 0; i < kept.Length; i++)
                    kept[i] = existing.Marks.IsKept(old, old.ToPosition(i).OnFloor(oldGround));
                foreach (var stair in old.Stairs)
                {
                    if (stair.First.Floor != oldGround && stair.UpperFloor != oldGround) continue;
                    if (!stair.Fits(old)) continue;
                    KeepStairCell(stair.First);
                    KeepStairCell(stair.Second);
                    var landing = stair.First.Floor == oldGround ? stair.LowerLanding : stair.UpperLanding;
                    KeepStairCell(landing);
                }

                void KeepStairCell(GridPosition cell) => kept[cell.Y * width + cell.X] = true;
            }

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
            var ground = sameSize ? oldGround : layout.Building?.GroundFloor ?? 0;
            if (sameSize && layout.Building != null && oldGround != layout.Building.GroundFloor)
                layout.Problems.Add("Building: use Generate New to change the basement setting.");
            var geometry = Build(layout, sameSize ? old : null, ground);
            if (layout.Building != null && (old == null || !sameSize || old.FloorCount == 1 && old.Stairs.Count == 0))
                layout.Building.Build(geometry, layout);

            var arena = layout.Arena;
            if (arena != null && arena.Floor != ground)
            {
                var moved = new ArenaData(arena.Rect, arena.Ring, ground);
                moved.MutableEntries.AddRange(arena.Entries);
                arena = moved;
            }

            var result = new GenerationResult(geometry,
                layout.HasStart && !layout.StartKept ? new[] { layout.Start.OnFloor(ground) } : Array.Empty<GridPosition>(),
                layout.Spawns.Skip(layout.KeptSpawnCount).Select(cell => cell.OnFloor(ground)).ToArray(),
                arena, layout.Problems)
            {
                Decor = layout.Decor,
                Doors = layout.Doors.Select(door => new KeyValuePair<GridPosition, GridPosition?>(
                    door.Key.OnFloor(ground), door.Value?.OnFloor(ground))).ToArray(),
                Barrels = layout.Barrels.Skip(layout.KeptBarrelCount).Select(o => new LayoutObject<BarrelKind>(o.Cell.OnFloor(ground), o.Kind)).ToArray(),
                Chests = layout.Chests.Skip(layout.KeptChestCount).Select(o => new LayoutObject<ChestKind>(o.Cell.OnFloor(ground), o.Kind)).ToArray(),
                Vehicles = layout.Vehicles.Skip(layout.KeptVehicleCount).Select(o => new LayoutVehicle(o.Cell.OnFloor(ground), o.Definition, o.Heading)).ToArray(),
            };
            return result;
        }

        /// <summary>Kept cells, and the start, spawn points and arena standing on them.</summary>
        private static void LoadKept(LevelLayout layout, LevelData level)
        {
            var geometry = level.Geometry;
            var ground = geometry.GroundFloor;
            for (var i = 0; i < layout.CellCount; i++)
            {
                if (!layout.IsKept(i)) continue;
                var p = layout.Position(i).OnFloor(ground);
                layout.Load(i, geometry.GetCell(p), geometry.GetSurface(p), geometry.GetObstacle(p));
            }

            foreach (var start in level.PlayerStarts)
                if (start.Position.Floor == ground && layout.IsKept(layout.Index(start.Position)))
                {
                    layout.SetStart(start.Position.OnFloor(0));
                    layout.StartKept = true;
                    break;
                }

            foreach (var spawn in level.MonsterSpawns)
                if (spawn.Position.Floor == ground && layout.IsKept(layout.Index(spawn.Position)))
                    layout.Spawns.Add(spawn.Position.OnFloor(0));
            layout.KeptSpawnCount = layout.Spawns.Count;

            foreach (var barrel in level.Barrels)
                if (barrel.Position.Floor == ground && layout.IsKept(layout.Index(barrel.Position)))
                    layout.Barrels.Add(new LayoutObject<BarrelKind>(barrel.Position.OnFloor(0), barrel.Kind));
            layout.KeptBarrelCount = layout.Barrels.Count;
            foreach (var chest in level.Chests)
                if (chest.Position.Floor == ground && layout.IsKept(layout.Index(chest.Position)))
                    layout.Chests.Add(new LayoutObject<ChestKind>(chest.Position.OnFloor(0), chest.Kind));
            layout.KeptChestCount = layout.Chests.Count;
            foreach (var vehicle in level.Vehicles)
                if (vehicle.Position.Floor == ground && layout.IsKept(layout.Index(vehicle.Position)))
                    layout.Vehicles.Add(new LayoutVehicle(vehicle.Position.OnFloor(0), vehicle.Definition, vehicle.Heading));
            layout.KeptVehicleCount = layout.Vehicles.Count;

            var arena = level.Arena;
            if (arena.IsEmpty || arena.Floor != ground) return;
            var outer = arena.Outer;
            for (var y = outer.Y; y < outer.YMax; y++)
            for (var x = outer.X; x < outer.XMax; x++)
                if (layout.InBounds(x, y) && layout.IsKept(layout.Index(x, y)))
                {
                    layout.Arena = arena;
                    return;
                }
        }

        private static LevelGeometry Build(LevelLayout layout, LevelGeometry old, int ground)
        {
            var floors = old != null ? old.FloorCount : ground + 1;
            var geometry = new LevelGeometry(layout.Width, layout.Height, CellType.Void, floors, ground);
            for (var i = 0; i < layout.CellCount; i++)
            {
                var p = layout.Position(i).OnFloor(ground);
                geometry.SetCell(p, layout.Cell(i));
                if (layout.Surface(i) != CellSurface.None) geometry.SetSurface(p, layout.Surface(i));
                if (layout.Obstacle(i) != null) geometry.SetObstacle(p, layout.Obstacle(i));
            }

            for (var floor = 0; old != null && floor < floors; floor++)
            for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                if (floor == ground) continue;
                var p = new GridPosition(x, y, floor);
                geometry.SetCell(p, old.GetCell(p));
                if (old.GetSurface(p) != CellSurface.None) geometry.SetSurface(p, old.GetSurface(p));
                if (old.GetObstacle(p) != null) geometry.SetObstacle(p, old.GetObstacle(p));
            }

            if (old != null)
                foreach (var stair in old.Stairs)
                    if (stair.Fits(geometry)) geometry.AddStair(stair);
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
