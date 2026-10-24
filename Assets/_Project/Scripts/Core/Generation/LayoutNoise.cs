using System;
using Purge.Core.Common;

namespace Purge.Core.Generation
{
    /// <summary>Smooth value noise for the generator (groves, grass patches, winding trails); 0..1, stable per seed.</summary>
    public static class LayoutNoise
    {
        /// <summary>Two octaves at <paramref name="scale"/> lattice cells per grid cell.</summary>
        public static float Fractal(int seed, int x, int y, float scale) =>
            0.65f * Value(seed, x * scale, y * scale) + 0.35f * Value(seed ^ 0x5EED, x * scale * 2.13f + 17.3f, y * scale * 2.13f + 5.1f);

        public static float Value(int seed, float x, float y)
        {
            var ix = (int)Math.Floor(x);
            var iy = (int)Math.Floor(y);
            var fx = Smooth(x - ix);
            var fy = Smooth(y - iy);
            var a = Lattice(seed, ix, iy);
            var b = Lattice(seed, ix + 1, iy);
            var c = Lattice(seed, ix, iy + 1);
            var d = Lattice(seed, ix + 1, iy + 1);
            return Lerp(Lerp(a, b, fx), Lerp(c, d, fx), fy);
        }

        private static float Lattice(int seed, int x, int y) =>
            (StableHash.Combine(StableHash.Combine((ulong)(uint)seed, (ulong)(uint)x), (ulong)(uint)y) >> 40) / (float)(1UL << 24);

        private static float Smooth(float t) => t * t * (3f - 2f * t);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
