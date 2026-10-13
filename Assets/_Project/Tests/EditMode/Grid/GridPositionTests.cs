using Maze.Core.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Grid
{
    public class GridPositionTests
    {
        [Test]
        public void Equality_SameCoordinates_AreEqual()
        {
            Assert.AreEqual(new GridPosition(3, 4), new GridPosition(3, 4));
            Assert.IsTrue(new GridPosition(3, 4) == new GridPosition(3, 4));
            Assert.IsTrue(new GridPosition(3, 4) != new GridPosition(4, 3));
        }

        [TestCase(Direction.North, 5, 6)]
        [TestCase(Direction.East, 6, 5)]
        [TestCase(Direction.South, 5, 4)]
        [TestCase(Direction.West, 4, 5)]
        public void Neighbour_ReturnsAdjacentCell(Direction direction, int x, int y)
        {
            Assert.AreEqual(new GridPosition(x, y), new GridPosition(5, 5).Neighbour(direction));
        }

        [Test]
        public void Opposite_And_RotateClockwise()
        {
            Assert.AreEqual(Direction.South, Direction.North.Opposite());
            Assert.AreEqual(Direction.East, Direction.West.Opposite());
            Assert.AreEqual(Direction.East, Direction.North.RotateClockwise());
            Assert.AreEqual(Direction.North, Direction.West.RotateClockwise());
        }

        [Test]
        public void ManhattanDistance_IsSumOfAxisDeltas()
        {
            Assert.AreEqual(7, new GridPosition(1, 2).ManhattanDistance(new GridPosition(4, -2)));
        }

        [Test]
        public void WorldConversion_RoundTrips_AndCellCoversHalfUnit()
        {
            var cell = new GridPosition(7, 3);
            Assert.AreEqual(new Vector3(7f, 0f, 3f), cell.ToWorld());
            Assert.AreEqual(cell, GridPosition.FromWorld(cell.ToWorld()));
            Assert.AreEqual(cell, GridPosition.FromWorld(new Vector3(7.49f, 0f, 2.51f)));
            Assert.AreEqual(new GridPosition(8, 3), GridPosition.FromWorld(new Vector3(7.5f, 0f, 3f)));
            Assert.AreEqual(new GridPosition(-1, 0), GridPosition.FromWorld(new Vector3(-0.51f, 0f, 0f)));
        }
    }
}
