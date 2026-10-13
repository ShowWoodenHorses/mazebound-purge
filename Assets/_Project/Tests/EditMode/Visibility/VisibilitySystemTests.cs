using System;
using System.Threading;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;
using Maze.Gameplay.Visibility;
using Maze.Tests.EditMode.Player;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Visibility
{
    public class VisibilitySystemTests
    {
        private const float Dt = 1f / 60f;

        private LevelData _level;
        private PlayerDefinition _definition;
        private FakePlayerInput _input;
        private LevelGrid _grid;
        private DoorSystem _doors;
        private PlayerSystem _player;
        private VisibilitySystem _visibility;

        /// <summary>20x3 corridor along y = 1, x = 1..18. Closed doors at (4,1) and (15,1). Start at (1,1).</summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(20, 3);
            for (var x = 1; x <= 18; x++)
                geometry.SetCell(new GridPosition(x, 1), CellType.Floor);
            geometry.SetCell(new GridPosition(4, 1), CellType.Door);
            geometry.SetCell(new GridPosition(15, 1), CellType.Door);
            _level.ReplaceGeometry(geometry);
            _level.MutableDoors.Add(new DoorData("door_1", new GridPosition(4, 1)));
            _level.MutableDoors.Add(new DoorData("door_2", new GridPosition(15, 1)));
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(1, 1)));

            _definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _definition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _input = new FakePlayerInput();
            _grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            var occupancy = new OccupancyMap(_grid);
            _player = new PlayerSystem(_level, _definition, _input, new LevelPassability(_grid, _doors), occupancy,
                new LevelLaunchOptions(startIndex: 0), Aim.FreeNoAssist);
            _visibility = new VisibilitySystem(_grid, _doors, _player);

            // Load order of the real level: InitializeVisibility before SpawnPlayer.
            _visibility.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsFalse(_visibility.HasResult, "Nothing to see before the player spawned.");
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _visibility.Dispose();
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_definition);
        }

        private void Run(float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt)
                _player.Tick(Dt);
        }

        [Test]
        public void Spawn_Calculates_ClosedDoorBlocks()
        {
            Assert.IsTrue(_visibility.HasResult);
            Assert.AreEqual(1, _visibility.RecalculationCount);
            Assert.IsTrue(_visibility.IsVisible(new GridPosition(1, 1)));
            Assert.IsTrue(_visibility.IsVisible(new GridPosition(4, 1)), "The closed door is seen.");
            Assert.IsFalse(_visibility.IsVisible(new GridPosition(5, 1)), "Behind the closed door.");
        }

        [Test]
        public void RecalculatesOnCellChange_NotEveryTick()
        {
            _input.Move = new Vector2(0.2f, 0f);
            Run(0.3f); // 0.18 cells: still in (1,1)
            Assert.AreEqual(1, _visibility.RecalculationCount);

            _input.Move = Vector2.right;
            Run(0.3f); // enters (2,1)
            Assert.AreEqual(new GridPosition(2, 1), _player.Cell);
            Assert.AreEqual(2, _visibility.RecalculationCount);
        }

        [Test]
        public void DoorInWindow_Recalculates_DoorOutsideDoesNot()
        {
            var changed = 0;
            _visibility.Changed += () => changed++;

            _doors.SetOpen("door_2", true); // (15,1): far outside the window around (1,1)
            Assert.AreEqual(0, changed);

            _doors.SetOpen("door_1", true);
            Assert.AreEqual(1, changed);
            Assert.IsTrue(_visibility.IsVisible(new GridPosition(5, 1)), "Seen through the open door.");
            Assert.IsTrue(_visibility.IsVisible(new GridPosition(6, 1)));
            Assert.IsFalse(_visibility.IsVisible(new GridPosition(7, 1)), "Outside 11x11.");
        }

        [Test]
        public void Dispose_Unsubscribes()
        {
            _visibility.Dispose();
            _doors.SetOpen("door_1", true);
            _input.Move = Vector2.right;
            Run(0.3f);
            Assert.AreEqual(1, _visibility.RecalculationCount);
        }

    }
}
