using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Core.Localization;
using Maze.Core.Visual;
using Maze.Presentation.Localization;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Localization: turns the translation table <c>Data/Localization/Strings.tsv</c> (key + a
    /// column per language) into a <see cref="LocalizationTable"/> per language and the <see cref="LanguageCatalog"/>
    /// (all Addressable, group Maze Shared), checks it (<see cref="LocalizationCheck"/>: keys of <see cref="TextKeys"/>
    /// missing from the table, unused keys, untranslated texts, placeholders; names of weapons and messages for key
    /// colours of the content) and rebuilds the font atlases for the characters of all languages.
    /// Run after editing the table.
    /// </summary>
    internal static class LocalizationBuilder
    {
        public const string Folder = "Assets/_Project/Data/Localization";
        public const string SourcePath = Folder + "/Strings.tsv";
        public const string CatalogPath = Folder + "/Languages.asset";

        [MenuItem("Maze/Dev/Build Localization")]
        public static void BuildMenu()
        {
            var problems = Build(out var report);
            Debug.Log("[Maze] Build Localization:\n" + report);
            if (problems > 0)
                EditorUtility.DisplayDialog("Build Localization", $"{problems} error(s), see the console.", "OK");
        }

        /// <summary>Builds the tables and the catalog; returns the number of errors (nothing is written on errors).</summary>
        public static int Build(out string report)
        {
            var text = File.Exists(SourcePath) ? File.ReadAllText(SourcePath, Encoding.UTF8) : null;
            var log = new StringBuilder();
            if (text == null)
            {
                report = $"Translation table '{SourcePath}' not found.";
                return 1;
            }

            var source = LocalizationSource.Parse(text);
            var issues = Check(source);
            var errors = issues.Count(issue => issue.IsError);
            foreach (var issue in issues.OrderByDescending(issue => issue.IsError))
                log.AppendLine(issue.ToString());
            if (errors > 0)
            {
                report = log.ToString();
                return errors;
            }

            var entries = new List<LanguageEntry>();
            for (var l = 0; l < source.Languages.Count; l++)
            {
                var code = source.Languages[l];
                var table = WriteTable(source, l);
                var name = source.Find(LocalizationSource.NameKey, code) ?? code;
                entries.Add(new LanguageEntry(code, name, SystemLanguages(source, code, log),
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(table))));
                log.AppendLine($"{code} ({name}): {table.Count} texts, {source.Rows.Count(row => row.Texts[l] == null)} untranslated.");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<LanguageCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LanguageCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.SetLanguages(entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            var problem = LevelDesigner.LevelSync.SyncShared(AddressableAssetSettingsDefaultObject.GetSettings(true));
            if (problem != null) log.AppendLine("Warning: " + problem);
            AssetDatabase.SaveAssets();

            // The font atlases hold only the characters the texts use.
            UiStyleBuilder.Build();

            report = log.ToString();
            return 0;
        }

        /// <summary>The table checks plus content: every weapon has a name, every key colour its "locked" message.</summary>
        public static List<LocalizationCheck.Issue> Check(LocalizationSource source)
        {
            var keys = new List<string>();
            var prefixes = new List<string>();
            foreach (var field in typeof(TextKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                (field.Name.EndsWith("Prefix", StringComparison.Ordinal) ? prefixes : keys).Add((string)field.GetRawConstantValue());
            }

            var issues = LocalizationCheck.Run(source, keys, prefixes);
            var tableKeys = new HashSet<string>(source.Rows.Select(row => row.Key));

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(WeaponDefinition)))
            {
                var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                var id = weapon != null && !string.IsNullOrEmpty(weapon.Id) ? weapon.Id : weapon?.name;
                if (id != null && !tableKeys.Contains(TextKeys.WeaponPrefix + id))
                    issues.Add(new LocalizationCheck.Issue(false, $"Weapon '{id}' has no name ('{TextKeys.WeaponPrefix}{id}')."));
            }

            var colors = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(VisualSet)))
            {
                var set = AssetDatabase.LoadAssetAtPath<VisualSet>(AssetDatabase.GUIDToAssetPath(guid));
                if (set == null) continue;
                foreach (var variant in set.Variants)
                    if (variant != null && variant.HasColor && !variant.ColorTag.StartsWith("#", StringComparison.Ordinal))
                        colors.Add(variant.ColorTag);
            }

            foreach (var color in colors)
                if (!tableKeys.Contains(TextKeys.HudLockedPrefix + color))
                    issues.Add(new LocalizationCheck.Issue(false,
                        $"Key colour '{color}' has no message ('{TextKeys.HudLockedPrefix}{color}'); the plain one is shown."));

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(TutorialScript)))
            {
                var script = AssetDatabase.LoadAssetAtPath<TutorialScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script == null) continue;
                foreach (var step in script.Steps)
                    if (!string.IsNullOrEmpty(step.Text) && !tableKeys.Contains(step.Text))
                        issues.Add(new LocalizationCheck.Issue(true, $"Tutorial '{script.name}' uses '{step.Text}', missing in the table."));
            }

            return issues;
        }

        private static LocalizationTable WriteTable(LocalizationSource source, int language)
        {
            var code = source.Languages[language];
            var path = $"{Folder}/Texts_{code}.asset";
            var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(path);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<LocalizationTable>();
                AssetDatabase.CreateAsset(table, path);
            }

            var keys = new List<string>();
            var texts = new List<string>();
            var missing = new List<string>();
            foreach (var row in source.Rows)
            {
                if (row.Key == LocalizationSource.SystemKey) continue;
                keys.Add(row.Key);
                var text = row.Texts[language];
                if (text == null) missing.Add(row.Key);
                texts.Add(text ?? row.Texts[0]);
            }

            table.Fill(code, keys, texts, missing);
            EditorUtility.SetDirty(table);
            return table;
        }

        private static IEnumerable<SystemLanguage> SystemLanguages(LocalizationSource source, string code, StringBuilder log)
        {
            var names = source.Find(LocalizationSource.SystemKey, code);
            if (string.IsNullOrEmpty(names)) yield break;
            foreach (var name in names.Split(','))
            {
                if (Enum.TryParse(name.Trim(), out SystemLanguage language)) yield return language;
                else log.AppendLine($"Warning: '{code}': unknown system language '{name.Trim()}'.");
            }
        }
    }
}
