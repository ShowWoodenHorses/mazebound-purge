using System.Collections.Generic;
using System.Linq;
using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Convert Object Materials to Maze/Lit: every opaque Standard material of the prefabs the game shows
    /// besides floor and walls — themes' object sets (doors, exits, keys, pickups, zombies...), the player, weapons in
    /// hands — switches to the shared lit shader (lantern and level lights, no shadows). Property names match, so
    /// colors and textures are kept. Transparent and cutout materials are left as they are (reported).
    /// Run after adding new models (Build Weapons, Build Zombie/Player Animations create prefabs).
    /// </summary>
    internal static class LitMaterialsConverter
    {
        private const string LitShader = "Maze/Lit";

        [MenuItem("Maze/Dev/Convert Object Materials to Maze Lit")]
        public static void Convert()
        {
            var lit = Shader.Find(LitShader);
            if (lit == null)
            {
                Debug.LogError($"[Maze] Shader '{LitShader}' not found.");
                return;
            }

            var converted = new List<string>();
            var skipped = new List<string>();
            foreach (var material in CollectMaterials())
            {
                if (material.shader == lit)
                    continue;
                if (material.shader == null || material.shader.name != "Standard")
                {
                    skipped.Add($"{material.name} ({(material.shader != null ? material.shader.name : "no shader")})");
                    continue;
                }

                // Standard's rendering mode: 0 = Opaque, 1 = Cutout, 2 = Fade, 3 = Transparent.
                if (material.HasProperty("_Mode") && material.GetFloat("_Mode") > 0.5f)
                {
                    skipped.Add($"{material.name} (not opaque)");
                    continue;
                }

                Undo.RecordObject(material, "Convert to Maze/Lit");
                material.shader = lit;
                material.shaderKeywords = new string[0];
                material.renderQueue = -1;
                EditorUtility.SetDirty(material);
                converted.Add(material.name);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Maze/Lit: {converted.Count} material(s) converted" +
                      (converted.Count > 0 ? ": " + string.Join(", ", converted) : "") +
                      (skipped.Count > 0 ? $". Skipped {skipped.Count}: " + string.Join(", ", skipped) : "."));
        }

        private static IEnumerable<Material> CollectMaterials()
        {
            var prefabs = new HashSet<GameObject>();

            foreach (var theme in Load<VisualTheme>())
            foreach (VisualKind kind in System.Enum.GetValues(typeof(VisualKind)))
            {
                if (kind == VisualKind.Floor || kind == VisualKind.Wall)
                    continue; // Level geometry: Maze/Geometry.
                var set = theme.GetSet(kind);
                if (set == null) continue;
                foreach (var variant in set.Variants)
                    Add(prefabs, variant.Prefab?.editorAsset as GameObject);
            }

            foreach (var player in Load<PlayerVisualDefinition>())
                Add(prefabs, player.Prefab?.editorAsset as GameObject);

            foreach (var catalog in Load<WeaponVisualCatalog>())
            foreach (var weapon in catalog.Weapons)
                Add(prefabs, weapon.HeldPrefab?.editorAsset as GameObject);

            return prefabs
                .SelectMany(prefab => prefab.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => !(renderer is ParticleSystemRenderer))
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null && AssetDatabase.GetAssetPath(material).StartsWith("Assets/"))
                .Distinct();
        }

        private static void Add(HashSet<GameObject> prefabs, GameObject prefab)
        {
            if (prefab != null) prefabs.Add(prefab);
        }

        private static IEnumerable<T> Load<T>() where T : Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null);
    }
}
