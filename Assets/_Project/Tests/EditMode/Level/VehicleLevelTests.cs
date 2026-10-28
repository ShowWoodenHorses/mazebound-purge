using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Generation;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Validation;
using Purge.Core.Vehicles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Level
{
    /// <summary>Stage 9в: vehicles on the level — the generator's step, the validator.</summary>
    public class VehicleLevelTests
    {
        private readonly List<Object> _created = new List<Object>();
        private VehicleDefinition _light, _heavy;
        private BiomeRecipe _recipe;

        [SetUp]
        public void SetUp()
        {
            _light = Make<VehicleDefinition>();
            _light.Configure("light", new HullModule { Health = 300f, Length = 3.5f, Width = 1.4f });
            _heavy = Make<VehicleDefinition>();
            _heavy.Configure("heavy", new HullModule { Health = 500f, Length = 5.2f, Width = 2f });
            var tree = Make<ObstacleDefinition>();
            tree.Configure("tree", true, true, false, false);
            var stone = Make<ObstacleDefinition>();
            stone.Configure("stone", true, false, false, false);
            _recipe = Make<BiomeRecipe>();
            var obstacles = new ObstaclesStep();
            obstacles.MutableObstacles.Add(new WeightedObstacle(tree, 60));
            obstacles.MutableObstacles.Add(new WeightedObstacle(stone, 40));
            var vehicles = new VehiclesStep();
            vehicles.MutableVehicles.Add(_light);
            vehicles.MutableVehicles.Add(_heavy);
            _recipe.MutableSteps.AddRange(new GenerationStep[]
            {
                new PerimeterStep(), new StartStep(), new ArenaStep(), new SpawnPointsStep(), new ClearingsStep(),
                new TrailsStep(), obstacles, new GroundStep(), new GrassStep(), new ObjectsStep(), new DecorStep(), vehicles,
            });
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
        public void Step_LightAndHeavy_OnFreeFloorWithARunway_NothingCutOff()
        {
            var cells = new List<GridPosition>();
            for (var seed = 0; seed < 8; seed++)
            {
                var tag = $"seed {seed}";
                var level = Level(seed);
                Assert.AreEqual(2, level.Vehicles.Count, tag);
                Assert.IsTrue(level.Vehicles.Any(v => v.Definition == _light) && level.Vehicles.Any(v => v.Definition == _heavy), tag);
                var start = level.PlayerStarts[0].Position;
                foreach (var vehicle in level.Vehicles)
                {
                    Assert.IsTrue(vehicle.Id.StartsWith("vehicle_"), tag);
                    Assert.AreEqual(0f, vehicle.Heading % 45f, 1e-3f, tag);
                    Assert.GreaterOrEqual(Vector2.Distance(new Vector2(vehicle.Position.X, vehicle.Position.Y),
                        new Vector2(start.X, start.Y)), 6f, tag);

                    // The way ahead: its body slid 8 cells forward stays on free floor.
                    var hull = vehicle.Definition.Hull;
                    var heading = vehicle.Heading * Mathf.Deg2Rad;
                    var center = new Vector2(vehicle.Position.X, vehicle.Position.Y) + VehicleFootprint.Forward(heading) * 8f;
                    VehicleFootprint.Cells(center, heading, hull.Length * 0.5f, hull.Width * 0.5f, 0, VehicleFootprint.BlockMargin, cells);
                    foreach (var cell in cells)
                        Assert.IsTrue(ArenaLayout.IsWalkable(level.Geometry, cell), $"{tag}: {vehicle.Id} runway at {cell}");
                }

                var report = LevelValidator.Validate(level);
                var issues = report.Issues
                    .Where(i => i.Code == ValidationCodes.VehicleBlocked || i.Code == ValidationCodes.LevelDisconnected)
                    .Select(i => i.ToString()).ToList();
                CollectionAssert.IsEmpty(issues, tag);
            }
        }

        [Test]
        public void Step_KeptVehicle_IsNotRepeated()
        {
            var level = Level(2);
            var light = level.Vehicles.Single(v => v.Definition == _light);
            LevelEditing.AddLockedArea(level, new GridRect(light.Position.X, light.Position.Y, 1, 1));
            LevelAuthoring.Generate(level);
            Assert.AreEqual(1, level.Vehicles.Count(v => v.Definition == _light), "The kept one stays, no second.");
            Assert.AreEqual(1, level.Vehicles.Count(v => v.Definition == _heavy));
            Assert.AreEqual(light.Position, level.Vehicles.Single(v => v.Definition == _light).Position);
        }

        [Test]
        public void Validator_BodyOnATree_IsAnError()
        {
            var level = Level(1);
            var vehicle = level.Vehicles[0];
            Assert.IsFalse(LevelValidator.Validate(level).Has(ValidationCodes.VehicleBlocked));
            var cells = new List<GridPosition>();
            VehicleFootprint.Cells(vehicle, cells);
            var tree = Make<ObstacleDefinition>();
            tree.Configure("tree2", true, true, false, false);
            LevelEditing.SetObstacle(level, cells.First(c => c != vehicle.Position), tree);
            Assert.IsTrue(LevelValidator.Validate(level).Has(ValidationCodes.VehicleBlocked));
        }
    }
}
