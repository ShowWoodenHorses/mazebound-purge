using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Visual;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Items: the items that appear during play — what monsters drop and what the player throws.
    /// <list type="number">
    /// <item>Throwables: a <see cref="ThrowableDefinition"/> per model in <c>Art/Weapons/Purge/Throw</c> (top level; a
    /// new one — <c>Data/Throwables/&lt;model&gt;.asset</c> without effects: they are set in the asset, never
    /// overwritten). Its pickup prefab (the model on the floor) and flying prefab (centred) — <c>Art/Items/Pickups</c>.</item>
    /// <item>Medkits: a <see cref="MedkitDefinition"/> per model in <c>Art/Items/Purge/Medical</c> (new — heals to full,
    /// tune in <c>Data/Medkits</c>) and its pickup prefab.</item>
    /// <item>Ammo: the pickup of an <see cref="AmmoDefinition"/> is the model <c>Art/Items/Purge/Ammo/&lt;X&gt;_ammo</c>
    /// whose X is one of its <see cref="AmmoDefinition.ModelFolders"/> (or the start of one: "9" → "9_mm").</item>
    /// <item>Weapons: their pickups from the placeholder theme's Weapon set (Build Weapons).</item>
    /// </list>
    /// All go to <see cref="ItemVisualCatalog"/> (<c>Data/Items/ItemVisuals</c>, "Items/Visual"); the prefabs are
    /// Addressable in Purge Visuals (a level loads only those its drop pool can produce). Items are scaled to fit
    /// <see cref="PickupSize"/> of a cell, so a grenade is seen from the top-down camera. Run after new models.
    /// </summary>
    internal static class ItemsBuilder
    {
        private const string ThrowModels = "Assets/_Project/Art/Weapons/Purge/Throw";
        private const string MedicalModels = "Assets/_Project/Art/Items/Purge/Medical";
        private const string AmmoModels = "Assets/_Project/Art/Items/Purge/Ammo";
        private const string PickupsPath = "Assets/_Project/Art/Items/Pickups";
        private const string ThrowablesPath = "Assets/_Project/Data/Throwables";
        private const string MedkitsPath = "Assets/_Project/Data/Medkits";
        private const string CatalogPath = "Assets/_Project/Data/Items/ItemVisuals.asset";
        private const string WeaponSetPath = "Assets/_Project/Data/Themes/PlaceholderWeaponSet.asset";

        /// <summary>Largest horizontal size of an item lying in a cell, cells.</summary>
        private const float PickupSize = 0.45f;

        /// <summary>Largest size of a thrown item in flight, cells (a little bigger than real: it must be seen).</summary>
        private const float FlyingSize = 0.35f;

        [MenuItem("Purge/Dev/Build Items")]
        public static void Build()
        {
            try
            {
                EnsureFolder(PickupsPath);
                EnsureFolder(ThrowablesPath);
                EnsureFolder(MedkitsPath);
                EnsureFolder(Path.GetDirectoryName(CatalogPath).Replace('\\', '/'));
                var catalog = AssetDatabase.LoadAssetAtPath<ItemVisualCatalog>(CatalogPath);
                if (catalog == null)
                {
                    catalog = ScriptableObject.CreateInstance<ItemVisualCatalog>();
                    AssetDatabase.CreateAsset(catalog, CatalogPath);
                }

                var icons = new Dictionary<ContentDefinition, Sprite>();
                foreach (var old in catalog.MutableItems)
                    if (old?.Item != null && old.Icon != null) icons[old.Item] = old.Icon;
                catalog.MutableItems.Clear();

                var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
                var group = settings.FindGroup(LevelDesigner.LevelSync.VisualsGroup) ?? settings.DefaultGroup;
                var created = new List<string>();
                var used = new HashSet<string>();

                foreach (var model in Models(ThrowModels))
                {
                    var throwable = LoadOrCreate<ThrowableDefinition>($"{ThrowablesPath}/{Name(model)}.asset", created);
                    var pickup = BuildPrefab(model, "_Pickup", PickupSize, onFloor: true);
                    var flying = BuildPrefab(model, "_Flying", FlyingSize, onFloor: false);
                    used.Add(pickup);
                    used.Add(flying);
                    icons.TryGetValue(throwable, out var icon);
                    catalog.MutableItems.Add(new ItemVisualDefinition(throwable, Reference(settings, group, pickup),
                        Reference(settings, group, flying), icon));
                }

                foreach (var model in Models(MedicalModels))
                {
                    var medkit = LoadOrCreate<MedkitDefinition>($"{MedkitsPath}/{Name(model)}.asset", created);
                    var pickup = BuildPrefab(model, "_Pickup", PickupSize, onFloor: true);
                    used.Add(pickup);
                    catalog.MutableItems.Add(new ItemVisualDefinition(medkit, Reference(settings, group, pickup)));
                }

                var ammoModels = Models(AmmoModels).ToList();
                var missingAmmo = new List<string>();
                foreach (var guid in AssetDatabase.FindAssets("t:AmmoDefinition"))
                {
                    var ammo = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    var model = ammoModels.FirstOrDefault(path => Matches(ammo, Name(path)));
                    if (model == null)
                    {
                        missingAmmo.Add(ammo.name);
                        continue;
                    }

                    var pickup = BuildPrefab(model, "_Pickup", PickupSize, onFloor: true);
                    used.Add(pickup);
                    catalog.MutableItems.Add(new ItemVisualDefinition(ammo, Reference(settings, group, pickup)));
                }

                var weapons = 0;
                var set = AssetDatabase.LoadAssetAtPath<VisualSet>(WeaponSetPath);
                if (set != null)
                    foreach (var variant in set.Variants)
                        if (variant.Definition is WeaponDefinition weapon && variant.Prefab != null && variant.Prefab.RuntimeKeyIsValid())
                        {
                            catalog.MutableItems.Add(new ItemVisualDefinition(weapon,
                                new AssetReferenceGameObject(variant.Prefab.AssetGUID)));
                            weapons++;
                        }

                var removed = DeleteUnused(used, settings);
                EditorUtility.SetDirty(catalog);
                EditorUtility.SetDirty(settings);
                var problem = LevelDesigner.LevelSync.SyncShared(settings);
                if (problem != null) Debug.LogWarning("[Purge] " + problem);
                AssetDatabase.SaveAssets();
                UiIconsBuilder.BuildItemIcons(catalog); // Silhouettes of the throwables for the HUD.

                foreach (var path in AssetDatabase.FindAssets("t:ThrowableDefinition").Select(AssetDatabase.GUIDToAssetPath))
                {
                    var problems = new List<string>();
                    AssetDatabase.LoadAssetAtPath<ThrowableDefinition>(path).Validate(problems);
                    foreach (var p in problems) Debug.LogWarning("[Purge] " + p);
                }

                if (missingAmmo.Count > 0)
                    Debug.LogWarning($"[Purge] No pickup model for ammo {string.Join(", ", missingAmmo)}: add '<folder>_ammo' to {AmmoModels}.");
                var news = created.Count > 0 ? $" New: {string.Join(", ", created)} (set them up in their assets)." : "";
                Debug.Log($"[Purge] Items built: {catalog.Items.Count} look(s) ({weapons} weapon(s)), {removed} unused prefab(s) removed.{news}");
            }
            catch (Exception e)
            {
                Debug.LogError("[Purge] Building items failed: " + e.Message);
                throw;
            }
        }

        /// <summary>"5x45_ammo" belongs to the type whose model folder is "5x45" or starts with "9_" for "9".</summary>
        private static bool Matches(AmmoDefinition ammo, string modelName)
        {
            var suffix = modelName.LastIndexOf("_ammo", StringComparison.OrdinalIgnoreCase);
            if (suffix <= 0) return false;
            var key = modelName.Substring(0, suffix);
            return ammo.ModelFolders.Any(folder => string.Equals(folder, key, StringComparison.OrdinalIgnoreCase) ||
                                                   folder.StartsWith(key + "_", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Prefab models directly in the folder (not in subfolders).</summary>
        private static IEnumerable<string> Models(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return Enumerable.Empty<string>();
            return AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetDirectoryName(path).Replace('\\', '/') == folder)
                .OrderBy(path => path, StringComparer.Ordinal);
        }

        /// <summary>
        /// Art/Items/Pickups/&lt;model&gt;&lt;suffix&gt;: the model scaled so its largest size is <paramref name="size"/>,
        /// centred, resting on the floor or centred at the pivot. Rebuilt in place (GUID kept).
        /// </summary>
        private static string BuildPrefab(string modelPath, string suffix, float size, bool onFloor)
        {
            var name = Name(modelPath) + suffix;
            var path = $"{PickupsPath}/{name}.prefab";
            var root = new GameObject(name);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), root.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                var bounds = Bounds(model);
                var largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                var scale = largest > 1e-4f ? size / largest : 1f;
                model.transform.localScale = Vector3.one * scale;
                bounds = Bounds(model);
                model.transform.localPosition = onFloor
                    ? new Vector3(-bounds.center.x, -bounds.min.y + 0.005f, -bounds.center.z)
                    : -bounds.center;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            return path;
        }

        private static Bounds Bounds(GameObject model)
        {
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            var first = true;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (first) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                first = false;
            }

            return bounds;
        }

        /// <summary>Prefabs of Pickups no item uses any more leave the folder and Addressables.</summary>
        private static int DeleteUnused(HashSet<string> used, UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings)
        {
            var removed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PickupsPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (used.Contains(path)) continue;
                settings.RemoveAssetEntry(guid);
                AssetDatabase.DeleteAsset(path);
                removed++;
            }

            return removed;
        }

        private static AssetReferenceGameObject Reference(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings,
            UnityEditor.AddressableAssets.Settings.AddressableAssetGroup group, string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (settings.FindAssetEntry(guid) == null) settings.CreateOrMoveEntry(guid, group);
            return new AssetReferenceGameObject(guid);
        }

        private static T LoadOrCreate<T>(string path, List<string> created) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path); // the id comes from the file name (ContentIdAssigner)
            created.Add(Path.GetFileNameWithoutExtension(path));
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static string Name(string path) => Path.GetFileNameWithoutExtension(path);

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
        }
    }
}
