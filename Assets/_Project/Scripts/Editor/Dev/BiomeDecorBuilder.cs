using System.Collections.Generic;
using System.Linq;
using Purge.Core.Definitions;
using Purge.Core.Generation;
using Purge.Core.Visual;
using Purge.Editor.LevelDesigner;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Biome Decor (stage 7): prepares the biome props in <c>Art/Biomes/Purge/&lt;Biome&gt;</c> for the
    /// generator and fills the biome theme. The folder a prefab lies in is what it does in the game:
    /// <c>Stop_shoot_and_move</c> — obstacle Tree (movement + bullets), its light trees also make the shapeless forest
    /// wall; <c>Stop_move</c> — Bush (bushes, burn) or Stone (the rest: movement only); <c>Floor_decor</c> — plain decor,
    /// <c>Floor_decor/For_fire</c> — decor of grass cells (category Grass).
    /// <para>Every prefab gets materials on <c>Purge/Geometry</c> (copies in <c>Materials</c>): the Synty vegetation
    /// shader keeps trunk and leaves in one material told apart by vertex colour (blue &gt; 0.5 = leaf), so such meshes
    /// are split into a trunk and a leaf sub-mesh (copies in <c>Meshes</c>); leaves are alpha-tested and two-sided.
    /// Root scale fits the footprint per folder (trees overlap their neighbours a little); grass is squashed to a fixed
    /// height. Heavy models (more than <see cref="MaxAutoTriangles"/> triangles) and huge patches stay out of the auto
    /// sets (weight 0 / skipped). Weights of existing variants stay. Repeatable.</para>
    /// </summary>
    internal static class BiomeDecorBuilder
    {
        private const string Root = "Assets/_Project/Art/Biomes/Purge";
        private const string ThemesFolder = "Assets/_Project/Data/Themes";
        private const string ObstaclesFolder = "Assets/_Project/Data/Obstacles";
        private const string RecipesFolder = "Assets/_Project/Data/Recipes";
        private const string GeometryShaderName = "Purge/Geometry";
        private const string VegetationShader = "SyntyStudios/VegitationShader";
        private const string TreesShader = "SyntyStudios/Trees";
        private const string TriplanarShader = "SyntyStudios/TriplanarBasic";

        /// <summary>Models heavier than this are not placed automatically (mobile budget: hundreds of trees on screen).</summary>
        public const int MaxAutoTriangles = 3000;

        /// <summary>Models wider than this (metres, at scale 1) are skipped: ground patches for whole scenes.</summary>
        private const float MaxSourceFootprint = 10f;

        [MenuItem("Purge/Dev/Build Biome Decor")]
        public static void Build()
        {
            BuildForest();
            AssetDatabase.SaveAssets();
        }

        private static void BuildForest()
        {
            const string biome = "Forest";
            var folder = $"{Root}/{biome}";
            EnsureFolder(folder, "Materials");
            EnsureFolder(folder, "Meshes");

            var tree = AssetDatabase.LoadAssetAtPath<ObstacleDefinition>($"{ObstaclesFolder}/Tree.asset");
            var bush = AssetDatabase.LoadAssetAtPath<ObstacleDefinition>($"{ObstaclesFolder}/Bush.asset");
            var stone = Stone();

            var wallVariants = new List<(string Id, string Guid, int Weight)>();
            var decorVariants = new List<(string Id, string Guid, int Weight, VisualCategory Category, ObstacleDefinition Definition)>();
            var skipped = new List<string>();
            foreach (var path in Prefabs(folder))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var role = RoleOf(path);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (SourceFootprint(source) > MaxSourceFootprint)
                {
                    skipped.Add(name);
                    continue;
                }

                Prepare(path, folder, role);
                var guid = AssetDatabase.AssetPathToGUID(path);
                var light = Triangles(AssetDatabase.LoadAssetAtPath<GameObject>(path)) <= MaxAutoTriangles;
                var weight = light ? 1 : 0;
                switch (role)
                {
                    case Role.Blocker:
                        decorVariants.Add((name, guid, light ? (name.Contains("Tree") ? 4 : 1) : 0, VisualCategory.General, tree));
                        if (name.Contains("Tree"))
                            wallVariants.Add((name, guid, weight));
                        break;
                    case Role.Low:
                        decorVariants.Add((name, guid, weight, VisualCategory.General, name.Contains("Bush") ? bush : stone));
                        break;
                    case Role.Grass:
                        decorVariants.Add((name, guid, weight, VisualCategory.Grass, null));
                        break;
                    default:
                        decorVariants.Add((name, guid, weight, VisualCategory.General, null));
                        break;
                }
            }

            var wallSet = FillSet($"{ThemesFolder}/{biome}BiomeWallSet.asset", VisualKind.Wall,
                wallVariants.Select(v => (v.Id, v.Guid, v.Weight, VisualCategory.General, (ObstacleDefinition)null)).ToList());
            wallSet.ShapelessWalls = true;
            wallSet.DefaultVariantId = wallVariants.Where(v => v.Weight > 0).Select(v => v.Id).FirstOrDefault();
            var decorSet = FillSet($"{ThemesFolder}/{biome}BiomeDecorSet.asset", VisualKind.Decor, decorVariants);
            decorSet.DefaultVariantId = null;
            DecorHeights.Refresh(decorSet);
            EditorUtility.SetDirty(wallSet);
            EditorUtility.SetDirty(decorSet);

            var theme = Theme(biome, wallSet, decorSet);
            var recipe = ForestRecipe(tree, bush, stone);
            Debug.Log($"[Purge] Biome decor built: {theme.name} — {wallSet.Variants.Count} wall and {decorSet.Variants.Count} " +
                      $"decor variants; recipe {AssetDatabase.GetAssetPath(recipe)}" +
                      (skipped.Count > 0 ? $"; skipped (too big): {string.Join(", ", skipped)}" : "") + ".");
        }

        // ------------------------------------------------------------ Prefabs

        private enum Role { Blocker, Low, Decor, Grass }

        private static Role RoleOf(string path)
        {
            if (path.Contains("/Stop_shoot_and_move/")) return Role.Blocker;
            if (path.Contains("/Stop_move/")) return Role.Low;
            if (path.Contains("/For_fire/")) return Role.Grass;
            return Role.Decor;
        }

        private static IEnumerable<string> Prefabs(string folder) =>
            AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.Contains("/Stop_shoot_and_move/") || p.Contains("/Stop_move/") || p.Contains("/Floor_decor/"))
                .OrderBy(p => p);

        private static void Prepare(string path, string folder, Role role)
        {
            // Model import settings first: re-importing a model while its prefab is open for editing is unreliable.
            foreach (var filter in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    MakeReadable(filter.sharedMesh);

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    Convert(filter, renderer, folder, root.name);
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                Fit(root, role);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Materials onto Purge/Geometry; vegetation sub-meshes split into trunk and leaves.</summary>
        private static void Convert(MeshFilter filter, MeshRenderer renderer, string folder, string prefabName)
        {
            var mesh = filter.sharedMesh;
            var materials = renderer.sharedMaterials;
            if (materials.All(m => m == null || m.shader.name == GeometryShaderName)) return; // done before

            var split = materials.Any(m => m != null && m.shader.name == VegetationShader);
            if (!split)
            {
                for (var i = 0; i < materials.Length; i++)
                    if (materials[i] != null) materials[i] = CopyMaterial(materials[i], folder, Part.Whole);
                renderer.sharedMaterials = materials;
                return;
            }

            var colors = mesh.colors;
            var parts = new List<(int[] Triangles, Material Material)>();
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var source = sub < materials.Length ? materials[sub] : null;
                var triangles = mesh.GetTriangles(sub);
                if (source == null || source.shader.name != VegetationShader)
                {
                    parts.Add((triangles, source == null ? null : CopyMaterial(source, folder, Part.Whole)));
                    continue;
                }

                var trunk = new List<int>();
                var leaves = new List<int>();
                for (var t = 0; t < triangles.Length; t += 3)
                {
                    var leaf = colors.Length > 0 &&
                               colors[triangles[t]].b + colors[triangles[t + 1]].b + colors[triangles[t + 2]].b > 1.5f;
                    (leaf ? leaves : trunk).AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
                }

                if (trunk.Count > 0) parts.Add((trunk.ToArray(), CopyMaterial(source, folder, Part.Trunk)));
                if (leaves.Count > 0) parts.Add((leaves.ToArray(), CopyMaterial(source, folder, Part.Leaves)));
            }

            var meshPath = $"{folder}/Meshes/{prefabName}_{mesh.name}.asset";
            var copy = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var created = copy == null;
            if (created) copy = new Mesh();
            copy.Clear();
            copy.name = $"{prefabName}_{mesh.name}";
            copy.indexFormat = mesh.indexFormat;
            copy.vertices = mesh.vertices;
            copy.normals = mesh.normals;
            copy.tangents = mesh.tangents;
            copy.uv = mesh.uv;
            copy.subMeshCount = parts.Count;
            for (var i = 0; i < parts.Count; i++)
                copy.SetTriangles(parts[i].Triangles, i);
            copy.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(copy, meshPath);
            else EditorUtility.SetDirty(copy);

            filter.sharedMesh = copy;
            renderer.sharedMaterials = parts.Select(p => p.Material).ToArray();
        }

        private enum Part { Whole, Trunk, Leaves }

        private static Material CopyMaterial(Material source, string folder, Part part)
        {
            var suffix = part == Part.Trunk ? "_Trunk" : part == Part.Leaves ? "_Leaves" : "";
            var path = $"{folder}/Materials/{source.name}{suffix}.mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = new Material(Shader.Find(GeometryShaderName));
                AssetDatabase.CreateAsset(copy, path);
            }

            Texture main = null, normal = null;
            var color = Color.white;
            var cutout = false;
            var cutoff = 0.5f;
            switch (source.shader.name)
            {
                case VegetationShader when part == Part.Leaves:
                    main = Tex(source, "_LeafTex");
                    normal = Tex(source, "_LeafNormalMap");
                    cutout = true;
                    cutoff = Float(source, "_AlphaClip", 0.5f);
                    break;
                case VegetationShader:
                    if (Float(source, "_TrunkFlatColourSwitch", 0f) > 0.5f) color = source.GetColor("_TrunkBaseColour");
                    else main = Tex(source, "_TunkTex");
                    normal = Tex(source, "_TrunkNormalMap");
                    break;
                case TreesShader:
                    main = Tex(source, "_MainTexture");
                    if (source.HasProperty("_ColorTint")) color = source.GetColor("_ColorTint");
                    cutout = source.name.Contains("Leaves");
                    cutoff = Float(source, "_Cutoff", 0.5f);
                    break;
                case TriplanarShader:
                    // World-projected: the meshes have no UVs for it — a flat colour of the side texture.
                    var sides = Tex(source, "_Sides");
                    if (sides != null) color = AverageColor(sides);
                    break;
                default:
                    main = Tex(source, "_MainTex") ?? Tex(source, "_BaseMap");
                    normal = Tex(source, "_BumpMap");
                    if (source.HasProperty("_Color")) color = source.GetColor("_Color");
                    break;
            }

            color.a = 1f;
            copy.SetColor("_Color", color);
            copy.SetTexture("_MainTex", main);
            copy.SetTexture("_BumpMap", normal);
            copy.SetFloat("_Glossiness", 0.1f);
            copy.SetFloat("_Metallic", 0f);
            copy.SetColor("_EmissionColor", Color.black);
            copy.SetFloat("_AlphaTest", cutout ? 1f : 0f);
            copy.SetFloat("_Cutoff", cutoff);
            copy.SetFloat("_Cull", cutout ? (float)UnityEngine.Rendering.CullMode.Off : (float)UnityEngine.Rendering.CullMode.Back);
            if (cutout) copy.EnableKeyword("_ALPHATEST_ON");
            else copy.DisableKeyword("_ALPHATEST_ON");
            EditorUtility.SetDirty(copy);
            return copy;
        }

        private static Color AverageColor(Texture texture)
        {
            const int size = 32;
            var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            var sum = Color.black;
            foreach (var c in read.GetPixels()) sum += c;
            Object.DestroyImmediate(read);
            return sum / (size * size);
        }

        private static Texture Tex(Material material, string property) =>
            material.HasProperty(property) ? material.GetTexture(property) : null;

        private static float Float(Material material, string property, float fallback) =>
            material.HasProperty(property) ? material.GetFloat(property) : fallback;

        /// <summary>
        /// Uniform root scale so the footprint is the folder's size (from the meshes at scale 1; repeatable); grass is
        /// squashed to <see cref="GrassHeight"/>.
        /// </summary>
        private static void Fit(GameObject root, Role role)
        {
            root.transform.localScale = Vector3.one;
            var bounds = DecorHeights.MeshBounds(root);
            var footprint = Mathf.Max(bounds.size.x, bounds.size.z);
            var fit = role == Role.Blocker ? 1.6f : role == Role.Low ? 1.2f : role == Role.Grass ? 1.3f : 1f;
            var scale = footprint > fit ? fit / footprint : 1f;
            var y = scale;
            if (role == Role.Grass && bounds.size.y > 0f)
                y = GrassHeight / bounds.size.y;
            root.transform.localScale = new Vector3(scale, y, scale);
        }

        private const float GrassHeight = 0.55f;

        private static float SourceFootprint(GameObject prefab)
        {
            var bounds = DecorHeights.MeshBounds(prefab);
            var scale = prefab.transform.localScale;
            return Mathf.Max(bounds.size.x / Mathf.Max(scale.x, 1e-4f), bounds.size.z / Mathf.Max(scale.z, 1e-4f));
        }

        private static int Triangles(GameObject prefab) =>
            prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null)
                .Sum(f => (int)(f.sharedMesh.triangles.Length / 3));

        private static void MakeReadable(Mesh mesh)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) as ModelImporter;
            if (importer == null || importer.isReadable) return;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------ Data

        private static VisualSet FillSet(string path, VisualKind kind,
            List<(string Id, string Guid, int Weight, VisualCategory Category, ObstacleDefinition Definition)> built)
        {
            var set = AssetDatabase.LoadAssetAtPath<VisualSet>(path);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<VisualSet>();
                AssetDatabase.CreateAsset(set, path);
            }

            set.Kind = kind;
            var old = set.MutableVariants.ToDictionary(v => v.Id);
            set.MutableVariants.Clear();
            foreach (var v in built)
            {
                var weight = old.TryGetValue(v.Id, out var existing) ? existing.Weight : v.Weight;
                set.MutableVariants.Add(new VisualVariant(v.Id, weight, v.Category, v.Definition, new AssetReferenceGameObject(v.Guid)));
            }

            return set;
        }

        /// <summary>The biome theme: a copy of the old <c>&lt;Biome&gt;Theme</c> (floor, lighting, fog…) with the new walls and decor.</summary>
        private static VisualTheme Theme(string biome, VisualSet walls, VisualSet decor)
        {
            var path = $"{ThemesFolder}/{biome}BiomeTheme.asset";
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(path);
            if (theme == null)
            {
                AssetDatabase.CopyAsset($"{ThemesFolder}/{biome}Theme.asset", path);
                theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(path);
            }

            theme.SetSet(VisualKind.Wall, walls);
            theme.SetSet(VisualKind.Decor, decor);
            EditorUtility.SetDirty(theme);
            return theme;
        }

        /// <summary>Obstacle that stops only movement and does not burn (stumps, rock piles).</summary>
        private static ObstacleDefinition Stone()
        {
            var path = $"{ObstaclesFolder}/Stone.asset";
            var stone = AssetDatabase.LoadAssetAtPath<ObstacleDefinition>(path);
            if (stone != null) return stone;
            stone = ScriptableObject.CreateInstance<ObstacleDefinition>();
            AssetDatabase.CreateAsset(stone, path); // the id ("stone") comes from ContentIdAssigner
            var so = new SerializedObject(stone);
            so.FindProperty("_blocksMovement").boolValue = true;
            so.FindProperty("_blocksBullets").boolValue = false;
            so.FindProperty("_blocksSight").boolValue = false;
            so.FindProperty("_flammable").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            return stone;
        }

        /// <summary>The Forest recipe with the steps of the stage 7 card; an existing recipe is left as it is.</summary>
        private static BiomeRecipe ForestRecipe(ObstacleDefinition tree, ObstacleDefinition bush, ObstacleDefinition stone)
        {
            var path = $"{RecipesFolder}/ForestRecipe.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<BiomeRecipe>(path);
            if (recipe != null) return recipe;

            if (!AssetDatabase.IsValidFolder(RecipesFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Recipes");
            recipe = ScriptableObject.CreateInstance<BiomeRecipe>();
            var obstacles = new ObstaclesStep();
            obstacles.MutableObstacles.Add(new WeightedObstacle(tree, 60));
            obstacles.MutableObstacles.Add(new WeightedObstacle(bush, 25));
            obstacles.MutableObstacles.Add(new WeightedObstacle(stone, 15));
            recipe.MutableSteps.AddRange(new GenerationStep[]
            {
                new PerimeterStep(), new StartStep(), new ArenaStep(), new SpawnPointsStep(), new ClearingsStep(),
                new TrailsStep(), obstacles, new GrassStep(), new DecorStep(),
            });
            AssetDatabase.CreateAsset(recipe, path);
            return recipe;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
