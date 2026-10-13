using Maze.Core.Common;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Common
{
    public class DeterministicRandomTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new DeterministicRandom(42);
            var b = new DeterministicRandom(42);
            for (var i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);
            var same = 0;
            for (var i = 0; i < 100; i++)
                if (a.NextInt(1000) == b.NextInt(1000))
                    same++;

            Assert.Less(same, 10);
        }

        [Test]
        public void KnownSeed_ProducesKnownValues()
        {
            // Guards against accidental algorithm changes: generated content must stay reproducible.
            var random = new DeterministicRandom(0);
            Assert.AreEqual(0xE220A8397B1DCDAFUL, random.NextULong());
            Assert.AreEqual(0x6E789E6AA1B965F4UL, random.NextULong());
        }

        [Test]
        public void NextInt_StaysInRange_AndCoversAllValues()
        {
            var random = new DeterministicRandom(7);
            var hits = new int[5];
            for (var i = 0; i < 5000; i++)
            {
                var value = random.NextInt(5);
                Assert.That(value, Is.InRange(0, 4));
                hits[value]++;
            }

            foreach (var count in hits)
                Assert.Greater(count, 800);
        }

        [Test]
        public void NextDouble_IsInUnitInterval()
        {
            var random = new DeterministicRandom(3);
            for (var i = 0; i < 1000; i++)
                Assert.That(random.NextDouble(), Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0));
        }
    }
}
