using Maze.Presentation.UI.Touch;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Player
{
    /// <summary>On-screen stick response: dead zone, soft start, full output before the edge.</summary>
    public class StickResponseTests
    {
        private const float DeadZone = 0.2f;

        [Test]
        public void InsideDeadZone_IsZero()
        {
            var exponent = StickResponse.ExponentFor(0.5f);
            Assert.AreEqual(Vector2.zero, StickResponse.Shape(Vector2.zero, DeadZone, exponent));
            Assert.AreEqual(Vector2.zero, StickResponse.Shape(new Vector2(0.19f, 0f), DeadZone, exponent));
        }

        [Test]
        public void PastDeadZone_StartsAtSlowestWalk_KeepsDirection()
        {
            var value = StickResponse.Shape(new Vector2(0f, -0.21f), DeadZone, StickResponse.ExponentFor(0.5f));

            Assert.That(value.magnitude, Is.EqualTo(StickResponse.MinOutput).Within(0.01f));
            Assert.That(value.magnitude, Is.GreaterThan(0.1f), "Above the dead zone of PlayerSystem.");
            Assert.That(Vector2.Angle(value, Vector2.down), Is.LessThan(0.01f));
        }

        [Test]
        public void NearTheEdge_IsFull()
        {
            var exponent = StickResponse.ExponentFor(0f);
            Assert.That(StickResponse.Shape(new Vector2(StickResponse.FullAt, 0f), DeadZone, exponent).magnitude, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(StickResponse.Shape(new Vector2(3f, 4f), DeadZone, exponent).magnitude, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void LowerSensitivity_IsSlowerInTheMiddle_AndMonotonic()
        {
            var middle = new Vector2(0.5f, 0f);
            var soft = StickResponse.Shape(middle, DeadZone, StickResponse.ExponentFor(0f)).magnitude;
            var quick = StickResponse.Shape(middle, DeadZone, StickResponse.ExponentFor(1f)).magnitude;
            Assert.That(soft, Is.LessThan(quick));

            var previous = 0f;
            for (var x = 0f; x <= 1.2f; x += 0.05f)
            {
                var output = StickResponse.Shape(new Vector2(x, 0f), DeadZone, StickResponse.ExponentFor(0.5f)).magnitude;
                Assert.That(output, Is.GreaterThanOrEqualTo(previous));
                previous = output;
            }
        }
    }
}
