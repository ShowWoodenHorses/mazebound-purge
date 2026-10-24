using System;
using System.Collections.Generic;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Visual;
using Purge.Core.Waves;
using Purge.Editor.LevelDesigner;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Run Level: <c>Level_Run</c> (stage 6a, main catalog — PLAY): an open forest field 60×60 with
    /// pillars, the start in the centre, six monster spawn points (corners and the west/east edges, cleared around),
    /// two weapons south of the start. Its horde <c>Data/Horde/RunHorde.asset</c> (forest and common monsters, the
    /// sandbox's drop and champions) and waves <c>Data/Waves/Run.asset</c> (5 waves, ~5 min) are made once and then tuned
    /// by hand; re-running regenerates the level (same seed) and copies the looks from the sandbox's horde.
    /// </summary>
    internal static class RunLevelBuilder
    {
        private const string LevelPath = "Assets/_Project/Data/Levels/Level_Run.asset";
        private const string ThemePath = "Assets/_Project/Data/Themes/ForestTheme.asset";
        private const string HordePath = "Assets/_Project/Data/Horde/RunHorde.asset";
        private const string SandboxHordePath = "Assets/_Project/Data/Horde/SandboxHorde.asset";
        private const string WavesFolder = "Assets/_Project/Data/Waves";
        private const string WavesPath = WavesFolder + "/Run.asset";
        private const string MonstersPath = "Assets/_Project/Data/Monsters";
        private const string WeaponsPath = "Assets/_Project/Data/Weapons";
        private const int Size = 60;
        private const int Seed = 6060;
        private const float ObstacleDensity = 0.08f;

        [MenuItem("Purge/Dev/Build Run Level")]
        public static void Build()
        {
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath)
                        ?? throw new InvalidOperationException($"No theme at {ThemePath}.");
            var sandbox = AssetDatabase.LoadAssetAtPath<HordeDefinition>(SandboxHordePath)
                          ?? throw new InvalidOperationException($"No sandbox horde at {SandboxHordePath}: run Build Stress Test Levels.");
            var waves = EnsureWaves();
            var horde = EnsureHorde(sandbox, waves);

            var level = AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath);
            if (level == null)
            {
                level = ScriptableObject.CreateInstance<LevelData>();
                AssetDatabase.CreateAsset(level, LevelPath);
            }

            level.Settings.DisplayName = "Run";
            level.VisualTheme = theme;
            level.StressTest = null;
            level.Horde = horde;
            level.Generation.Width = Size;
            level.Generation.Height = Size;
            level.Generation.LayoutSeed = Seed;
            level.Generation.VisualSeed = Seed;
            level.Generation.ObstacleDensity = ObstacleDensity;
            LevelAuthoring.GenerateNew(level);
            PlaceSpawnPoints(level);
            LayOutWeapons(level);
            LayOutObjects(level);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();

            var report = EditorLevelValidator.Validate(level);
            if (!report.IsValid)
            {
                Debug.LogError($"[Purge] 'Level_Run' has {report.ErrorCount} validation error(s); not synced.");
                return;
            }

            if (LevelSync.IsDevLevel(level))
                LevelSync.SetDevLevel(level, false);
            LevelSync.Sync(level);
            Debug.Log($"[Purge] Level_Run built: {level.MonsterSpawns.Count} spawn point(s), {waves.Count} wave(s).");
        }

        /// <summary>Corners (5 cells in) and the middle of the west and east edges (3 cells in), each with a clear 3×3.</summary>
        private static void PlaceSpawnPoints(LevelData level)
        {
            var floor = level.PlayerStarts[0].Position.Floor;
            var far = Size - 1;
            var points = new[]
            {
                (5, 5), (far - 5, 5), (5, far - 5), (far - 5, far - 5), (3, far / 2), (far - 3, far / 2),
            };
            foreach (var (x, y) in points)
            {
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    LevelEditing.ClearCell(level, new GridPosition(x + dx, y + dy, floor));
                LevelEditing.AddMonsterSpawn(level, new GridPosition(x, y, floor));
            }
        }

        /// <summary>An AK-74 and a pump shotgun two cells south of the start (the run's start kit is stage 6e).</summary>
        private static void LayOutWeapons(LevelData level)
        {
            var start = level.PlayerStarts[0].Position;
            var x = start.X - 1;
            foreach (var name in new[] { "AK-74", "Pomp_Shotgun" })
            {
                var weapon = FindAsset<WeaponDefinition>(WeaponsPath, name);
                if (weapon == null)
                {
                    Debug.LogWarning($"[Purge] No weapon '{name}' in {WeaponsPath}.");
                    continue;
                }

                var cell = new GridPosition(x, start.Y - 2, start.Floor);
                LevelEditing.ClearCell(level, cell);
                LevelEditing.AddWeapon(level, cell, weapon);
                x += 2;
            }
        }

        /// <summary>
        /// Stage 7в: the safe dome at the start; three barrels (one of each kind) and the four chests of the card
        /// outside the start's view (Chebyshev 13–15).
        /// </summary>
        private static void LayOutObjects(LevelData level)
        {
            level.Settings.Refuge = true;
            level.Settings.RefugeRadius = 2;
            var start = level.PlayerStarts[0].Position;
            GridPosition At(int dx, int dy) => new GridPosition(start.X + dx, start.Y + dy, start.Floor);

            foreach (var (cell, kind) in new[]
                     {
                         (At(13, 0), BarrelKind.Gas), (At(-13, 4), BarrelKind.Fire), (At(2, 14), BarrelKind.Acid),
                     })
            {
                LevelEditing.ClearCell(level, cell);
                LevelEditing.AddBarrel(level, cell, kind);
            }

            foreach (var (cell, kind) in new[]
                     {
                         (At(14, 14), ChestKind.Common), (At(-14, -14), ChestKind.Common),
                         (At(14, -15), ChestKind.Locked), (At(-14, 15), ChestKind.Challenge),
                     })
            {
                LevelEditing.ClearCell(level, cell);
                LevelEditing.AddChest(level, cell, kind);
            }
        }

        private static HordeDefinition EnsureHorde(HordeDefinition sandbox, WaveScenario waves)
        {
            var horde = AssetDatabase.LoadAssetAtPath<HordeDefinition>(HordePath);
            if (horde == null)
            {
                horde = ScriptableObject.CreateInstance<HordeDefinition>();
                horde.ConfigureCapacity(300, 64);
                horde.ConfigurePerception(sandbox.Difficulty, sandbox.WanderGroups, sandbox.WanderRetarget);
                // Without the sandbox's grenade launcher and flamer: what lies on the ground early on.
                var weapons = Array.FindAll(sandbox.PoolWeapons, weapon => weapon != null &&
                                                                          (weapon.name == "AK-74" || weapon.name == "Pomp_Shotgun" ||
                                                                           weapon.name == "Sword" || weapon.name == "Tesla"));
                horde.ConfigureDrops(sandbox.Drop, weapons, sandbox.PoolThrowables);
                var champions = sandbox.Champions;
                horde.Champions.Configure(champions.Chance, champions.Affixes, champions.HealthFactor, champions.Scale,
                    champions.DropOf(MonsterRank.Mass), champions.DropOf(MonsterRank.Officer));
                AssetDatabase.CreateAsset(horde, HordePath);
            }

            horde.SetWaves(waves);
            // Looks: the sandbox's horde has every built look (Build Monsters refreshes them for every horde).
            var monsters = horde.AllMonsters();
            bool Has(string id) => monsters.Exists(monster => monster.Id == id);
            horde.SetMonsterVisuals(Array.FindAll(sandbox.MonsterVisuals, visual => visual != null && Has(visual.MonsterId)));
            horde.SetMonsterViews(Array.FindAll(sandbox.MonsterViews, view => view != null && Has(view.MonsterId)));
            EditorUtility.SetDirty(horde);
            return horde;
        }

        /// <summary>Draft waves (numbers — stage 9), made once; tuned by hand afterwards.</summary>
        private static WaveScenario EnsureWaves()
        {
            var waves = AssetDatabase.LoadAssetAtPath<WaveScenario>(WavesPath);
            if (waves != null) return waves;

            if (!AssetDatabase.IsValidFolder(WavesFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Waves");
            var zombie = Monster("Zombie_Common");
            var runner = Monster("Zombie_2_Common");
            var forest = Monster("Zombie_Forest");
            var distant = Monster("Distant_Forest");
            var ram = Monster("Ram_Forest");
            var shouter = Monster("Shouter_Common");
            var armadillo = Monster("Armadillo_Common");
            var shaman = Monster("Shaman_Forest");

            WaveMonster[] Mass() => new[] { new WaveMonster(zombie, 3f), new WaveMonster(runner, 2f), new WaveMonster(forest, 2f) };
            WaveMonster[] WithOfficers(float share)
            {
                var list = new List<WaveMonster>(Mass())
                {
                    new WaveMonster(distant, share), new WaveMonster(ram, share * 0.5f), new WaveMonster(shouter, share * 0.5f),
                };
                return list.ToArray();
            }

            waves = ScriptableObject.CreateInstance<WaveScenario>();
            waves.Configure(new[]
            {
                new WaveDefinition(40f, Mass(), null, 25f, 6, 80),
                new WaveDefinition(70f, WithOfficers(0.1f), new[] { new WaveGroup(shouter, 1, 15f) }, 35f, 6, 120),
                new WaveDefinition(110f, WithOfficers(0.2f), new[] { new WaveGroup(ram, 2, 25f) }, 40f, 8, 150),
                new WaveDefinition(150f, WithOfficers(0.3f), new[] { new WaveGroup(armadillo, 1, 20f) }, 45f, 8, 200),
                new WaveDefinition(220f, WithOfficers(0.4f), new[]
                {
                    new WaveGroup(shaman, 1, 10f), new WaveGroup(armadillo, 1, 35f), new WaveGroup(ram, 2, 50f),
                }, 55f, 10, 250),
            }, 20f, 3f);
            AssetDatabase.CreateAsset(waves, WavesPath);
            return waves;
        }

        private static MonsterDefinition Monster(string name) =>
            FindAsset<MonsterDefinition>(MonstersPath, name) ?? throw new InvalidOperationException($"No monster '{name}' in {MonstersPath}: run Build Monsters.");

        private static T FindAsset<T>(string folder, string name) where T : UnityEngine.Object
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}", new[] { folder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.name == name) return asset;
            }

            return null;
        }
    }
}
