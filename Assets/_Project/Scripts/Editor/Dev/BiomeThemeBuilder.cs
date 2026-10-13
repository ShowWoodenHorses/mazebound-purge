using System;
using System.Collections.Generic;
using System.IO;
using Maze.Core.Lighting;
using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// "Maze → Dev → Build Biome Themes": Snow and Forest themes from procedural, seamless textures (albedo + normal
    /// map from a height field). Floors and walls are own block meshes with tiling UVs (Synty blocks are mapped to a
    /// palette); a wall has two materials — sides and top. Everything except Floor and Wall is shared with
    /// CastleBlockTheme: a new theme starts as its copy (lighting, fog, footprints and awareness retuned once);
    /// later runs rebuild textures, materials and prefabs and keep the hand-tuned theme values and set contents
    /// (only variants of a category the set does not have yet are added — e.g. snowdrift floors).
    /// </summary>
    internal static class BiomeThemeBuilder
    {
        private const string ArtRoot = "Assets/_Project/Art/Biomes";
        private const string ThemesFolder = "Assets/_Project/Data/Themes";
        private const string SourceTheme = ThemesFolder + "/CastleBlockTheme.asset";
        private const string FogSource = "Assets/_Project/Art/Fog/Fog.mat";
        private const int FloorSize = 512;
        private const float WallHeight = 1.5f;

        private static readonly VisualCategory[] WallCategories =
        {
            VisualCategory.Straight, VisualCategory.Corner, VisualCategory.TJunction,
            VisualCategory.End, VisualCategory.Cross, VisualCategory.Isolated,
        };

        [MenuItem("Maze/Dev/Build Biome Themes")]
        public static void Build()
        {
            var floorMesh = SaveMesh(BuildFloorMesh(), $"{ArtRoot}/Meshes/BiomeFloor.asset");
            var wallMesh = SaveMesh(BuildWallMesh(), $"{ArtRoot}/Meshes/BiomeWall.asset");

            BuildBiome(Snow(), floorMesh, wallMesh);
            BuildBiome(Forest(), floorMesh, wallMesh);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Biome themes built: {ThemesFolder}/SnowTheme.asset, {ThemesFolder}/ForestTheme.asset.");
        }

        // ------------------------------------------------------------------ Biome descriptions

        private sealed class Surface
        {
            public string Name;
            public int Width = FloorSize, Height = FloorSize;
            public Func<float, float, Sample> Paint;
            public float Bump = 4f;
            public float Gloss;
        }

        private struct Sample
        {
            public Color Color;
            public float Height;
            public Sample(Color color, float height) { Color = color; Height = height; }
        }

        private sealed class Biome
        {
            public string Name;
            public (Surface Surface, int Weight)[] Floors;
            public (Surface Sides, int Weight)[] Walls;
            public Surface WallTop;

            /// <summary>
            /// Floors of snowdrift cells (category Snowdrift): a low mound on the floor block, each its own shape, with
            /// the material of the first floor — the drift whiteness and its soft edge are drawn by Maze/Geometry over
            /// any floor (SnowdriftMask), so a drift texture of its own would show a seam at the cell border.
            /// </summary>
            public (int Seed, int Weight)[] Drifts;

            public Action<VisualTheme, Material> Tune;
        }

        private static Biome Snow() => new Biome
        {
            Name = "Snow",
            Floors = new[]
            {
                (new Surface { Name = "floor_snow", Paint = (u, v) => SnowGround(u, v, 11, drift: false), Bump = 2.5f }, 60),
                (new Surface { Name = "floor_snow_drift", Paint = (u, v) => SnowGround(u, v, 12, drift: true), Bump = 3f }, 25),
                (new Surface { Name = "floor_ice", Paint = Ice, Bump = 2f, Gloss = 0.55f }, 15),
            },
            Walls = new[]
            {
                (new Surface { Name = "wall_snow_stone", Height = 768, Paint = (u, v) => SnowStoneWall(u, v, 21, rows: 6, dark: false), Bump = 6f }, 70),
                (new Surface { Name = "wall_snow_blocks", Height = 768, Paint = (u, v) => SnowStoneWall(u, v, 22, rows: 4, dark: true), Bump = 6f }, 30),
            },
            WallTop = new Surface { Name = "wall_snow_top", Paint = (u, v) => SnowGround(u, v, 13, drift: false, bright: true), Bump = 2f },
            Drifts = new[] { (61, 1), (62, 1), (63, 1) },
            Tune = (theme, fog) =>
            {
                var l = theme.Lighting;
                l.Ambient = Hex("3A4A66");
                l.MoonColor = Hex("C8D8FF");
                l.MoonIntensity = 0.45f;
                l.LanternColor = Hex("F2F0FF");
                l.LanternIntensity = 0.85f;
                l.LightPresets = new List<LightPreset>
                {
                    new LightPreset { Id = "torch", Weight = 2, Color = Hex("FFA060"), Radius = 4f, Intensity = 1.2f, Flicker = 0.6f },
                    new LightPreset { Id = "ice_lamp", Weight = 2, Color = Hex("8FD0FF"), Radius = 5f, Intensity = 1f, Flicker = 0.08f },
                };
                TintFog(fog, Hex("8C9DB8"), Hex("E6EEFF"));
            },
        };

        private static Biome Forest() => new Biome
        {
            Name = "Forest",
            Floors = new[]
            {
                (new Surface { Name = "floor_forest_soil", Paint = (u, v) => ForestSoil(u, v, 31), Bump = 4f }, 45),
                (new Surface { Name = "floor_forest_grass", Paint = (u, v) => Grass(u, v, 32, flowers: true), Bump = 3f }, 35),
                (new Surface { Name = "floor_forest_flagstones", Paint = (u, v) => MossFlagstones(u, v, 33), Bump = 6f }, 20),
            },
            Walls = new[]
            {
                (new Surface { Name = "wall_forest_mossy_stone", Height = 768, Paint = (u, v) => MossyStoneWall(u, v, 41), Bump = 6f }, 65),
                (new Surface { Name = "wall_forest_logs", Height = 768, Paint = (u, v) => LogWall(u, v, 42), Bump = 7f }, 35),
            },
            WallTop = new Surface { Name = "wall_forest_top", Paint = (u, v) => Hedge(u, v, 43), Bump = 8f },
            Tune = (theme, fog) =>
            {
                var l = theme.Lighting;
                l.Ambient = Hex("2E3A2A");
                l.MoonColor = Hex("CFE3B8");
                l.MoonIntensity = 0.35f;
                l.LanternColor = Hex("FFDCA0");
                l.LanternIntensity = 0.9f;
                l.LightPresets = new List<LightPreset>
                {
                    new LightPreset { Id = "torch", Weight = 3, Color = Hex("FF9A4A"), Radius = 4f, Intensity = 1.3f, Flicker = 0.6f },
                    new LightPreset { Id = "firefly_lamp", Weight = 1, Color = Hex("C8FF7A"), Radius = 4.5f, Intensity = 0.9f, Flicker = 0.15f },
                };
                TintFog(fog, Hex("2F3D2C"), Hex("8FA67E"));
            },
        };

        // ------------------------------------------------------------------ Build pipeline

        private static void BuildBiome(Biome biome, Mesh floorMesh, Mesh wallMesh)
        {
            var folder = $"{ArtRoot}/{biome.Name}";
            var floorSet = Set($"{ThemesFolder}/{biome.Name}FloorSet.asset", VisualKind.Floor, out var newFloorSet);
            var wallSet = Set($"{ThemesFolder}/{biome.Name}WallSet.asset", VisualKind.Wall, out var newWallSet);
            var floorAdds = new SetAdditions(floorSet, newFloorSet);
            var wallAdds = new SetAdditions(wallSet, newWallSet);

            Material baseFloor = null;
            foreach (var (surface, weight) in biome.Floors)
            {
                var material = MaterialFor(folder, surface);
                baseFloor ??= material;
                var prefab = Prefab(folder, surface.Name, floorMesh, material);
                floorAdds.Add(Variant(surface.Name, weight, VisualCategory.General, prefab));
            }
            if (newFloorSet)
                floorSet.DefaultVariantId = biome.Floors[0].Surface.Name;

            if (biome.Drifts != null)
            {
                for (var i = 0; i < biome.Drifts.Length; i++)
                {
                    var (seed, weight) = biome.Drifts[i];
                    var name = $"floor_snowdrift_{i + 1:00}";
                    var mesh = SaveMesh(BuildDriftMesh(seed), $"{ArtRoot}/Meshes/BiomeDrift_{i + 1:00}.asset");
                    floorAdds.Add(Variant(name, weight, VisualCategory.Snowdrift, Prefab(folder, name, mesh, baseFloor)));
                }
            }

            var top = MaterialFor(folder, biome.WallTop);
            foreach (var (sides, weight) in biome.Walls)
            {
                var prefab = Prefab(folder, sides.Name, wallMesh, MaterialFor(folder, sides), top);
                foreach (var category in WallCategories)
                    wallAdds.Add(Variant($"{sides.Name}_{category.ToString().ToLowerInvariant()}", weight, category, prefab));
            }
            if (newWallSet)
                wallSet.DefaultVariantId = $"{biome.Walls[0].Sides.Name}_straight";
            EditorUtility.SetDirty(floorSet);
            EditorUtility.SetDirty(wallSet);

            var themePath = $"{ThemesFolder}/{biome.Name}Theme.asset";
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(themePath);
            if (theme == null)
            {
                // First build: a copy of the castle theme (shared doors, keys, items, zombies, decor, lights),
                // then this biome's mood. Later builds keep whatever was tuned on the theme by hand.
                AssetDatabase.CopyAsset(SourceTheme, themePath);
                theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(themePath);
                var fogPath = $"{folder}/Materials/Fog_{biome.Name}.mat";
                AssetDatabase.CopyAsset(FogSource, fogPath);
                var fog = AssetDatabase.LoadAssetAtPath<Material>(fogPath);
                theme.FogMaterial = fog;
                biome.Tune(theme, fog);
                var serialized = new SerializedObject(theme);
                serialized.FindProperty("_id").stringValue = biome.Name.ToLowerInvariant();
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            theme.SetSet(VisualKind.Floor, floorSet);
            theme.SetSet(VisualKind.Wall, wallSet);
            EditorUtility.SetDirty(theme);
        }

        private static VisualSet Set(string path, VisualKind kind, out bool created)
        {
            var set = AssetDatabase.LoadAssetAtPath<VisualSet>(path);
            created = set == null;
            if (created)
            {
                set = ScriptableObject.CreateInstance<VisualSet>();
                AssetDatabase.CreateAsset(set, path);
            }
            set.Kind = kind;
            return set;
        }

        /// <summary>
        /// Puts built variants into a set without undoing hand edits: a new set gets them all; an existing one keeps
        /// its list, weights and default (variants removed by hand stay removed) and only gets the variants of a
        /// category it has none of yet (e.g. drifts added in a later version of the builder).
        /// </summary>
        private sealed class SetAdditions
        {
            private readonly VisualSet _set;
            private readonly HashSet<VisualCategory> _known = new HashSet<VisualCategory>();

            public SetAdditions(VisualSet set, bool created)
            {
                _set = set;
                if (!created)
                    foreach (var variant in set.Variants)
                        _known.Add(variant.Category);
            }

            public void Add(VisualVariant variant)
            {
                if (_known.Contains(variant.Category) || _set.FindVariant(variant.Id) != null)
                    return;
                _set.MutableVariants.Add(variant);
            }
        }

        private static VisualVariant Variant(string id, int weight, VisualCategory category, GameObject prefab) =>
            new VisualVariant(id, weight, category, null,
                new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab))));

        private static GameObject Prefab(string folder, string name, Mesh mesh, params Material[] materials)
        {
            EnsureFolder($"{folder}/Prefabs");
            var root = new GameObject(name);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterials = materials;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/Prefabs/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static Material MaterialFor(string folder, Surface surface)
        {
            var (albedo, normal) = BakeTextures(folder, surface);
            EnsureFolder($"{folder}/Materials");
            var path = $"{folder}/Materials/{surface.Name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Maze/Geometry"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Maze/Geometry");
            material.SetColor("_Color", Color.white);
            material.SetTexture("_MainTex", albedo);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_Glossiness", surface.Gloss);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static (Texture2D Albedo, Texture2D Normal) BakeTextures(string folder, Surface surface)
        {
            EnsureFolder($"{folder}/Textures");
            int w = surface.Width, h = surface.Height;
            var colors = new Color[w * h];
            var heights = new float[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var sample = surface.Paint((x + 0.5f) / w, (y + 0.5f) / h);
                colors[y * w + x] = sample.Color;
                heights[y * w + x] = sample.Height;
            }

            var albedoPath = $"{folder}/Textures/{surface.Name}.png";
            var normalPath = $"{folder}/Textures/{surface.Name}_n.png";
            WritePng(albedoPath, w, h, colors);
            WritePng(normalPath, w, h, NormalsFromHeight(heights, w, h, surface.Bump, wrapV: surface.Height == surface.Width));
            Import(albedoPath, normalMap: false);
            Import(normalPath, normalMap: true);
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath), AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        }

        private static Color[] NormalsFromHeight(float[] height, int w, int h, float strength, bool wrapV)
        {
            var result = new Color[w * h];
            float H(int x, int y)
            {
                x = (x % w + w) % w;
                y = wrapV ? (y % h + h) % h : Mathf.Clamp(y, 0, h - 1);
                return height[y * w + x];
            }

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = (H(x + 1, y) - H(x - 1, y)) * strength;
                var dy = (H(x, y + 1) - H(x, y - 1)) * strength;
                var n = new Vector3(-dx, -dy, 1f).normalized;
                result[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            return result;
        }

        private static void WritePng(string path, int w, int h, Color[] pixels)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, linear: true);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void Import(string path, bool normalMap)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normalMap;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ Meshes

        /// <summary>1 × 0.1 × 1 slab, top at y = 0; top face UV = the cell (0..1), sides a thin strip.</summary>
        private static Mesh BuildFloorMesh()
        {
            var builder = new BoxBuilder();
            builder.Box(new Vector3(-0.5f, -0.1f, -0.5f), new Vector3(0.5f, 0f, 0.5f), sideV: 0.1f, separateTop: false);
            return builder.ToMesh("BiomeFloor", 1);
        }

        private const float DriftHeight = 0.2f;
        private const int DriftGrid = 10;

        /// <summary>
        /// Floor block with a snow mound: the top is a <see cref="DriftGrid"/>² height field up to <see cref="DriftHeight"/>,
        /// lumpy and off-centre by <paramref name="seed"/>, exactly flat at the cell border (no seams with neighbours).
        /// </summary>
        private static Mesh BuildDriftMesh(int seed)
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            var cx = (Hash(seed, 1, 7) - 0.5f) * 0.2f;
            var cz = (Hash(seed, 2, 7) - 0.5f) * 0.2f;
            var stretch = 0.8f + Hash(seed, 3, 7) * 0.4f;

            for (var j = 0; j <= DriftGrid; j++)
            for (var i = 0; i <= DriftGrid; i++)
            {
                var u = (float)i / DriftGrid;
                var v = (float)j / DriftGrid;
                var x = u - 0.5f;
                var z = v - 0.5f;
                var dx = (x - cx) * stretch;
                var dz = (z - cz) / stretch;
                var lumps = (Fbm(u, v, 3, seed, 3) - 0.5f) * 0.18f;
                var dome = Smooth(0.48f, 0.08f, Mathf.Sqrt(dx * dx + dz * dz) + lumps);
                var border = Smooth(0f, 0.18f, Mathf.Min(0.5f - Mathf.Abs(x), 0.5f - Mathf.Abs(z)));
                vertices.Add(new Vector3(x, DriftHeight * dome * border, z));
                uv.Add(new Vector2(u, v));
            }

            for (var j = 0; j < DriftGrid; j++)
            for (var i = 0; i < DriftGrid; i++)
            {
                var a = j * (DriftGrid + 1) + i;
                var b = a + DriftGrid + 1;
                triangles.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
            }

            var mesh = new Mesh { name = $"BiomeDrift_{seed}" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>1 × 1.5 × 1 block: sub-mesh 0 — the four sides (u wraps around the block, v = height / 1.5), 1 — the top.</summary>
        private static Mesh BuildWallMesh()
        {
            var builder = new BoxBuilder();
            builder.Box(new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, WallHeight, 0.5f), sideV: 1f, separateTop: true);
            return builder.ToMesh("BiomeWall", 2);
        }

        private sealed class BoxBuilder
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int>[] _triangles = { new List<int>(), new List<int>() };

            public void Box(Vector3 min, Vector3 max, float sideV, bool separateTop)
            {
                var top = separateTop ? 1 : 0;
                // Top (+Y): uv = cell coordinates.
                Quad(top,
                    new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z),
                    Vector3.up, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));

                // Sides, counter-clockwise seen from outside, u continues around the block (S, E, N, W).
                Side(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), Vector3.back, 0f, min.y, max.y, sideV);
                Side(new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), Vector3.right, 1f, min.y, max.y, sideV);
                Side(new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z), Vector3.forward, 2f, min.y, max.y, sideV);
                Side(new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z), Vector3.left, 3f, min.y, max.y, sideV);

                // Bottom (never seen, kept for a closed block).
                Quad(0,
                    new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                    Vector3.down, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }

            private void Side(Vector3 a, Vector3 b, Vector3 normal, float u0, float y0, float y1, float sideV)
            {
                Quad(0,
                    a, new Vector3(a.x, y1, a.z), new Vector3(b.x, y1, b.z), b,
                    normal, new Vector2(u0, 0), new Vector2(u0, sideV), new Vector2(u0 + 1, sideV), new Vector2(u0 + 1, 0));
            }

            private void Quad(int subMesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
                Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                var i = _vertices.Count;
                _vertices.AddRange(new[] { a, b, c, d });
                _normals.AddRange(new[] { normal, normal, normal, normal });
                _uv.AddRange(new[] { ua, ub, uc, ud });
                _triangles[subMesh].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            public Mesh ToMesh(string name, int subMeshes)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uv);
                mesh.subMeshCount = subMeshes;
                for (var s = 0; s < subMeshes; s++)
                    mesh.SetTriangles(_triangles[s], s);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            existing.Clear();
            existing.SetVertices(mesh.vertices);
            existing.SetNormals(mesh.normals);
            existing.SetUVs(0, mesh.uv);
            existing.subMeshCount = mesh.subMeshCount;
            for (var s = 0; s < mesh.subMeshCount; s++)
                existing.SetTriangles(mesh.GetTriangles(s), s);
            existing.RecalculateTangents();
            existing.RecalculateBounds();
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ------------------------------------------------------------------ Painters (u, v in 0..1, tile in u; floors tile in v too)

        private static Sample SnowGround(float u, float v, int seed, bool drift, bool bright = false)
        {
            var n = drift
                ? Fbm(u, v + (Fbm(u, v, 4, seed + 7, 2) - 0.5f) * 0.08f, 2, seed, 4, periodY: 9) // wind ripples along u
                : Fbm(u, v, 3, seed, 5);
            // Floors are trodden, blue-grey snow; wall tops fresh and white — the maze must read from above.
            var shadow = bright ? Hex("D8E3F0") : Hex("8E9FB6");
            var light = bright ? Hex("FFFFFF") : Hex("C3CFDE");
            var color = Color.Lerp(shadow, light, Smooth(0.25f, 0.8f, n));
            if (Hash(Mathf.FloorToInt(u * 512), Mathf.FloorToInt(v * 512), seed + 99) < 0.006f && n > 0.45f)
                color = Color.Lerp(color, Color.white, 0.8f); // sparkles
            return new Sample(color, n);
        }

        private static Sample Ice(float u, float v)
        {
            var cloud = Fbm(u, v, 3, 51, 4);
            var (f1, f2, _) = Voronoi(u, v, 5, 52);
            var edge = f2 - f1;
            var crack = 1f - Smooth(0.0f, 0.035f, edge);
            var fine = Fbm(u, v, 16, 53, 3);
            var color = Color.Lerp(Hex("8FB7D1"), Hex("C9E3F0"), cloud);
            color = Color.Lerp(color, Hex("EAF6FF"), crack * 0.8f);
            color = Color.Lerp(color, Hex("7FA6C2"), Smooth(0.6f, 0.9f, fine) * 0.25f);
            return new Sample(color, 0.5f + cloud * 0.1f - crack * 0.4f);
        }

        private static Sample SnowStoneWall(float u, float v, int seed, int rows, bool dark)
        {
            var (brick, mortar, id) = Bricks(u, v, rows, rows == 6 ? 2 : 1, seed, 0.012f);
            var tint = Hash(id, 0, seed);
            var grain = Fbm(u, v * 1.5f, 6, seed + 1, 4);
            var stone = Color.Lerp(dark ? Hex("4A5466") : Hex("6B7486"), dark ? Hex("697489") : Hex("8E97A8"), grain * 0.7f + tint * 0.3f);
            var color = Color.Lerp(Hex("2E3440"), stone, brick);

            // Snow resting on the top of every brick and a cap along the upper edge of the wall.
            var rowV = v * rows % 1f;
            var ledge = brick * Smooth(0.86f, 0.97f, rowV + (Fbm(u, v, 12, seed + 3, 3) - 0.5f) * 0.12f);
            var cap = Smooth(0.9f, 0.96f, v + (Fbm(u, 0f, 8, seed + 4, 3) - 0.5f) * 0.08f);
            var frost = Smooth(0.25f, 0f, v) * 0.35f;
            var snow = Mathf.Clamp01(Mathf.Max(ledge * 0.85f, cap) + frost);
            color = Color.Lerp(color, Hex("EEF3FA"), snow);
            return new Sample(color, brick * (0.6f + grain * 0.4f) + snow * 0.3f);
        }

        private static Sample ForestSoil(float u, float v, int seed)
        {
            var n = Fbm(u, v, 4, seed, 5);
            var color = Color.Lerp(Hex("3B2A1A"), Hex("6A4A2C"), n);
            var height = n * 0.5f;

            var (f1, _, cell) = Voronoi(u, v, 12, seed + 1);
            if (Hash(cell, 1, seed) < 0.18f && f1 < 0.022f) // pebbles
            {
                color = Color.Lerp(Hex("6E6A62"), Hex("9A958A"), Hash(cell, 2, seed));
                height = 0.8f - f1 * 10f;
            }

            var leaf = Leaves(u, v, seed + 5, 70, out var leafColor);
            if (leaf > 0f)
            {
                color = Color.Lerp(color, leafColor, leaf);
                height = Mathf.Max(height, 0.55f * leaf);
            }
            return new Sample(color, height);
        }

        private static Sample Grass(float u, float v, int seed, bool flowers)
        {
            var n = Fbm(u, v, 4, seed, 5);
            var blades = Fbm(u, v, 96, seed + 1, 2, periodY: 24); // fine noise stretched along v reads as short grass
            var color = Color.Lerp(Hex("5A7430"), Hex("8DA24A"), n * 0.6f + blades * 0.4f);
            color = Color.Lerp(color, Hex("A9B85E"), Smooth(0.7f, 0.95f, blades) * 0.5f);
            var height = n * 0.3f + blades * 0.7f;

            if (flowers)
            {
                var (f1, _, cell) = Voronoi(u, v, 14, seed + 2);
                var pick = Hash(cell, 3, seed);
                if (pick < 0.08f && f1 < 0.012f)
                {
                    color = pick < 0.04f ? Hex("F2E46A") : Hex("F4F1EA");
                    height = 1f;
                }
            }
            return new Sample(color, height);
        }

        /// <summary>Hedge seen from above: dark leaf clumps, each a lit dome, darker gaps between them.</summary>
        private static Sample Hedge(float u, float v, int seed)
        {
            var (f1, f2, cell) = Voronoi(u, v, 9, seed);
            var dome = Smooth(0.07f, 0.0f, f1);
            var gap = Smooth(0.0f, 0.03f, f2 - f1);
            var leaves = Fbm(u, v, 40, seed + 1, 2);
            var color = Color.Lerp(Hex("12230C"), Hex("2F5418"), dome * gap);
            color = Color.Lerp(color, Hex("4E7A26"), Smooth(0.55f, 0.85f, leaves) * dome * 0.8f);
            color = Color.Lerp(color, Hex("22401A"), Hash(cell, 0, seed) * 0.3f);
            return new Sample(color, dome * gap * 0.8f + leaves * 0.2f);
        }

        private static Sample MossFlagstones(float u, float v, int seed)
        {
            var (f1, f2, cell) = Voronoi(u, v, 4, seed);
            var gap = Smooth(0.02f, 0.06f, f2 - f1);
            var grain = Fbm(u, v, 8, seed + 1, 4);
            var stone = Color.Lerp(Hex("5D5A50"), Hex("8A8576"), grain * 0.6f + Hash(cell, 0, seed) * 0.4f);
            var moss = Fbm(u, v, 6, seed + 2, 4);
            var mossColor = Color.Lerp(Hex("2C4A18"), Hex("5F8A2A"), moss);
            var mossAmount = Mathf.Max(1f - gap, Smooth(0.62f, 0.75f, moss) * 0.8f);
            return new Sample(Color.Lerp(stone, mossColor, mossAmount), gap * (0.6f + grain * 0.4f) + mossAmount * 0.15f);
        }

        private static Sample MossyStoneWall(float u, float v, int seed)
        {
            var (brick, _, id) = Bricks(u, v, 6, 2, seed, 0.012f);
            var grain = Fbm(u, v * 1.5f, 6, seed + 1, 4);
            var stone = Color.Lerp(Hex("55594D"), Hex("80836F"), grain * 0.6f + Hash(id, 0, seed) * 0.4f);
            var color = Color.Lerp(Hex("1F2618"), stone, brick);

            var moss = Fbm(u, v * 1.5f, 5, seed + 2, 4);
            var low = Smooth(0.35f, 0.05f, v + (moss - 0.5f) * 0.2f);
            var cap = Smooth(0.9f, 0.97f, v + (moss - 0.5f) * 0.1f);
            var patches = Smooth(0.68f, 0.8f, moss) * 0.7f;
            var amount = Mathf.Clamp01(Mathf.Max(Mathf.Max(low, cap), patches) + (1f - brick) * 0.5f);
            color = Color.Lerp(color, Color.Lerp(Hex("2D4C17"), Hex("6A9530"), moss), amount);
            return new Sample(color, brick * (0.6f + grain * 0.4f) + amount * 0.2f);
        }

        private static Sample LogWall(float u, float v, int seed)
        {
            const int logs = 4;
            var x = u * logs;
            var index = Mathf.FloorToInt(x);
            var across = x - index;
            var round = Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow(across * 2f - 1f, 2f)));
            var bark = Fbm(u, v, 24, seed + index, 3, periodY: 4);
            var knots = Fbm(u, v, 6, seed + 5, 3);
            var wood = Color.Lerp(Hex("3A2615"), Hex("6E4A2A"), bark * 0.7f + Hash(index, 0, seed) * 0.3f);
            var color = Color.Lerp(Hex("1A120A"), wood, Smooth(0.0f, 0.35f, round));
            color *= 0.75f + 0.25f * round;

            var moss = Fbm(u, v, 5, seed + 2, 4);
            var amount = Mathf.Max(Smooth(0.25f, 0.0f, v + (moss - 0.5f) * 0.2f), Smooth(0.92f, 0.98f, v + (moss - 0.5f) * 0.1f));
            amount = Mathf.Max(amount, Smooth(0.72f, 0.82f, knots) * 0.6f);
            color = Color.Lerp(color, Color.Lerp(Hex("2D4C17"), Hex("6A9530"), moss), amount);
            color.a = 1f;
            return new Sample(color, round * 0.8f + bark * 0.2f + amount * 0.1f);
        }

        // ------------------------------------------------------------------ Pattern helpers (all periodic in u and v)

        /// <summary>Running-bond bricks: brick mask (1 inside, 0 mortar), mortar, brick id.</summary>
        private static (float Brick, float Mortar, int Id) Bricks(float u, float v, int rows, int perRow, int seed, float mortar)
        {
            var row = Mathf.FloorToInt(v * rows);
            var offset = (row & 1) * 0.5f / perRow + (Hash(row, 7, seed) - 0.5f) * 0.1f / perRow;
            var x = (u + offset) * perRow;
            var column = Mathf.FloorToInt(x);
            var fx = x - column;
            var fy = v * rows - row;
            var mx = mortar * perRow * 2f;
            var my = mortar * rows * 1.33f;
            var edge = Mathf.Min(Mathf.Min(fx, 1f - fx) / mx, Mathf.Min(fy, 1f - fy) / my);
            var brick = Smooth(0.6f, 1.4f, edge);
            var id = (column % perRow + perRow) % perRow + row * 31;
            return (brick, 1f - brick, id);
        }

        /// <summary>Scattered leaves (ellipses) on a jittered grid; returns coverage and the leaf colour.</summary>
        private static float Leaves(float u, float v, int seed, int count, out Color color)
        {
            color = Color.clear;
            var grid = Mathf.CeilToInt(Mathf.Sqrt(count));
            var best = 0f;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var cx = Mathf.FloorToInt(u * grid) + dx;
                var cy = Mathf.FloorToInt(v * grid) + dy;
                var wx = (cx % grid + grid) % grid;
                var wy = (cy % grid + grid) % grid;
                if (Hash(wx, wy, seed) > 0.55f)
                    continue;
                var px = (cx + Hash(wx, wy, seed + 1)) / grid;
                var py = (cy + Hash(wx, wy, seed + 2)) / grid;
                var angle = Hash(wx, wy, seed + 3) * Mathf.PI;
                var ddx = u - px;
                var ddy = v - py;
                var ax = ddx * Mathf.Cos(angle) + ddy * Mathf.Sin(angle);
                var ay = -ddx * Mathf.Sin(angle) + ddy * Mathf.Cos(angle);
                var size = 0.022f + Hash(wx, wy, seed + 4) * 0.014f;
                var d = Mathf.Sqrt(ax * ax / (size * size) + ay * ay / (size * size * 0.25f));
                var cover = Smooth(1f, 0.8f, d);
                if (cover > best)
                {
                    best = cover;
                    var pick = Hash(wx, wy, seed + 5);
                    color = pick < 0.4f ? Hex("B5651D") : pick < 0.7f ? Hex("D4A02A") : pick < 0.85f ? Hex("7A2E12") : Hex("4F6B22");
                }
            }
            return best;
        }

        /// <summary>Periodic Voronoi with <paramref name="cells"/> cells per side: nearest, second distance, nearest cell id.</summary>
        private static (float F1, float F2, int Cell) Voronoi(float u, float v, int cells, int seed)
        {
            var x = u * cells;
            var y = v * cells;
            var ix = Mathf.FloorToInt(x);
            var iy = Mathf.FloorToInt(y);
            float f1 = 9f, f2 = 9f;
            var cell = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var cx = ix + dx;
                var cy = iy + dy;
                var wx = (cx % cells + cells) % cells;
                var wy = (cy % cells + cells) % cells;
                var px = cx + Hash(wx, wy, seed) * 0.8f + 0.1f;
                var py = cy + Hash(wx, wy, seed + 1) * 0.8f + 0.1f;
                var d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y)) / cells;
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                    cell = wy * cells + wx;
                }
                else if (d < f2)
                    f2 = d;
            }
            return (f1, f2, cell);
        }

        /// <summary>Periodic fractal value noise, 0..1; <paramref name="period"/> lattice cells per unit for the first octave.</summary>
        private static float Fbm(float u, float v, int period, int seed, int octaves, int periodY = -1)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            var px = period;
            var py = periodY > 0 ? periodY : period;
            for (var o = 0; o < octaves; o++)
            {
                sum += amplitude * ValueNoise(u * px, v * py, px, py, seed + o * 101);
                total += amplitude;
                amplitude *= 0.5f;
                px *= 2;
                py *= 2;
            }
            return sum / total;
        }

        private static float ValueNoise(float x, float y, int px, int py, int seed)
        {
            var ix = Mathf.FloorToInt(x);
            var iy = Mathf.FloorToInt(y);
            var fx = x - ix;
            var fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float Corner(int cx, int cy) => Hash((cx % px + px) % px, (cy % py + py) % py, seed);
            var a = Mathf.Lerp(Corner(ix, iy), Corner(ix + 1, iy), fx);
            var b = Mathf.Lerp(Corner(ix, iy + 1), Corner(ix + 1, iy + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private static float Smooth(float from, float to, float x)
        {
            var t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var color);
            return color;
        }

        private static void TintFog(Material fog, Color fogColor, Color wispColor)
        {
            if (fog == null) return;
            foreach (var (name, color) in new[] { ("_Color", fogColor), ("_WispColor", wispColor) })
                if (fog.HasProperty(name))
                    fog.SetColor(name, color);
            EditorUtility.SetDirty(fog);
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
