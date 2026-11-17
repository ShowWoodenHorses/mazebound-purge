using System;
using System.Linq;
using Purge.Core.Authoring;
using Purge.Core.Generation;
using Purge.Core.Generation.Steps;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Editor.LevelDesigner;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Editor.Dev
{
    internal static class StairsLevelBuilder
    {
        private const string LevelPath = "Assets/_Project/Data/Levels/Level_Stairs.asset";
        private const string RecipePath = "Assets/_Project/Data/Recipes/DevBuilding.asset";
        private const string ThemePath = "Assets/_Project/Data/Themes/CastleBlockTheme.asset";
        private const string StairPath = "Assets/_Project/Art/Items/Purge/Stairs/Stairs_1x2.prefab";

        [MenuItem("Purge/Dev/Build Stairs Level")]
        public static void Build()
        {
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath)
                        ?? throw new InvalidOperationException("CastleBlockTheme is missing.");
            var run = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/_Project/Data/Levels/Level_Run.asset")
                      ?? throw new InvalidOperationException("Build the Run level first.");
            var stairs = AssetDatabase.AssetPathToGUID(StairPath);
            if (string.IsNullOrEmpty(stairs)) throw new InvalidOperationException("The stair prefab is missing.");
            theme.Stairs = new AssetReferenceGameObject(stairs);
            EditorUtility.SetDirty(theme);

            var recipe = LoadOrCreate<BiomeRecipe>(RecipePath);
            recipe.MutableSteps.Clear();
            recipe.MutableSteps.Add(new PerimeterStep());
            recipe.MutableSteps.Add(new StartStep());
            recipe.MutableSteps.Add(new BuildingStep { Basement = true });
            EditorUtility.SetDirty(recipe);

            var level = LoadOrCreate<LevelData>(LevelPath);
            level.Settings.DisplayName = "Stairs";
            level.VisualTheme = theme;
            level.Horde = run.Horde;
            level.Generation.Recipe = recipe;
            level.Generation.Width = 24;
            level.Generation.Height = 24;
            level.Generation.LayoutSeed = 1301;
            level.Generation.VisualSeed = 1301;
            level.Generation.DecorDensity = 0f;
            var result = LevelAuthoring.GenerateNew(level);
            if (result.Problems.Count > 0)
                throw new InvalidOperationException(string.Join("; ", result.Problems));

            var geometry = level.Geometry;
            for (var floor = 0; floor < geometry.FloorCount; floor++)
            {
                if (floor == geometry.GroundFloor) continue;
                var spawn = new GridPosition(geometry.Width / 2, geometry.Height / 2, floor);
                if (geometry.Stairs.Any(stair => stair.Uses(spawn)))
                    spawn = new GridPosition(spawn.X + 2, spawn.Y);
                LevelEditing.AddMonsterSpawn(level, spawn.OnFloor(floor));
            }

            var report = EditorLevelValidator.Validate(level);
            if (!report.IsValid)
                throw new InvalidOperationException(string.Join("; ", report.Issues
                    .Where(issue => issue.Severity == Purge.Core.Validation.ValidationSeverity.Error)
                    .Select(issue => issue.Message)));

            EditorUtility.SetDirty(level);
            LevelSync.SetDevLevel(level, true);
            LevelSync.Sync(level);
            Debug.Log($"[Purge] {level.name}: {geometry.FloorCount} floors, {geometry.Stairs.Count} stairs.");
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
