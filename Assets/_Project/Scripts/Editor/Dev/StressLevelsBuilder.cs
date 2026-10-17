using System;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Grid;
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
    /// (<c>Data/Horde/StressTest.asset</c>, made by Build Horde Visuals), Build / Sync. Also the stage 4 combat sandbox
    /// <c>Level_Sandbox</c> (40×40) with its settings <c>Data/Horde/Sandbox.asset</c> (made once as a copy of the stress
    /// test's, sandbox on; then tuned by hand): its <see cref="StressTestDefinition.SandboxWeapons"/> lie in a row south
    /// of the start; hedges of bushes north and east of it (fire spreads along them — stage 4б).
    /// Re-running regenerates them (same seeds) and keeps the assets' GUIDs.
    /// </summary>
    internal static class StressLevelsBuilder
    {
        private const string LevelsPath = "Assets/_Project/Data/Levels";
        private const string ThemePath = "Assets/_Project/Data/Themes/ForestTheme.asset";
        private const string StressTestPath = "Assets/_Project/Data/Horde/StressTest.asset";
        private const string SandboxPath = "Assets/_Project/Data/Horde/Sandbox.asset";
        private const float ObstacleDensity = 0.12f;
        private const string BushPath = "Assets/_Project/Data/Obstacles/Bush.asset";

        private static readonly (string Name, int Size, int Seed, bool Sandbox)[] Levels =
        {
            ("Level_Stress64", 64, 6464, false),
            ("Level_Stress100", 100, 100100, false),
            ("Level_Sandbox", 40, 4040, true),
        };

        [MenuItem("Purge/Dev/Build Stress Test Levels")]
        public static void Build()
        {
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath)
                        ?? throw new InvalidOperationException($"No theme at {ThemePath}.");
            var stress = AssetDatabase.LoadAssetAtPath<StressTestDefinition>(StressTestPath)
                         ?? throw new InvalidOperationException($"No stress test settings at {StressTestPath}: run Build Horde Visuals.");
            var sandbox = AssetDatabase.LoadAssetAtPath<StressTestDefinition>(SandboxPath);
            if (sandbox == null)
            {
                AssetDatabase.CopyAsset(StressTestPath, SandboxPath);
                sandbox = AssetDatabase.LoadAssetAtPath<StressTestDefinition>(SandboxPath);
                sandbox.ConfigureSandbox(true, 500, 60);
                EditorUtility.SetDirty(sandbox);
            }

            foreach (var (name, size, seed, isSandbox) in Levels)
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
                level.StressTest = isSandbox ? sandbox : stress;
                level.Generation.Width = size;
                level.Generation.Height = size;
                level.Generation.LayoutSeed = seed;
                level.Generation.VisualSeed = seed;
                level.Generation.ObstacleDensity = isSandbox ? ObstacleDensity * 0.5f : ObstacleDensity;
                LevelAuthoring.GenerateNew(level);
                if (isSandbox)
                {
                    LayOutWeapons(level, sandbox);
                    PlantBushes(level);
                }
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

        /// <summary>
        /// Two hedges of bushes (flammable): a line five cells north of the start and an L to the east, with gaps to walk
        /// through — a molotov there shows the fire spreading.
        /// </summary>
        private static void PlantBushes(LevelData level)
        {
            var bush = AssetDatabase.LoadAssetAtPath<ObstacleDefinition>(BushPath);
            if (bush == null)
            {
                Debug.LogWarning($"[Purge] No bush at {BushPath}: the sandbox has no hedges.");
                return;
            }

            var start = level.PlayerStarts[0].Position;
            for (var dx = -7; dx <= 7; dx++)
                if (Mathf.Abs(dx) != 3)
                    LevelEditing.SetObstacle(level, new GridPosition(start.X + dx, start.Y + 5, start.Floor), bush);
            for (var dy = -2; dy <= 6; dy++)
                if (dy != 2)
                    LevelEditing.SetObstacle(level, new GridPosition(start.X + 9, start.Y + dy, start.Floor), bush);
            for (var dx = 9; dx <= 13; dx++)
                LevelEditing.SetObstacle(level, new GridPosition(start.X + dx, start.Y - 3, start.Floor), bush);
        }

        /// <summary>The sandbox's weapons in a row two cells south of the start, on free cells.</summary>
        private static void LayOutWeapons(LevelData level, StressTestDefinition sandbox)
        {
            var start = level.PlayerStarts[0].Position;
            var grid = new LevelGrid(level.Geometry);
            var weapons = sandbox.SandboxWeapons;
            var x = start.X - weapons.Length / 2;
            foreach (var weapon in weapons)
            {
                if (weapon == null) continue;
                var cell = new GridPosition(x++, start.Y - 2, start.Floor);
                for (var y = cell.Y; y > 1 && grid.Has(cell, CellFlags.BlocksMovement); y--)
                    cell = new GridPosition(cell.X, y - 1, cell.Floor);
                LevelEditing.AddWeapon(level, cell, weapon);
            }
        }
    }
}
