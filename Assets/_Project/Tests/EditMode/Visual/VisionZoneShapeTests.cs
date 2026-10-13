using Maze.Core.Grid;
using Maze.Core.Visibility;
using Maze.Presentation.Visual;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Visual
{
    public class VisionZoneShapeTests
    {
        /// <summary>Open floor from x = -20..20, y = -20..20, except the wall cells listed; outside is opaque.</summary>
        private readonly struct Room : IGridOpacity
        {
            private readonly GridPosition[] _walls;

            public Room(params GridPosition[] walls) => _walls = walls;

            public bool IsOpaque(GridPosition position)
            {
                if (Mathf.Abs(position.X) > 20 || Mathf.Abs(position.Y) > 20) return true;
                if (_walls == null) return false;
                foreach (var wall in _walls)
                    if (wall == position) return true;
                return false;
            }
        }

        [Test]
        public void Cone_OpenFloor_RaysReachTheRange_EdgesAtHalfTheAngle()
        {
            var points = new Vector2[VisionZoneShape.RayCount(90f, 5f)];
            var count = VisionZoneShape.Cast(Vector2.zero, Vector2.up, 90f, 5f, 5f, new Room(), points);

            Assert.AreEqual(19, count, "90 / 5 + 1 rays (both edges).");
            for (var i = 0; i < count; i++)
                Assert.AreEqual(5f, points[i].magnitude, 1e-3f, $"Ray {i} is not cut.");
            Assert.AreEqual(-45f, Vector2.SignedAngle(Vector2.up, points[0]), 0.01f, "First edge: clockwise of the facing.");
            Assert.AreEqual(45f, Vector2.SignedAngle(Vector2.up, points[count - 1]), 0.01f, "Last edge: counter-clockwise.");
        }

        [Test]
        public void Rays_StopWhereTheyEnterAWall()
        {
            // Wall cell 3 north of the origin: its south face is at y = 2.5.
            var points = new Vector2[VisionZoneShape.RayCount(10f, 5f)];
            var count = VisionZoneShape.Cast(Vector2.zero, Vector2.up, 10f, 5f, 5f, new Room(new GridPosition(0, 3)), points);

            var middle = points[count / 2];
            Assert.AreEqual(0f, middle.x, 1e-3f);
            Assert.AreEqual(2.5f, middle.y, 1e-3f, "Cut at the wall's face.");
        }

        [Test]
        public void Circle_AllAround_CutBySurroundingWalls()
        {
            var walls = new[]
            {
                new GridPosition(-2, -2), new GridPosition(-1, -2), new GridPosition(0, -2), new GridPosition(1, -2), new GridPosition(2, -2),
                new GridPosition(-2, 2), new GridPosition(-1, 2), new GridPosition(0, 2), new GridPosition(1, 2), new GridPosition(2, 2),
                new GridPosition(-2, -1), new GridPosition(-2, 0), new GridPosition(-2, 1),
                new GridPosition(2, -1), new GridPosition(2, 0), new GridPosition(2, 1),
            };
            var points = new Vector2[VisionZoneShape.RayCount(360f, 10f)];
            var count = VisionZoneShape.Cast(Vector2.zero, Vector2.right, 360f, 6f, 10f, new Room(walls), points);

            Assert.AreEqual(36, count);
            for (var i = 0; i < count; i++)
            {
                Assert.LessOrEqual(Mathf.Max(Mathf.Abs(points[i].x), Mathf.Abs(points[i].y)), 1.5f + 1e-3f, $"Ray {i} stays in the 3x3 room.");
                if (i > 0)
                    Assert.Greater(Vector2.SignedAngle(points[i - 1], points[i]), 0f, "Counter-clockwise.");
            }
        }
    }
}
