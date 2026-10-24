using System;
using System.Collections.Generic;
using System.IO;
using Purge.Core.Drops;
using Purge.Core.Effects;
using Purge.Core.Level;
using Purge.Core.Objects;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Level Objects (stage 7в): the assets of the level objects in <c>Data/Objects</c> — a
    /// <see cref="BarrelDefinition"/> per <see cref="BarrelKind"/>, a <see cref="ChestDefinition"/> per
    /// <see cref="ChestKind"/>, the <see cref="RefugeDefinition"/>. A missing asset is made with the draft numbers of the
    /// card (HP 30, blast 60 r 2, a zone r 2; officer / elite drop tables; 10 s, 5 %/s); an existing one keeps its
    /// numbers and effects — only its model and an empty reward table are set. Models are
    /// Addressable in Purge Visuals (a level loads only those it has). Then the content catalog.
    /// </summary>
    internal static class LevelObjectsBuilder
    {
        private const string DataPath = "Assets/_Project/Data/Objects";
        private const string BarrelModels = "Assets/_Project/Art/Items/Purge/Barrel";
        private const string ChestModels = "Assets/_Project/Art/Items/Purge/Chest";
        private const string DomeLook = "Assets/_Project/Art/Effects/PolygonArsenal/FX_Refuge_Dome.prefab";
        private const string OfficerDrop = "Assets/_Project/Data/Drops/OfficerDrop.asset";
        private const string EliteDrop = "Assets/_Project/Data/Drops/EliteDrop.asset";

        /// <summary>The model of each kind (chests: chosen by the agent, seen by the user at the review).</summary>
        private static readonly Dictionary<BarrelKind, string> BarrelModel = new Dictionary<BarrelKind, string>
        {
            { BarrelKind.Gas, "barrel_gas" },
            { BarrelKind.Fire, "barrel_fuel" },
            { BarrelKind.Acid, "barrel_acid" },
        };

        private static readonly Dictionary<ChestKind, string> ChestModel = new Dictionary<ChestKind, string>
        {
            { ChestKind.Common, "Chest_01" },
            { ChestKind.Locked, "Chest_03" },
            { ChestKind.Challenge, "Chest_04" },
        };

        /// <summary>Zone and its seconds of each barrel kind (ТЗ 7в).</summary>
        private static readonly Dictionary<BarrelKind, (string Zone, float Duration)> BarrelZone =
            new Dictionary<BarrelKind, (string, float)>
            {
                { BarrelKind.Gas, ("Zone_Poison", 6f) },
                { BarrelKind.Fire, ("Zone_Fire", 4f) },
                { BarrelKind.Acid, ("Zone_Acid", 6f) },
            };

        [MenuItem("Purge/Dev/Build Level Objects")]
        public static void Build()
        {
            try
            {
                EnsureFolder(DataPath);
                var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
                var group = settings.FindGroup(LevelDesigner.LevelSync.VisualsGroup) ?? settings.DefaultGroup;
                var created = new List<string>();

                foreach (BarrelKind kind in Enum.GetValues(typeof(BarrelKind)))
                {
                    var path = $"{DataPath}/Barrel_{kind}.asset";
                    var isNew = AssetDatabase.LoadAssetAtPath<BarrelDefinition>(path) == null;
                    var barrel = LoadOrCreate<BarrelDefinition>(path, created);
                    if (isNew) Seed(barrel, kind);
                    barrel.SetPrefab(Model(settings, group, BarrelModels, BarrelModel[kind]));
                    EditorUtility.SetDirty(barrel);
                }

                foreach (ChestKind kind in Enum.GetValues(typeof(ChestKind)))
                {
                    var path = $"{DataPath}/Chest_{kind}.asset";
                    var isNew = AssetDatabase.LoadAssetAtPath<ChestDefinition>(path) == null;
                    var chest = LoadOrCreate<ChestDefinition>(path, created);
                    var table = AssetDatabase.LoadAssetAtPath<DropTable>(kind == ChestKind.Common ? OfficerDrop : EliteDrop);
                    if (isNew) chest.Configure(chest.Id, kind, table);
                    else if (chest.Reward == null) chest.SetReward(table);
                    chest.SetPrefab(Model(settings, group, ChestModels, ChestModel[kind]));
                    EditorUtility.SetDirty(chest);
                }

                var refugePath = $"{DataPath}/Refuge.asset";
                var refugeIsNew = AssetDatabase.LoadAssetAtPath<RefugeDefinition>(refugePath) == null;
                var refuge = LoadOrCreate<RefugeDefinition>(refugePath, created);
                if (refugeIsNew) refuge.Configure(refuge.Id, 10f, 0.05f, 0.5f);
                var dome = Reference(settings, group, DomeLook);
                if (dome == null) Debug.LogWarning($"[Purge] No dome look at {DomeLook}: run Build Pack Effects.");
                refuge.SetLook(dome);
                EditorUtility.SetDirty(refuge);

                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Content.ContentCatalogBuilder.Build(log: false);
                var news = created.Count > 0 ? $" New: {string.Join(", ", created)}." : "";
                Debug.Log($"[Purge] Level objects built.{news}");
            }
            catch (Exception e)
            {
                Debug.LogError("[Purge] Building level objects failed: " + e.Message);
                throw;
            }
        }

        /// <summary>Draft numbers of the card: HP 30, a blast of 60 in r 2, the kind's zone in r 2.</summary>
        private static void Seed(BarrelDefinition barrel, BarrelKind kind)
        {
            var explosion = new ExplosionEffect { Damage = 60f, EdgeDamage = 0.35f, SoundRadius = 12f };
            explosion.SetRadius(2f);
            var (zoneName, duration) = BarrelZone[kind];
            var zone = AssetDatabase.LoadAssetAtPath<ZoneDefinition>($"Assets/_Project/Data/Effects/{zoneName}.asset");
            if (zone == null) Debug.LogWarning($"[Purge] Barrel {kind}: no {zoneName} in Data/Effects.");
            var place = new PlaceZoneEffect { Zone = zone, Duration = duration, Strength = 1f };
            place.SetRadius(2f);
            barrel.Configure(barrel.Id, kind, 30f, explosion, place);
        }

        private static AssetReferenceGameObject Model(AddressableAssetSettings settings, AddressableAssetGroup group,
            string folder, string name)
        {
            var reference = Reference(settings, group, $"{folder}/{name}.prefab");
            if (reference == null) Debug.LogWarning($"[Purge] No model {folder}/{name}.prefab.");
            return reference;
        }

        private static AssetReferenceGameObject Reference(AddressableAssetSettings settings, AddressableAssetGroup group, string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return null;
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

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
        }
    }
}
