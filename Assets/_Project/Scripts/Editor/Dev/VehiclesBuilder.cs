using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Purge.Core.Effects;
using Purge.Core.Vehicles;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Vehicles (stage 9a): a <see cref="VehicleDefinition"/> in <c>Data/Vehicles</c> for every model
    /// in <c>Art/Vehicles</c> (the same name). A new one gets draft modules from the model's size (its root scale counts:
    /// a light car is about 1 cell wide, a truck 1.5) — the body rectangle from its bounds; a long one (over 3.2 m) is heavy: slower, stronger,
    /// wider turns, a bigger blast — and the engine sounds whose folders start with its name's last word
    /// (<c>Vehicle_Light</c> → <c>Sounds/Vehicle/light_idle_loop</c>…). An existing one keeps its numbers: only its model
    /// is set, and what stage 9b added when missing — the wreck module, the ram's stun, the smoke. Models are Addressable
    /// in Purge Visuals (a level loads only those it has). Then the content catalog. A new vehicle = a model in the
    /// folder + this command.
    /// </summary>
    internal static class VehiclesBuilder
    {
        public const string ModelsPath = "Assets/_Project/Art/Vehicles";
        private const string DataPath = "Assets/_Project/Data/Vehicles";
        private const string StunPath = "Assets/_Project/Data/Effects/Status_Stun.asset";

        /// <summary>The wreck's smoke (a copy of Build Pack Effects).</summary>
        private const string SmokePath = "Assets/_Project/Art/Effects/PolygonParticleFX/FX_Vehicle_Smoke.prefab";

        /// <summary>Longer than this, cells, the drafts are of a heavy vehicle.</summary>
        private const float HeavyLength = 3.2f;

        [MenuItem("Purge/Dev/Build Vehicles")]
        public static void Build()
        {
            try
            {
                EnsureFolder(DataPath);
                var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
                var group = settings.FindGroup(LevelDesigner.LevelSync.VisualsGroup) ?? settings.DefaultGroup;
                var created = new List<string>();
                var models = AssetDatabase.FindAssets("t:Prefab", new[] { ModelsPath })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (models.Count == 0) Debug.LogWarning($"[Purge] Build Vehicles: no models in {ModelsPath}.");

                foreach (var modelPath in models)
                {
                    var name = Path.GetFileNameWithoutExtension(modelPath);
                    var path = $"{DataPath}/{name}.asset";
                    var vehicle = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
                    if (vehicle == null)
                    {
                        vehicle = ScriptableObject.CreateInstance<VehicleDefinition>();
                        AssetDatabase.CreateAsset(vehicle, path); // the id comes from the file name (ContentIdAssigner)
                        vehicle = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
                        Seed(vehicle, AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), name);
                        created.Add(name);
                    }

                    var guid = AssetDatabase.AssetPathToGUID(modelPath);
                    if (settings.FindAssetEntry(guid) == null) settings.CreateOrMoveEntry(guid, group);
                    var look = vehicle.Look;
                    if (look == null)
                    {
                        look = new VehicleLookModule();
                        vehicle.MutableModules.Add(look);
                    }

                    look.SetPrefab(new AssetReferenceGameObject(guid));
                    AddStage9b(vehicle, look);
                    var problems = new List<string>();
                    vehicle.Validate(problems);
                    foreach (var problem in problems) Debug.LogWarning("[Purge] " + problem);
                    EditorUtility.SetDirty(vehicle);
                }

                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Content.ContentCatalogBuilder.Build(log: false);
                var news = created.Count > 0 ? $" New: {string.Join(", ", created)}." : "";
                Debug.Log($"[Purge] Vehicles built: {models.Count}.{news}");
            }
            catch (Exception e)
            {
                Debug.LogError("[Purge] Building vehicles failed: " + e.Message);
                throw;
            }
        }

        /// <summary>Draft modules of a new vehicle (ТЗ 9а; numbers are drafts until stage 10).</summary>
        private static void Seed(VehicleDefinition vehicle, GameObject model, string name)
        {
            var size = ModelSize(model);
            var length = Mathf.Max(1f, size.y * 0.95f);
            var width = Mathf.Max(1f, size.x * 0.85f);
            var heavy = length > HeavyLength;
            var engine = new EngineModule
            {
                MaxSpeed = heavy ? 5.95f : 7.7f,
                Acceleration = heavy ? 3.15f : 4.9f,
                Braking = heavy ? 10f : 14f,
                Rolling = heavy ? 2.5f : 3f,
                ReverseSpeed = heavy ? 2.1f : 2.8f,
                Power = heavy ? 2f : 1f,
            };
            var steering = new SteeringModule { TurnRadius = heavy ? 4f : 2.8f };
            var hull = new HullModule
            {
                Health = heavy ? 500f : 300f,
                Length = Round(length),
                Width = Round(width),
                WallSafeSpeed = 3f,
                WallDamage = 4f,
            };
            var ram = new RamModule { Mass = heavy ? 20f : 8f, BounceDamage = heavy ? 30f : 25f };
            var look = new VehicleLookModule { CameraZoom = 3f };
            var prefix = name.Substring(name.LastIndexOf('_') + 1).ToLowerInvariant();
            var sounds = AudioBuilder.VehicleSounds();
            look.SetSounds(Sound(sounds, prefix, "idle_loop"), Sound(sounds, prefix, "drive_loop"), Sound(sounds, prefix, "start"),
                Sound(sounds, prefix, "rev"));
            vehicle.Configure(vehicle.Id, engine, steering, hull, ram, Wreck(heavy), look);
        }

        /// <summary>What stage 9b added, for a vehicle built before it: the wreck, the ram's stun, the smoke.</summary>
        private static void AddStage9b(VehicleDefinition vehicle, VehicleLookModule look)
        {
            if (!vehicle.TryGet(out WreckModule _))
                vehicle.MutableModules.Insert(Mathf.Max(0, vehicle.MutableModules.IndexOf(look)),
                    Wreck(vehicle.Hull.Length > HeavyLength));
            if (vehicle.TryGet(out RamModule ram) && ram.Stun == null)
                ram.Stun = AssetDatabase.LoadAssetAtPath<StatusDefinition>(StunPath);
            if (look.Smoke == null || !look.Smoke.RuntimeKeyIsValid())
            {
                var smoke = AssetDatabase.AssetPathToGUID(SmokePath);
                if (string.IsNullOrEmpty(smoke)) Debug.LogWarning($"[Purge] Build Vehicles: no {SmokePath} (Build Pack Effects first).");
                else look.SetSmoke(new AssetReferenceGameObject(smoke));
            }
        }

        /// <summary>Draft wreck: the heavy one (a tanker truck) blows up wider and harder.</summary>
        private static WreckModule Wreck(bool heavy)
        {
            var wreck = new WreckModule();
            var explosion = new ExplosionEffect { Damage = heavy ? 260f : 150f, EdgeDamage = 0.4f, SoundRadius = 16f };
            explosion.SetRadius(heavy ? 4f : 3f);
            wreck.MutableEffects.Add(explosion);
            return wreck;
        }

        private static string Sound(List<string> sounds, string prefix, string role)
        {
            var name = $"{prefix}_{role}";
            return sounds.FirstOrDefault(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The model's footprint, metres: x — width, y — length (along +Z).</summary>
        private static Vector2 ModelSize(GameObject model)
        {
            if (model == null) return new Vector2(2f, 4.6f);
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Vector2(2f, 4.6f);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return new Vector2(bounds.size.x, bounds.size.z);
        }

        private static float Round(float value) => Mathf.Round(value * 10f) / 10f;

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
        }
    }
}
