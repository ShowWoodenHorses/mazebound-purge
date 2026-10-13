using System.Collections.Generic;
using System.Linq;
using System.Text;
using Maze.Core.Authoring;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Validation;
using Maze.Core.Visual;
using Maze.Tests.EditMode.Visual;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Validation
{
    public class LevelValidatorTests
    {
        private VisualFixture _fixture;
        private LevelData Level => _fixture.Level;

        [SetUp]
        public void SetUp() => _fixture = new VisualFixture();

        [TearDown]
        public void TearDown() => _fixture.Dispose();

        /// <summary>
        /// 10x4 corridor: row y = 1, x = 1..8 is floor. Start at (1,1), exit at (8,1),
        /// door cell at (5,1) (no DoorData yet). Visuals assigned.
        /// </summary>
        private void BuildCorridor(bool withDoor = true)
        {
            var geometry = new LevelGeometry(10, 4);
            for (var x = 1; x <= 8; x++)
                geometry.SetCell(new GridPosition(x, 1), CellType.Floor);

            Level.ReplaceGeometry(geometry);
            Level.ClearObjects();
            Level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(1, 1)));
            Level.MutableExits.Add(new ExitData("exit_1", new GridPosition(8, 1)));

            if (withDoor)
            {
                geometry.SetCell(new GridPosition(5, 1), CellType.Door);
                Level.MutableDoors.Add(new DoorData("door_1", new GridPosition(5, 1)));
            }

            Reassign();
        }

        private void Reassign() => LevelAuthoring.RegenerateVisuals(Level, clearOverrides: false);

        private ValidationReport Validate() => LevelValidator.Validate(Level);

        private static string Describe(ValidationReport report) =>
            string.Join("\n", report.Issues.Where(i => i.Severity == ValidationSeverity.Error));

        private static void AssertHasError(ValidationReport report, string code)
        {
            Assert.IsTrue(report.Has(code), $"Expected {code}. Got:\n{string.Join("\n", report.Issues)}");
            Assert.IsTrue(report.Issues.Any(i => i.Code == code && i.Severity == ValidationSeverity.Error), $"{code} must be an error.");
            Assert.IsFalse(report.IsValid);
        }

        private static void AssertHasWarning(ValidationReport report, string code)
        {
            Assert.IsTrue(report.Issues.Any(i => i.Code == code && i.Severity == ValidationSeverity.Warning),
                $"Expected warning {code}. Got:\n{string.Join("\n", report.Issues)}");
        }

        // ------------------------------------------------------------ Valid levels

        [Test]
        public void GeneratedLevel_HasNoErrors()
        {
            LevelAuthoring.GenerateNew(Level);
            var report = Validate();
            Assert.IsTrue(report.IsValid, Describe(report));
            Assert.IsTrue(report.Has(ValidationCodes.ShortestPath));
        }

        [Test]
        public void CorridorWithDoor_HasNoErrors()
        {
            BuildCorridor();
            var report = Validate();
            Assert.IsTrue(report.IsValid, Describe(report));
        }

        // ------------------------------------------------------------ Structural

        [Test]
        public void MissingStartOrExit_IsError()
        {
            BuildCorridor();
            Level.MutablePlayerStarts.Clear();
            Level.MutableExits.Clear();
            var report = Validate();
            AssertHasError(report, ValidationCodes.NoPlayerStart);
            AssertHasError(report, ValidationCodes.NoExit);
        }

        [Test]
        public void DuplicateAndEmptyIds_AreErrors()
        {
            BuildCorridor();
            Level.MutableMedkits.Add(new MedkitData("exit_1", new GridPosition(3, 1)));
            Level.MutableMedkits.Add(new MedkitData("", new GridPosition(4, 1)));
            var report = Validate();
            AssertHasError(report, ValidationCodes.DuplicateId);
            AssertHasError(report, ValidationCodes.EmptyId);
        }

        [Test]
        public void ObjectsInWallsOrOutside_AreErrors()
        {
            BuildCorridor();
            Level.PlayerStarts[0].Position = new GridPosition(0, 0);
            Level.MutableKeys.Add(new KeyData("key_1", new GridPosition(50, 1)));
            var report = Validate();
            AssertHasError(report, ValidationCodes.InsideWall);
            AssertHasError(report, ValidationCodes.OutOfBounds);
        }

        [Test]
        public void DoorAndDoorCellMismatch_AreErrors()
        {
            BuildCorridor();
            Level.MutableDoors.Clear();
            Level.MutableDoors.Add(new DoorData("door_2", new GridPosition(3, 1)));
            var report = Validate();
            AssertHasError(report, ValidationCodes.DoorCellWithoutDoor);
            AssertHasError(report, ValidationCodes.DoorNotOnDoorCell);
        }

        [Test]
        public void Intersections_AreErrors_ButZombiesMayShareCells()
        {
            BuildCorridor();
            Level.MutableZombieSpawns.Add(new ZombieSpawnData("zombie_1", new GridPosition(3, 1), _fixture.Walker));
            Level.MutableZombieSpawns.Add(new ZombieSpawnData("zombie_2", new GridPosition(3, 1), _fixture.Walker));
            Reassign();
            Assert.IsTrue(Validate().IsValid, Describe(Validate()));

            Level.MutableMedkits.Add(new MedkitData("medkit_1", new GridPosition(8, 1)));
            Level.MutableZombieSpawns.Add(new ZombieSpawnData("zombie_3", new GridPosition(1, 1), _fixture.Walker));
            Reassign();
            var report = Validate();
            AssertHasError(report, ValidationCodes.Intersection);
            AssertHasError(report, ValidationCodes.ZombieOnPlayerStart);
        }

        [Test]
        public void KeyReferences_MissingIsError_UnusedIsWarning()
        {
            BuildCorridor();
            Level.Doors[0].KeyId = "key_missing";
            Level.MutableKeys.Add(new KeyData("key_1", new GridPosition(2, 1)));
            Reassign();
            var report = Validate();
            AssertHasError(report, ValidationCodes.MissingKeyReference);
            AssertHasWarning(report, ValidationCodes.UnusedKey);
        }

        /// <summary>Corridor with locked doors at x = 3, 5, 7 and their keys right before them.</summary>
        private void BuildThreeLockedDoors()
        {
            BuildCorridor(withDoor: false);
            for (var i = 0; i < 3; i++)
            {
                var x = 3 + i * 2;
                Level.Geometry.SetCell(new GridPosition(x, 1), CellType.Door);
                Level.MutableDoors.Add(new DoorData($"door_{i + 1}", new GridPosition(x, 1), $"key_{i + 1}"));
                Level.MutableKeys.Add(new KeyData($"key_{i + 1}", new GridPosition(x - 1, 1)));
            }

            Reassign();
        }

        [Test]
        public void OneKeyForSeveralDoors_IsError()
        {
            BuildThreeLockedDoors();
            Level.Doors[1].KeyId = "key_1";
            Reassign();
            AssertHasError(Validate(), ValidationCodes.KeySharedByDoors);
        }

        [Test]
        public void ThreeLockedDoors_TwoColours_ValidWithRepeatedColourWarning()
        {
            BuildThreeLockedDoors();
            var report = Validate();
            Assert.IsTrue(report.IsValid, Describe(report));
            AssertHasWarning(report, ValidationCodes.RepeatedKeyColor);
        }

        [Test]
        public void KeyColourDifferentFromDoor_IsError()
        {
            BuildThreeLockedDoors();
            var doorColor = VisualAssigner.ResolvedColor(Level, Level.Doors[0]);
            Level.VisualData.SetObjectOverride("key_1", new VisualChoice(doorColor == "red" ? "key_blue" : "key_red"));
            AssertHasError(Validate(), ValidationCodes.KeyDoorColorMismatch);
        }

        [Test]
        public void LockedDoorWithoutColour_IsError_UnlockedDoorWithColour_IsWarning()
        {
            BuildThreeLockedDoors();
            Level.VisualData.SetObjectOverride("door_1", new VisualChoice("door_01"));
            AssertHasError(Validate(), ValidationCodes.KeyDoorColorMismatch);

            Level.VisualData.ClearObjectOverride("door_1");
            Level.Doors[2].KeyId = null;
            Level.MutableKeys.RemoveAt(2);
            Level.VisualData.SetObjectOverride("door_3", new VisualChoice("door_red"));
            AssertHasWarning(Validate(), ValidationCodes.UnlockedDoorWithColor);
        }

        [Test]
        public void MissingDefinitions_AreErrors()
        {
            BuildCorridor();
            Level.MutableZombieSpawns.Add(new ZombieSpawnData("zombie_1", new GridPosition(3, 1), null));
            Level.MutableWeapons.Add(new WeaponPickupData("weapon_1", new GridPosition(4, 1), null));
            Reassign();
            AssertHasError(Validate(), ValidationCodes.MissingDefinition);
        }

        [Test]
        public void MapFragments_InvalidRegionAndOverlap_AreErrors()
        {
            BuildCorridor();
            Level.MutableMapFragments.Add(new MapFragmentData("fragment_1", new GridPosition(2, 1), new GridRect(0, 0, 5, 4)));
            Level.MutableMapFragments.Add(new MapFragmentData("fragment_2", new GridPosition(3, 1), new GridRect(5, 0, 5, 4)));
            Reassign();
            Assert.IsFalse(Validate().Has(ValidationCodes.FragmentOverlap));

            Level.MapFragments[1].Region = new GridRect(3, 0, 6, 4);
            AssertHasError(Validate(), ValidationCodes.FragmentOverlap);

            Level.MapFragments[1].Region = new GridRect(5, 0, 9, 4);
            AssertHasError(Validate(), ValidationCodes.InvalidFragmentRegion);
        }

        [Test]
        public void Patrols_References_Points_AndRoutesThroughClosedDoors()
        {
            BuildCorridor();
            Level.MutablePatrols.Add(new PatrolData("patrol_1", new[] { new GridPosition(2, 1), new GridPosition(4, 1) }));
            Level.MutableZombieSpawns.Add(new ZombieSpawnData("zombie_1", new GridPosition(3, 1), _fixture.Walker, patrolId: "patrol_1"));
            Reassign();
            Assert.IsTrue(Validate().IsValid, Describe(Validate()));
            Assert.IsFalse(Validate().Has(ValidationCodes.PatrolPointUnreachable));

            // Route crosses the closed door at (5,1): zombies do not open doors.
            Level.Patrols[0].MutablePoints.Add(new GridPosition(7, 1));
            AssertHasWarning(Validate(), ValidationCodes.PatrolPointUnreachable);

            Level.Doors[0].IsInitiallyOpen = true;
            Assert.IsFalse(Validate().Has(ValidationCodes.PatrolPointUnreachable));

            Level.Patrols[0].MutablePoints.Add(new GridPosition(5, 0));
            Level.ZombieSpawns[0].PatrolId = "patrol_missing";
            var report = Validate();
            AssertHasError(report, ValidationCodes.InvalidPatrolPoint);
            AssertHasError(report, ValidationCodes.MissingPatrolReference);
            AssertHasWarning(report, ValidationCodes.UnusedPatrol);
        }

        // ------------------------------------------------------------ Gameplay

        [Test]
        public void KeyBeforeLockedDoor_IsValid()
        {
            BuildCorridor();
            Level.Doors[0].KeyId = "key_1";
            Level.MutableKeys.Add(new KeyData("key_1", new GridPosition(3, 1)));
            Reassign();
            Assert.IsTrue(Validate().IsValid, Describe(Validate()));
        }

        [Test]
        public void KeyBehindItsLockedDoor_SoftLocksTheExit()
        {
            BuildCorridor();
            Level.Doors[0].KeyId = "key_1";
            Level.MutableKeys.Add(new KeyData("key_1", new GridPosition(7, 1)));
            Level.MutableWeapons.Add(new WeaponPickupData("weapon_1", new GridPosition(6, 1), _fixture.Pistol));
            Reassign();

            var report = Validate();
            AssertHasError(report, ValidationCodes.NoReachableExit);
            AssertHasError(report, ValidationCodes.ExitUnreachable);
            AssertHasWarning(report, ValidationCodes.KeyUnreachable);
            AssertHasWarning(report, ValidationCodes.ObjectUnreachable);
        }

        [Test]
        public void ExitReachableFromOneStartOnly_IsErrorForTheOtherStart()
        {
            BuildCorridor(withDoor: false);
            Level.Geometry.SetCell(new GridPosition(6, 2), CellType.Floor);
            Level.Geometry.SetCell(new GridPosition(5, 1), CellType.Wall);
            Level.MutablePlayerStarts.Add(new PlayerStartData("start_2", new GridPosition(6, 2)));
            Reassign();

            var report = Validate();
            AssertHasError(report, ValidationCodes.NoReachableExit);
            Assert.AreEqual("start_1", report.Issues.First(i => i.Code == ValidationCodes.NoReachableExit).EntityId);
            Assert.IsFalse(report.Has(ValidationCodes.ExitUnreachable));
        }

        [Test]
        public void DoorNotInPassage_IsWarning()
        {
            BuildCorridor(withDoor: false);
            Level.Geometry.SetCell(new GridPosition(8, 2), CellType.Door);
            Level.MutableDoors.Add(new DoorData("door_1", new GridPosition(8, 2)));
            Reassign();
            AssertHasWarning(Validate(), ValidationCodes.DoorWithoutPassage);
        }

        // ------------------------------------------------------------ Visual

        [Test]
        public void NoTheme_IsError()
        {
            BuildCorridor();
            Level.VisualTheme = null;
            AssertHasError(Validate(), ValidationCodes.NoVisualTheme);
        }

        [Test]
        public void MissingSetForPresentObjects_IsError()
        {
            BuildCorridor();
            _fixture.Theme.SetSet(VisualKind.Door, null);
            AssertHasError(Validate(), ValidationCodes.MissingVisualSet);
        }

        [Test]
        public void BrokenPrefabAndBadDefault_AreErrors()
        {
            BuildCorridor();
            _fixture.Exit.MutableVariants.Add(new VisualVariant("exit_no_prefab", 0));
            _fixture.Exit.DefaultVariantId = "exit_missing";
            var report = Validate();
            AssertHasError(report, ValidationCodes.BrokenPrefabReference);
            AssertHasError(report, ValidationCodes.InvalidDefaultVariant);
        }

        [Test]
        public void OverrideWithUnknownVariant_IsError_AndIsNotSilentlyReplaced()
        {
            BuildCorridor();
            Level.VisualData.SetCellOverride(new GridPosition(2, 1), CellLayer.Floor, new VisualChoice("floor_does_not_exist"));
            Level.VisualData.SetObjectOverride("exit_1", new VisualChoice("exit_does_not_exist"));
            Level.VisualData.SetObjectOverride("ghost_1", new VisualChoice("exit_01"));

            var report = Validate();
            AssertHasError(report, ValidationCodes.UnknownVariant);
            AssertHasWarning(report, ValidationCodes.OverrideForMissingEntity);
            Assert.AreEqual(2, report.Count(ValidationCodes.UnknownVariant));
        }

        [Test]
        public void GeometryEditWithoutReassign_IsDetected()
        {
            BuildCorridor();
            Level.Geometry.SetCell(new GridPosition(3, 2), CellType.Floor);

            // The floor under the removed wall already exists: only neighbouring wall shapes are out of date.
            var stale = Validate();
            AssertHasWarning(stale, ValidationCodes.StaleWallVisual);
            Assert.IsTrue(stale.IsValid, Describe(stale));

            VisualAssigner.ReassignAround(Level, new GridPosition(3, 2));
            var report = Validate();
            Assert.IsFalse(report.Has(ValidationCodes.StaleWallVisual));
            Assert.IsTrue(report.IsValid, Describe(report));
        }

        [Test]
        public void WallOverrideOnNonWallCell_IsIgnoredWithWarning()
        {
            BuildCorridor();
            Level.VisualData.SetCellOverride(new GridPosition(2, 1), CellLayer.Wall, new VisualChoice("wall_cross"));
            var report = Validate();
            AssertHasWarning(report, ValidationCodes.OverrideLayerMismatch);
            Assert.IsTrue(report.IsValid, Describe(report));
        }

        [Test]
        public void ResizedGeometry_ReportsAssignmentsOutOfSync()
        {
            BuildCorridor();
            var bigger = new LevelGeometry(12, 4);
            for (var x = 1; x <= 8; x++)
                bigger.SetCell(new GridPosition(x, 1), CellType.Floor);
            bigger.SetCell(new GridPosition(5, 1), CellType.Door);
            Level.ReplaceGeometry(bigger);

            AssertHasError(Validate(), ValidationCodes.AssignmentsOutOfSync);
        }

        [Test]
        public void WallCategoryWithoutVariants_WarnsWithDefault_ErrorsWithout()
        {
            BuildCorridor();
            _fixture.Wall.MutableVariants.RemoveAll(v => v.Category == VisualCategory.Straight);
            Reassign();
            AssertHasWarning(Validate(), ValidationCodes.WallCategoryWithoutVariants);

            _fixture.Wall.DefaultVariantId = null;
            Reassign();
            var report = Validate();
            AssertHasError(report, ValidationCodes.WallCategoryWithoutVariants);
            AssertHasError(report, ValidationCodes.MissingVisual);
        }

        [Test]
        public void ManyIdenticalIssues_AreTruncated_ButCounted()
        {
            LevelAuthoring.GenerateNew(Level);
            _fixture.Floor.MutableVariants.Clear();
            Reassign();

            var report = Validate();
            var listed = report.Issues.Count(i => i.Code == ValidationCodes.MissingVisual && i.Severity == ValidationSeverity.Error);
            Assert.AreEqual(ValidationReport.MaxIssuesPerCode, listed);
            Assert.Greater(report.Count(ValidationCodes.MissingVisual), ValidationReport.MaxIssuesPerCode);
            Assert.AreEqual(report.Count(ValidationCodes.MissingVisual), report.ErrorCount);
        }

        // ------------------------------------------------------------ Mass test (ТЗ §111)

        [Test]
        public void MassTest_1000Seeds_GenerateAssignValidate_NoErrors()
        {
            var sizes = new DeterministicRandom(111);
            var errorsByCode = new Dictionary<string, int>();
            var examples = new StringBuilder();
            var warnings = 0;

            for (var seed = 0; seed < 1000; seed++)
            {
                var g = Level.Generation;
                g.Width = sizes.NextInt(2, 21) * 2 + 1;
                g.Height = sizes.NextInt(2, 21) * 2 + 1;
                g.MazeSeed = seed;
                g.VisualSeed = seed * 31 + 7;
                g.LoopDensity = (float)sizes.NextDouble() * 0.5f;
                g.InitialPlayerStartCount = sizes.NextInt(1, 3);
                g.InitialExitCount = sizes.NextInt(1, 3);

                LevelAuthoring.GenerateNew(Level);
                var report = Validate();
                warnings += report.WarningCount;

                foreach (var issue in report.Issues.Where(i => i.Severity == ValidationSeverity.Error))
                {
                    errorsByCode.TryGetValue(issue.Code, out var count);
                    errorsByCode[issue.Code] = count + 1;
                    if (count == 0)
                        examples.AppendLine($"seed {seed} {g.Width}x{g.Height}: {issue}");
                }
            }

            Assert.IsEmpty(errorsByCode,
                "Errors by code:\n" + string.Join("\n", errorsByCode.Select(p => $"{p.Key}: {p.Value}")) + "\nExamples:\n" + examples);
            TestContext.WriteLine($"1000 generated levels: 0 errors, {warnings} warnings.");
        }
    }
}
