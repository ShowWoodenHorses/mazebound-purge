using Maze.Presentation.UI.Shapes;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.UI
{
    public class UiShapeTests
    {
        private const float Eps = 1e-3f;

        private static Vector2 Polar(float degrees, float radius)
        {
            var a = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        [Test]
        public void RoundedRect_InsideEdgeOutside_AndRoundedCorner()
        {
            var half = new Vector2(50f, 20f);
            Assert.AreEqual(-20f, UiShapeMath.RoundedRect(Vector2.zero, half, 4f), Eps, "Centre: nearest edge is 20 away.");
            Assert.AreEqual(0f, UiShapeMath.RoundedRect(new Vector2(50f, 0f), half, 4f), Eps, "On the right edge.");
            Assert.AreEqual(10f, UiShapeMath.RoundedRect(new Vector2(0f, 30f), half, 4f), Eps, "Above the top edge.");

            var corner = UiShapeMath.RoundedRect(new Vector2(50f, 20f), half, 4f);
            Assert.AreEqual(Mathf.Sqrt(32f) - 4f, corner, Eps, "Rect corner lies outside the rounding.");
            Assert.AreEqual(0f, UiShapeMath.RoundedRect(new Vector2(50f, 20f), half, 0f), Eps, "Sharp corner is on the edge.");
        }

        [Test]
        public void RoundedRect_RadiusIsClampedToHalfTheShortSide()
        {
            var half = new Vector2(50f, 20f);
            Assert.AreEqual(UiShapeMath.RoundedRect(new Vector2(49f, 18f), half, 20f),
                UiShapeMath.RoundedRect(new Vector2(49f, 18f), half, 1000f), Eps);
        }

        [Test]
        public void Arrow_HealthBarFrame_PointedRightEnd()
        {
            var half = new Vector2(100f, 5f);
            const float tip = 8f;
            Assert.AreEqual(UiShapeMath.RoundedRect(new Vector2(-50f, 1f), half, 0f),
                UiShapeMath.Arrow(new Vector2(-50f, 1f), half, tip), Eps, "Away from the tip it is the rect.");
            Assert.AreEqual(0f, UiShapeMath.Arrow(new Vector2(100f, 0f), half, tip), Eps, "The tip lies on the edge.");
            Assert.Greater(UiShapeMath.Arrow(new Vector2(99f, 4f), half, tip), 0f, "The rect corner is cut off.");
            Assert.Less(UiShapeMath.Arrow(new Vector2(95f, 0f), half, tip), 0f);
            Assert.AreEqual(0f, UiShapeMath.Arrow(new Vector2(96f, 2.5f), half, tip), Eps, "Midpoint of the slanted edge.");
        }

        [Test]
        public void QuarterPie_AttackButton_CornerAtTheCentre()
        {
            // Right angle at the bottom-right: the pie spreads up (+y) and left (-x).
            float Pie(Vector2 p) => UiShapeMath.Sector(p, 0f, 100f, 90f, 90f);

            Assert.AreEqual(-10f, Pie(new Vector2(-10f, 50f)), Eps, "Nearest edge is the vertical one.");
            Assert.Less(Pie(new Vector2(-30f, 30f)), 0f);
            Assert.AreEqual(30f, Pie(new Vector2(30f, 30f)), Eps, "Right of the vertical edge.");
            Assert.AreEqual(10f, Pie(new Vector2(-50f, -10f)), Eps, "Below the horizontal edge.");
            Assert.AreEqual(Mathf.Sqrt(2f) * 80f - 100f, Pie(new Vector2(-80f, 80f)), Eps, "Beyond the arc.");
        }

        [Test]
        public void AnnularSector_WeaponSlot_InsideBetweenRadiiAndAngles()
        {
            float Slot(Vector2 p) => UiShapeMath.Sector(p, 130f, 182f, 93f, 40f);

            Assert.AreEqual(-26f, Slot(Polar(113f, 156f)), 0.01f, "Middle of the band: 26 from both arcs.");
            Assert.AreEqual(10f, Slot(Polar(113f, 120f)), 0.01f, "Inside the inner radius.");
            Assert.AreEqual(8f, Slot(Polar(113f, 190f)), 0.01f, "Beyond the outer radius.");
            Assert.Greater(Slot(Polar(140f, 156f)), 0f, "Past the end angle.");
            Assert.Greater(Slot(Polar(85f, 156f)), 0f, "Before the start angle.");
        }

        [Test]
        public void FullRing_HasNoSeam()
        {
            foreach (var angle in new[] { 0f, 90f, 180f, 270f, 45f })
            {
                Assert.AreEqual(-4f, UiShapeMath.Sector(Polar(angle, 68f), 64f, 72f, 0f, 360f), Eps, $"Angle {angle}.");
            }

            Assert.AreEqual(64f, UiShapeMath.Sector(Vector2.zero, 64f, 72f, 0f, 360f), Eps, "Centre is the hole.");
        }

        [Test]
        public void SweepOver180_GapIsOutside()
        {
            float Arc(Vector2 p) => UiShapeMath.Sector(p, 0f, 50f, 0f, 270f);

            Assert.Less(Arc(Polar(200f, 30f)), 0f);
            Assert.Less(Arc(Polar(10f, 30f)), 0f);
            Assert.Greater(Arc(Polar(300f, 30f)), 0f, "270..360 is the gap.");
        }

        [Test]
        public void SectorBounds_QuarterPie_AnnularSlot_FullRing()
        {
            var pie = UiShapeMath.SectorBounds(0f, 100f, 90f, 90f);
            Assert.AreEqual(-100f, pie.xMin, Eps);
            Assert.AreEqual(0f, pie.xMax, Eps);
            Assert.AreEqual(0f, pie.yMin, Eps);
            Assert.AreEqual(100f, pie.yMax, Eps);

            var slot = UiShapeMath.SectorBounds(130f, 182f, 93f, 40f);
            Assert.AreEqual(182f * Mathf.Cos(133f * Mathf.Deg2Rad), slot.xMin, Eps);
            Assert.AreEqual(130f * Mathf.Cos(93f * Mathf.Deg2Rad), slot.xMax, Eps);
            Assert.AreEqual(130f * Mathf.Sin(133f * Mathf.Deg2Rad), slot.yMin, Eps);
            Assert.AreEqual(182f * Mathf.Sin(93f * Mathf.Deg2Rad), slot.yMax, Eps, "No axis inside 93..133.");

            var crossing = UiShapeMath.SectorBounds(0f, 10f, 80f, 20f);
            Assert.AreEqual(10f, crossing.yMax, Eps, "Crosses +y: the outer radius is the top.");

            var ring = UiShapeMath.SectorBounds(5f, 10f, 30f, 360f);
            Assert.AreEqual(new Rect(-10f, -10f, 20f, 20f), ring);
        }

        [Test]
        public void Component_Distance_UsesTheRectAndCentre()
        {
            var go = new GameObject("Shape", typeof(RectTransform));
            try
            {
                var shape = go.AddComponent<UiShape>();
                ((RectTransform)go.transform).sizeDelta = new Vector2(100f, 100f);

                // Quarter pie with its corner at the rect's bottom-right corner (local 50, -50).
                shape.SetSector(new Vector2(1f, 0f), 0f, 100f, 90f, 90f);
                Assert.Less(shape.Distance(new Vector2(40f, -40f)), 0f);
                Assert.Greater(shape.Distance(new Vector2(-45f, 45f)), 0f, "Top-left corner of the rect is past the arc.");

                ((RectTransform)go.transform).sizeDelta = new Vector2(100f, 40f);
                shape.Kind = UiShapeKind.Capsule;
                Assert.AreEqual(-2f, shape.Distance(new Vector2(48f, 0f)), Eps, "Pill: round end of radius 20.");
                Assert.Greater(shape.Distance(new Vector2(49f, 18f)), 0f, "Rect corner is outside the pill.");

                shape.Kind = UiShapeKind.Rect;
                shape.CornerRadius = 0f;
                Assert.AreEqual(-1f, shape.Distance(new Vector2(49f, 18f)), Eps);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
