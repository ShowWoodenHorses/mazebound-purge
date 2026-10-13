using System.Linq;
using Maze.Core.Authoring;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Lighting;
using Maze.Core.Validation;
using Maze.Core.Visual;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Visual
{
    /// <summary>Fixtures of light sources (torches): saved variant, wall the fixture hangs on, usage and validation.</summary>
    public class LightFixturesTests
    {
        private LevelData _level;
        private VisualTheme _theme;
        private VisualSet _lightSet;

        [SetUp]
        public void SetUp()
        {
            _theme = ScriptableObject.CreateInstance<VisualTheme>();
            _theme.Lighting.LightPresets.Add(new LightPreset { Id = "torch", Weight = 1f, Color = Color.red, Radius = 4f });
            _lightSet = ScriptableObject.CreateInstance<VisualSet>();
            _lightSet.Kind = VisualKind.Light;
            _lightSet.MutableVariants.Add(VisualFixture.Variant("torch_a", 1));
            _lightSet.MutableVariants.Add(VisualFixture.Variant("torch_b", 1));
            _theme.SetSet(VisualKind.Light, _lightSet);

            // 9x9 room: border walls, a wall at (4, 4) in the middle.
            var geometry = new LevelGeometry(9, 9);
            for (var y = 1; y < 8; y++)
            for (var x = 1; x < 8; x++)
                geometry.SetCell(new GridPosition(x, y), x == 4 && y == 4 ? CellType.Wall : CellType.Floor);

            _level = ScriptableObject.CreateInstance<LevelData>();
            _level.ReplaceGeometry(geometry);
            _level.VisualTheme = _theme;
            _level.Generation.VisualSeed = 7;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_lightSet);
            Object.DestroyImmediate(_theme);
        }

        [Test]
        public void AutoPlacedLights_GetAFixtureFromTheLightSet_Deterministically()
        {
            _level.Generation.LightDensity = 1f;
            LightPlacer.PlaceAll(_level, keepManual: true);

            Assert.That(_level.Lights.Count, Is.GreaterThan(0));
            var first = _level.Lights.Select(l => l.Visual.VariantId).ToList();
            foreach (var id in first)
                Assert.IsNotNull(_lightSet.FindVariant(id), id);

            LightPlacer.PlaceAll(_level, keepManual: true);
            CollectionAssert.AreEqual(first, _level.Lights.Select(l => l.Visual.VariantId).ToList());
        }

        [Test]
        public void ManualLight_GetsFixture_AndKeepsAChosenOne()
        {
            var light = LevelEditing.AddLight(_level, new GridPosition(1, 3));
            Assert.IsFalse(light.Visual.IsEmpty);

            LevelEditing.SetLightVisual(light, "torch_b");
            LevelEditing.PlaceLights(_level);
            Assert.AreEqual("torch_b", light.Visual.VariantId, "Auto placement keeps the chosen fixture.");
        }

        [Test]
        public void Fixture_HangsOnTheWallTheLightIsShiftedTo_FacingAway()
        {
            // Shifted West towards the border wall.
            var light = new LightSourceData("light_1", new GridPosition(1, 3), new Vector2(-0.42f, 0.1f), Color.white, 4f, 1f, 0f, false);
            Assert.IsTrue(LightFixtures.TryGetPose(_level.Geometry, light, out var offset, out var yaw));
            Assert.AreEqual(-0.5f, offset.x, 1e-5f, "On the wall face.");
            Assert.AreEqual(0.1f, offset.z, 1e-5f, "Keeps the shift along the wall.");
            Assert.AreEqual(LightFixtures.MountHeight, offset.y, 1e-5f);
            Assert.AreEqual(90f, yaw, 1e-5f, "Points East, away from the wall.");

            // Shifted North towards the inner wall at (4, 4).
            light = new LightSourceData("light_2", new GridPosition(4, 3), new Vector2(0f, 0.42f), Color.white, 4f, 1f, 0f, false);
            Assert.IsTrue(LightFixtures.TryGetPose(_level.Geometry, light, out offset, out yaw));
            Assert.AreEqual(0.5f, offset.z, 1e-5f);
            Assert.AreEqual(180f, yaw, 1e-5f, "Points South.");
        }

        [Test]
        public void Fixture_WithoutWallInTheShift_UsesAWallAround_OrNone()
        {
            // Shifted East into open floor; the only wall around (2, 1) is South (border).
            var light = new LightSourceData("light_1", new GridPosition(2, 1), new Vector2(0.3f, 0f), Color.white, 4f, 1f, 0f, false);
            Assert.IsTrue(LightFixtures.TryGetWallSide(_level.Geometry, light, out var side));
            Assert.AreEqual(Direction.South, side);

            var open = new LightSourceData("light_2", new GridPosition(2, 5), Vector2.zero, Color.white, 4f, 1f, 0f, false);
            Assert.IsFalse(LightFixtures.TryGetPose(_level.Geometry, open, out _, out _), "No wall next to it: no fixture.");
        }

        [Test]
        public void Usage_IncludesFixtures_AndValidatorReportsUnknownOnes()
        {
            var light = LevelEditing.AddLight(_level, new GridPosition(1, 3));
            LevelEditing.SetLightVisual(light, "torch_a");
            CollectionAssert.Contains(LevelVisualUsage.Collect(_level), new VisualKey(VisualKind.Light, "torch_a"));

            LevelEditing.SetLightVisual(light, "torch_missing");
            var report = LevelValidator.Validate(_level);
            Assert.IsTrue(report.Issues.Any(i => i.Code == ValidationCodes.UnknownVariant && i.EntityId == light.Id));
        }

        [Test]
        public void Exit_FacesAnOpenSideWithAWallBehind()
        {
            // Corridor cell (2, 1): walls South (border) and none North; West/East open → faces North (wall behind).
            Assert.AreEqual((int)Direction.North, ObjectOrientation.For(VisualKind.Exit, _level.Geometry, new GridPosition(2, 1)));

            // Dead end facing East: walls N, S, W.
            var geometry = new LevelGeometry(5, 3);
            geometry.SetCell(new GridPosition(1, 1), CellType.Floor);
            geometry.SetCell(new GridPosition(2, 1), CellType.Floor);
            geometry.SetCell(new GridPosition(3, 1), CellType.Floor);
            Assert.AreEqual((int)Direction.East, ObjectOrientation.For(VisualKind.Exit, geometry, new GridPosition(1, 1)));
        }
    }
}
