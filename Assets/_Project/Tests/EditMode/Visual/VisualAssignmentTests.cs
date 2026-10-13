using System.Linq;
using Maze.Core.Authoring;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Visual
{
    public class VisualAssignmentTests
    {
        /// <summary>Both layers of every cell, in a stable order.</summary>
        private static VisualChoice[] ResolveAllCells(LevelData level)
        {
            var geometry = level.Geometry;
            return Enumerable.Range(0, geometry.CellCount)
                .SelectMany(i => CellLayers.All.Select(layer => VisualResolver.ResolveCell(level, geometry.ToPosition(i), layer)))
                .ToArray();
        }

        [Test]
        public void GenerateNew_AssignsFloorToEveryCell_AndWallOnTopOfWallCells()
        {
            using (var f = new VisualFixture())
            {
                LevelAuthoring.GenerateNew(f.Level);
                var geometry = f.Level.Geometry;
                var data = f.Level.VisualData;

                Assert.IsTrue(data.HasCellAssignments(geometry.CellCount));
                for (var i = 0; i < geometry.CellCount; i++)
                {
                    var isWall = geometry.GetCell(geometry.ToPosition(i)) == CellType.Wall;
                    Assert.IsFalse(data.GetCellAssignment(CellLayer.Floor, i).IsEmpty, $"Floor under {geometry.ToPosition(i)}");
                    Assert.AreEqual(isWall, !data.GetCellAssignment(CellLayer.Wall, i).IsEmpty, $"Wall layer of {geometry.ToPosition(i)}");
                    Assert.IsNotNull(f.Floor.FindVariant(data.GetCellAssignment(CellLayer.Floor, i).VariantId));
                }

                foreach (var exit in f.Level.Exits)
                    Assert.AreEqual("exit_01", VisualResolver.ResolveObject(f.Level, exit).VariantId);
            }
        }

        [Test]
        public void WallLayer_OfNonWallCell_ResolvesToNothing()
        {
            using (var f = new VisualFixture())
            {
                LevelAuthoring.GenerateNew(f.Level);
                var start = f.Level.PlayerStarts[0].Position;

                Assert.IsTrue(VisualResolver.ResolveCell(f.Level, start, CellLayer.Wall, out var source).IsEmpty);
                Assert.AreEqual(VisualSource.None, source);
            }
        }

        [Test]
        public void SameSeedsAndSets_GiveIdenticalVisuals()
        {
            using (var a = new VisualFixture(mazeSeed: 4, visualSeed: 9))
            using (var b = new VisualFixture(mazeSeed: 4, visualSeed: 9))
            {
                LevelAuthoring.GenerateNew(a.Level);
                LevelAuthoring.GenerateNew(b.Level);
                CollectionAssert.AreEqual(ResolveAllCells(a.Level), ResolveAllCells(b.Level));
            }
        }

        [Test]
        public void VisualSeed_IsIndependentFromMazeSeed()
        {
            using (var a = new VisualFixture(mazeSeed: 4, visualSeed: 1))
            using (var b = new VisualFixture(mazeSeed: 4, visualSeed: 2))
            {
                LevelAuthoring.GenerateNew(a.Level);
                LevelAuthoring.GenerateNew(b.Level);

                CollectionAssert.AreEqual(a.Level.Geometry.CopyCells(), b.Level.Geometry.CopyCells(), "Same maze.");
                CollectionAssert.AreNotEqual(ResolveAllCells(a.Level), ResolveAllCells(b.Level), "Different visuals.");
            }
        }

        [Test]
        public void ChangingVisualSeed_DoesNotChangeFinishedLevel_UntilRegenerate()
        {
            using (var f = new VisualFixture())
            {
                LevelAuthoring.GenerateNew(f.Level);
                var before = ResolveAllCells(f.Level);

                f.Level.Generation.VisualSeed = 777;
                CollectionAssert.AreEqual(before, ResolveAllCells(f.Level));

                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: false);
                CollectionAssert.AreNotEqual(before, ResolveAllCells(f.Level));
            }
        }

        [Test]
        public void Weights_ApproximateDistribution_AndZeroOrSpecialNeverPicked()
        {
            using (var f = new VisualFixture(width: 81, height: 81, loopDensity: 1f))
            {
                LevelAuthoring.GenerateNew(f.Level);
                var counts = VisualResolver.CountCellVariants(f.Level, CellLayer.Floor);
                var total = (double)counts.Values.Sum();

                Assert.AreEqual(f.Level.Geometry.CellCount, (int)total, "Floor layer covers every cell, including under walls.");
                Assert.AreEqual(0.80, counts["floor_01"] / total, 0.03);
                Assert.AreEqual(0.10, counts["floor_02"] / total, 0.03);
                Assert.AreEqual(0.10, counts["floor_03"] / total, 0.03);
                Assert.IsFalse(counts.ContainsKey("floor_unused"));
                Assert.IsFalse(counts.ContainsKey("floor_special_blood"));
            }
        }

        [Test]
        public void Walls_UseVariantOfTheirContextCategory_OrExplicitDefault()
        {
            using (var f = new VisualFixture(loopDensity: 1f))
            {
                LevelAuthoring.GenerateNew(f.Level);
                var geometry = f.Level.Geometry;
                var sawDefault = false;

                for (var i = 0; i < geometry.CellCount; i++)
                {
                    var p = geometry.ToPosition(i);
                    if (geometry.GetCell(p) != CellType.Wall)
                        continue;

                    var (category, rotation) = WallShapes.Classify(new CellVisualContext(geometry, p).WallConnections);
                    var choice = VisualResolver.ResolveCell(f.Level, p, CellLayer.Wall);
                    var variant = f.Wall.FindVariant(choice.VariantId);

                    Assert.AreEqual(rotation, choice.Rotation, $"Rotation at {p}");
                    if (category == VisualCategory.Isolated)
                    {
                        // The test set has no Isolated variants: explicit default is used, never a random other variant.
                        Assert.AreEqual("wall_default", choice.VariantId);
                        sawDefault = true;
                    }
                    else
                    {
                        Assert.AreEqual(category, variant.Category, $"Category at {p}");
                    }
                }

                Assert.IsTrue(sawDefault, "Full loop density should produce isolated pillars.");
            }
        }

        [Test]
        public void NoMatchingVariant_AndNoDefault_LeavesCellEmpty()
        {
            using (var f = new VisualFixture())
            {
                f.Floor.MutableVariants.Clear();
                LevelAuthoring.GenerateNew(f.Level);

                var floor = f.Level.PlayerStarts[0].Position;
                Assert.IsTrue(VisualResolver.ResolveCell(f.Level, floor, CellLayer.Floor, out var source).IsEmpty);
                Assert.AreEqual(VisualSource.None, source);
            }
        }

        [Test]
        public void Overrides_WinOverAssignments_SurviveRegenerate_ClearedOnRequestOrGenerateNew()
        {
            using (var f = new VisualFixture())
            {
                LevelAuthoring.GenerateNew(f.Level);
                var cell = f.Level.PlayerStarts[0].Position;
                var exit = f.Level.Exits[0];

                f.Level.VisualData.SetCellOverride(cell, CellLayer.Floor, new VisualChoice("floor_special_blood"));
                f.Level.VisualData.SetObjectOverride(exit.Id, new VisualChoice("exit_01", 2));

                Assert.AreEqual("floor_special_blood", VisualResolver.ResolveCell(f.Level, cell, CellLayer.Floor, out var source).VariantId);
                Assert.AreEqual(VisualSource.Override, source);

                f.Level.Generation.VisualSeed = 99;
                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: false);
                Assert.AreEqual("floor_special_blood", VisualResolver.ResolveCell(f.Level, cell, CellLayer.Floor).VariantId);
                Assert.AreEqual(2, VisualResolver.ResolveObject(f.Level, exit).Rotation);

                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: true);
                Assert.AreNotEqual(VisualSource.Override, Resolve(f.Level, cell));
                Assert.IsEmpty(f.Level.VisualData.ObjectOverrides);

                f.Level.VisualData.SetCellOverride(cell, CellLayer.Floor, new VisualChoice("floor_special_blood"));
                LevelAuthoring.GenerateNew(f.Level);
                Assert.IsEmpty(f.Level.VisualData.CellOverrides);
            }
        }

        [Test]
        public void ClearCellOverride_RestoresSavedAssignment()
        {
            using (var f = new VisualFixture())
            {
                LevelAuthoring.GenerateNew(f.Level);
                var cell = f.Level.PlayerStarts[0].Position;
                var saved = VisualResolver.ResolveCell(f.Level, cell, CellLayer.Floor);

                f.Level.VisualData.SetCellOverride(cell, CellLayer.Floor, new VisualChoice("floor_03"));
                Assert.IsTrue(f.Level.VisualData.ClearCellOverride(cell, CellLayer.Floor));

                Assert.AreEqual(saved, VisualResolver.ResolveCell(f.Level, cell, CellLayer.Floor, out var source));
                Assert.AreEqual(VisualSource.Assignment, source);
            }
        }

        [Test]
        public void MissingAssignment_FallsBackToSetDefault()
        {
            using (var f = new VisualFixture())
            {
                LevelAuthoring.GenerateNew(f.Level);
                f.Level.VisualData.ResetCellAssignments(f.Level.Geometry.CellCount);

                var corner = new GridPosition(0, 0);
                var choice = VisualResolver.ResolveCell(f.Level, corner, CellLayer.Wall, out var source);
                Assert.AreEqual(VisualSource.Default, source);
                Assert.AreEqual("wall_default", choice.VariantId);
                Assert.AreEqual(WallShapes.Classify(new CellVisualContext(f.Level.Geometry, corner).WallConnections).Rotation,
                    choice.Rotation);
            }
        }

        [Test]
        public void ReassignAround_MatchesFullReassignment_AfterGeometryEdit()
        {
            using (var f = new VisualFixture())
            {
                LevelAuthoring.GenerateNew(f.Level);
                var edited = new GridPosition(2, 1);
                f.Level.Geometry.SetCell(edited, f.Level.Geometry.GetCell(edited) == CellType.Wall ? CellType.Floor : CellType.Wall);

                VisualAssigner.ReassignAround(f.Level, edited);
                var local = ResolveAllCells(f.Level);

                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: false);
                CollectionAssert.AreEqual(ResolveAllCells(f.Level), local);
            }
        }

        [Test]
        public void Objects_RespectDefinitionFilter_DoorOrientation_AndRemoval()
        {
            using (var f = new VisualFixture(width: 13, height: 13, loopDensity: 0f))
            {
                LevelAuthoring.GenerateNew(f.Level);
                var level = f.Level;

                // Horizontal corridor cell between rooms (1,1) and (3,1) -> walls North/South -> rotation 1.
                level.Geometry.SetCell(new GridPosition(1, 1), CellType.Floor);
                level.Geometry.SetCell(new GridPosition(2, 1), CellType.Door);
                level.Geometry.SetCell(new GridPosition(3, 1), CellType.Floor);
                level.Geometry.SetCell(new GridPosition(2, 0), CellType.Wall);
                level.Geometry.SetCell(new GridPosition(2, 2), CellType.Wall);

                level.MutableDoors.Add(new DoorData("door_1", new GridPosition(2, 1)));
                for (var i = 0; i < 20; i++)
                    level.MutableWeapons.Add(new WeaponPickupData($"weapon_{i + 1}", new GridPosition(1, 1), i % 2 == 0 ? f.Pistol : f.Knife));

                LevelAuthoring.RegenerateVisuals(level, clearOverrides: false);

                Assert.AreEqual(1, VisualResolver.ResolveObject(level, level.Doors[0]).Rotation);
                foreach (var weapon in level.Weapons)
                {
                    var id = VisualResolver.ResolveObject(level, weapon).VariantId;
                    Assert.AreSame(weapon.Definition, f.Weapon.FindVariant(id).Definition, weapon.Id);
                }

                level.MutableDoors.Clear();
                LevelAuthoring.RegenerateVisuals(level, clearOverrides: false);
                Assert.IsTrue(level.VisualData.GetObjectAssignment("door_1").IsEmpty);
            }
        }

        [Test]
        public void NoTheme_ProducesNoAssignmentsWithoutErrors()
        {
            using (var f = new VisualFixture())
            {
                f.Level.VisualTheme = null;
                LevelAuthoring.GenerateNew(f.Level);
                Assert.IsTrue(VisualResolver.ResolveCell(f.Level, new GridPosition(0, 0), CellLayer.Floor).IsEmpty);
                Assert.IsTrue(VisualResolver.ResolveCell(f.Level, new GridPosition(0, 0), CellLayer.Wall).IsEmpty);
            }
        }

        private static VisualSource Resolve(LevelData level, GridPosition cell)
        {
            VisualResolver.ResolveCell(level, cell, CellLayer.Floor, out var source);
            return source;
        }
    }
}
