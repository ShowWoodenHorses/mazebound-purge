using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Purge.Core.Monsters;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Balance
{
    /// <summary>
    /// Balance numbers of the assets as CSV for the economy model (stage 10б): Export writes one table per type
    /// (a row per asset, a column per number — modules by type name), Apply writes the edited values back.
    /// Columns starting with <c>~</c> are read-only (resolved values for the model).
    /// </summary>
    internal static class BalanceTables
    {
        public const string Root = "Tools/Balance/Data";
        private const string DataRoot = "Assets/_Project/Data";
        private const char ReadOnly = '~';

        private sealed class Table
        {
            public string File;
            public string Type;
            public string[] SkipPrefixes = Array.Empty<string>();
            public Func<UnityEngine.Object, IEnumerable<(string, string)>> Extra;
        }

        private static readonly Table[] Tables =
        {
            new Table
            {
                File = "monsters.csv", Type = "MonsterDefinition",
                Extra = asset => MonsterExtra((MonsterDefinition)asset),
            },
            new Table { File = "difficulties.csv", Type = "DifficultyDefinition" },
            new Table { File = "weapons.csv", Type = "WeaponDefinition" },
            new Table { File = "throwables.csv", Type = "ThrowableDefinition" },
            new Table { File = "medkits.csv", Type = "MedkitDefinition" },
            new Table { File = "cards.csv", Type = "CardDefinition" },
            new Table { File = "deck.csv", Type = "CardDeck" },
            new Table { File = "player.csv", Type = "PlayerDefinition" },
            new Table { File = "waves.csv", Type = "WaveScenario" },
            new Table { File = "hordes.csv", Type = "HordeDefinition", SkipPrefixes = new[] { "_monster." } },
        };

        // Cosmetic modules and fields — not balance.
        private static readonly HashSet<string> SkipModules = new HashSet<string> { "FeelModule", "WeaponEffectsModule" };
        private static readonly HashSet<string> SkipFields = new HashSet<string> { "_id", "_soundPitch", "_soundVolume" };

        // References named in read-only columns: whose the numbers next to them are (a wave's monster, a gun's ammo…).
        private static readonly HashSet<string> Named = new HashSet<string>
            { "_monster", "_ammo", "_status", "_zone", "_difficulty", "_baseWeapon", "_card" };

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly Regex Element = new Regex(@"\.Array\.data\[(\d+)\]", RegexOptions.Compiled);

        [MenuItem("Purge/Balance/Export Tables", priority = 40)]
        private static void ExportMenu()
        {
            var count = Export();
            Debug.Log($"[Purge] Balance: exported {count} assets to {Root}/.");
        }

        [MenuItem("Purge/Balance/Apply Tables", priority = 41)]
        private static void ApplyMenu()
        {
            var report = Apply();
            Debug.Log("[Purge] Balance: " + report);
        }

        /// <summary>Writes every table; returns the number of assets.</summary>
        public static int Export()
        {
            Directory.CreateDirectory(Root);
            var total = 0;
            foreach (var table in Tables)
            {
                var rows = new List<(string Path, List<(string Column, string Value)> Cells)>();
                foreach (var (path, asset) in Assets(table))
                {
                    var cells = new List<(string, string)>();
                    if (table.Extra != null) cells.AddRange(table.Extra(asset));
                    foreach (var (column, property) in Columns(new SerializedObject(asset), table))
                        cells.Add((column, Read(property)));
                    rows.Add((path, cells));
                }

                var columns = new List<string>();
                var known = new HashSet<string>();
                foreach (var row in rows)
                foreach (var (column, _) in row.Cells)
                    if (known.Add(column)) columns.Add(column);

                var text = new StringBuilder();
                text.Append("asset");
                foreach (var column in columns) text.Append(',').Append(Quote(column));
                text.Append('\n');
                foreach (var row in rows)
                {
                    var values = row.Cells.ToDictionary(cell => cell.Column, cell => cell.Value);
                    text.Append(Quote(row.Path));
                    foreach (var column in columns)
                        text.Append(',').Append(values.TryGetValue(column, out var value) ? Quote(value) : "");
                    text.Append('\n');
                }

                File.WriteAllText(Path.Combine(Root, table.File), text.ToString());
                total += rows.Count;
            }

            return total;
        }

        /// <summary>Writes the tables' values into the assets; returns a short report.</summary>
        public static string Apply()
        {
            var changed = 0;
            var assets = 0;
            var problems = new List<string>();
            var speedChanged = false;
            foreach (var table in Tables)
            {
                var file = Path.Combine(Root, table.File);
                if (!File.Exists(file)) continue;
                var lines = ReadCsv(File.ReadAllText(file));
                if (lines.Count == 0) continue;
                var header = lines[0];
                var byPath = Assets(table).ToDictionary(pair => pair.Path, pair => pair.Asset);
                for (var r = 1; r < lines.Count; r++)
                {
                    var line = lines[r];
                    if (line.Count == 0 || string.IsNullOrEmpty(line[0])) continue;
                    if (!byPath.TryGetValue(line[0], out var asset))
                    {
                        problems.Add($"{table.File}: no asset {line[0]}");
                        continue;
                    }

                    var serialized = new SerializedObject(asset);
                    var properties = Columns(serialized, table).ToDictionary(pair => pair.Column, pair => pair.Property);
                    var assetChanged = false;
                    for (var c = 1; c < header.Count && c < line.Count; c++)
                    {
                        var column = header[c];
                        if (column.Length == 0 || column[0] == ReadOnly || line[c].Length == 0) continue;
                        if (!properties.TryGetValue(column, out var property))
                        {
                            problems.Add($"{table.File}: {line[0]} has no {column}");
                            continue;
                        }

                        if (Read(property) == line[c]) continue;
                        if (!Write(property, line[c]))
                        {
                            problems.Add($"{table.File}: {line[0]} {column} = '{line[c]}' is not a value");
                            continue;
                        }

                        changed++;
                        assetChanged = true;
                        if (column == "_stats.Speed") speedChanged = true;
                    }

                    if (!assetChanged) continue;
                    Undo.RecordObject(asset, "Apply Balance Tables");
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(asset);
                    assets++;
                }
            }

            AssetDatabase.SaveAssets();
            var report = $"applied {changed} values to {assets} assets.";
            if (speedChanged) report += " Monster speed changed — run Purge → Build Monsters (animation speed).";
            foreach (var problem in problems.Take(20)) report += "\n  " + problem;
            if (problems.Count > 20) report += $"\n  …and {problems.Count - 20} more";
            return report;
        }

        private static IEnumerable<(string Path, UnityEngine.Object Asset)> Assets(Table table)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + table.Type, new[] { DataRoot }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g), StringComparer.Ordinal))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null || asset.GetType().Name != table.Type) continue;
                yield return (path.Substring(DataRoot.Length + 1), asset);
            }
        }

        private static IEnumerable<(string, string)> MonsterExtra(MonsterDefinition monster)
        {
            yield return ("~rank", monster.Rank.ToString());
            yield return ("~biome", monster.Biome.ToString());
            yield return ("~cost", F(monster.Cost));
            yield return ("~money", F(monster.Money));
            yield return ("~experience", F(monster.Experience));
            yield return ("~score", F(monster.Score));
        }

        /// <summary>Numeric, boolean and enum leaves of the asset, named by field path; modules — by type name.</summary>
        private static IEnumerable<(string Column, SerializedProperty Property)> Columns(SerializedObject serialized, Table table)
        {
            var names = new Dictionary<string, string>();
            var seen = new Dictionary<string, int>();
            var property = serialized.GetIterator();
            var enter = true;
            while (property.Next(enter))
            {
                enter = false;
                var path = property.propertyPath;
                if (path.StartsWith("m_", StringComparison.Ordinal)) continue;
                if (property.propertyType == SerializedPropertyType.ManagedReference)
                {
                    var type = property.managedReferenceFullTypename;
                    var name = type.Length == 0 ? "" : type.Substring(type.LastIndexOfAny(new[] { ' ', '.' }) + 1);
                    if (name.Length == 0 || SkipModules.Contains(name)) continue;
                    var owner = path.Substring(0, path.LastIndexOf(".Array.data[", StringComparison.Ordinal));
                    var key = owner == "_modules" || owner == "_effects" ? name : ColumnOf(owner, names) + "." + name;
                    seen.TryGetValue(key, out var index);
                    seen[key] = ++index;
                    names[path] = index == 1 ? key : key + "#" + index;
                    enter = true;
                    continue;
                }

                switch (property.propertyType)
                {
                    case SerializedPropertyType.Generic:
                        enter = !property.isArray || property.arrayElementType != "string";
                        continue;
                    case SerializedPropertyType.ObjectReference:
                        if (!Named.Contains(path.Substring(path.LastIndexOf('.') + 1))) continue;
                        yield return (ReadOnly + ColumnOf(path, names), property.Copy());
                        continue;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Float:
                    case SerializedPropertyType.Boolean:
                    case SerializedPropertyType.Enum:
                        break;
                    default:
                        continue;
                }

                if (path.EndsWith(".Array.size", StringComparison.Ordinal)) continue;
                var field = path.Substring(path.LastIndexOf('.') + 1);
                if (SkipFields.Contains(field) || field.StartsWith("m_", StringComparison.Ordinal)) continue;
                var column = ColumnOf(path, names);
                if (table.SkipPrefixes.Any(prefix => column.StartsWith(prefix, StringComparison.Ordinal))) continue;
                yield return (column, property.Copy());
            }
        }

        // "_modules.Array.data[2]._damage" → "BulletAttack._damage"; "_waves.Array.data[0]._budget" → "_waves[0]._budget";
        // a module of a list other than _modules/_effects keeps the list: "_abilities.ChargeAbility._range".
        private static string ColumnOf(string path, Dictionary<string, string> modules)
        {
            var best = modules.Keys.Where(prefix => path == prefix || path.StartsWith(prefix + ".", StringComparison.Ordinal))
                .OrderByDescending(prefix => prefix.Length).FirstOrDefault();
            var tail = best != null ? modules[best] + path.Substring(best.Length) : path;
            return Element.Replace(tail, m => "[" + m.Groups[1].Value + "]");
        }

        private static string Read(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer: return property.longValue.ToString(Invariant);
                case SerializedPropertyType.Float: return F(property.floatValue);
                case SerializedPropertyType.Boolean: return property.boolValue ? "1" : "0";
                case SerializedPropertyType.ObjectReference: return property.objectReferenceValue != null ? property.objectReferenceValue.name : "";
                case SerializedPropertyType.Enum:
                    var index = property.enumValueIndex;
                    return index >= 0 && index < property.enumNames.Length ? property.enumNames[index] : property.intValue.ToString(Invariant);
                default: return "";
            }
        }

        private static bool Write(SerializedProperty property, string value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (!double.TryParse(value, NumberStyles.Float, Invariant, out var number)) return false;
                    property.longValue = (long)Math.Round(number);
                    return true;
                case SerializedPropertyType.Float:
                    if (!float.TryParse(value, NumberStyles.Float, Invariant, out var single)) return false;
                    property.floatValue = single;
                    return true;
                case SerializedPropertyType.Boolean:
                    if (value != "0" && value != "1") return false;
                    property.boolValue = value == "1";
                    return true;
                case SerializedPropertyType.Enum:
                    var index = Array.IndexOf(property.enumNames, value);
                    if (index < 0) return false;
                    property.enumValueIndex = index;
                    return true;
                default:
                    return false;
            }
        }

        private static string F(float value) => value.ToString("0.####", Invariant);

        private static string Quote(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";

        private static List<List<string>> ReadCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '"': quoted = true; break;
                    case ',': row.Add(cell.ToString()); cell.Clear(); break;
                    case '\r': break;
                    case '\n':
                        row.Add(cell.ToString()); cell.Clear();
                        rows.Add(row); row = new List<string>();
                        break;
                    default: cell.Append(c); break;
                }
            }

            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
