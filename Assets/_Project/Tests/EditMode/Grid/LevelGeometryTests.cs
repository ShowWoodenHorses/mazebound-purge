using System;
using System.Collections.Generic;
using Maze.Core.Grid;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Grid
{
    public class LevelGeometryTests
    {
        [Test]
        public void NewGeometry_IsFilledWithWalls()
        {
            var geometry = new LevelGeometry(4, 6);
            Assert.AreEqual(24, geometry.CellCount);
            Assert.IsTrue(geometry.IsConsistent);
            for (var i = 0; i < geometry.CellCount; i++)
                Assert.AreEqual(CellType.Wall, geometry.GetCell(geometry.ToPosition(i)));
        }

        [Test]
        public void Constructor_RejectsNonPositiveSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelGeometry(0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelGeometry(4, -2));
        }

        [Test]
        public void IndexAndPosition_RoundTrip_RowMajor()
        {
            var geometry = new LevelGeometry(5, 3);
            Assert.AreEqual(7, geometry.ToIndex(new GridPosition(2, 1)));
            for (var i = 0; i < geometry.CellCount; i++)
                Assert.AreEqual(i, geometry.ToIndex(geometry.ToPosition(i)));
        }

        [Test]
        public void OutOfBounds_Throws()
        {
            var geometry = new LevelGeometry(4, 4);
            Assert.IsFalse(geometry.IsInside(new GridPosition(4, 0)));
            Assert.IsFalse(geometry.IsInside(new GridPosition(0, -1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => geometry.GetCell(new GridPosition(4, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => geometry.ToPosition(16));
        }

        [Test]
        public void SetCell_ChangesOnlyThatCell()
        {
            var geometry = new LevelGeometry(4, 4);
            geometry.SetCell(new GridPosition(1, 2), CellType.Floor);
            Assert.AreEqual(CellType.Floor, geometry.GetCell(new GridPosition(1, 2)));
            Assert.AreEqual(CellType.Wall, geometry.GetCell(new GridPosition(2, 1)));
        }

        [Test]
        public void LevelGrid_IsIndependentCopyOfGeometry()
        {
            var geometry = new LevelGeometry(4, 4);
            geometry.SetCell(new GridPosition(1, 1), CellType.Door);
            var grid = new LevelGrid(geometry);

            geometry.SetCell(new GridPosition(1, 1), CellType.Floor);

            Assert.AreEqual(CellType.Door, grid.GetCell(new GridPosition(1, 1)));
            Assert.AreEqual(CellType.Wall, grid.GetCellOrWall(new GridPosition(-1, 0)));
        }

        [Test]
        public void LevelGrid_GetNeighbours_SkipsOutOfBounds()
        {
            var grid = new LevelGrid(new LevelGeometry(4, 4));
            var result = new List<GridPosition>();

            grid.GetNeighbours(new GridPosition(0, 0), result);
            CollectionAssert.AreEquivalent(new[] { new GridPosition(0, 1), new GridPosition(1, 0) }, result);

            grid.GetNeighbours(new GridPosition(2, 2), result);
            Assert.AreEqual(4, result.Count);
        }
    }
}
