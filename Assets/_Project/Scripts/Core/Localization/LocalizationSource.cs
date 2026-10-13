using System;
using System.Collections.Generic;
using System.Text;

namespace Maze.Core.Localization
{
    /// <summary>
    /// The translation source: one tab-separated table (<c>Data/Localization/Strings.tsv</c>), first row
    /// <c>key, en, ru, …</c> (language codes; the first language is the base — its text fills gaps of the others),
    /// then a row per key. Empty lines and lines starting with <c>#</c> are skipped; <c>\n</c> in a cell is a line
    /// break. Rows <see cref="NameKey"/> and <see cref="SystemKey"/> describe the languages themselves.
    /// Parsed by Maze → Dev → Build Localization into per-language <see cref="LocalizationTable"/> assets.
    /// </summary>
    public sealed class LocalizationSource
    {
        /// <summary>The language's own name, shown in the language setting ("Deutsch").</summary>
        public const string NameKey = "language.name";

        /// <summary><c>UnityEngine.SystemLanguage</c> names (comma-separated) chosen automatically on the first start.</summary>
        public const string SystemKey = "language.system";

        private readonly List<string> _languages = new List<string>();
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<string> _errors = new List<string>();

        /// <summary>Language codes in column order; the first is the base language.</summary>
        public IReadOnlyList<string> Languages => _languages;

        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>Problems that make the table unusable (bad header, duplicate keys, empty base text…).</summary>
        public IReadOnlyList<string> Errors => _errors;

        public sealed class Row
        {
            public Row(string key, string[] texts, int line)
            {
                Key = key;
                Texts = texts;
                Line = line;
            }

            public string Key { get; }

            /// <summary>Text per language (<see cref="Languages"/> order); null where it is not translated.</summary>
            public string[] Texts { get; }

            /// <summary>1-based line in the file.</summary>
            public int Line { get; }
        }

        public static LocalizationSource Parse(string tsv)
        {
            var source = new LocalizationSource();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var lines = (tsv ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var header = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length > 0 && line[0] == '﻿') line = line.Substring(1);
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;

                var cells = line.Split('\t');
                if (!header)
                {
                    header = true;
                    if (cells.Length < 2 || cells[0].Trim() != "key")
                    {
                        source._errors.Add("The first row must be 'key<TAB>en<TAB>…' (language codes).");
                        return source;
                    }

                    for (var c = 1; c < cells.Length; c++)
                    {
                        var code = cells[c].Trim();
                        if (code.Length == 0 || source._languages.Contains(code))
                            source._errors.Add($"Line {i + 1}: language column {c} is empty or repeated.");
                        source._languages.Add(code);
                    }

                    continue;
                }

                var key = cells[0].Trim();
                if (key.Length == 0)
                {
                    source._errors.Add($"Line {i + 1}: no key.");
                    continue;
                }

                if (!keys.Add(key))
                {
                    source._errors.Add($"Line {i + 1}: key '{key}' is repeated.");
                    continue;
                }

                if (cells.Length - 1 > source._languages.Count)
                    source._errors.Add($"Line {i + 1}: '{key}' has more cells than languages (a tab inside a text?).");

                var texts = new string[source._languages.Count];
                for (var l = 0; l < texts.Length; l++)
                {
                    var cell = l + 1 < cells.Length ? cells[l + 1] : string.Empty;
                    texts[l] = cell.Length == 0 ? null : Unescape(cell);
                }

                if (texts.Length > 0 && texts[0] == null)
                    source._errors.Add($"Line {i + 1}: '{key}' has no text in the base language '{source._languages[0]}'.");

                source._rows.Add(new Row(key, texts, i + 1));
            }

            if (!header) source._errors.Add("The table is empty.");
            return source;
        }

        /// <summary>Index of the language column, or -1.</summary>
        public int IndexOf(string language) => _languages.IndexOf(language);

        /// <summary>Text of <paramref name="key"/> in <paramref name="language"/> (null when absent or not translated).</summary>
        public string Find(string key, string language)
        {
            var index = IndexOf(language);
            if (index < 0) return null;
            foreach (var row in _rows)
                if (row.Key == key) return row.Texts[index];
            return null;
        }

        /// <summary>
        /// Characters of all texts in <paramref name="language"/> (base-language text where a cell is empty), without
        /// line breaks; for font atlases.
        /// </summary>
        public string CharactersOf(string language)
        {
            var index = IndexOf(language);
            var set = new SortedSet<char>();
            if (index < 0) return string.Empty;
            foreach (var row in _rows)
            {
                var text = row.Texts[index] ?? row.Texts[0];
                if (text == null) continue;
                foreach (var c in text)
                    if (c >= ' ') set.Add(c);
            }

            var result = new StringBuilder(set.Count);
            foreach (var c in set) result.Append(c);
            return result.ToString();
        }

        private static string Unescape(string cell) => cell.Replace("\\n", "\n");
    }
}
