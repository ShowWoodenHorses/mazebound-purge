using UnityEngine;

namespace Maze.Presentation.UI.Shapes
{
    /// <summary>Kind of a <see cref="UiShape"/>; the numbers are passed to the <c>Maze/UIShape</c> shader.</summary>
    public enum UiShapeKind
    {
        /// <summary>The rect with rounded corners (<see cref="UiShape"/> corner radius, 0 = sharp).</summary>
        Rect = 0,

        /// <summary>The rect rounded fully: a circle when square, a pill otherwise.</summary>
        Capsule = 1,

        /// <summary>Annular sector around a centre: inner/outer radius, start angle and sweep. Inner 0 = pie.</summary>
        Sector = 2,

        /// <summary>The rect with its right end pointed (an arrow tip as long as the corner radius field says).</summary>
        Arrow = 3,
    }

    /// <summary>
    /// Signed distances of the <see cref="UiShape"/> shapes (negative inside, in canvas units) — the same formulas as
    /// the <c>Maze/UIShape</c> shader, so hit tests match what is drawn. Angles are degrees, 0 = right (+x),
    /// counter-clockwise (y up).
    /// </summary>
    public static class UiShapeMath
    {
        /// <summary>Sweep from which a sector is a full ring (no wedge cut).</summary>
        public const float FullSweep = 359.99f;

        /// <summary>Marker in the half-angle cosine that tells the shader to skip the wedge.</summary>
        public const float NoWedge = -2f;

        /// <summary>Rect of half size <paramref name="halfSize"/> centred at 0, corners rounded by <paramref name="radius"/>.</summary>
        public static float RoundedRect(Vector2 p, Vector2 halfSize, float radius)
        {
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(halfSize.x, halfSize.y));
            var qx = Mathf.Abs(p.x) - halfSize.x + radius;
            var qy = Mathf.Abs(p.y) - halfSize.y + radius;
            var outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>
        /// Rect of half size <paramref name="halfSize"/> centred at 0 whose right end is a point: the edges run from
        /// (halfSize.x − <paramref name="tip"/>, ±halfSize.y) to (halfSize.x, 0).
        /// </summary>
        public static float Arrow(Vector2 p, Vector2 halfSize, float tip)
        {
            var box = RoundedRect(p, halfSize, 0f);
            tip = Mathf.Clamp(tip, 0f, halfSize.x * 2f);
            if (tip <= 0f) return box;
            var normal = new Vector2(halfSize.y, tip).normalized; // Outward normal of the upper edge.
            var edge = Vector2.Dot(new Vector2(p.x - (halfSize.x - tip), Mathf.Abs(p.y) - halfSize.y), normal);
            return Mathf.Max(box, edge);
        }

        /// <summary>
        /// Point <paramref name="p"/> (relative to the sector centre) turned so the middle of the sweep looks along +y —
        /// the frame the sector distance is measured in. Linear in <paramref name="p"/>, so the shader gets it per vertex.
        /// </summary>
        public static Vector2 ToSectorFrame(Vector2 p, float startAngle, float sweep)
        {
            var turn = (90f - (startAngle + sweep * 0.5f)) * Mathf.Deg2Rad;
            var cos = Mathf.Cos(turn);
            var sin = Mathf.Sin(turn);
            return new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
        }

        /// <summary>Sine and cosine of the half sweep (the wedge), or <see cref="NoWedge"/> in y for a full ring.</summary>
        public static Vector2 HalfSweep(float sweep)
        {
            if (sweep >= FullSweep) return new Vector2(0f, NoWedge);
            var half = Mathf.Clamp(sweep, 0f, FullSweep) * 0.5f * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(half), Mathf.Cos(half));
        }

        /// <summary>Annular sector; <paramref name="p"/> relative to its centre.</summary>
        public static float Sector(Vector2 p, float inner, float outer, float startAngle, float sweep) =>
            SectorInFrame(ToSectorFrame(p, startAngle, sweep), inner, outer, HalfSweep(sweep));

        /// <summary>Annular sector for a point already in the sector frame (<see cref="ToSectorFrame"/>).</summary>
        public static float SectorInFrame(Vector2 q, float inner, float outer, Vector2 halfSweep)
        {
            var length = q.magnitude;
            var ring = length - outer;
            if (inner > 0f) ring = Mathf.Max(ring, inner - length);
            if (halfSweep.y <= NoWedge * 0.75f) return ring;

            // Wedge symmetric around +y (IQ's pie): distance to the nearer edge ray, signed by the side.
            var x = Mathf.Abs(q.x);
            var along = Mathf.Max(x * halfSweep.x + q.y * halfSweep.y, 0f);
            var toRay = new Vector2(x - halfSweep.x * along, q.y - halfSweep.y * along).magnitude;
            var side = halfSweep.y * x - halfSweep.x * q.y;
            var wedge = side > 0f ? toRay : -toRay;
            return Mathf.Max(ring, wedge);
        }

        /// <summary>Bounds of a sector (relative to its centre): the radii at the sweep ends and every axis it crosses.</summary>
        public static Rect SectorBounds(float inner, float outer, float startAngle, float sweep)
        {
            if (sweep >= FullSweep) return Rect.MinMaxRect(-outer, -outer, outer, outer);

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            void Add(float angle, float radius)
            {
                var a = angle * Mathf.Deg2Rad;
                var point = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            var end = startAngle + sweep;
            Add(startAngle, inner);
            Add(startAngle, outer);
            Add(end, inner);
            Add(end, outer);
            for (var axis = Mathf.Ceil(startAngle / 90f) * 90f; axis < end; axis += 90f)
            {
                Add(axis, outer);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
