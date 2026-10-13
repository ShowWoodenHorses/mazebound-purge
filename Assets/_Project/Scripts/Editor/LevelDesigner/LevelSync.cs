using System;
using System.Collections.Generic;
using System.Linq;
using Maze.Core.Audio;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Core.Localization;
using Maze.Core.Visual;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.AnalyzeRules;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>
    /// Build / Sync (ТЗ §30, §84): fixes the current state of the level for runtime. Validates, makes the level
    /// and its visual prefabs Addressable, adds the level to the <see cref="LevelCatalog"/> (main menu), saves.
    /// Never re-randomizes anything. A development level (<see cref="IsDevLevel"/>, the Level Designer's "Dev"
    /// toggle) goes to the development catalog and the <see cref="DevLevelsGroup"/> group instead, which release
    /// builds leave out. A level with a tutorial script (<see cref="LevelData.Tutorial"/>) becomes the catalog's tutorial
    /// (the main menu button), not a numbered level.
    /// </summary>
    internal static class LevelSync
    {
        public const string LevelsGroup = "Maze Levels";
        public const string DevLevelsGroup = "Maze Dev Levels";
        public const string VisualsGroup = "Maze Visuals";
        public const string SharedGroup = "Maze Shared";
        public const string SharedDependenciesGroup = "Maze Shared Dependencies";

        /// <summary>Music tracks: a bundle per track, downloaded only when it starts playing (not with the start-up bundle).</summary>
        public const string MusicGroup = "Maze Music";
        public const string LevelAddressPrefix = "Levels/";
        public const string CatalogPath = "Assets/_Project/Data/Levels/LevelCatalog.asset";
        public const string DevCatalogPath = "Assets/_Project/Data/Levels/DevLevelCatalog.asset";

        /// <summary>The level is listed in the development catalog.</summary>
        public static bool IsDevLevel(LevelData level)
        {
            if (level == null) return false;
            var dev = AssetDatabase.LoadAssetAtPath<LevelCatalog>(DevCatalogPath);
            return dev != null && dev.Find(level.name) != null;
        }

        /// <summary>Moves the level between the game and development catalogs (and groups), without validation.</summary>
        public static void SetDevLevel(LevelData level, bool dev)
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var address = Place(settings, level, dev);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] '{level.name}' is now a {(dev ? "development" : "game")} level ('{address}').");
        }

        /// <summary>
        /// Makes the level Addressable in its group and lists it in its catalog (removed from the other one).
        /// Returns the address.
        /// </summary>
        private static string Place(AddressableAssetSettings settings, LevelData level, bool dev)
        {
            var levelsGroup = GetOrCreateGroup(settings, LevelsGroup);
            var devGroup = GetOrCreateGroup(settings, DevLevelsGroup);
            var levelGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(level));
            var levelEntry = settings.CreateOrMoveEntry(levelGuid, dev ? devGroup : levelsGroup);
            levelEntry.address = LevelAddressPrefix + level.name;

            var catalog = GetOrCreateCatalog(settings, levelsGroup);
            var devCatalog = GetOrCreateCatalog(settings, devGroup, DevCatalogPath, LevelCatalog.DevAddress);
            var target = dev ? devCatalog : catalog;
            var other = dev ? catalog : devCatalog;
            if (other.Remove(level.name))
                EditorUtility.SetDirty(other);
            if (!dev && level.Tutorial != null)
            {
                // The tutorial: out of the numbered list, into its own place.
                var wasListed = catalog.Levels.Any(e => e.LevelId == level.name);
                if (wasListed) catalog.Remove(level.name);
                if (catalog.SetTutorial(level.name, levelEntry.address, level.Settings.DisplayName) || wasListed)
                    EditorUtility.SetDirty(catalog);
                return levelEntry.address;
            }

            if (!dev && catalog.Tutorial?.LevelId == level.name && catalog.Remove(level.name))
                EditorUtility.SetDirty(catalog); // No longer the tutorial: back to the numbered levels.
            if (target.AddOrUpdate(level.name, levelEntry.address, level.Settings.DisplayName))
                EditorUtility.SetDirty(target);
            return levelEntry.address;
        }

        public static bool Sync(LevelData level)
        {
            var report = EditorLevelValidator.Validate(level);
            if (!report.IsValid && !EditorUtility.DisplayDialog("Build / Sync",
                    $"The level has {report.ErrorCount} validation error(s) and is not ready.\n\nSync it anyway?",
                    "Sync Anyway", "Cancel"))
                return false;

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var visualsGroup = GetOrCreateGroup(settings, VisualsGroup);
            PackSeparately(visualsGroup);
            var address = Place(settings, level, IsDevLevel(level));

            var added = 0;
            var theme = level.VisualTheme;
            if (theme != null)
            {
                var prefabGuids = Enum.GetValues(typeof(VisualKind)).Cast<VisualKind>()
                    .Select(theme.GetSet)
                    .Where(set => set != null)
                    .SelectMany(set => set.Variants)
                    .Where(v => v.Prefab != null && v.Prefab.RuntimeKeyIsValid())
                    .Select(v => v.Prefab.AssetGUID)
                    .Distinct();

                // Prefabs already in some group are left where the team put them.
                // The ice of frozen doors (weather) is loaded the same way as the variants.
                var ice = theme.Weather.HasIceOverlay ? new[] { theme.Weather.IceOverlay.AssetGUID } : Array.Empty<string>();
                foreach (var guid in prefabGuids.Concat(ice).Distinct())
                    if (settings.FindAssetEntry(guid) == null && !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                    {
                        settings.CreateOrMoveEntry(guid, visualsGroup);
                        added++;
                    }
            }

            var sharedProblem = SyncShared(settings);
            if (sharedProblem != null)
                Debug.LogWarning("[Maze] Build/Sync: " + sharedProblem);

            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Build/Sync '{level.name}': address '{address}', {added} visual prefab(s) made Addressable, " +
                      $"{report.ErrorCount} error(s), {report.WarningCount} warning(s).");
            return true;
        }

        /// <summary>
        /// Keeps the definitions shared by all levels Addressable at their fixed addresses: <see cref="PlayerDefinition"/>,
        /// <see cref="PlayerVisualDefinition"/> and its prefab, <see cref="CombatVisualDefinition"/> and its prefabs,
        /// <see cref="WeaponVisualCatalog"/> and the held weapon prefabs, <see cref="AudioCatalog"/> and its music clips,
        /// <see cref="LanguageCatalog"/> and the language tables.
        /// Returns a problem description or null.
        /// </summary>
        public static string SyncShared(AddressableAssetSettings settings)
        {
            var sharedGroup = GetOrCreateGroup(settings, SharedGroup);
            var player = EnsureSingleAddressable<PlayerDefinition>(settings, sharedGroup, PlayerDefinition.Address);
            var visual = EnsureSingleAddressable<PlayerVisualDefinition>(settings, sharedGroup, PlayerVisualDefinition.Address);
            if (player == null || visual == null)
                return "Player definition or player visual is missing: run Maze → Dev → Create Placeholder Player or create them " +
                       "(Create → Maze → Definitions → Player, Create → Maze → Visual → Player Visual). The game cannot start a level without them.";

            if (visual.Prefab == null || !visual.Prefab.RuntimeKeyIsValid())
                return $"'{visual.name}' has no prefab.";

            EnsurePrefabAddressable(settings, sharedGroup, visual.Prefab);

            var combat = EnsureSingleAddressable<CombatVisualDefinition>(settings, sharedGroup, CombatVisualDefinition.Address);
            if (combat == null)
                return "Combat visual is missing: run Maze → Dev → Build Combat Effects or create one " +
                       "(Create → Maze → Visual → Combat Visual). The game cannot start without it.";
            foreach (var prefab in combat.Prefabs)
                EnsurePrefabAddressable(settings, sharedGroup, prefab);

            var weapons = EnsureSingleAddressable<WeaponVisualCatalog>(settings, sharedGroup, WeaponVisualCatalog.Address);
            if (weapons == null)
                return "Weapon visuals are missing: run Maze → Dev → Build Weapons or create them " +
                       "(Create → Maze → Visual → Weapon Visuals). The game cannot start without them.";
            foreach (var weapon in weapons.Weapons)
                if (weapon != null)
                    EnsurePrefabAddressable(settings, sharedGroup, weapon.HeldPrefab);

            var audio = EnsureSingleAddressable<AudioCatalog>(settings, sharedGroup, AudioCatalog.Address);
            if (audio == null)
                return "Audio catalog is missing: run Maze → Dev → Build Audio or create one (Create → Maze → Audio Catalog). " +
                       "The game cannot start without it.";
            // Music is loaded only while it plays: its own entries, each track in its own bundle (a web build downloads
            // a bundle whole: inside the start-up bundle both tracks would be downloaded before the menu).
            var musicGroup = GetOrCreateGroup(settings, MusicGroup);
            PackSeparately(musicGroup);
            MoveToGroup(settings, musicGroup, audio.MenuMusic.Clip);
            MoveToGroup(settings, musicGroup, audio.LevelMusic.Clip);

            var languages = EnsureSingleAddressable<LanguageCatalog>(settings, sharedGroup, LanguageCatalog.Address);
            if (languages == null)
                return "Language catalog is missing: run Maze → Dev → Build Localization. The game cannot start without it.";
            // Only the chosen language is loaded: every table is its own entry.
            foreach (var language in languages.Languages)
                if (language != null) EnsurePrefabAddressable(settings, sharedGroup, language.Table);

            EditorUtility.SetDirty(settings);
            return null;
        }

        private static void EnsurePrefabAddressable(AddressableAssetSettings settings, AddressableAssetGroup group, AssetReference prefab)
        {
            if (prefab != null && prefab.RuntimeKeyIsValid() && settings.FindAssetEntry(prefab.AssetGUID) == null)
                settings.CreateOrMoveEntry(prefab.AssetGUID, group);
        }

        /// <summary>Makes the asset addressable in <paramref name="group"/>, moving it there from another group.</summary>
        private static void MoveToGroup(AddressableAssetSettings settings, AddressableAssetGroup group, AssetReference asset)
        {
            if (asset == null || !asset.RuntimeKeyIsValid()) return;
            var entry = settings.FindAssetEntry(asset.AssetGUID);
            if (entry == null || entry.parentGroup != group)
                settings.CreateOrMoveEntry(asset.AssetGUID, group);
        }

        private static T EnsureSingleAddressable<T>(AddressableAssetSettings settings, AddressableAssetGroup group, string address)
            where T : ScriptableObject
        {
            var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            if (guids.Length == 0)
                return null;
            if (guids.Length > 1)
                Debug.LogWarning($"[Maze] {guids.Length} {typeof(T).Name} assets found; '{AssetDatabase.GUIDToAssetPath(guids[0])}' is used.");

            var entry = settings.FindAssetEntry(guids[0]) ?? settings.CreateOrMoveEntry(guids[0], group);
            entry.address = address;
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>The project's game <see cref="LevelCatalog"/>, created on first sync and kept Addressable.</summary>
        public static LevelCatalog GetOrCreateCatalog(AddressableAssetSettings settings, AddressableAssetGroup levelsGroup) =>
            GetOrCreateCatalog(settings, levelsGroup, CatalogPath, LevelCatalog.Address);

        /// <summary>A catalog asset at a fixed path, created when missing, Addressable in the group at the address.</summary>
        private static LevelCatalog GetOrCreateCatalog(AddressableAssetSettings settings, AddressableAssetGroup group,
            string path, string address)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(path);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LevelCatalog>();
                AssetDatabase.CreateAsset(catalog, path);
                Debug.Log($"[Maze] Created level catalog at {path}.");
            }

            var guid = AssetDatabase.AssetPathToGUID(path);
            var entry = settings.FindAssetEntry(guid);
            if (entry == null || entry.parentGroup != group)
                entry = settings.CreateOrMoveEntry(guid, group);
            if (entry.address != address)
            {
                entry.address = address;
                EditorUtility.SetDirty(settings);
            }

            return catalog;
        }

        /// <summary>
        /// Includes or leaves out the development levels group in the content build; returns the previous value.
        /// </summary>
        public static bool SetDevLevelsIncluded(AddressableAssetSettings settings, bool included)
        {
            var schema = settings.FindGroup(DevLevelsGroup)?.GetSchema<BundledAssetGroupSchema>();
            if (schema == null) return true;
            var previous = schema.IncludeInBuild;
            schema.IncludeInBuild = included;
            return previous;
        }

        /// <summary>
        /// Assets that would be copied into more than one bundle (a non-Addressable dependency of several groups:
        /// weapon definitions used by levels and by the weapon catalog, models shared by the player and zombies, the
        /// Synty atlas…), from the Addressables "Check Duplicate Bundle Dependencies" analysis.
        /// </summary>
        public static List<string> FindDuplicates(AddressableAssetSettings settings)
        {
            var rule = new CheckBundleDupeDependencies();
            var paths = new HashSet<string>();
            foreach (var result in rule.RefreshAnalysis(settings))
            {
                // "Group:bundle:Assets/…" — the asset path is the last part.
                var parts = result.resultName.Split(':');
                var path = parts[parts.Length - 1];
                if (parts.Length >= 3 && path.StartsWith("Assets/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(path))
                    paths.Add(path);
            }

            return paths.OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Makes every duplicated dependency an explicit entry of <see cref="SharedDependenciesGroup"/>, so a build has
        /// one copy of it: smaller, and the same object everywhere (a definition copied into two bundles would be two
        /// different objects at runtime). The group is rebuilt from scratch: an asset that is no longer shared (e.g. a
        /// model replaced by extracted meshes) leaves it and is not built any more. Returns the group's entry count.
        /// </summary>
        public static int IsolateDuplicates(AddressableAssetSettings settings)
        {
            var group = settings.FindGroup(SharedDependenciesGroup);
            if (group != null)
                foreach (var stale in group.entries.ToList())
                    group.RemoveAssetEntry(stale, false);

            var duplicates = FindDuplicates(settings);
            if (duplicates.Count == 0)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                return 0;
            }

            group = group != null ? group : GetOrCreateGroup(settings, SharedDependenciesGroup);
            PackTogether(group);
            foreach (var path in duplicates)
            {
                var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group);
                entry.address = path;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return duplicates.Count;
        }

        /// <summary>
        /// One bundle for the shared dependencies: the menu already needs most of them (weapons, common textures), and a
        /// bundle per asset made the start ~3 s slower on a network — the browser's Unity cache asks the server about
        /// every file on every start (measured 2026-10-12, 80 ms / 20 Mbit/s).
        /// </summary>
        public static void PackTogether(AddressableAssetGroup group)
        {
            var schema = group != null ? group.GetSchema<BundledAssetGroupSchema>() : null;
            if (schema == null || schema.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackTogether) return;
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            EditorUtility.SetDirty(schema);
        }

        /// <summary>
        /// A bundle per entry. A web build downloads a bundle whole: the visuals of every theme (and every shared
        /// texture or model) in one bundle were all downloaded before the first level, which needs a few of them —
        /// the sites' initial-download limits (CrazyGames: 20 MB for the mobile home page). A level loads only the
        /// variants it uses (<c>LevelVisualUsage</c>), so only their bundles are downloaded now.
        /// </summary>
        public static void PackSeparately(AddressableAssetGroup group)
        {
            var schema = group != null ? group.GetSchema<BundledAssetGroupSchema>() : null;
            if (schema == null) return;
            // Bundle names are the hash only: a bundle per asset is named after its whole path otherwise, and the
            // build folder's paths went past Windows' 260 characters.
            if (schema.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackSeparately &&
                schema.BundleNaming == BundledAssetGroupSchema.BundleNamingStyle.OnlyHash)
                return;
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            schema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.OnlyHash;
            EditorUtility.SetDirty(schema);
        }

        private static AddressableAssetGroup GetOrCreateGroup(AddressableAssetSettings settings, string name)
        {
            var group = settings.FindGroup(name);
            return group != null
                ? group
                : settings.CreateGroup(name, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        }
    }
}
