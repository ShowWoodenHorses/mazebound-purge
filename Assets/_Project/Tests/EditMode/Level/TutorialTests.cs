using System.Collections.Generic;
using System.Threading;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Validation;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Level;
using Maze.Gameplay.Map;
using Maze.Gameplay.Navigation;
using Maze.Gameplay.Pickups;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Spatial;
using Maze.Gameplay.Tutorial;
using Maze.Gameplay.Weapons;
using Maze.Gameplay.Zombies;
using Maze.Tests.EditMode.Player;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Level
{
    /// <summary>Tutorial steps: order, finishing by game state, skipping steps done ahead, the route to the target.</summary>
    public class TutorialTests
    {
        private const float Dt = 1f / 60f;

        private LevelData _level;
        private TutorialScript _script;
        private PlayerDefinition _definition;
        private ZombieDefinition _zombieDefinition;
        private FakePlayerInput _input;
        private DoorSystem _doors;
        private PlayerSystem _player;
        private PlayerHealth _health;
        private PickupSystem _pickups;
        private MapSystem _map;
        private ZombieSystem _zombies;
        private RouteHintSystem _route;
        private TutorialSystem _tutorial;
        private readonly List<int> _started = new List<int>();
        private int _finished;

        /// <summary>
        /// Corridor x = 1..10 at y = 1: start (1,1), medkit_1 at (2,1), key_1 at (3,1) for the locked door (5,1), zombie_1 at (9,1), exit (10,1).
        /// </summary>
        private void Build(params TutorialStep[] steps)
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(12, 3);
            for (var x = 1; x <= 10; x++) geometry.SetCell(new GridPosition(x, 1), CellType.Floor);
            geometry.SetCell(new GridPosition(5, 1), CellType.Door);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(1, 1)));
            _level.MutableDoors.Add(new DoorData("door_1", new GridPosition(5, 1), keyId: "key_1"));
            _level.MutableKeys.Add(new KeyData("key_1", new GridPosition(3, 1)));
            _level.MutableExits.Add(new ExitData("exit_1", new GridPosition(10, 1)));
            _level.MutableMedkits.Add(new MedkitData("medkit_1", new GridPosition(2, 1)));
            _zombieDefinition = ScriptableObject.CreateInstance<ZombieDefinition>();
            _level.MutableZombieSpawns.Add(new ZombieSpawnData("zombie_1", new GridPosition(9, 1), _zombieDefinition, Direction.West));

            if (steps.Length > 0)
            {
                _script = ScriptableObject.CreateInstance<TutorialScript>();
                _script.MutableSteps.AddRange(steps);
                _level.Tutorial = _script;
            }

            _definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _definition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _input = new FakePlayerInput();
            var grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            var passability = new LevelPassability(grid, _doors);
            var occupancy = new OccupancyMap(grid);
            var inventory = new PlayerInventory();
            var health = _health = new PlayerHealth(_definition);
            var weapons = new WeaponSystem(_input);
            var sounds = new SoundEventBus();
            _player = new PlayerSystem(_level, _definition, _input, passability, occupancy,
                new LevelLaunchOptions(startIndex: 0), Aim.FreeNoAssist);
            _map = new MapSystem(_level);
            _pickups = new PickupSystem(_level, _player, health, inventory, weapons, sounds, _map);
            _route = new RouteHintSystem(_level, grid, _doors, _player, inventory, _map, _pickups);
            _zombies = new ZombieSystem(_level, new NavigationSystem(grid, passability, _doors), _player, health, occupancy,
                new SpatialQueryService(grid, _doors), sounds);
            _tutorial = new TutorialSystem(_level, _player, _pickups, _doors, _zombies, _route);
            _tutorial.StepStarted += _started.Add;
            _tutorial.Finished += () => _finished++;
            _started.Clear();
            _finished = 0;

            _pickups.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _route.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _zombies.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _tutorial.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _tutorial?.Dispose();
            _zombies?.Dispose();
            _route?.Dispose();
            _pickups?.Dispose();
            _map?.Dispose();
            foreach (var o in new Object[] { _level, _script, _definition, _zombieDefinition })
                if (o != null) Object.DestroyImmediate(o);
            _script = null;
        }

        private void WalkTo(int x)
        {
            for (var i = 0; i < 600 && _player.Cell.X != x; i++)
            {
                _input.Move = new Vector2(Mathf.Sign(x - _player.Cell.X), 0f);
                _player.Tick(Dt);
            }

            _input.Move = Vector2.zero;
            Assert.AreEqual(x, _player.Cell.X, "The player walked there.");
        }

        [Test]
        public void Steps_FinishByTheGameState_InOrder_TheLineLeadsToEachTarget()
        {
            Build(new TutorialStep("tutorial.move", TutorialGoal.Reach, cell: new Vector2Int(2, 1)),
                new TutorialStep("tutorial.key", TutorialGoal.Collect, "key_1"),
                new TutorialStep("tutorial.unlock", TutorialGoal.UnlockDoor, "door_1"),
                new TutorialStep("tutorial.map", TutorialGoal.OpenMap),
                new TutorialStep("tutorial.zombie", TutorialGoal.Kill, "zombie_1"));

            Assert.IsTrue(_tutorial.IsActive);
            CollectionAssert.AreEqual(new[] { 0 }, _started);
            Assert.AreEqual(RouteTarget.Guide, _route.Target, "The line leads to the step's cell.");
            Assert.AreEqual(new GridPosition(2, 1), _route.TargetCell);
            Assert.IsTrue(_tutorial.TryGetTarget(out var target));
            Assert.AreEqual(new Vector2(2f, 1f), target);

            WalkTo(2);
            Assert.AreEqual(1, _tutorial.StepIndex, "Reached: the key is next.");
            Assert.AreEqual(new GridPosition(3, 1), _route.TargetCell, "The line now leads to the key.");

            WalkTo(3);
            Assert.AreEqual(2, _tutorial.StepIndex, "Key taken: unlock the door.");
            Assert.AreEqual(new GridPosition(5, 1), _route.TargetCell);

            _doors.Unlock("door_1");
            Assert.AreEqual(3, _tutorial.StepIndex, "Unlocked: open the map.");
            Assert.IsFalse(_tutorial.TryGetTarget(out _), "The map step points at nothing.");
            Assert.AreNotEqual(RouteTarget.Guide, _route.Target, "No line without a target.");

            _tutorial.ReportMapOpened();
            Assert.AreEqual(4, _tutorial.StepIndex);
            Assert.AreEqual(new GridPosition(9, 1), _route.TargetCell, "The line leads to the zombie.");

            _zombies.Zombies[0].ApplyDamage(10000f, Vector2.left);
            Assert.IsTrue(_tutorial.IsFinished);
            Assert.AreEqual(1, _finished);
            Assert.IsFalse(_route.IsActive, "The tutorial line is gone with the last step.");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, _started);
        }

        [Test]
        public void StepDoneAhead_IsSkipped_LastStepWithoutGoalNeverFinishes()
        {
            Build(new TutorialStep("tutorial.move", TutorialGoal.Reach, cell: new Vector2Int(4, 1)),
                new TutorialStep("tutorial.key", TutorialGoal.Collect, "key_1"),
                new TutorialStep("tutorial.exit", TutorialGoal.None, "exit_1"));

            WalkTo(4); // Over the key on the way.
            CollectionAssert.AreEqual(new[] { 0, 2 }, _started, "The key step was done ahead: skipped.");
            Assert.AreEqual(_level.Tutorial.Steps[2], _tutorial.Current);
            Assert.IsTrue(_tutorial.TryGetTarget(out var target));
            Assert.AreEqual(new Vector2(10f, 1f), target, "The arrow points at the exit.");
            Assert.AreEqual(0, _finished);
        }

        [Test]
        public void ReachAMedkit_UsedOnEntering_FinishesTheStep()
        {
            Build(new TutorialStep("tutorial.medkit", TutorialGoal.Reach, "medkit_1"),
                new TutorialStep("tutorial.key", TutorialGoal.Collect, "key_1"));

            _health.Damage(10);
            WalkTo(2); // Hurt: the medkit is used and gone before the step is checked.
            Assert.IsFalse(_pickups.IsLying("medkit_1"), "The medkit was used.");
            CollectionAssert.AreEqual(new[] { 0, 1 }, _started, "Reached: the next step started.");
        }

        [Test]
        public void HintAskedByThePlayer_IsNotReplaced()
        {
            Build(new TutorialStep("tutorial.key", TutorialGoal.Collect, "key_1"),
                new TutorialStep("tutorial.exit", TutorialGoal.None, "exit_1"));

            _route.Request(); // The magnifier on the map: the nearest goal (here the exit).
            var target = _route.Target;
            WalkTo(2);
            Assert.AreEqual(target, _route.Target, "The player's own hint stays.");
        }

        [Test]
        public void LevelWithoutScript_IsInactive()
        {
            Build();
            Assert.IsFalse(_tutorial.IsActive);
            Assert.IsNull(_tutorial.Current);
            WalkTo(3);
            Assert.IsEmpty(_started);
            Assert.IsFalse(_route.IsActive);
        }

        [Test]
        public void Validator_ChecksTargetsAndTexts()
        {
            Build(new TutorialStep("tutorial.key", TutorialGoal.Collect, "key_1"),
                new TutorialStep("", TutorialGoal.Kill, "door_1"),
                new TutorialStep("tutorial.move", TutorialGoal.Reach, "nothing_1"),
                new TutorialStep("tutorial.move", TutorialGoal.Reach, cell: new Vector2Int(0, 0)));

            var report = LevelValidator.Validate(_level);
            Assert.AreEqual(4, report.Count(ValidationCodes.TutorialStepInvalid),
                "No text; Kill on a door; unknown target; a wall cell.");
        }

        [Test]
        public void Catalog_KeepsTheTutorialApartFromNumberedLevels()
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            try
            {
                catalog.AddOrUpdate("Level_01", "Levels/Level_01", "One");
                Assert.IsNull(catalog.Tutorial);
                Assert.IsTrue(catalog.SetTutorial("Level_Tutorial", "Levels/Level_Tutorial", "Tutorial"));
                Assert.IsFalse(catalog.SetTutorial("Level_Tutorial", "Levels/Level_Tutorial", "Tutorial"), "No change.");
                Assert.AreEqual(1, catalog.Levels.Count, "Not a numbered level.");
                Assert.AreEqual("Levels/Level_Tutorial", catalog.Find("Level_Tutorial").Address);

                Assert.IsTrue(catalog.AddOrUpdate("Level_Tutorial", "Levels/Level_Tutorial", "Tutorial"),
                    "Adding it as a numbered level is a separate entry.");
                Assert.IsTrue(catalog.Remove("Level_Tutorial"));
                Assert.IsNull(catalog.Tutorial);
                Assert.AreEqual(1, catalog.Levels.Count);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }
    }
}
