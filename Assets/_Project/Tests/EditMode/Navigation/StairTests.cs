using NUnit.Framework;
using Purge.Core.Grid;
using Purge.Core.Navigation;
using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Tests.EditMode
{
    public sealed class StairTests
    {
        private static LevelGeometry Geometry()
        {
            var geometry = new LevelGeometry(9, 9, CellType.Floor, 3, groundFloor: 1);
            geometry.AddStair(new StairLink(new GridPosition(2, 3), false));
            geometry.AddStair(new StairLink(new GridPosition(5, 3, 1), true));
            return geometry;
        }

        [Test]
        public void LandingsLinkAcrossTheFlightAndUpperFootprintIsOpen()
        {
            var geometry = Geometry();
            var grid = new LevelGrid(geometry);
            var stair = geometry.Stairs[0];
            Assert.That(grid.TryGetStairDestination(stair.LowerLanding, out var upper), Is.True);
            Assert.That(upper, Is.EqualTo(stair.UpperLanding));
            Assert.That(grid.TryGetStairDestination(upper, out var lower), Is.True);
            Assert.That(lower, Is.EqualTo(stair.LowerLanding));
            foreach (var cell in new[] { stair.First, stair.Second })
            {
                Assert.That(grid.Has(cell, CellFlags.BlocksMovement), Is.True);
                Assert.That(grid.GetCell(cell.OnFloor(stair.UpperFloor)), Is.EqualTo(CellType.Void));
                Assert.That(grid.TryGetStairDestination(cell, out _), Is.False);
            }
            geometry.RemoveTopFloor();
            Assert.That(geometry.Stairs, Has.Count.EqualTo(1));
        }

        [Test]
        public void PursuitFollowsTwoStairsAndStopsAtBlockedLandings()
        {
            var geometry = Geometry();
            var grid = new LevelGrid(geometry);
            var field = new FlowField(grid);
            var mask = new bool[grid.CellCount];
            for (var i = 0; i < mask.Length; i++)
                mask[i] = !grid.Has(grid.ToPosition(i), CellFlags.BlocksMovement);
            var target = new GridPosition(7, 7, 2);
            var start = new GridPosition(1, 1);
            field.Build(target, mask);
            var steps = 0;
            var cell = start;
            while (field.TryNext(cell, out var next) && steps++ < grid.CellCount)
            {
                Assert.That(field.DistanceAt(next), Is.LessThan(field.DistanceAt(cell)));
                cell = next;
            }
            Assert.That(cell, Is.EqualTo(target));

            mask[grid.ToIndex(geometry.Stairs[0].UpperLanding)] = false;
            field.Build(target, mask);
            Assert.That(field.DistanceAt(start), Is.EqualTo(FlowField.Unreachable));
        }

        [Test]
        public void SoundIsHalvedAtEachStair()
        {
            var geometry = Geometry();
            var grid = new LevelGrid(geometry);
            var field = new NoiseField(20, grid);
            var first = geometry.Stairs[0];
            var second = geometry.Stairs[1];
            var origin = first.LowerLanding;
            field.Build(grid.Width, grid.Height, origin.X, origin.Y, origin.Floor, 16f, new OpenAcoustics());
            Assert.That(field.LoudnessAt(first.UpperLanding), Is.EqualTo(8f).Within(0.001f));
            Assert.That(field.LoudnessAt(second.UpperLanding), Is.EqualTo(field.LoudnessAt(second.LowerLanding) / 2f).Within(0.001f));
            Assert.That(field.TryStepToward(first.UpperLanding, out var previous), Is.True);
            Assert.That(previous, Is.EqualTo(origin));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ArtTreadsMeetTheFlightAtBothHeights(bool alongX)
        {
            var stair = new StairLink(new GridPosition(4, 4, 1), alongX);
            var fit = StairMeshPlacement.Matrix(stair);
            Assert.That(Vector3.Distance(fit.MultiplyPoint3x4(new Vector3(5f, 0f, -1f)), stair.Bottom), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(fit.MultiplyPoint3x4(new Vector3(0f, 5f, -1f)), stair.Top), Is.LessThan(0.001f));
            var width = Vector3.Distance(fit.MultiplyPoint3x4(new Vector3(5f, 0f, 0.16f)),
                fit.MultiplyPoint3x4(new Vector3(5f, 0f, -2.16f)));
            Assert.That(width, Is.EqualTo(0.92f).Within(0.001f));
        }

        private readonly struct OpenAcoustics : IGridAcoustics
        {
            public float Transmission(int x, int y) => 1f;
        }
    }
}
