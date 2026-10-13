using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Navigation;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Navigation
{
    public class KeyDoorSolverTests
    {
        // Corridor y = 1, x = 1..10. Doors at x = 4 and x = 8.
        private static LevelGeometry Corridor()
        {
            var geometry = new LevelGeometry(12, 3);
            for (var x = 1; x <= 10; x++)
                geometry.SetCell(new GridPosition(x, 1), CellType.Floor);

            geometry.SetCell(new GridPosition(4, 1), CellType.Door);
            geometry.SetCell(new GridPosition(8, 1), CellType.Door);
            return geometry;
        }

        private static readonly GridPosition Start = new GridPosition(1, 1);
        private static readonly GridPosition End = new GridPosition(10, 1);

        [Test]
        public void DoorsWithoutKeys_ArePassable()
        {
            var doors = new[] { new DoorData("door_1", new GridPosition(4, 1)), new DoorData("door_2", new GridPosition(8, 1)) };
            var result = KeyDoorSolver.Solve(Corridor(), doors, new KeyData[0], Start);
            Assert.IsTrue(result.IsReachable(End));
        }

        [Test]
        public void KeyBeforeDoor_OpensIt_AndChainsToNextKey()
        {
            var doors = new[]
            {
                new DoorData("door_1", new GridPosition(4, 1), "key_a"),
                new DoorData("door_2", new GridPosition(8, 1), "key_b"),
            };
            var keys = new[] { new KeyData("key_a", new GridPosition(2, 1)), new KeyData("key_b", new GridPosition(6, 1)) };

            var result = KeyDoorSolver.Solve(Corridor(), doors, keys, Start);

            Assert.IsTrue(result.IsReachable(End));
            CollectionAssert.AreEquivalent(new[] { "key_a", "key_b" }, result.CollectedKeyIds);
        }

        [Test]
        public void KeyBehindItsOwnDoor_IsUnreachable()
        {
            var doors = new[] { new DoorData("door_1", new GridPosition(4, 1), "key_a") };
            var keys = new[] { new KeyData("key_a", new GridPosition(6, 1)) };

            var result = KeyDoorSolver.Solve(Corridor(), doors, keys, Start);

            Assert.IsTrue(result.IsReachable(new GridPosition(3, 1)));
            Assert.IsFalse(result.IsReachable(new GridPosition(4, 1)));
            Assert.IsFalse(result.IsReachable(End));
            Assert.IsEmpty(result.CollectedKeyIds);
        }

        [Test]
        public void InitiallyOpenLockedDoor_IsPassable()
        {
            var doors = new[] { new DoorData("door_1", new GridPosition(4, 1), "key_a", isInitiallyOpen: true) };
            var result = KeyDoorSolver.Solve(Corridor(), doors, new KeyData[0], Start);
            Assert.IsTrue(result.IsReachable(End));
        }

        [Test]
        public void KeyOpensOnlyItsOwnDoor()
        {
            var doors = new[]
            {
                new DoorData("door_1", new GridPosition(4, 1), "key_a"),
                new DoorData("door_2", new GridPosition(8, 1), "key_b"),
            };
            var keys = new[] { new KeyData("key_a", new GridPosition(3, 1)), new KeyData("key_b", new GridPosition(9, 1)) };

            var result = KeyDoorSolver.Solve(Corridor(), doors, keys, Start);

            Assert.IsTrue(result.IsReachable(new GridPosition(7, 1)));
            Assert.IsFalse(result.IsReachable(new GridPosition(8, 1)), "key_a must not open door_2.");
            Assert.IsFalse(result.IsReachable(End));
        }

        [Test]
        public void StartInsideWall_ReachesNothing()
        {
            var result = KeyDoorSolver.Solve(Corridor(), new DoorData[0], new KeyData[0], new GridPosition(0, 0));
            Assert.IsFalse(result.IsReachable(new GridPosition(0, 0)));
            Assert.IsFalse(result.IsReachable(Start));
        }
    }
}
