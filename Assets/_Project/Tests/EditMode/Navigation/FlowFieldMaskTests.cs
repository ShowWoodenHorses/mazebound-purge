using System.Diagnostics;
using NUnit.Framework;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Navigation;

namespace Purge.Tests.EditMode.Navigation
{
    /// <summary>The flow field from a cached walkability mask (stage 7г): same result as the per-cell query, fast enough.</summary>
    public sealed class FlowFieldMaskTests
    {
        private static bool[] Forest(int width, int height, int seed)
        {
            var random = new DeterministicRandom(seed);
            var cells = new bool[width * height];
            for (var i = 0; i < cells.Length; i++) cells[i] = random.NextDouble() > 0.3;
            cells[(height / 2) * width + width / 2] = true;
            return cells;
        }

        [Test]
        public void MaskBuild_EqualsPassabilityBuild()
        {
            const int w = 40, h = 30;
            var mask = Forest(w, h, 7);
            var target = new GridPosition(w / 2, h / 2);
            var byQuery = new FlowField(w, h);
            var byMask = new FlowField(w, h);
            byQuery.Build(target, new CellMaskPassability(w, h, mask));
            byMask.Build(target, mask);

            for (var i = 0; i < w * h; i++)
            {
                Assert.AreEqual(byQuery.DistanceAt(i), byMask.DistanceAt(i), $"cell {i}");
                Assert.AreEqual(byQuery.NextAt(i), byMask.NextAt(i), $"cell {i}");
            }

            // Built again for another target: nothing of the old result stays.
            byMask.Build(new GridPosition(1, 1), mask);
            byQuery.Build(new GridPosition(1, 1), new CellMaskPassability(w, h, mask));
            for (var i = 0; i < w * h; i++)
                Assert.AreEqual(byQuery.DistanceAt(i), byMask.DistanceAt(i), $"cell {i}, second build");
        }

        [Test]
        public void MaskBuild_100x100_IsFast()
        {
            const int w = 100, h = 100;
            var mask = Forest(w, h, 3);
            var field = new FlowField(w, h);
            var target = new GridPosition(50, 50);
            field.Build(target, mask); // warm-up (JIT)

            const int runs = 20;
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < runs; i++) field.Build(target, mask);
            var ms = watch.Elapsed.TotalMilliseconds / runs;
            UnityEngine.Debug.Log($"[Purge] Flow field 100x100 from the mask: {ms:0.00} ms (editor; target on A16 ≤ 2 ms).");
            // The editor compiles C# in Debug (several times slower than a build); the ≤ 2 ms target is checked on the
            // phone (StressTest, FlowMs). Here — a bound that catches a regression (the per-cell query version took ~6 ms).
            Assert.Less(ms, 4.0);
        }
    }
}
