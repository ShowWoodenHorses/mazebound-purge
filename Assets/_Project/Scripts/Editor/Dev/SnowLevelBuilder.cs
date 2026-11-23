using System;
using System.Linq;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Generation;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Validation;
using Purge.Core.Vehicles;
using Purge.Core.Visual;
using Purge.Core.Waves;
using Purge.Editor.LevelDesigner;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
{
    internal static class SnowLevelBuilder
    {
        private const string Data = "Assets/_Project/Data";
        private const string LevelPath = Data + "/Levels/Level_Snow_01.asset";
        private const string RecipePath = Data + "/Recipes/SnowRecipe.asset";

        [MenuItem("Purge/Dev/Build Snow Level")]
        public static void Build()
        {
            var theme = Require<VisualTheme>("/Themes/SnowBiomeTheme.asset");
            var recipe = EnsureRecipe();
            var level = AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath);
            var isNew = level == null;
            if (isNew)
            {
                level = ScriptableObject.CreateInstance<LevelData>();
                level.Settings.DisplayName = "Snow";
                level.Settings.Location = LevelLocation.Snow;
                level.VisualTheme = theme;
                level.Generation.Recipe = recipe;
                level.Generation.Width = 42;
                level.Generation.Height = 36;
                level.Generation.LayoutSeed = 1401;
                level.Generation.VisualSeed = 1401;
                level.Generation.DecorDensity = 0.10f;
            }

            var result = isNew ? LevelAuthoring.GenerateNew(level) : LevelAuthoring.Generate(level);
            if (result.Problems.Count > 0)
                throw new InvalidOperationException(string.Join("; ", result.Problems));

            level.Horde = EnsureHorde();
            var report = EditorLevelValidator.Validate(level);
            if (!report.IsValid)
                throw new InvalidOperationException(string.Join("; ", report.Issues
                    .Where(issue => issue.Severity == ValidationSeverity.Error).Select(issue => issue.Message)));

            if (isNew)
                AssetDatabase.CreateAsset(level, LevelPath);
            if (LevelSync.IsDevLevel(level)) LevelSync.SetDevLevel(level, false);
            EditorUtility.SetDirty(level);
            LevelSync.Sync(level);
        }

        private static HordeDefinition EnsureHorde()
        {
            const string hordePath = Data + "/Horde/Snow01Horde.asset";
            const string wavesPath = Data + "/Waves/Snow_01.asset";
            var sandbox = Require<HordeDefinition>("/Horde/SandboxHorde.asset");
            var baseline = Require<HordeDefinition>("/Horde/Forest01Horde.asset");
            var waves = AssetDatabase.LoadAssetAtPath<WaveScenario>(wavesPath);
            if (waves == null)
            {
                waves = ScriptableObject.CreateInstance<WaveScenario>();
                ConfigureWaves(waves);
                AssetDatabase.CreateAsset(waves, wavesPath);
            }

            var horde = AssetDatabase.LoadAssetAtPath<HordeDefinition>(hordePath);
            if (horde == null)
            {
                horde = UnityEngine.Object.Instantiate(baseline);
                AssetDatabase.CreateAsset(horde, hordePath);
            }

            horde.SetWaves(waves);
            horde.ConfigureMonsters(Array.Empty<HordeSpawn>());
            var ids = horde.AllMonsters().Select(monster => monster.Id).ToHashSet();
            horde.SetMonsterVisuals(sandbox.MonsterVisuals.Where(look => look != null && ids.Contains(look.MonsterId)).ToArray());
            horde.SetMonsterViews(sandbox.MonsterViews.Where(look => look != null && ids.Contains(look.MonsterId)).ToArray());
            EditorUtility.SetDirty(horde);
            return horde;
        }

        private static void ConfigureWaves(WaveScenario scenario)
        {
            MonsterDefinition Snow(string name) => Require<MonsterDefinition>($"/Monsters/Snow/{name}_Snow.asset");
            MonsterDefinition Common(string name) => Require<MonsterDefinition>($"/Monsters/Common/{name}_Common.asset");
            var ghoul = Snow("Ghoul");
            var distant = Snow("Distant");
            var ram = Snow("Ram");
            var shaman = Snow("Shaman");
            var capture = Snow("Capture");
            var armadillo = Common("Armadillo");
            var shouter = Common("Shouter");
            var zombie = Common("Zombie_2");
            var waves = new WaveDefinition[18];

            void Set(int number, float budget, float seconds, int pack, float ghoulWeight, params WaveGroup[] groups)
            {
                var pool = new[] { new WaveMonster(ghoul, ghoulWeight), new WaveMonster(zombie, 1f - ghoulWeight) };
                waves[number - 1] = new WaveDefinition(budget, pool, groups, seconds, pack, number <= 6 ? 150 : number <= 12 ? 185 : 220)
                {
                    SkipRate = Mathf.Min(8, 1 + (number - 1) / 2),
                    MoneyBonus = 5 * number,
                };
            }

            Set(1, 22, 15f, 4, 0.75f);
            Set(2, 28, 16f, 5, 0.8f, new WaveGroup(distant, 1, 9f));
            Set(3, 35, 17f, 5, 0.78f, new WaveGroup(distant, 2, 8f));
            Set(4, 43, 18f, 6, 0.65f, new WaveGroup(ram, 1, 6f), new WaveGroup(distant, 2, 13f));
            Set(5, 50, 19f, 6, 0.6f, new WaveGroup(shouter, 1, 5f), new WaveGroup(ram, 2, 13f));
            Set(6, 56, 20f, 7, 0.7f, new WaveGroup(distant, 2, 8f), new WaveGroup(ram, 1, 16f));
            Set(7, 62, 21f, 7, 0.85f, new WaveGroup(shaman, 1, 12f));
            Set(8, 69, 22f, 7, 0.72f, new WaveGroup(distant, 3, 6f), new WaveGroup(capture, 1, 15f));
            Set(9, 76, 23f, 8, 0.58f, new WaveGroup(armadillo, 1, 9f), new WaveGroup(ram, 2, 17f));
            Set(10, 83, 24f, 8, 0.78f, new WaveGroup(shouter, 2, 6f), new WaveGroup(shaman, 1, 16f));
            Set(11, 89, 24f, 8, 0.65f, new WaveGroup(distant, 3, 7f), new WaveGroup(capture, 1, 18f));
            Set(12, 96, 25f, 9, 0.82f, new WaveGroup(ram, 3, 8f), new WaveGroup(shaman, 2, 17f));
            Set(13, 103, 26f, 9, 0.6f, new WaveGroup(armadillo, 2, 9f), new WaveGroup(distant, 4, 18f));
            Set(14, 110, 27f, 9, 0.78f, new WaveGroup(capture, 2, 10f), new WaveGroup(shouter, 2, 19f));
            Set(15, 118, 28f, 10, 0.7f, new WaveGroup(ram, 3, 6f), new WaveGroup(shaman, 2, 19f));
            Set(16, 127, 29f, 10, 0.57f, new WaveGroup(distant, 4, 7f), new WaveGroup(armadillo, 2, 20f));
            Set(17, 137, 30f, 11, 0.78f, new WaveGroup(ram, 3, 8f), new WaveGroup(capture, 2, 15f), new WaveGroup(shaman, 2, 23f));
            Set(18, 144, 30f, 11, 0.8f, new WaveGroup(distant, 4, 10f), new WaveGroup(ram, 3, 21f));
            waves[5].SetBoss(new WaveBoss(Common("Golem"), DifficultyLevel.Normal,
                new WaveGroup(ghoul, 8, 0f), new WaveGroup(ram, 1, 8f)));
            waves[10].SetBoss(new WaveBoss(Common("Medusa"), DifficultyLevel.Normal,
                new WaveGroup(ghoul, 10, 0f), new WaveGroup(capture, 1, 10f)));
            waves[17].SetBoss(new WaveBoss(Snow("Elemental"),
                new WaveGroup(ghoul, 12, 0f), new WaveGroup(distant, 2, 6f), new WaveGroup(ram, 2, 15f)));

            scenario.Configure(waves, pauseTime: 18f, firstDelay: 5f);
        }

        private static BiomeRecipe EnsureRecipe()
        {
            var recipe = AssetDatabase.LoadAssetAtPath<BiomeRecipe>(RecipePath);
            if (recipe != null) return recipe;
            recipe = ScriptableObject.CreateInstance<BiomeRecipe>();
            var obstacles = new ObstaclesStep { Density = 0.2f };
            obstacles.MutableObstacles.Add(new WeightedObstacle(Require<ObstacleDefinition>("/Obstacles/Tree.asset"), 8));
            obstacles.MutableObstacles.Add(new WeightedObstacle(Require<ObstacleDefinition>("/Obstacles/Stone.asset"), 2));
            var vehicles = new VehiclesStep();
            vehicles.MutableVehicles.Add(Require<VehicleDefinition>("/Vehicles/Vehicle_Light.asset"));
            vehicles.MutableVehicles.Add(Require<VehicleDefinition>("/Vehicles/Vehicle_Heavy.asset"));
            recipe.MutableSteps.AddRange(new GenerationStep[]
            {
                new PerimeterStep { Thickness = 1 },
                new StartStep { Position = new Vector2(0.17f, 0.5f), ClearRadius = 2.5f },
                new ArenaStep(), new SpawnPointsStep(), new ClearingsStep(), new TrailsStep(),
                new SnowTerrainStep(), obstacles, vehicles, new DecorStep(),
            });
            AssetDatabase.CreateAsset(recipe, RecipePath);
            return recipe;
        }

        private static T Require<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(Data + path) ?? throw new InvalidOperationException($"{path} is missing.");
    }
}
