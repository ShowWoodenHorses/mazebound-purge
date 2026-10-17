using NUnit.Framework;
using Purge.Core.Navigation;

namespace Purge.Tests.EditMode.Navigation
{
    /// <summary>Stage 5b: a sound carries along the passages — walls stop it, a closed door muffles it.</summary>
    public class NoiseFieldTests
    {
        private const int Size = 30;

        private readonly struct Map : IGridAcoustics
        {
            private readonly float[] _cells;

            public Map(float[] cells) => _cells = cells;

            public float Transmission(int x, int y) => _cells[y * Size + x];
        }

        private static float[] Open()
        {
            var cells = new float[Size * Size];
            for (var i = 0; i < cells.Length; i++) cells[i] = 1f;
            return cells;
        }

        private static NoiseField Build(float[] cells, int x, int y, float radius)
        {
            var field = new NoiseField(24);
            field.Build(Size, Size, x, y, 0, radius, new Map(cells));
            return field;
        }

        [Test]
        public void OpenField_LoudnessIsRadiusMinusPath()
        {
            var field = Build(Open(), 10, 10, 8f);

            Assert.That(field.LoudnessAt(10, 10), Is.EqualTo(8f).Within(1e-4f));
            Assert.That(field.LoudnessAt(15, 10), Is.EqualTo(3f).Within(1e-4f));
            Assert.That(field.LoudnessAt(13, 13), Is.EqualTo(8f - 3f * 1.41421356f).Within(1e-3f), "diagonal steps");
            Assert.IsTrue(field.IsHeardAt(17, 10));
            Assert.IsFalse(field.IsHeardAt(18, 10), "beyond the radius");
            Assert.IsFalse(field.IsHeardAt(10, 25), "outside the window");
        }

        [Test]
        public void Wall_Stops_SoundGoesAroundThroughTheGap()
        {
            var cells = Open();
            // A wall along x = 12 from y = 0 to y = 14; the gap is at y = 15 and above.
            for (var y = 0; y <= 14; y++) cells[y * Size + 12] = 0f;
            var field = Build(cells, 10, 10, 16f);

            Assert.That(field.LoudnessAt(12, 10), Is.EqualTo(0f), "the wall itself");
            // Straight through it would be 4 cells; around the wall end it is much longer.
            var behind = field.LoudnessAt(14, 10);
            Assert.That(behind, Is.LessThan(16f - 4f - 1f));
            Assert.That(behind, Is.GreaterThan(0f), "heard around the wall");

            var short3 = Build(cells, 10, 10, 6f);
            Assert.IsFalse(short3.IsHeardAt(14, 10), "a short sound does not reach around");
            Assert.IsTrue(short3.IsHeardAt(5, 10), "the same distance on the open side is heard");
        }

        [Test]
        public void ClosedDoor_HalvesWhatIsLeft()
        {
            var cells = new float[Size * Size];
            // A corridor y = 5, x = 0..29, a closed door at x = 13.
            for (var x = 0; x < Size; x++) cells[5 * Size + x] = 1f;
            cells[5 * Size + 13] = 0.5f;
            var field = Build(cells, 10, 5, 10f);

            Assert.That(field.LoudnessAt(12, 5), Is.EqualTo(8f).Within(1e-4f));
            Assert.That(field.LoudnessAt(13, 5), Is.EqualTo((10f - 3f) * 0.5f).Within(1e-4f));
            Assert.That(field.LoudnessAt(15, 5), Is.EqualTo((10f - 3f) * 0.5f - 2f).Within(1e-4f));
            Assert.IsFalse(field.IsHeardAt(17, 5), "the door muffled it: open, it would reach x = 19");
            Assert.IsTrue(field.IsHeardAt(5, 5), "the other way it carries the whole radius");
        }

        [Test]
        public void Steps_LeadToTheSource()
        {
            var cells = Open();
            for (var y = 0; y <= 14; y++) cells[y * Size + 12] = 0f;
            var field = Build(cells, 10, 10, 20f);

            int x = 14, y0 = 10, steps = 0;
            while (field.TryStepToward(x, y0, out var nx, out var ny) && steps < 100)
            {
                Assert.That(cells[ny * Size + nx], Is.GreaterThan(0f), "never through the wall");
                Assert.That(field.LoudnessAt(nx, ny), Is.GreaterThan(field.LoudnessAt(x, y0)), "louder every step");
                x = nx;
                y0 = ny;
                steps++;
            }

            Assert.That((x, y0), Is.EqualTo((10, 10)));
            Assert.That(steps, Is.GreaterThan(4), "around the wall, not through it");
        }
    }
}
