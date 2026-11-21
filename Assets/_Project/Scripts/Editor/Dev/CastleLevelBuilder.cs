using System;
using System.Linq;
using Purge.Core.Authoring;
using Purge.Core.Generation;
using Purge.Core.Generation.Steps;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Validation;
using Purge.Core.Visual;
using Purge.Editor.LevelDesigner;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
{
    internal static class CastleLevelBuilder
    {
        private const string Data = "Assets/_Project/Data";
        private const string RecipePath = Data + "/Recipes/CastleRecipe.asset";
        private const string LevelPath = Data + "/Levels/Level_Castle_01.asset";

        [MenuItem("Purge/Dev/Build Castle Level")]
        public static void Build()
        {
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(Data + "/Themes/CastleBlockTheme.asset")
                        ?? throw new InvalidOperationException("CastleBlockTheme is missing.");
            var recipe = AssetDatabase.LoadAssetAtPath<BiomeRecipe>(RecipePath);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<BiomeRecipe>();
                recipe.MutableSteps.Add(new PerimeterStep { Thickness = 1 });
                recipe.MutableSteps.Add(new StartStep { Position = new Vector2(0.16f, 0.5f), ClearRadius = 1f });
                recipe.MutableSteps.Add(new CastleRoomsStep());
                recipe.MutableSteps.Add(SpawnPointsStep.CastleSpawns());
                recipe.MutableSteps.Add(ObjectsStep.CastleBarrels());
                recipe.MutableSteps.Add(new BuildingStep { Basement = true, InsetPerFloor = 1 });
                AssetDatabase.CreateAsset(recipe, RecipePath);
            }

            var level = AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath);
            var isNew = level == null;
            if (isNew)
            {
                level = ScriptableObject.CreateInstance<LevelData>();
                level.Settings.DisplayName = "Castle";
                level.Settings.Location = LevelLocation.Castle;
                level.VisualTheme = theme;
                level.Generation.Recipe = recipe;
                level.Generation.Width = 32;
                level.Generation.Height = 26;
                level.Generation.LayoutSeed = 1302;
                level.Generation.VisualSeed = 1302;
                level.Generation.DecorDensity = 0f;
            }

            var result = isNew ? LevelAuthoring.GenerateNew(level) : LevelAuthoring.Generate(level);
            if (result.Problems.Count > 0)
                throw new InvalidOperationException(string.Join("; ", result.Problems));
            var report = EditorLevelValidator.Validate(level);
            if (!report.IsValid)
                throw new InvalidOperationException(string.Join("; ", report.Issues
                    .Where(issue => issue.Severity == ValidationSeverity.Error).Select(issue => issue.Message)));

            if (isNew)
            {
                AssetDatabase.CreateAsset(level, LevelPath);
                LevelSync.SetDevLevel(level, true);
            }
            EditorUtility.SetDirty(level);
            BuildCommonMaterials();
            LevelSync.Sync(level);
        }

        private static void BuildCommonMaterials()
        {
            foreach (var name in new[] { "Zombie_Common", "Zombie_2_Common" })
            {
                var visual = AssetDatabase.LoadAssetAtPath<HordeVisualDefinition>(Data + "/Monsters/Visuals/" + name + ".asset")
                             ?? throw new InvalidOperationException("Missing horde visual: " + name);
                var materials = new Material[visual.Variants.Length];
                for (var i = 0; i < materials.Length; i++)
                {
                    var source = visual.Variants[i];
                    var path = $"Assets/_Project/Art/Horde/{name}/{name}_Castle_{i + 1}.mat";
                    materials[i] = CoolMaterial(source, path);
                }

                visual.CastleVariants = materials;
                EditorUtility.SetDirty(visual);
            }

            var shouter = LoadView("Shouter_Common");
            var shouterBase = BodyMaterial(shouter);
            var shouterPalette = CoolMaterial(shouterBase,
                "Assets/_Project/Art/Monster/Common/Officer/Shouter_CastlePalette.mat");
            shouter.SetPalettes(shouterBase, new BiomePalette(MonsterBiome.Castle, shouterPalette));
            EditorUtility.SetDirty(shouter);

            var armadillo = LoadView("Armadillo_Common");
            var armadilloBase = BodyMaterial(armadillo);
            var armadilloPalette = LoadView("Golem_Common").PaletteFor(MonsterBiome.Castle)
                                    ?? throw new InvalidOperationException("Missing Fantasy Rivals Castle palette.");
            armadillo.SetPalettes(armadilloBase, new BiomePalette(MonsterBiome.Castle, armadilloPalette));
            EditorUtility.SetDirty(armadillo);
        }

        private static MonsterViewDefinition LoadView(string name) =>
            AssetDatabase.LoadAssetAtPath<MonsterViewDefinition>(Data + "/Monsters/Views/" + name + ".asset")
            ?? throw new InvalidOperationException("Missing monster view: " + name);

        private static Material BodyMaterial(MonsterViewDefinition view) =>
            view.Prefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterial;

        private static Material CoolMaterial(Material source, string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
                material.CopyPropertiesFromMaterial(source);

            material.color = source.color * new Color(0.78f, 0.84f, 0.94f, 1f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
