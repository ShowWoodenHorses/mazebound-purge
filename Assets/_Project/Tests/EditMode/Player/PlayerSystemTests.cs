using System;
using System.Collections.Generic;
using System.Threading;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Player
{
    public class PlayerSystemTests
    {
        private const float Dt = 1f / 60f;

        private LevelData _level;
        private PlayerDefinition _definition;
        private FakePlayerInput _input;
        private LevelGrid _grid;
        private DoorSystem _doors;
        private OccupancyMap _occupancy;

        /// <summary>
        /// 10x3 corridor along y = 1, x = 1..8. Door at (5,1). Starts at (1,1) and (8,1), exit at (3,1).
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(10, 3);
            for (var x = 1; x <= 8; x++)
                geometry.SetCell(new GridPosition(x, 1), CellType.Floor);
            geometry.SetCell(new GridPosition(5, 1), CellType.Door);
            _level.ReplaceGeometry(geometry);
            _level.MutableDoors.Add(new DoorData("door_1", new GridPosition(5, 1)));
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(1, 1)));
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_2", new GridPosition(8, 1)));
            _level.MutableExits.Add(new ExitData("exit_1", new GridPosition(3, 1)));

            _definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _definition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _input = new FakePlayerInput();
            _grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            _occupancy = new OccupancyMap(_grid);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_definition);
        }

        private PlayerSystem Spawn(LevelLaunchOptions options)
        {
            var player = new PlayerSystem(_level, _definition, _input, new LevelPassability(_grid, _doors), _occupancy, options, Aim.FreeNoAssist);
            player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            return player;
        }

        private static void Run(PlayerSystem player, float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt)
                player.Tick(Dt);
        }

        [Test]
        public void Snowdrift_SlowsThePlayer_AndTheWalkAnimation()
        {
            var free = Spawn(new LevelLaunchOptions(startIndex: 0));
            _input.Move = Vector2.right;
            Run(free, 0.2f);
            var freeDistance = free.Position.x - 1f;
            Assert.AreEqual(1f, free.SpeedFactor, 0.01f);

            _level.Geometry.SetSurface(new GridPosition(1, 1), CellSurface.Snowdrift);
            _grid = new LevelGrid(_level.Geometry);
            _occupancy = new OccupancyMap(_grid);
            var slowed = Spawn(new LevelLaunchOptions(startIndex: 0));
            Run(slowed, 0.2f);

            Assert.AreEqual(freeDistance * _definition.SnowdriftSpeed, slowed.Position.x - 1f, 0.02f);
            Assert.AreEqual(_definition.SnowdriftSpeed, slowed.SpeedFactor, 0.01f);
        }

        [Test]
        public void ForcedStart_SpawnsThere_AndOccupiesCell()
        {
            var player = Spawn(new LevelLaunchOptions(startIndex: 1));

            Assert.AreEqual(1, player.StartIndex);
            Assert.AreEqual(new GridPosition(8, 1), player.Cell);
            Assert.AreEqual(new Vector2(8f, 1f), player.Position);
            Assert.AreEqual(new GridPosition(8, 1), _occupancy.PlayerCell);
            Assert.AreEqual(Vector2.left, player.Facing, "Faces the only open direction.");
        }

        [Test]
        public void RandomStart_IsDeterministicForSeed_AndCoversAllStarts()
        {
            Assert.AreEqual(
                PlayerStartSelector.Choose(5, new LevelLaunchOptions(seed: 42), 0),
                PlayerStartSelector.Choose(5, new LevelLaunchOptions(seed: 42), 0));

            var seen = new HashSet<int>();
            for (var seed = 0; seed < 200; seed++)
                seen.Add(PlayerStartSelector.Choose(2, new LevelLaunchOptions(seed: seed), 0));
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, seen);
        }

        [Test]
        public void InvalidForcedStart_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PlayerStartSelector.Choose(2, new LevelLaunchOptions(startIndex: 2), 0));
            Assert.Throws<InvalidOperationException>(() => PlayerStartSelector.Choose(0, LevelLaunchOptions.Default, 0));
        }

        [Test]
        public void Moving_RaisesCellChangedOncePerCell_AndUpdatesOccupancy()
        {
            var player = Spawn(new LevelLaunchOptions(startIndex: 0));
            var changes = new List<(GridPosition, GridPosition)>();
            player.CellChanged += (from, to) => changes.Add((from, to));

            _input.Move = Vector2.right;
            Run(player, 0.75f); // 2.25 cells at speed 3

            CollectionAssert.AreEqual(new[]
            {
                (new GridPosition(1, 1), new GridPosition(2, 1)),
                (new GridPosition(2, 1), new GridPosition(3, 1)),
            }, changes);
            Assert.AreEqual(new GridPosition(3, 1), _occupancy.PlayerCell);
        }

        [Test]
        public void Facing_FollowsMovement_AndIsKeptAfterStop()
        {
            var player = Spawn(new LevelLaunchOptions(startIndex: 0));
            _input.Move = new Vector2(0.5f, 0f);
            Run(player, 0.1f);
            Assert.AreEqual(Vector2.right, player.Facing);
            Assert.Greater(player.SpeedFactor, 0f);

            _input.Move = Vector2.zero;
            Run(player, 0.1f);
            Assert.AreEqual(Vector2.right, player.Facing);
            Assert.AreEqual(0f, player.SpeedFactor);
        }

        [Test]
        public void ClosedDoor_Blocks_OpenDoor_Passes()
        {
            var player = Spawn(new LevelLaunchOptions(startIndex: 0));
            _input.Move = Vector2.right;
            Run(player, 3f);
            Assert.AreEqual(new GridPosition(4, 1), player.Cell, "Stopped before the closed door.");

            _doors.SetOpen("door_1", true);
            Run(player, 3f);
            Assert.AreEqual(new GridPosition(8, 1), player.Cell, "Walked through the open door.");
        }

        [Test]
        public void ZombieCell_BlocksPlayer()
        {
            var player = Spawn(new LevelLaunchOptions(startIndex: 0));
            _occupancy.AddZombie(new GridPosition(3, 1));
            _input.Move = Vector2.right;
            Run(player, 2f);
            Assert.AreEqual(new GridPosition(2, 1), player.Cell);
        }

        [Test]
        public void ExitSystem_ReportsEveryEntry_NotStaying()
        {
            var player = Spawn(new LevelLaunchOptions(startIndex: 0));
            var exits = new ExitSystem(_level, player);
            var reached = 0;
            exits.ExitReached += _ => reached++;

            _input.Move = Vector2.right;
            Run(player, 0.6f); // enters (3,1)
            Assert.AreEqual(1, reached);

            _input.Move = Vector2.zero;
            Run(player, 0.5f);
            Assert.AreEqual(1, reached, "Standing on the exit does not ask again.");

            _input.Move = Vector2.left;
            Run(player, 0.4f); // back to (2,1)
            _input.Move = Vector2.right;
            Run(player, 0.4f); // (3,1) again
            Assert.AreEqual(2, reached, "Entering again asks again.");
            exits.Dispose();
        }

        [Test]
        public void Occupancy_PlayerAndZombiesNeverShareCell()
        {
            var cell = new GridPosition(2, 1);
            _occupancy.SetPlayer(cell);
            Assert.IsFalse(_occupancy.CanZombieEnter(cell));
            Assert.Throws<InvalidOperationException>(() => _occupancy.AddZombie(cell));

            var other = new GridPosition(3, 1);
            _occupancy.AddZombie(other);
            _occupancy.AddZombie(other);
            Assert.AreEqual(2, _occupancy.ZombieCount(other), "Several zombies may share a cell.");
            Assert.IsFalse(_occupancy.CanPlayerEnter(other));
            Assert.Throws<InvalidOperationException>(() => _occupancy.SetPlayer(other));

            _occupancy.RemoveZombie(other);
            _occupancy.RemoveZombie(other);
            Assert.IsTrue(_occupancy.CanPlayerEnter(other));
        }

        [Test]
        public void DoorSystem_StartsFromLevelData_AndReportsChanges()
        {
            var changes = new List<bool>();
            _doors.DoorChanged += (door, open) => changes.Add(open);

            Assert.IsFalse(_doors.IsOpenAt(new GridPosition(5, 1)));
            _doors.SetOpen("door_1", true);
            _doors.SetOpen("door_1", true);
            _doors.SetOpen("door_1", false);

            CollectionAssert.AreEqual(new[] { true, false }, changes, "No event when nothing changes.");
        }

    }
}
