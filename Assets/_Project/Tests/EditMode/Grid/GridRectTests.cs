using Maze.Core.Grid;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Grid
{
    public class GridRectTests
    {
        [Test]
        public void Contains_MinInclusive_MaxExclusive()
        {
            var rect = new GridRect(2, 3, 4, 2);
            Assert.IsTrue(rect.Contains(new GridPosition(2, 3)));
            Assert.IsTrue(rect.Contains(new GridPosition(5, 4)));
            Assert.IsFalse(rect.Contains(new GridPosition(6, 4)));
            Assert.IsFalse(rect.Contains(new GridPosition(5, 5)));
            Assert.IsFalse(rect.Contains(new GridPosition(1, 3)));
        }

        [Test]
        public void Overlaps_DetectsSharedCellsOnly()
        {
            var a = new GridRect(0, 0, 4, 4);
            Assert.IsTrue(a.Overlaps(new GridRect(3, 3, 2, 2)));
            Assert.IsFalse(a.Overlaps(new GridRect(4, 0, 2, 2)), "Touching edges do not overlap.");
            Assert.IsFalse(a.Overlaps(new GridRect(1, 1, 0, 2)), "Empty rect never overlaps.");
        }

        [Test]
        public void FromCorners_IsOrderIndependent()
        {
            var expected = new GridRect(1, 2, 3, 4);
            Assert.AreEqual(expected, GridRect.FromCorners(new GridPosition(1, 2), new GridPosition(3, 5)));
            Assert.AreEqual(expected, GridRect.FromCorners(new GridPosition(3, 5), new GridPosition(1, 2)));
            Assert.AreEqual(12, expected.CellCount);
        }
    }
}
