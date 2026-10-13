namespace Maze.Core.Common
{
    /// <summary>
    /// Platform-independent hashing. Never use string.GetHashCode for persisted or seeded data:
    /// it is not guaranteed to be stable across runtimes.
    /// </summary>
    public static class StableHash
    {
        public static ulong Mix(ulong value)
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        public static ulong Combine(ulong a, ulong b) => Mix(a * 0x9E3779B97F4A7C15UL + b);

        /// <summary>FNV-1a over UTF-16 code units.</summary>
        public static ulong Of(string value)
        {
            var hash = 0xCBF29CE484222325UL;
            if (value == null)
                return hash;

            foreach (var c in value)
            {
                hash ^= c;
                hash *= 0x100000001B3UL;
            }

            return hash;
        }
    }
}
