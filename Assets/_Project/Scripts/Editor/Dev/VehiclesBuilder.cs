using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Purge.Core.Vehicles;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Vehicles (stage 9a): a <see cref="VehicleDefinition"/> in <c>Data/Vehicles</c> for every model
    /// in <c>Art/Vehicles</c> (the same name). A new one gets draft modules from the model's size — the body rectangle
    /// from its bounds; a long one (over 5.5 m) is heavy: slower, stronger, wider turns — and the engine sounds whose
    /// folders start with its name's last word (<c>Vehicle_Light</c> → <c>Sounds/Vehicle/light_idle_loop</c>…). An
    /// existing one keeps its numbers: only its model is set. Models are Addressable in Purge Visuals (a level loads only
    /// those it has). Then the content catalog. A new vehicle = a model in the folder + this command.
    /// </summary>
    internal static class VehiclesBuilder
    {
        public const string ModelsPath = "Assets/_Project/Art/Vehicles";
        private const string DataPath = "Assets/_Project/Data/Vehicles";

        /// <summary>Longer than this, cells, the drafts are of a heavy vehicle.</summary>
        private const float HeavyLength = 5.5f;

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
                MaxSpeed = heavy ? 8.5f : 11f,
                Acceleration = heavy ? 4.5f : 7f,
                Braking = heavy ? 10f : 14f,
                Rolling = heavy ? 2.5f : 3f,
                ReverseSpeed = heavy ? 3f : 4f,
                Power = heavy ? 2f : 1f,
            };
            var steering = new SteeringModule { TurnRadius = heavy ? 5f : 3.5f };
            var hull = new HullModule
            {
                Health = heavy ? 500f : 300f,
                Length = Round(length),
                Width = Round(width),
                WallSafeSpeed = 3f,
                WallDamage = 4f,
            };
            var ram = new RamModule { Mass = heavy ? 20f : 8f };
            var look = new VehicleLookModule { CameraZoom = 3f };
            var prefix = name.Substring(name.LastIndexOf('_') + 1).ToLowerInvariant();
            var sounds = AudioBuilder.VehicleSounds();
            look.SetSounds(Sound(sounds, prefix, "idle_loop"), Sound(sounds, prefix, "drive_loop"), Sound(sounds, prefix, "start"),
                Sound(sounds, prefix, "rev"));
            vehicle.Configure(vehicle.Id, engine, steering, hull, ram, look);
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
