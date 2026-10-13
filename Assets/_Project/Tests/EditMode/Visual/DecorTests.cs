using System.Linq;
using Maze.Core.Authoring;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Validation;
using Maze.Core.Visual;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Visual
{
    /// <summary>Decor layer: auto placement (density, height limit, VisualSeed), manual overrides, validation.</summary>
    public class DecorTests
    {
        private static VisualSet AddDecor(VisualFixture f)
        {
            var low = VisualFixture.Variant("decor_pool", 1);
            low.Height = 0f;
            var rug = VisualFixture.Variant("decor_rug", 1);
            rug.Height = 0.05f;
            var barrel = VisualFixture.Variant("decor_barrel", 5);
            barrel.Height = 1f;
            var special = VisualFixture.Variant("decor_special", 5, VisualCategory.Special);
            return f.AddSet(VisualKind.Decor, null, low, rug, barrel, special);
        }

        private static GridPosition[] FloorCells(LevelData level) =>
            Enumerable.Range(0, level.Geometry.CellCount).Select(level.Geometry.ToPosition)
                .Where(p => level.Geometry.GetCell(p) == CellType.Floor).ToArray();

        [Test]
        public void Density_ZeroNone_OneEveryFloorCell_OnlyLowGeneralVariants()
        {
            using (var f = new VisualFixture())
            {
                AddDecor(f);
                f.Level.Generation.DecorDensity = 0f;
                LevelAuthoring.GenerateNew(f.Level);
                var geometry = f.Level.Geometry;
                for (var i = 0; i < geometry.CellCount; i++)
                    Assert.IsTrue(VisualResolver.ResolveDecor(f.Level, geometry.ToPosition(i)).IsEmpty, "Density 0: no decor.");

                f.Level.Generation.DecorDensity = 1f;
                f.Level.Generation.MaxAutoDecorHeight = 0.3f;
                LevelEditing.PlaceDecor(f.Level);
                for (var i = 0; i < geometry.CellCount; i++)
                {
                    var cell = geometry.ToPosition(i);
                    var decor = VisualResolver.ResolveDecor(f.Level, cell);
                    if (geometry.GetCell(cell) != CellType.Floor)
                    {
                        Assert.IsTrue(decor.IsEmpty, $"No decor in walls and doors: {cell}.");
                        continue;
                    }

                    Assert.IsFalse(decor.IsEmpty, $"Density 1: decor in every floor cell ({cell}).");
                    CollectionAssert.Contains(new[] { "decor_pool", "decor_rug" }, decor.VariantId,
                        "Taller than the limit and Special variants are never auto placed.");
                    Assert.That(decor.Rotation, Is.InRange(0, 3));
                }
            }
        }

        [Test]
        public void Density_Proportional_Deterministic_AndHeightLimitIsASetting()
        {
            using (var a = new VisualFixture(visualSeed: 7))
            using (var b = new VisualFixture(visualSeed: 7))
            {
                AddDecor(a);
                AddDecor(b);
                a.Level.Generation.DecorDensity = b.Level.Generation.DecorDensity = 0.3f;
                LevelAuthoring.GenerateNew(a.Level);
                LevelAuthoring.GenerateNew(b.Level);

                var floors = FloorCells(a.Level);
                var decorA = floors.Select(c => VisualResolver.ResolveDecor(a.Level, c)).ToArray();
                CollectionAssert.AreEqual(decorA, floors.Select(c => VisualResolver.ResolveDecor(b.Level, c)).ToArray(),
                    "Same VisualSeed and settings: same decor.");
                var share = decorA.Count(d => !d.IsEmpty) / (float)floors.Length;
                Assert.That(share, Is.InRange(0.18f, 0.42f), "About the density.");

                a.Level.Generation.MaxAutoDecorHeight = 2f;
                a.Level.Generation.DecorDensity = 1f;
                LevelEditing.PlaceDecor(a.Level);
                Assert.IsTrue(floors.Any(c => VisualResolver.ResolveDecor(a.Level, c).VariantId == "decor_barrel"),
                    "A higher limit lets taller decor be auto placed.");
            }
        }

        [Test]
        public void ManualDecor_AndNoDecor_SurviveRegenerate_UntilOverridesCleared()
        {
            using (var f = new VisualFixture())
            {
                AddDecor(f);
                f.Level.Generation.DecorDensity = 1f;
                LevelAuthoring.GenerateNew(f.Level);
                var floors = FloorCells(f.Level);
                var barrelCell = floors[0];
                var emptyCell = floors[1];

                LevelEditing.SetCellOverride(f.Level, barrelCell, CellLayer.Decor, new VisualChoice("decor_barrel", 2));
                LevelEditing.SetCellOverride(f.Level, emptyCell, CellLayer.Decor, VisualChoice.None);
                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: false);
                LevelEditing.PlaceDecor(f.Level);

                Assert.AreEqual(new VisualChoice("decor_barrel", 2), VisualResolver.ResolveDecor(f.Level, barrelCell, out var source));
                Assert.AreEqual(VisualSource.Override, source, "Placed by hand.");
                Assert.IsTrue(VisualResolver.ResolveDecor(f.Level, emptyCell, out source).IsEmpty, "'No decor here' stays.");
                Assert.AreEqual(VisualSource.Override, source);

                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: true);
                Assert.AreEqual(VisualSource.Assignment, Resolve(f.Level, barrelCell));
                Assert.AreEqual(VisualSource.Assignment, Resolve(f.Level, emptyCell), "Density 1: auto decor is back.");
            }
        }

        [Test]
        public void PaintingWallOrDoor_RemovesDecor_AndValidatorReportsBadDecor()
        {
            using (var f = new VisualFixture())
            {
                AddDecor(f);
                f.Level.Generation.DecorDensity = 1f;
                LevelAuthoring.GenerateNew(f.Level);
                var floors = FloorCells(f.Level);
                var cell = floors.First(p => f.Level.AllEntities().All(e => e.Position != p));

                LevelEditing.SetCellOverride(f.Level, cell, CellLayer.Decor, new VisualChoice("decor_barrel"));
                LevelEditing.SetCellType(f.Level, cell, CellType.Wall);
                Assert.IsTrue(VisualResolver.ResolveDecor(f.Level, cell).IsEmpty, "No decor in a wall.");
                Assert.IsFalse(f.Level.VisualData.TryGetCellOverride(cell, CellLayer.Decor, out _), "Its manual decor is removed.");
                Assert.IsTrue(f.Level.VisualData.GetCellAssignment(CellLayer.Decor, f.Level.Geometry.ToIndex(cell)).IsEmpty);

                // A hand-made (stale) decor override in a wall: ignored, with a warning.
                f.Level.VisualData.SetCellOverride(cell, CellLayer.Decor, new VisualChoice("decor_rug"));
                var other = floors.Last(p => p != cell);
                LevelEditing.SetCellOverride(f.Level, other, CellLayer.Decor, new VisualChoice("decor_missing"));
                var report = LevelValidator.Validate(f.Level);
                Assert.IsTrue(report.Issues.Any(i => i.Code == ValidationCodes.OverrideLayerMismatch && i.Position == cell));
                Assert.IsTrue(report.Issues.Any(i => i.Code == ValidationCodes.UnknownVariant && i.Position == other &&
                                                     i.Severity == ValidationSeverity.Error));
            }
        }

        [Test]
        public void LevelWithoutDecorData_IsValid_AndUsageListsDecor()
        {
            using (var f = new VisualFixture())
            {
                LevelAuthoring.GenerateNew(f.Level); // No decor set: like levels made before decor.
                var floors = FloorCells(f.Level);
                Assert.IsTrue(floors.All(c => VisualResolver.ResolveDecor(f.Level, c).IsEmpty));
                Assert.IsFalse(LevelValidator.Validate(f.Level).Issues.Any(i =>
                        i.Severity == ValidationSeverity.Error && i.Message.ToLowerInvariant().Contains("decor")),
                    "No decor (and no Decor set) is not an error.");

                AddDecor(f);
                LevelEditing.SetCellOverride(f.Level, floors[0], CellLayer.Decor, new VisualChoice("decor_rug"));
                CollectionAssert.Contains(LevelVisualUsage.Collect(f.Level), new VisualKey(VisualKind.Decor, "decor_rug"));
            }
        }

        [Test]
        public void Placement_MakesAutoDecorManual_IsClamped_SurvivesRegenerate_AndGoesWithTheDecor()
        {
            using (var f = new VisualFixture())
            {
                AddDecor(f);
                f.Level.Generation.DecorDensity = 1f;
                LevelAuthoring.GenerateNew(f.Level);
                var cell = FloorCells(f.Level).First(p => f.Level.AllEntities().All(e => e.Position != p));
                var auto = VisualResolver.ResolveDecor(f.Level, cell);
                Assert.AreEqual(VisualSource.Assignment, Resolve(f.Level, cell));

                VisualResolver.ResolveDecorPose(f.Level, cell, auto, out var offset, out var yaw);
                Assert.AreEqual(Vector3.zero, offset, "Auto decor: the cell centre.");
                Assert.AreEqual(90f * auto.Rotation, yaw, "And its quarter turn.");

                Assert.IsTrue(LevelEditing.SetDecorPlacement(f.Level, cell, new Vector2(0.9f, -0.2f), 5f, 370f));
                Assert.AreEqual(VisualSource.Override, Resolve(f.Level, cell), "Edited decor becomes manual.");
                Assert.AreEqual(auto.VariantId, VisualResolver.ResolveDecor(f.Level, cell).VariantId, "Same model.");
                VisualResolver.ResolveDecorPose(f.Level, cell, VisualResolver.ResolveDecor(f.Level, cell), out offset, out yaw);
                Assert.AreEqual(new Vector3(DecorPlacement.MaxOffset, DecorPlacement.MaxHeight, -0.2f), offset, "Clamped: stays in its cell.");
                Assert.AreEqual(10f, yaw, 1e-3f, "Wrapped.");

                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: false);
                LevelEditing.PlaceDecor(f.Level);
                Assert.IsTrue(f.Level.VisualData.TryGetDecorPlacement(cell, out _), "Survives regeneration.");
                Assert.IsFalse(LevelValidator.Validate(f.Level).Has(ValidationCodes.StaleDecorPlacement));

                LevelEditing.SetCellOverride(f.Level, cell, CellLayer.Decor, new VisualChoice("decor_barrel"));
                Assert.IsTrue(f.Level.VisualData.TryGetDecorPlacement(cell, out _), "Another model keeps the placement.");
                LevelEditing.ClearCellOverride(f.Level, cell, CellLayer.Decor);
                Assert.IsFalse(f.Level.VisualData.TryGetDecorPlacement(cell, out _), "Back to automatic: placement dropped.");

                LevelEditing.SetDecorPlacement(f.Level, cell, new Vector2(0.1f, 0.1f), 0f, 45f);
                LevelEditing.SetCellOverride(f.Level, cell, CellLayer.Decor, VisualChoice.None);
                Assert.IsFalse(f.Level.VisualData.TryGetDecorPlacement(cell, out _), "No decor: no placement.");
                Assert.IsFalse(LevelEditing.SetDecorPlacement(f.Level, cell, Vector2.zero, 0f, 0f), "Nothing to place.");

                LevelEditing.SetCellOverride(f.Level, cell, CellLayer.Decor, new VisualChoice("decor_rug"));
                LevelEditing.SetDecorPlacement(f.Level, cell, new Vector2(0.1f, 0.1f), 0f, 45f);
                LevelEditing.SetCellType(f.Level, cell, CellType.Wall);
                Assert.IsFalse(f.Level.VisualData.TryGetDecorPlacement(cell, out _), "A wall removes decor and its placement.");

                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: true);
                Assert.AreEqual(0, f.Level.VisualData.DecorPlacements.Count, "Clearing overrides clears placements.");
            }
        }

        [Test]
        public void PickupPlacement_OnlyForPickups_Clamped_FollowsIdAndCell_ClearedWithOverrides()
        {
            using (var f = new VisualFixture())
            {
                LevelAuthoring.GenerateNew(f.Level);
                var floors = FloorCells(f.Level).Where(p => f.Level.AllEntities().All(e => e.Position != p)).ToArray();
                var medkit = LevelEditing.AddMedkit(f.Level, floors[0]);
                var exit = f.Level.Exits[0];

                Assert.IsFalse(LevelEditing.SetObjectPlacement(f.Level, exit, Vector2.zero, 1f, 0f), "Exits are not placeable.");
                Assert.IsTrue(LevelEditing.SetObjectPlacement(f.Level, medkit, new Vector2(0.2f, 0.7f), 0.58f, -30f));
                VisualResolver.ResolveObjectPose(f.Level, medkit, VisualResolver.ResolveObject(f.Level, medkit), out var offset, out var yaw);
                Assert.AreEqual(new Vector3(0.2f, 0.58f, DecorPlacement.MaxOffset), offset, "Clamped into its cell.");
                Assert.AreEqual(330f, yaw, 1e-3f);

                LevelEditing.Rename(f.Level, medkit, "medkit_table");
                Assert.IsTrue(f.Level.VisualData.TryGetObjectPlacement("medkit_table", out _), "Follows a rename.");

                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: false);
                Assert.IsTrue(f.Level.VisualData.TryGetObjectPlacement("medkit_table", out _), "Survives regeneration.");

                LevelEditing.Move(f.Level, medkit, floors[1]);
                Assert.IsFalse(f.Level.VisualData.TryGetObjectPlacement("medkit_table", out _), "Another cell: back to its centre.");

                LevelEditing.SetObjectPlacement(f.Level, medkit, Vector2.zero, 0.5f, 0f);
                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: true);
                Assert.AreEqual(0, f.Level.VisualData.ObjectPlacements.Count, "Cleared with the manual overrides.");

                LevelEditing.SetObjectPlacement(f.Level, medkit, Vector2.zero, 0.5f, 0f);
                LevelEditing.Remove(f.Level, medkit);
                Assert.AreEqual(0, f.Level.VisualData.ObjectPlacements.Count, "Removed with its object.");
            }
        }

        [Test]
        public void KeyCell_NoAutoDecor_ManualDecorAllowed()
        {
            using (var f = new VisualFixture())
            {
                AddDecor(f);
                f.Level.Generation.DecorDensity = 1f;
                LevelAuthoring.GenerateNew(f.Level);
                var floors = FloorCells(f.Level).Where(p => f.Level.AllEntities().All(e => e.Position != p)).ToArray();
                var cell = floors[0];
                var next = floors[1];
                var manual = floors[2];
                Assert.IsFalse(VisualResolver.ResolveDecor(f.Level, cell).IsEmpty, "Density 1: decor before the key.");

                var key = LevelEditing.AddKey(f.Level, cell);
                Assert.IsTrue(VisualResolver.ResolveDecor(f.Level, cell).IsEmpty, "The key wins its cell.");

                LevelEditing.Move(f.Level, key, next);
                Assert.IsFalse(VisualResolver.ResolveDecor(f.Level, cell).IsEmpty, "Auto decor is back where the key left.");
                Assert.IsTrue(VisualResolver.ResolveDecor(f.Level, next).IsEmpty);

                LevelAuthoring.RegenerateVisuals(f.Level, clearOverrides: true);
                LevelEditing.PlaceDecor(f.Level);
                Assert.IsTrue(VisualResolver.ResolveDecor(f.Level, next).IsEmpty, "Regenerate and Place Decor skip key cells.");

                LevelEditing.SetCellOverride(f.Level, manual, CellLayer.Decor, new VisualChoice("decor_barrel"));
                LevelEditing.Move(f.Level, key, manual);
                Assert.AreEqual("decor_barrel", VisualResolver.ResolveDecor(f.Level, manual).VariantId, "Decor placed by hand stays.");

                LevelEditing.Remove(f.Level, key);
                Assert.IsFalse(VisualResolver.ResolveDecor(f.Level, next).IsEmpty);
            }
        }

        private static VisualSource Resolve(LevelData level, GridPosition cell)
        {
            VisualResolver.ResolveDecor(level, cell, out var source);
            return source;
        }
    }
}
