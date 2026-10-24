using System;
using System.Collections.Generic;
using System.Linq;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Generation;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Level
{
    /// <summary>Biome generator (stage 7а): Forest recipe steps, guarantees of the card, determinism, kept cells.</summary>
    public class BiomeGeneratorTests
    {
        private readonly List<Object> _created = new List<Object>();
        private ObstacleDefinition _tree, _bush, _stone;
        private BiomeRecipe _recipe;

        [SetUp]
        public void SetUp()
        {
            _tree = Obstacle("tree", true, true, false);
            _bush = Obstacle("bush", true, false, true);
            _stone = Obstacle("stone", true, false, false);
            _recipe = Make<BiomeRecipe>();
            var obstacles = new ObstaclesStep();
            obstacles.MutableObstacles.Add(new WeightedObstacle(_tree, 60));
            obstacles.MutableObstacles.Add(new WeightedObstacle(_bush, 25));
            obstacles.MutableObstacles.Add(new WeightedObstacle(_stone, 15));
            _recipe.MutableSteps.AddRange(new GenerationStep[]
            {
                new PerimeterStep(), new StartStep(), new ArenaStep(), new SpawnPointsStep(), new ClearingsStep(),
                new TrailsStep(), obstacles, new GrassStep(), new ObjectsStep(), new DecorStep(),
            });
        }

        [Test]
        public void Objects_BarrelsAndChests_OutOfTheStartsView_NoCellCutOff()
        {
            var generator = new BiomeGenerator();
            for (var seed = 0; seed < 12; seed++)
            {
                var result = generator.Generate(Settings(seed), _recipe);
                var tag = $"seed {seed}";
                var start = result.PlayerStarts.Single();
                Assert.That(result.Barrels.Count, Is.InRange(3, 5), tag);
                Assert.AreEqual(2, result.Chests.Count(c => c.Kind == ChestKind.Common), tag);
                Assert.AreEqual(1, result.Chests.Count(c => c.Kind == ChestKind.Locked), tag);
                Assert.AreEqual(1, result.Chests.Count(c => c.Kind == ChestKind.Challenge), tag);
                foreach (var cell in result.Barrels.Select(b => b.Cell).Concat(result.Chests.Select(c => c.Cell)))
                {
                    Assert.Greater(Math.Max(Math.Abs(cell.X - start.X), Math.Abs(cell.Y - start.Y)), 10, tag);
                    Assert.IsTrue(result.Geometry.GetCell(cell) == CellType.Floor && result.Geometry.GetObstacle(cell) == null, tag);
                }

                var locked = result.Chests.Single(c => c.Kind == ChestKind.Locked).Cell;
                Assert.GreaterOrEqual(Math.Sqrt((locked.X - start.X) * (locked.X - start.X) + (locked.Y - start.Y) * (locked.Y - start.Y)), 20, tag);
                for (var i = 0; i < result.Barrels.Count; i++)
                for (var j = i + 1; j < result.Barrels.Count; j++)
                {
                    float dx = result.Barrels[i].Cell.X - result.Barrels[j].Cell.X, dy = result.Barrels[i].Cell.Y - result.Barrels[j].Cell.Y;
                    Assert.GreaterOrEqual(Math.Sqrt(dx * dx + dy * dy), 4, tag);
                }
            }

            // On the level: the validator's connectivity goes around them (Forest_PassesTheValidator covers the errors).
            var level = Level(3);
            Assert.That(level.Barrels.Count, Is.InRange(3, 5));
            Assert.AreEqual(4, level.Chests.Count);
            Assert.IsTrue(level.Barrels.All(b => b.Id.StartsWith("barrel_")) && level.Chests.All(c => c.Id.StartsWith("chest_")));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Make<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }

        private ObstacleDefinition Obstacle(string id, bool movement, bool bullets, bool flammable)
        {
            var o = Make<ObstacleDefinition>();
            o.Configure(id, movement, bullets, false, flammable);
            return o;
        }

        private static LevelGenerationSettings Settings(int seed, int size = 64) =>
            new LevelGenerationSettings { Width = size, Height = size, LayoutSeed = seed };

        private LevelData Level(int seed)
        {
            var level = Make<LevelData>();
            level.Generation.Width = 64;
            level.Generation.Height = 64;
            level.Generation.LayoutSeed = seed;
            level.Generation.Recipe = _recipe;
            LevelAuthoring.GenerateNew(level);
            return level;
        }

        [Test]
        public void SameSeed_SameLevel_OtherSeed_Other()
        {
            var generator = new BiomeGenerator();
            var a = generator.Generate(Settings(7), _recipe).Geometry;
            var b = generator.Generate(Settings(7), _recipe).Geometry;
            var c = generator.Generate(Settings(8), _recipe).Geometry;
            Assert.AreEqual(Dump(a), Dump(b));
            Assert.AreNotEqual(Dump(a), Dump(c));
        }

        [Test]
        public void Forest_MeetsTheCard_OnManySeeds()
        {
            var generator = new BiomeGenerator();
            for (var seed = 0; seed < 12; seed++)
            {
                var result = generator.Generate(Settings(seed), _recipe);
                var g = result.Geometry;
                var tag = $"seed {seed}";
                CollectionAssert.IsEmpty(result.Problems, tag);

                // Perimeter: 2 wall cells along the edge.
                for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                    if (Math.Min(Math.Min(x, y), Math.Min(63 - x, 63 - y)) < 2)
                        Assert.AreEqual(CellType.Wall, g.GetCell(new GridPosition(x, y)), tag);

                var start = result.PlayerStarts.Single();
                Assert.AreEqual(new GridPosition(32, 32), start, tag);

                // Arena: 16×16 free, 3–4 entries of width ≥ 3, centre ≥ 20 from the start.
                var arena = result.Arena;
                Assert.IsNotNull(arena, tag);
                Assert.AreEqual(16, arena.Rect.Width, tag);
                for (var y = arena.Rect.Y; y < arena.Rect.YMax; y++)
                for (var x = arena.Rect.X; x < arena.Rect.XMax; x++)
                    Assert.IsTrue(Walkable(g, new GridPosition(x, y)), $"{tag}: arena cell {x},{y}");
                Assert.That(arena.Entries.Count, Is.InRange(3, 4), tag);
                foreach (var entry in arena.Entries)
                    Assert.GreaterOrEqual(entry.Width, 3, tag);
                Assert.GreaterOrEqual(Distance(arena.Center, start), 20f, tag);

                // Spawn points: 6–8, outside the start's view and ≥ 12 away, open 3×3, a 3×3 body reaches the arena.
                Assert.That(result.MonsterSpawns.Count, Is.InRange(6, 8), tag);
                var body = BodyReach(g, arena.Center);
                foreach (var spawn in result.MonsterSpawns)
                {
                    Assert.Greater(Math.Max(Math.Abs(spawn.X - start.X), Math.Abs(spawn.Y - start.Y)), 10, tag);
                    Assert.GreaterOrEqual(Distance(spawn, start), 12f, tag);
                    Assert.IsTrue(body[spawn.Y * 64 + spawn.X], $"{tag}: no 3x3 way from spawn {spawn} to the arena");
                }

                // Every walkable cell is reachable from the start.
                var reach = Reach(g, start);
                for (var i = 0; i < g.CellCount; i++)
                    if (Walkable(g, g.ToPosition(i)))
                        Assert.IsTrue(reach[i], $"{tag}: {g.ToPosition(i)} cut off");

                // Obstacle budget: ≤ 35 % of an 8×8 chunk.
                for (var cy = 0; cy < 8; cy++)
                for (var cx = 0; cx < 8; cx++)
                {
                    var count = 0;
                    for (var y = cy * 8; y < cy * 8 + 8; y++)
                    for (var x = cx * 8; x < cx * 8 + 8; x++)
                        if (g.GetObstacle(new GridPosition(x, y)) != null) count++;
                    Assert.LessOrEqual(count, 22, $"{tag}: chunk {cx},{cy}");
                }
            }
        }

        /// <summary>Stage 7б: the validator's layout checks agree with the generator — a generated Forest passes them.</summary>
        [Test]
        public void Forest_PassesTheLayoutValidator_OnManySeeds()
        {
            var codes = new[]
            {
                Purge.Core.Validation.ValidationCodes.LevelDisconnected, Purge.Core.Validation.ValidationCodes.ArenaTooSmall,
                Purge.Core.Validation.ValidationCodes.ArenaFewEntries, Purge.Core.Validation.ValidationCodes.SpawnInStartView,
                Purge.Core.Validation.ValidationCodes.SpawnUnreachable, Purge.Core.Validation.ValidationCodes.SpawnNoBodyPath,
                Purge.Core.Validation.ValidationCodes.ObstacleBudget,
            };
            for (var seed = 0; seed < 12; seed++)
            {
                var report = Purge.Core.Validation.LevelValidator.Validate(Level(seed));
                var issues = report.Issues.Where(i => codes.Contains(i.Code)).Select(i => i.ToString()).ToList();
                CollectionAssert.IsEmpty(issues, $"seed {seed}");
            }
        }

        [Test]
        public void Grass_Burns_AndIsNotOnTrailsOrUnderObstacles()
        {
            var g = new BiomeGenerator().Generate(Settings(3), _recipe).Geometry;
            var grass = 0;
            for (var i = 0; i < g.CellCount; i++)
            {
                var p = g.ToPosition(i);
                if (g.GetSurface(p) != CellSurface.Grass) continue;
                grass++;
                Assert.IsNull(g.GetObstacle(p));
                Assert.IsTrue(g.GetFlags(p).HasFlag(CellFlags.Flammable));
                Assert.IsFalse(g.GetFlags(p).HasFlag(CellFlags.BlocksMovement));
            }

            Assert.Greater(grass, 200);
        }

        [Test]
        public void Generate_KeepsHandEditedCells_TheirObjects_AndTheStart()
        {
            var level = Level(5);
            var start = level.PlayerStarts.Single();
            LevelEditing.Move(level, start, new GridPosition(30, 30)); // marks the start's cells
            var rock = FreeCell(level, 10);
            LevelEditing.SetObstacle(level, rock, _stone);
            var wall = FreeCell(level, 50);
            LevelEditing.SetCellType(level, wall, CellType.Wall);
            var medkitCell = FreeCell(level, 40);
            var medkit = LevelEditing.AddMedkit(level, medkitCell);
            LevelEditing.AddLockedArea(level, new GridRect(20, 40, 3, 3));
            var locked = Snapshot(level.Geometry, new GridRect(20, 40, 3, 3));

            level.Generation.LayoutSeed = 99;
            LevelAuthoring.Generate(level);

            var g = level.Geometry;
            Assert.AreEqual(_stone, g.GetObstacle(rock));
            Assert.AreEqual(CellType.Wall, g.GetCell(wall));
            Assert.AreEqual(medkitCell, level.Medkits.Single().Position);
            Assert.AreEqual(medkit.Id, level.Medkits.Single().Id);
            Assert.AreEqual(new GridPosition(30, 30), level.PlayerStarts.Single().Position);
            Assert.AreEqual(locked, Snapshot(g, new GridRect(20, 40, 3, 3)));
            Assert.That(level.MonsterSpawns.Count, Is.InRange(6, 8));
            Assert.IsFalse(level.Arena.IsEmpty);
        }

        [Test]
        public void GenerateNew_ClearsMarks_GenerateWithoutMarks_IsAFreshLevel()
        {
            var level = Level(5);
            LevelEditing.SetObstacle(level, FreeCell(level, 10), _stone);
            Assert.AreEqual(1, level.Marks.ManualCount);
            Assert.IsTrue(LevelEditing.ClearManualMarks(level));
            Assert.AreEqual(0, level.Marks.ManualCount);

            level.Generation.LayoutSeed = 6;
            LevelAuthoring.Generate(level);
            var fresh = Level(6);
            Assert.AreEqual(Dump(fresh.Geometry), Dump(level.Geometry));

            LevelEditing.SetSurface(level, FreeCell(level, 3), CellSurface.None);
            LevelAuthoring.GenerateNew(level);
            Assert.AreEqual(0, level.Marks.ManualCount);
        }

        [Test]
        public void Edits_MarkTheirCells()
        {
            var level = Level(1);
            var cell = FreeCell(level, 1);
            Assert.IsFalse(level.Marks.IsManual(level.Geometry, cell));
            LevelEditing.SetSurface(level, cell, CellSurface.Grass);
            Assert.IsTrue(level.Marks.IsManual(level.Geometry, cell));

            var other = FreeCell(level, 2);
            LevelEditing.AddMonsterSpawn(level, other);
            Assert.IsTrue(level.Marks.IsManual(level.Geometry, other));
        }

        [Test]
        public void ShapelessWalls_AnyGeneralVariant_GrassCells_GetGrassDecor()
        {
            var level = Level(2);
            var theme = Make<VisualTheme>();
            var walls = Make<VisualSet>();
            walls.Kind = VisualKind.Wall;
            walls.ShapelessWalls = true;
            walls.MutableVariants.Add(new VisualVariant("tree_a", 1));
            walls.MutableVariants.Add(new VisualVariant("tree_b", 1));
            var decor = Make<VisualSet>();
            decor.Kind = VisualKind.Decor;
            decor.MutableVariants.Add(new VisualVariant("tall_grass", 1, VisualCategory.Grass));
            decor.MutableVariants.Add(new VisualVariant("flower", 1));
            theme.SetSet(VisualKind.Wall, walls);
            theme.SetSet(VisualKind.Decor, decor);
            level.VisualTheme = theme;
            level.Generation.GrassDecorDensity = 1f;
            level.Generation.DecorDensity = 1f;
            level.Generation.MaxAutoDecorHeight = 10f;
            LevelAuthoring.RegenerateVisuals(level, clearOverrides: true);

            var g = level.Geometry;
            var wallIds = new HashSet<string>();
            for (var i = 0; i < g.CellCount; i++)
            {
                var p = g.ToPosition(i);
                if (g.GetCell(p) == CellType.Wall)
                    wallIds.Add(VisualResolver.ResolveCell(level, p, CellLayer.Wall).VariantId);
                else if (g.GetCell(p) == CellType.Floor && g.GetObstacle(p) == null)
                {
                    var expected = g.GetSurface(p) == CellSurface.Grass ? "tall_grass" : "flower";
                    Assert.AreEqual(expected, VisualResolver.ResolveDecor(level, p).VariantId, p.ToString());
                }
            }

            CollectionAssert.AreEquivalent(new[] { "tree_a", "tree_b" }, wallIds);
        }

        // ------------------------------------------------------------ Helpers

        /// <summary>The n-th walkable cell without objects and outside the arena, counting from the bottom left.</summary>
        private static GridPosition FreeCell(LevelData level, int n)
        {
            var g = level.Geometry;
            var used = new HashSet<GridPosition>(level.AllEntities().Select(e => e.Position));
            foreach (var s in level.MonsterSpawns) used.Add(s.Position);
            var found = 0;
            for (var i = 0; i < g.CellCount; i++)
            {
                var p = g.ToPosition(i);
                if (!Walkable(g, p) || used.Contains(p) || level.Arena.Outer.Contains(p)) continue;
                if (found++ == n) return p;
            }

            throw new InvalidOperationException("No free cell.");
        }

        private static bool Walkable(LevelGeometry g, GridPosition p) =>
            g.IsInside(p) && !g.GetFlags(p).HasFlag(CellFlags.BlocksMovement);

        private static float Distance(GridPosition a, GridPosition b) =>
            (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static bool[] Reach(LevelGeometry g, GridPosition from) => Flood(g, from, p => Walkable(g, p));

        /// <summary>Cells a 3×3 body centred on them can reach from <paramref name="from"/>.</summary>
        private static bool[] BodyReach(LevelGeometry g, GridPosition from) => Flood(g, from, p =>
        {
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (!Walkable(g, new GridPosition(p.X + dx, p.Y + dy)))
                    return false;
            return true;
        });

        private static bool[] Flood(LevelGeometry g, GridPosition from, Func<GridPosition, bool> pass)
        {
            var seen = new bool[g.CellCount];
            if (!pass(from)) return seen;
            var queue = new Queue<GridPosition>();
            seen[g.ToIndex(from)] = true;
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in DirectionExtensions.All)
                {
                    var n = p + d.ToOffset();
                    if (!g.IsInside(n) || seen[g.ToIndex(n)] || !pass(n)) continue;
                    seen[g.ToIndex(n)] = true;
                    queue.Enqueue(n);
                }
            }

            return seen;
        }

        private static string Dump(LevelGeometry g)
        {
            var chars = new char[g.CellCount];
            for (var i = 0; i < g.CellCount; i++)
            {
                var p = g.ToPosition(i);
                var o = g.GetObstacle(p);
                chars[i] = g.GetCell(p) == CellType.Wall ? '#' : o != null ? o.Id[0] : g.GetSurface(p) == CellSurface.Grass ? ',' : '.';
            }

            return new string(chars);
        }

        private static string Snapshot(LevelGeometry g, GridRect rect)
        {
            var s = "";
            for (var y = rect.Y; y < rect.YMax; y++)
            for (var x = rect.X; x < rect.XMax; x++)
            {
                var p = new GridPosition(x, y);
                s += $"{g.GetCell(p)}/{g.GetSurface(p)}/{g.GetObstacle(p)?.Id};";
            }

            return s;
        }
    }
}
