using Maze.Core.Grid;
using Maze.Core.Visual;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Visual
{
    public class WallShapesTests
    {
        private const int N = 1, E = 2, S = 4, W = 8;

        [TestCase(0, VisualCategory.Isolated, 0)]
        [TestCase(N, VisualCategory.End, 0)]
        [TestCase(E, VisualCategory.End, 1)]
        [TestCase(S, VisualCategory.End, 2)]
        [TestCase(W, VisualCategory.End, 3)]
        [TestCase(N | S, VisualCategory.Straight, 0)]
        [TestCase(E | W, VisualCategory.Straight, 1)]
        [TestCase(N | E, VisualCategory.Corner, 0)]
        [TestCase(E | S, VisualCategory.Corner, 1)]
        [TestCase(S | W, VisualCategory.Corner, 2)]
        [TestCase(W | N, VisualCategory.Corner, 3)]
        [TestCase(N | E | S, VisualCategory.TJunction, 0)]
        [TestCase(E | S | W, VisualCategory.TJunction, 1)]
        [TestCase(S | W | N, VisualCategory.TJunction, 2)]
        [TestCase(W | N | E, VisualCategory.TJunction, 3)]
        [TestCase(N | E | S | W, VisualCategory.Cross, 0)]
        public void Classify_ReturnsCategoryAndRotation(int mask, VisualCategory category, int rotation)
        {
            Assert.AreEqual((category, rotation), WallShapes.Classify(mask));
        }

        [Test]
        public void ContextMask_BorderWallsDoNotConnectOutside_DoorsConnect()
        {
            // 4x3, all walls except a floor cell at (1,1) and a door at (2,1).
            var geometry = new LevelGeometry(4, 3);
            geometry.SetCell(new GridPosition(1, 1), CellType.Floor);
            geometry.SetCell(new GridPosition(2, 1), CellType.Door);

            // Bottom-left corner: outside does not connect, so only North (0,1) and East (1,0).
            Assert.AreEqual(N | E, new CellVisualContext(geometry, new GridPosition(0, 0)).WallConnections);

            // (2,0) bottom border: West wall, East wall, North is a door (connects).
            Assert.AreEqual(N | E | W, new CellVisualContext(geometry, new GridPosition(2, 0)).WallConnections);

            var context = new CellVisualContext(geometry, new GridPosition(1, 1));
            Assert.AreEqual(CellType.Door, context.East);
            Assert.AreEqual(CellType.Wall, context.SouthWest);
        }
    }
}
