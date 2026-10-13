using System.Collections.Generic;
using System.Linq;
using Maze.Core.Visual;
using Maze.Editor.LevelDesigner;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Decor: prepares the props in <c>Art/Decor</c> for the Decor layer and fills the theme's
    /// Decor set. Decor is merged into chunk meshes like floors and walls, so each prefab gets: materials on
    /// <c>Maze/Geometry</c> (copies of the original materials, <c>Art/Decor/Materials</c>), readable meshes (model
    /// import settings) and a root scale that fits a big prop into its cell (footprint ≤ <see cref="FitSize"/>;
    /// dead bodies stay life-size — characters must not shrink). Variant ids are the prefab names; weights and
    /// categories of existing variants stay; heights are measured (<see cref="DecorHeights"/>).
    /// </summary>
    internal static class DecorBuilder
    {
        public const string Folder = "Assets/_Project/Art/Decor";
        private const string MaterialsFolder = Folder + "/Materials";
        private const string SetPath = "Assets/_Project/Data/Themes/PlaceholderDecorSet.asset";
        private const string ThemePath = "Assets/_Project/Data/Themes/PlaceholderTheme.asset";

        /// <summary>Largest footprint side of an auto-fitted prop, metres (a cell is 1 m).</summary>
        private const float FitSize = 0.95f;

        [MenuItem("Maze/Dev/Build Decor")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
                AssetDatabase.CreateFolder(Folder, "Materials");

            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .ToList();

            // Model import settings first: re-importing a model while its prefab is open for editing is unreliable.
            foreach (var path in prefabs)
            foreach (var filter in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    MakeReadable(filter.sharedMesh);

            var geometryMaterials = new Dictionary<Material, Material>();
            foreach (var path in prefabs)
                PreparePrefab(path, geometryMaterials);

            var set = AssetDatabase.LoadAssetAtPath<VisualSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<VisualSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }

            set.Kind = VisualKind.Decor;
            set.DefaultVariantId = null; // Decor is optional: no fallback.
            var variants = set.MutableVariants;
            foreach (var path in prefabs)
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                var id = System.IO.Path.GetFileNameWithoutExtension(path);
                if (variants.Any(v => v.Id == id))
                {
                    var existing = variants.First(v => v.Id == id);
                    if (existing.Prefab == null || existing.Prefab.AssetGUID != guid)
                        variants[variants.IndexOf(existing)] = new VisualVariant(id, existing.Weight, existing.Category,
                            existing.Definition, new AssetReferenceGameObject(guid));
                    continue;
                }

                variants.Add(new VisualVariant(id, 1, VisualCategory.General, null, new AssetReferenceGameObject(guid)));
            }

            // Only the prefabs found in the folder: GUIDToAssetPath may still return the path of a just deleted asset.
            var found = new HashSet<string>(prefabs.Select(AssetDatabase.AssetPathToGUID));
            variants.RemoveAll(v => v.Prefab == null || !found.Contains(v.Prefab.AssetGUID));
            DecorHeights.Refresh(set);
            EditorUtility.SetDirty(set);

            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            if (theme != null && theme.GetSet(VisualKind.Decor) != set)
            {
                theme.SetSet(VisualKind.Decor, set);
                EditorUtility.SetDirty(theme);
            }

            AssetDatabase.SaveAssets();
            var auto = set.Variants.Count(v => v.Height <= 0.3f);
            Debug.Log($"[Maze] Decor built: {set.Variants.Count} variants in {SetPath} ({auto} not taller than 0.3 m).");
        }

        private static void PreparePrefab(string path, Dictionary<Material, Material> geometryMaterials)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++)
                        if (materials[i] != null)
                            materials[i] = GeometryMaterial(materials[i], geometryMaterials);
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                FitIntoCell(root, path);
                LiftFlat(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>A Maze/Geometry copy of a material (already one: kept).</summary>
        private static Material GeometryMaterial(Material source, Dictionary<Material, Material> cache)
        {
            if (source.shader != null && source.shader.name == GeometryShaderName) return source;
            if (cache.TryGetValue(source, out var copy)) return copy;

            var path = $"{MaterialsFolder}/{source.name}_Decor.mat";
            copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = new Material(Shader.Find(GeometryShaderName));
                AssetDatabase.CreateAsset(copy, path);
            }

            copy.SetColor("_Color", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            foreach (var texture in new[] { "_MainTex", "_BumpMap" })
                if (source.HasProperty(texture) && copy.HasProperty(texture))
                    copy.SetTexture(texture, source.GetTexture(texture));
            foreach (var value in new[] { "_Glossiness", "_Metallic" })
                if (source.HasProperty(value) && copy.HasProperty(value))
                    copy.SetFloat(value, source.GetFloat(value));
            if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(copy);
            cache[source] = copy;
            return copy;
        }

        private const string GeometryShaderName = "Maze/Geometry";

        private static void MakeReadable(Mesh mesh)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) as ModelImporter;
            if (importer == null || importer.isReadable) return;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// A flat prop (a pool, a sheet) at the floor's height would flicker with it: its mesh moves into a child
        /// raised by <see cref="FlatLift"/> (the root's position is not used when cells are merged).
        /// </summary>
        private static void LiftFlat(GameObject root)
        {
            var child = root.transform.Find(LiftedChild);
            var filter = root.GetComponent<MeshFilter>();
            if (child == null && filter != null && filter.sharedMesh != null)
            {
                var renderer = root.GetComponent<MeshRenderer>();
                if (filter.sharedMesh.bounds.size.y >= FlatThickness)
                    return;

                child = new GameObject(LiftedChild).transform;
                child.SetParent(root.transform, false);
                child.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var lifted = child.gameObject.AddComponent<MeshRenderer>();
                lifted.sharedMaterials = renderer.sharedMaterials;
                lifted.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Object.DestroyImmediate(renderer);
                Object.DestroyImmediate(filter);
            }

            if (child == null)
                return;

            var mesh = child.GetComponent<MeshFilter>().sharedMesh;
            var scale = Mathf.Max(root.transform.localScale.y, 1e-4f);
            child.localPosition = new Vector3(0f, FlatLift / scale - mesh.bounds.min.y, 0f);
        }

        private const string LiftedChild = "Mesh";
        private const float FlatThickness = 0.01f;
        private const float FlatLift = 0.004f;

        /// <summary>Uniform root scale so the footprint fits the cell (from the meshes at scale 1; repeatable).</summary>
        private static void FitIntoCell(GameObject root, string path)
        {
            if (System.IO.Path.GetFileNameWithoutExtension(path).Contains("DeadBody"))
            {
                root.transform.localScale = Vector3.one;
                return;
            }

            root.transform.localScale = Vector3.one;
            var bounds = DecorHeights.MeshBounds(root);
            var footprint = Mathf.Max(bounds.size.x, bounds.size.z);
            var scale = footprint > FitSize ? FitSize / footprint : 1f;
            root.transform.localScale = new Vector3(scale, scale, scale);
        }
    }
}
