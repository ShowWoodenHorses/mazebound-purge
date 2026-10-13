using System.Linq;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Lighting;
using Maze.Core.Visual;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Visual
{
    /// <summary>Auto placement of light sources: along walls, spacing by density, deterministic, manual lights kept.</summary>
    public class LightPlacerTests
    {
        private LevelData _level;
        private VisualTheme _theme;

        [SetUp]
        public void SetUp()
        {
            _theme = ScriptableObject.CreateInstance<VisualTheme>();
            _theme.Lighting.LightPresets.Add(new LightPreset { Id = "torch", Weight = 3f, Color = Color.red, Radius = 4f });
            _theme.Lighting.LightPresets.Add(new LightPreset { Id = "lamp", Weight = 1f, Color = Color.blue, Radius = 5f });

            // 15x15 room: border walls, one inner wall column at x = 7 (y 1..10).
            var geometry = new LevelGeometry(15, 15);
            for (var y = 1; y < 14; y++)
            for (var x = 1; x < 14; x++)
                geometry.SetCell(new GridPosition(x, y), x == 7 && y <= 10 ? CellType.Wall : CellType.Floor);

            _level = ScriptableObject.CreateInstance<LevelData>();
            _level.ReplaceGeometry(geometry);
            _level.VisualTheme = _theme;
            _level.Generation.VisualSeed = 42;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_theme);
        }

        [Test]
        public void DensityZero_PlacesNothing()
        {
            _level.Generation.LightDensity = 0f;
            LightPlacer.PlaceAll(_level, keepManual: true);
            Assert.AreEqual(0, _level.Lights.Count);
        }

        [Test]
        public void Lights_AreOnFloorNextToAWall_ShiftedToIt_Spaced()
        {
            _level.Generation.LightDensity = 1f;
            LightPlacer.PlaceAll(_level, keepManual: true);

            Assert.That(_level.Lights.Count, Is.GreaterThan(4));
            foreach (var light in _level.Lights)
            {
                Assert.AreEqual(CellType.Floor, _level.Geometry.GetCell(light.Cell), light.Id);
                Assert.AreEqual(LightPlacer.WallOffset, light.Offset.magnitude, 1e-5f);
                var side = new GridPosition(Mathf.RoundToInt(light.Offset.x / LightPlacer.WallOffset),
                    Mathf.RoundToInt(light.Offset.y / LightPlacer.WallOffset));
                Assert.AreEqual(CellType.Wall, _level.Geometry.GetCell(light.Cell + side), $"{light.Id} faces a wall.");
                Assert.IsTrue(light.IsGenerated);
                Assert.That(light.Color == Color.red || light.Color == Color.blue);
            }

            foreach (var a in _level.Lights)
            foreach (var b in _level.Lights)
                if (a != b)
                    Assert.That((a.Point - b.Point).magnitude, Is.GreaterThanOrEqualTo(LightPlacer.DenseSpacing - 1e-4f));

            var sparse = _level.Lights.Count;
            _level.Generation.LightDensity = 0.2f;
            LightPlacer.PlaceAll(_level, keepManual: true);
            Assert.That(_level.Lights.Count, Is.LessThan(sparse), "Lower density, fewer lights.");
        }

        [Test]
        public void Placement_IsDeterministicBySeed()
        {
            _level.Generation.LightDensity = 0.6f;
            LightPlacer.PlaceAll(_level, keepManual: true);
            var first = _level.Lights.Select(l => l.Point).ToList();
            LightPlacer.PlaceAll(_level, keepManual: true);
            CollectionAssert.AreEqual(first, _level.Lights.Select(l => l.Point).ToList());

            _level.Generation.VisualSeed = 7;
            LightPlacer.PlaceAll(_level, keepManual: true);
            CollectionAssert.AreNotEqual(first, _level.Lights.Select(l => l.Point).ToList());
        }

        [Test]
        public void ManualLights_SurviveRegeneration_UnlessCleared_AndKeepOthersAway()
        {
            var manual = new LightSourceData("light_99", new GridPosition(3, 3), Vector2.zero, Color.green, 4f, 1f, 0f, false);
            _level.MutableLights.Add(manual);
            _level.Generation.LightDensity = 1f;

            LightPlacer.PlaceAll(_level, keepManual: true);
            Assert.Contains(manual, _level.Lights.ToList());
            foreach (var light in _level.Lights)
                if (light != manual)
                    Assert.That((light.Point - manual.Point).magnitude, Is.GreaterThanOrEqualTo(LightPlacer.DenseSpacing - 1e-4f));
            Assert.AreEqual(_level.Lights.Count, _level.Lights.Select(l => l.Id).Distinct().Count(), "Unique ids.");

            LightPlacer.PlaceAll(_level, keepManual: false);
            Assert.IsFalse(_level.Lights.Contains(manual));
        }

        [Test]
        public void NoAutoLight_InExitCells()
        {
            _level.Generation.LightDensity = 1f;
            LightPlacer.PlaceAll(_level, keepManual: true);
            var lit = _level.Lights.Select(l => l.Cell).ToList();
            Assert.IsNotEmpty(lit);
            for (var i = 0; i < lit.Count; i++)
                _level.MutableExits.Add(new ExitData("exit_" + i, lit[i]));

            LightPlacer.PlaceAll(_level, keepManual: true);
            foreach (var light in _level.Lights)
                CollectionAssert.DoesNotContain(lit, light.Cell, light.Id);
        }

        [Test]
        public void ColouredKey_GetsTwoLightsOfItsColour_InItsCorridor_EvenWithoutDensity()
        {
            var keySet = ScriptableObject.CreateInstance<VisualSet>();
            keySet.Kind = VisualKind.Key;
            keySet.MutableVariants.Add(VisualFixture.Variant("key_red", 1, colorTag: "red"));
            _theme.SetSet(VisualKind.Key, keySet);

            // Right of the inner wall: cells left of it are close in a straight line but far along the floor.
            var keyCell = new GridPosition(8, 5);
            var key = new KeyData("key_1", keyCell);
            _level.MutableKeys.Add(key);
            _level.Generation.LightDensity = 0f;

            try
            {
                LightPlacer.PlaceAll(_level, keepManual: true);
                Assert.AreEqual(0, _level.Lights.Count, "A key without a coloured visual gets no lights.");

                _level.VisualData.SetObjectAssignment(key.Id, new VisualChoice("key_red"));
                LightPlacer.PlaceAll(_level, keepManual: true);

                Assert.AreEqual(LightPlacer.KeyLightCount, _level.Lights.Count);
                Assert.AreEqual(_level.Lights.Count, _level.Lights.Select(l => l.Cell).Distinct().Count());
                foreach (var light in _level.Lights)
                {
                    Assert.AreEqual(Color.red, light.Color);
                    Assert.That(light.Cell.X, Is.GreaterThanOrEqualTo(8), light.Id);
                    var steps = Mathf.Abs(light.Cell.X - keyCell.X) + Mathf.Abs(light.Cell.Y - keyCell.Y);
                    Assert.That(steps, Is.LessThanOrEqualTo(LightPlacer.KeyLightSteps), light.Id);
                    Assert.IsTrue(light.IsGenerated);
                }

                // Other auto lights keep their spacing from the key lights.
                // (The theme's "torch" preset is red too, so key lights are told apart by position.)
                var keyPoints = _level.Lights.Select(l => l.Point).ToList();
                _level.Generation.LightDensity = 1f;
                LightPlacer.PlaceAll(_level, keepManual: true);
                Assert.That(_level.Lights.Count, Is.GreaterThan(LightPlacer.KeyLightCount));
                CollectionAssert.IsSubsetOf(keyPoints, _level.Lights.Select(l => l.Point).ToList());
                foreach (var light in _level.Lights.Where(l => !keyPoints.Contains(l.Point)))
                foreach (var keyPoint in keyPoints)
                    Assert.That((light.Point - keyPoint).magnitude, Is.GreaterThanOrEqualTo(LightPlacer.DenseSpacing - 1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(keySet);
            }
        }
    }
}
