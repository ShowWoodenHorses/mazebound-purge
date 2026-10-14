using NUnit.Framework;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Navigation;

namespace Purge.Tests.EditMode.Navigation
{
    public sealed class FlowFieldTests
    {
        private static bool[] Open(int width, int height)
        {
            var cells = new bool[width * height];
            for (var i = 0; i < cells.Length; i++) cells[i] = true;
            return cells;
        }

        [Test]
        public void OpenField_DistancesAreOctile_AndEveryCellStepsCloser()
        {
            const int w = 9, h = 7;
            var field = new FlowField(w, h);
            field.Build(new GridPosition(4, 3), new CellMaskPassability(w, h, Open(w, h)));

            Assert.That(field.DistanceAt(3 * w + 4), Is.EqualTo(0));
            Assert.That(field.DistanceAt(3 * w + 5), Is.EqualTo(FlowField.OrthogonalCost));
            Assert.That(field.DistanceAt(4 * w + 5), Is.EqualTo(FlowField.DiagonalCost));
            // (0,0): 3 diagonal + 1 orthogonal steps.
            Assert.That(field.DistanceAt(0), Is.EqualTo(3 * FlowField.DiagonalCost + FlowField.OrthogonalCost));
            for (var i = 0; i < w * h; i++)
            {
                if (i == 3 * w + 4) continue;
                var next = field.NextAt(i);
                Assert.That(next, Is.GreaterThanOrEqualTo(0));
                Assert.That(field.DistanceAt(next), Is.LessThan(field.DistanceAt(i)));
            }
        }

        [Test]
        public void Walls_AreUnreachable_AndDiagonalsDoNotCutCorners()
        {
            // . # .
            // . # .
            // . . .
            const int w = 3, h = 3;
            var cells = Open(w, h);
            cells[2 * w + 1] = false; // (1,2)
            cells[1 * w + 1] = false; // (1,1)
            var field = new FlowField(w, h);
            field.Build(new GridPosition(2, 2), new CellMaskPassability(w, h, cells));

            Assert.That(field.DistanceAt(1 * w + 1), Is.EqualTo(FlowField.Unreachable));
            // (0,2) goes around the wall: (0,1) (0,0) (1,0) (2,0) (2,1) (2,2) — both diagonals past the wall's
            // corners ((0,1)→(1,0), (1,0)→(2,1)) are forbidden.
            var from = 2 * w + 0;
            Assert.That(field.DistanceAt(from), Is.EqualTo(FlowField.OrthogonalCost * 6));
            Assert.That(field.NextAt(from), Is.EqualTo(1 * w + 0));
        }

        [Test]
        public void ClosedRegion_StaysUnreachable_AndRebuildCountsVersion()
        {
            const int w = 5, h = 1;
            var cells = Open(w, h);
            cells[2] = false;
            var field = new FlowField(w, h);
            field.Build(new GridPosition(0, 0), new CellMaskPassability(w, h, cells));
            Assert.That(field.DistanceAt(4), Is.EqualTo(FlowField.Unreachable));
            Assert.That(field.NextAt(4), Is.EqualTo(-1));

            cells[2] = true;
            field.Build(new GridPosition(0, 0), new CellMaskPassability(w, h, cells));
            Assert.That(field.DistanceAt(4), Is.EqualTo(4 * FlowField.OrthogonalCost));
            Assert.That(field.Version, Is.EqualTo(2));
        }
    }

    public sealed class CellBucketsTests
    {
        [Test]
        public void Insert_GroupsByCell_AndClearEmptiesTouchedCells()
        {
            var buckets = new CellBuckets(4, 4, 8);
            buckets.Insert(0, 5);
            buckets.Insert(1, 5);
            buckets.Insert(2, 6);
            buckets.Insert(3, -1); // outside: skipped

            var count = 0;
            for (var a = buckets.First(5); a >= 0; a = buckets.Next(a)) count++;
            Assert.That(count, Is.EqualTo(2));
            Assert.That(buckets.First(2, 1), Is.EqualTo(2)); // (2,1) = index 6
            Assert.That(buckets.First(-1, 0), Is.EqualTo(-1));

            buckets.Clear();
            Assert.That(buckets.First(5), Is.EqualTo(-1));
            Assert.That(buckets.First(6), Is.EqualTo(-1));
        }
    }
}
