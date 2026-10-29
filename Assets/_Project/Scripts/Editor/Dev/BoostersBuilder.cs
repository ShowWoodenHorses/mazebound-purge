using System;
using System.Collections.Generic;
using System.IO;
using Purge.Core.Boosters;
using Purge.Core.Definitions;
using Purge.Core.Level;
using Purge.Presentation.UI.Style;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Boosters (stage 11б): makes the basic boosters in <c>Data/Boosters</c> that are not there yet
    /// (a booster already there keeps whatever was changed in it): modules, the icon from the UI icons and the prices by
    /// location and difficulty from the economy model (<c>Tools/Balance/Model/boosters.csv</c>, its column per booster).
    /// Boosters without an icon get theirs again. A new booster is an asset in the folder and its texts in Strings.tsv
    /// (<c>booster.&lt;id&gt;</c>, <c>booster.&lt;id&gt;.text</c>), no code; Build Content Catalog lists it.
    /// </summary>
    internal static class BoostersBuilder
    {
        public const string Folder = "Assets/_Project/Data/Boosters";
        private const string PricesPath = "Tools/Balance/Model/boosters.csv";
        private const string GrenadePath = "Assets/_Project/Data/Throwables/Grenade.asset";
        private const string MedkitPath = "Assets/_Project/Data/Medkits/Medical_Crate.asset";

        private sealed class Draft
        {
            public string File;
            public string Column;
            public Func<UiIconSet, Sprite> Icon;
            public Func<BoosterModule> Module;
        }

        [MenuItem("Purge/Dev/Build Boosters")]
        public static void BuildMenu() => Debug.Log("[Purge] " + Build());

        public static string Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Boosters");
            var grenade = AssetDatabase.LoadAssetAtPath<ThrowableDefinition>(GrenadePath);
            var medkit = AssetDatabase.LoadAssetAtPath<MedkitDefinition>(MedkitPath);
            var icons = AssetDatabase.LoadAssetAtPath<UiIconSet>(UiIconsBuilder.SetPath);
            var prices = ReadPrices();

            var drafts = new[]
            {
                new Draft { File = "Booster_Grenades", Column = "+3 grenades", Icon = set => set.Grenade,
                    Module = () => new ThrowablesBoost(3, grenade) },
                new Draft { File = "Booster_StartCard", Column = "Starting card", Icon = set => set.Star,
                    Module = () => new StartCardBoost() },
                new Draft { File = "Booster_Medkit", Column = "Medkit", Icon = set => set.Medkit,
                    Module = () => new MedkitBoost(medkit) },
                new Draft { File = "Booster_Reroll", Column = "+1 reroll", Icon = set => set.Retry,
                    Module = () => new RerollBoost() },
                new Draft { File = "Booster_Scouting", Column = "Scouting", Icon = set => set.Map,
                    Module = () => new ScoutingBoost() },
            };

            var created = 0;
            var problems = new List<string>();
            foreach (var draft in drafts)
            {
                var path = $"{Folder}/{draft.File}.asset";
                var booster = AssetDatabase.LoadAssetAtPath<BoosterDefinition>(path);
                if (booster == null)
                {
                    booster = ScriptableObject.CreateInstance<BoosterDefinition>();
                    prices.TryGetValue(draft.Column, out var table);
                    booster.Configure(null, BoosterUnlock.FirstLevel, table, draft.Module());
                    AssetDatabase.CreateAsset(booster, path); // the id is given by ContentIdAssigner
                    created++;
                }

                if (booster.Icon == null && icons != null)
                {
                    booster.Icon = draft.Icon(icons);
                    EditorUtility.SetDirty(booster);
                }

                booster.Validate(problems);
            }

            AssetDatabase.SaveAssets();
            foreach (var problem in problems) Debug.LogWarning("[Purge] " + problem);
            return $"Boosters built: {drafts.Length}, {created} new, {problems.Count} problem(s).";
        }

        /// <summary>The model's table: column → prices by location (rows «Forest,Easy,…»), 4 difficulties each.</summary>
        private static Dictionary<string, List<BoosterPrice>> ReadPrices()
        {
            var result = new Dictionary<string, List<BoosterPrice>>(StringComparer.Ordinal);
            if (!File.Exists(PricesPath))
            {
                Debug.LogWarning($"[Purge] {PricesPath} is missing: boosters are made without prices.");
                return result;
            }

            var lines = File.ReadAllLines(PricesPath);
            if (lines.Length == 0) return result;
            var header = lines[0].Split(',');
            var byColumn = new Dictionary<string, Dictionary<LevelLocation, int[]>>(StringComparer.Ordinal);
            for (var row = 1; row < lines.Length; row++)
            {
                var cells = lines[row].Split(',');
                if (cells.Length < 3 || !Enum.TryParse(cells[0], out LevelLocation location) ||
                    !Enum.TryParse(cells[1], out DifficultyName difficulty))
                    continue;
                for (var column = 2; column < cells.Length && column < header.Length; column++)
                {
                    if (!int.TryParse(cells[column], out var price)) continue;
                    if (!byColumn.TryGetValue(header[column], out var locations))
                        byColumn[header[column]] = locations = new Dictionary<LevelLocation, int[]>();
                    if (!locations.TryGetValue(location, out var values)) locations[location] = values = new int[4];
                    values[(int)difficulty] = price;
                }
            }

            foreach (var pair in byColumn)
            {
                var list = new List<BoosterPrice>();
                foreach (var location in pair.Value) list.Add(new BoosterPrice(location.Key, location.Value));
                result[pair.Key] = list;
            }

            return result;
        }

        /// <summary>Difficulty names of the model's tables.</summary>
        private enum DifficultyName
        {
            Easy = 0,
            Normal = 1,
            Hard = 2,
            Unreal = 3,
        }
    }
}
