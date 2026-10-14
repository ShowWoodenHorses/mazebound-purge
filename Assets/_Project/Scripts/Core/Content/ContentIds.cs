using System;
using System.Text;

namespace Purge.Core.Content
{
    /// <summary>
    /// Rules of stable content ids: lower-case latin letters and digits in words joined by '_' ("assault_rifle_01",
    /// "forest_zombie_walker"). Made from the asset's file name once; a taken id gets a "_2", "_3"… suffix.
    /// </summary>
    public static class ContentIds
    {
        public const int MaxLength = 48;

        public static bool IsValid(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxLength) return false;
            var previousUnderscore = true; // no leading '_'
            foreach (var c in id)
            {
                if (c == '_')
                {
                    if (previousUnderscore) return false;
                    previousUnderscore = true;
                    continue;
                }

                if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9')) return false;
                previousUnderscore = false;
            }

            return !previousUnderscore; // no trailing '_'
        }

        /// <summary>
        /// Id from a file name: words split at case changes and at anything but latin letters and digits
        /// ("ForestZombieWalker" → "forest_zombie_walker", "AK-74" → "ak_74", "MP5" → "mp5"). Never empty.
        /// </summary>
        public static string FromName(string name)
        {
            var builder = new StringBuilder();
            var pendingSeparator = false;
            char previous = '\0';
            foreach (var c in name ?? string.Empty)
            {
                var letter = c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z';
                var digit = c >= '0' && c <= '9';
                if (!letter && !digit)
                {
                    pendingSeparator = builder.Length > 0;
                    previous = c;
                    continue;
                }

                // A new word: an upper-case letter after a lower-case one ("zombieWalker").
                var upper = c >= 'A' && c <= 'Z';
                if (upper && previous >= 'a' && previous <= 'z' && builder.Length > 0)
                    pendingSeparator = true;

                if (pendingSeparator)
                {
                    builder.Append('_');
                    pendingSeparator = false;
                }

                builder.Append(char.ToLowerInvariant(c));
                previous = c;
            }

            var id = builder.Length > 0 ? builder.ToString() : "content";
            return id.Length <= MaxLength ? id : id.Substring(0, MaxLength).TrimEnd('_');
        }

        /// <summary><paramref name="id"/>, or the first of "id_2", "id_3"… that <paramref name="isTaken"/> rejects.</summary>
        public static string MakeUnique(string id, Func<string, bool> isTaken)
        {
            if (!isTaken(id)) return id;
            for (var n = 2; ; n++)
            {
                var suffix = "_" + n;
                var head = id.Length + suffix.Length > MaxLength ? id.Substring(0, MaxLength - suffix.Length).TrimEnd('_') : id;
                var candidate = head + suffix;
                if (!isTaken(candidate)) return candidate;
            }
        }
    }
}
