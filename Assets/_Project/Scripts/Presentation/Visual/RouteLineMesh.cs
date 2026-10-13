using System.Collections.Generic;
using Maze.Core.Grid;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Geometry of the route hint line (no Unity objects but the lists it fills, testable): the polyline from the
    /// player through the path's cell centres with rounded turns, and a flat ribbon along it. Grid units: a cell centre
    /// is at integer coordinates, x = East, y = North.
    /// </summary>
    public static class RouteLineMesh
    {
        /// <summary>Points per rounded turn.</summary>
        public const int CornerSegments = 6;

        /// <summary>
        /// The polyline: <paramref name="start"/> (the player), then the centres of <paramref name="path"/> after its
        /// first cell (the player's own); every turn rounded with <paramref name="cornerRadius"/>.
        /// </summary>
        public static void BuildPoints(Vector2 start, IReadOnlyList<GridPosition> path, float cornerRadius, List<Vector2> points,
            List<Vector2> scratch)
        {
            points.Clear();
            scratch.Clear();
            scratch.Add(start);
            for (var i = 1; i < path.Count; i++)
            {
                var center = new Vector2(path[i].X, path[i].Y);
                if ((center - scratch[scratch.Count - 1]).sqrMagnitude > 1e-6f) scratch.Add(center);
            }

            if (scratch.Count < 2) return;
            points.Add(scratch[0]);
            for (var i = 1; i < scratch.Count - 1; i++)
            {
                var a = scratch[i - 1];
                var p = scratch[i];
                var b = scratch[i + 1];
                var inLength = (p - a).magnitude;
                var outLength = (b - p).magnitude;
                var dIn = (p - a) / inLength;
                var dOut = (b - p) / outLength;
                var radius = Mathf.Min(cornerRadius, inLength * 0.5f, outLength * 0.5f);
                if (radius <= 1e-4f || Vector2.Dot(dIn, dOut) > 0.999f)
                {
                    points.Add(p);
                    continue;
                }

                var from = p - dIn * radius;
                var to = p + dOut * radius;
                for (var s = 0; s <= CornerSegments; s++)
                {
                    var t = (float)s / CornerSegments;
                    var u = 1f - t;
                    points.Add(u * u * from + 2f * u * t * p + t * t * to); // Quadratic Bézier with the corner as control.
                }
            }

            points.Add(scratch[scratch.Count - 1]);
        }

        /// <summary>
        /// A flat ribbon along <paramref name="points"/> at <paramref name="height"/>: two vertices per point (miter
        /// joints), uv = (distance from the start, ±1 across), uv2.x = distance left to the end. Clears the lists first.
        /// </summary>
        public static void BuildRibbon(List<Vector2> points, float halfWidth, float height, List<Vector3> vertices,
            List<Vector2> uv, List<Vector2> uv2, List<int> triangles)
        {
            vertices.Clear();
            uv.Clear();
            uv2.Clear();
            triangles.Clear();
            var count = points.Count;
            if (count < 2) return;

            var total = 0f;
            for (var i = 1; i < count; i++) total += (points[i] - points[i - 1]).magnitude;

            var travelled = 0f;
            for (var i = 0; i < count; i++)
            {
                if (i > 0) travelled += (points[i] - points[i - 1]).magnitude;
                var normal = NormalAt(points, i);
                var p = points[i];
                var left = p + normal * halfWidth;
                var right = p - normal * halfWidth;
                vertices.Add(new Vector3(left.x, height, left.y));
                vertices.Add(new Vector3(right.x, height, right.y));
                uv.Add(new Vector2(travelled, 1f));
                uv.Add(new Vector2(travelled, -1f));
                uv2.Add(new Vector2(total - travelled, 0f));
                uv2.Add(new Vector2(total - travelled, 0f));

                if (i == 0) continue;
                var v = i * 2;
                triangles.Add(v - 2);
                triangles.Add(v);
                triangles.Add(v - 1);
                triangles.Add(v - 1);
                triangles.Add(v);
                triangles.Add(v + 1);
            }
        }

        /// <summary>Left normal at a point; at joints the miter direction, lengthened up to twice.</summary>
        private static Vector2 NormalAt(List<Vector2> points, int i)
        {
            var count = points.Count;
            var before = i > 0 ? (points[i] - points[i - 1]).normalized : Vector2.zero;
            var after = i < count - 1 ? (points[i + 1] - points[i]).normalized : Vector2.zero;
            var nBefore = new Vector2(-before.y, before.x);
            var nAfter = new Vector2(-after.y, after.x);
            if (i == 0) return nAfter;
            if (i == count - 1) return nBefore;

            var miter = (nBefore + nAfter).normalized;
            var cos = Vector2.Dot(miter, nAfter);
            return cos > 0.5f ? miter / cos : miter * 2f;
        }
    }
}
