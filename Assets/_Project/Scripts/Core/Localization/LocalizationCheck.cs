using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Maze.Core.Localization
{
    /// <summary>
    /// Checks of the translation table (Maze → Dev → Build Localization, tests): keys the game uses but the table lacks
    /// (error), table keys nobody uses (warning), untranslated texts (warning), texts whose <c>{0}</c> placeholders differ
    /// from the base language's (error) and missing language rows (<see cref="LocalizationSource.NameKey"/>).
    /// </summary>
    public static class LocalizationCheck
    {
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)[^}]*\}", RegexOptions.Compiled);

        public readonly struct Issue
        {
            public Issue(bool error, string message)
            {
                IsError = error;
                Message = message;
            }

            public bool IsError { get; }
            public string Message { get; }

            public override string ToString() => (IsError ? "Error: " : "Warning: ") + Message;
        }

        /// <param name="usedKeys">Keys the game asks for (constants).</param>
        /// <param name="usedPrefixes">Key families (content ids follow the prefix); their keys are never "unused".</param>
        public static List<Issue> Run(LocalizationSource source, IEnumerable<string> usedKeys, IEnumerable<string> usedPrefixes)
        {
            var issues = new List<Issue>();
            foreach (var error in source.Errors) issues.Add(new Issue(true, error));
            if (source.Languages.Count == 0) return issues;

            var tableKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in source.Rows) tableKeys.Add(row.Key);

            var used = new HashSet<string>(usedKeys ?? Array.Empty<string>(), StringComparer.Ordinal)
            {
                LocalizationSource.NameKey,
                LocalizationSource.SystemKey,
            };
            var prefixes = new List<string>(usedPrefixes ?? Array.Empty<string>());

            foreach (var key in used)
                if (!tableKeys.Contains(key))
                    issues.Add(new Issue(true, $"Key '{key}' is used by the game but not in the table."));

            foreach (var row in source.Rows)
            {
                if (!used.Contains(row.Key) && !HasPrefix(row.Key, prefixes))
                    issues.Add(new Issue(false, $"Line {row.Line}: key '{row.Key}' is not used by the game."));

                var basePlaceholders = Placeholders(row.Texts[0]);
                for (var l = 1; l < row.Texts.Length; l++)
                {
                    var text = row.Texts[l];
                    if (text == null)
                    {
                        // Unity has no system language for some (Kazakh, Uzbek): the site or the settings choose them.
                        if (row.Key == LocalizationSource.SystemKey) continue;
                        issues.Add(new Issue(false, $"Line {row.Line}: '{row.Key}' is not translated into '{source.Languages[l]}'."));
                        continue;
                    }

                    if (!basePlaceholders.SetEquals(Placeholders(text)))
                        issues.Add(new Issue(true,
                            $"Line {row.Line}: '{row.Key}' in '{source.Languages[l]}' has other {{n}} placeholders than '{source.Languages[0]}'."));
                }
            }

            return issues;
        }

        /// <summary>Numbers of the <c>{n}</c> placeholders in <paramref name="text"/>.</summary>
        public static HashSet<int> Placeholders(string text)
        {
            var result = new HashSet<int>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (Match match in Placeholder.Matches(text))
                result.Add(int.Parse(match.Groups[1].Value));
            return result;
        }

        private static bool HasPrefix(string key, List<string> prefixes)
        {
            foreach (var prefix in prefixes)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
