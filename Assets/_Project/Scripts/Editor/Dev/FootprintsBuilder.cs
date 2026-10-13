using System.IO;
using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Footprints: the print atlas (left half — the player's boot, right half — a zombie's bare
    /// foot; right feet, toes up, white with alpha — tinted by <see cref="FootprintKind.Color"/>), its material
    /// (<c>Maze/Particle</c>, alpha, queue right after opaque geometry and before the blob shadows, so the fog covers
    /// it) and the theme's <see cref="ThemeFootprints"/> materials: prints and dust (the combat effects' dust,
    /// Build Combat Effects). The atlas and material are overwritten (GUIDs kept); the tuned numbers of the theme stay.
    /// </summary>
    internal static class FootprintsBuilder
    {
        private const string Folder = "Assets/_Project/Art/Effects";
        private const string AtlasPath = Folder + "/fx_footprints.png";
        private const string MaterialPath = Folder + "/fx_footprints.mat";
        private const string DustPath = Folder + "/fx_dust.mat";
        private const string ThemePath = "Assets/_Project/Data/Themes/PlaceholderTheme.asset";

        /// <summary>Geometry 2000, blob shadows 2010.</summary>
        private const int RenderQueue = 2005;

        private const int CellSize = 128;

        [MenuItem("Maze/Dev/Build Footprints")]
        public static void Build()
        {
            var shader = Shader.Find("Maze/Particle");
            if (shader == null)
            {
                Debug.LogError("[Maze] Shader 'Maze/Particle' not found.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Effects");

            var atlas = SaveAtlas();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "fx_footprints" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.shader = shader;
            material.SetTexture("_MainTex", atlas);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            material.renderQueue = RenderQueue;
            EditorUtility.SetDirty(material);

            var dust = AssetDatabase.LoadAssetAtPath<Material>(DustPath);
            if (dust == null)
                Debug.LogWarning($"[Maze] {DustPath} not found (Maze → Dev → Build Combat Effects): no step dust.");

            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            if (theme != null)
            {
                theme.Footprints.PrintMaterial = material;
                theme.Footprints.DustMaterial = dust;
                EditorUtility.SetDirty(theme);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Footprints built in {Folder}" + (theme != null ? ", assigned to PlaceholderTheme." : "."));
        }

        private static Texture2D SaveAtlas()
        {
            var width = CellSize * 2;
            var texture = new Texture2D(width, CellSize, TextureFormat.RGBA32, false);
            var pixels = new Color[width * CellSize];
            for (var y = 0; y < CellSize; y++)
            for (var x = 0; x < width; x++)
            {
                var u = (x % CellSize + 0.5f) / CellSize;
                var v = (y + 0.5f) / CellSize;
                var alpha = x < CellSize ? Boot(u, v) : BareFoot(u, v);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(AtlasPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true; // small on screen: mipmaps keep the edges calm
            importer.sRGBTexture = true;
            importer.maxTextureSize = width;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        }

        // ---- Shapes: a right foot, toes up (+v), soft edges; alpha 0..1 ----

        /// <summary>Boot: sole and a separate heel, with tread grooves.</summary>
        private static float Boot(float u, float v)
        {
            var sole = Ellipse(u, v, 0.54f, 0.63f, 0.25f, 0.33f);
            var heel = Ellipse(u, v, 0.47f, 0.17f, 0.19f, 0.13f);
            var shape = Mathf.Max(sole, heel);
            var groove = Mathf.Repeat(v * 9f, 1f) < 0.28f ? 0.45f : 1f;
            return shape * groove;
        }

        /// <summary>Bare foot: a sole narrowing at the arch on the inner (left) side, and five toes.</summary>
        private static float BareFoot(float u, float v)
        {
            var sole = Ellipse(u, v, 0.52f, 0.42f, 0.24f, 0.34f);
            var arch = Ellipse(u, v, 0.24f, 0.45f, 0.13f, 0.2f);
            var shape = sole * (1f - arch);
            shape = Mathf.Max(shape, Circle(u, v, 0.36f, 0.86f, 0.085f));
            shape = Mathf.Max(shape, Circle(u, v, 0.5f, 0.89f, 0.062f));
            shape = Mathf.Max(shape, Circle(u, v, 0.61f, 0.87f, 0.055f));
            shape = Mathf.Max(shape, Circle(u, v, 0.7f, 0.82f, 0.05f));
            shape = Mathf.Max(shape, Circle(u, v, 0.77f, 0.75f, 0.045f));
            return shape;
        }

        private static float Ellipse(float u, float v, float cu, float cv, float ru, float rv)
        {
            var du = (u - cu) / ru;
            var dv = (v - cv) / rv;
            return Edge(Mathf.Sqrt(du * du + dv * dv), Mathf.Min(ru, rv));
        }

        private static float Circle(float u, float v, float cu, float cv, float r) => Ellipse(u, v, cu, cv, r, r);

        /// <summary>1 inside (normalized distance &lt; 1), soft over about two pixels at the edge.</summary>
        private static float Edge(float normalized, float radius)
        {
            var soft = 2f / (CellSize * Mathf.Max(radius, 0.01f));
            return Mathf.Clamp01((1f - normalized) / soft + 0.5f);
        }
    }
}
