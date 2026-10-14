using System;
using Purge.Core.Authoring;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Editor.LevelDesigner;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Stress Test Levels: the stage 2 stress test levels (development catalog): open forest
    /// fields 64×64 and 100×100 with pillars (Generate New), the stress test settings
    /// (<c>Data/Horde/StressTest.asset</c>, made by Build Horde Visuals), Build / Sync. Re-running regenerates them
    /// (same seeds) and keeps the assets' GUIDs.
    /// </summary>
    internal static class StressLevelsBuilder
    {
        private const string LevelsPath = "Assets/_Project/Data/Levels";
        private const string ThemePath = "Assets/_Project/Data/Themes/ForestTheme.asset";
        private const string StressTestPath = "Assets/_Project/Data/Horde/StressTest.asset";
        private const float ObstacleDensity = 0.12f;

        private static readonly (string Name, int Size, int Seed)[] Levels =
        {
            ("Level_Stress64", 64, 6464),
            ("Level_Stress100", 100, 100100),
        };

        [MenuItem("Purge/Dev/Build Stress Test Levels")]
        public static void Build()
        {
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath)
                        ?? throw new InvalidOperationException($"No theme at {ThemePath}.");
            var stress = AssetDatabase.LoadAssetAtPath<StressTestDefinition>(StressTestPath)
                         ?? throw new InvalidOperationException($"No stress test settings at {StressTestPath}: run Build Horde Visuals.");

            foreach (var (name, size, seed) in Levels)
            {
                var path = $"{LevelsPath}/{name}.asset";
                var level = AssetDatabase.LoadAssetAtPath<LevelData>(path);
                if (level == null)
                {
                    level = ScriptableObject.CreateInstance<LevelData>();
                    AssetDatabase.CreateAsset(level, path);
                }

                level.Settings.DisplayName = name.Replace("Level_", "");
                level.VisualTheme = theme;
                level.StressTest = stress;
                level.Generation.Width = size;
                level.Generation.Height = size;
                level.Generation.LayoutSeed = seed;
                level.Generation.VisualSeed = seed;
                level.Generation.ObstacleDensity = ObstacleDensity;
                LevelAuthoring.GenerateNew(level);
                EditorUtility.SetDirty(level);
                AssetDatabase.SaveAssets();

                var report = EditorLevelValidator.Validate(level);
                if (!report.IsValid)
                {
                    Debug.LogError($"[Purge] '{name}' has {report.ErrorCount} validation error(s); not synced.");
                    continue;
                }

                if (!LevelSync.IsDevLevel(level))
                    LevelSync.SetDevLevel(level, true);
                LevelSync.Sync(level);
            }
        }
    }
}
