using System.IO;
using Maze.Core.Common;
using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Create Placeholder Fog: a tiling noise texture (baked once, so the shader stays cheap), a
    /// "Maze/Fog" material and its assignment to the placeholder theme. An existing material keeps its tuned values;
    /// only a missing noise texture is filled in.
    /// </summary>
    internal static class FogAssetsBuilder
    {
        private const string Folder = "Assets/_Project/Art/Fog";
        private const string NoisePath = Folder + "/FogNoise.png";
        private const string MaterialPath = Folder + "/Fog.mat";
        private const string ThemePath = "Assets/_Project/Data/Themes/PlaceholderTheme.asset";
        private const int Size = 128;

        [MenuItem("Maze/Dev/Create Placeholder Fog")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Fog");

            if (!File.Exists(NoisePath))
                CreateNoise();
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Maze/Fog");
                if (shader == null)
                {
                    Debug.LogError("[Maze] Shader 'Maze/Fog' not found.");
                    return;
                }

                material = new Material(shader) { name = "Fog" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            if (material.GetTexture("_NoiseTex") == null)
            {
                material.SetTexture("_NoiseTex", noise);
                EditorUtility.SetDirty(material);
            }

            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            if (theme != null && theme.FogMaterial != material)
            {
                theme.FogMaterial = material;
                EditorUtility.SetDirty(theme);
            }

            AssetDatabase.SaveAssets();
            Selection.activeObject = material;
            Debug.Log($"[Maze] Fog material at {MaterialPath}" + (theme != null ? ", assigned to PlaceholderTheme." : "."));
        }

        /// <summary>Tiling value noise, three octaves; the lattice wraps, so the texture repeats without seams.</summary>
        private static void CreateNoise()
        {
            var random = new DeterministicRandom(20261005);
            var octaves = new[] { (Period: 4, Weight: 0.55f), (Period: 8, Weight: 0.3f), (Period: 16, Weight: 0.15f) };
            var lattices = new float[octaves.Length][,];
            for (var o = 0; o < octaves.Length; o++)
            {
                var period = octaves[o].Period;
                lattices[o] = new float[period, period];
                for (var y = 0; y < period; y++)
                for (var x = 0; x < period; x++)
                    lattices[o][x, y] = (float)random.NextDouble();
            }

            var values = new float[Size * Size];
            float min = float.MaxValue, max = float.MinValue;
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var value = 0f;
                for (var o = 0; o < octaves.Length; o++)
                {
                    var period = octaves[o].Period;
                    var fx = x * period / (float)Size;
                    var fy = y * period / (float)Size;
                    int x0 = (int)fx, y0 = (int)fy;
                    float tx = Smooth(fx - x0), ty = Smooth(fy - y0);
                    var lattice = lattices[o];
                    var a = Mathf.Lerp(lattice[x0 % period, y0 % period], lattice[(x0 + 1) % period, y0 % period], tx);
                    var b = Mathf.Lerp(lattice[x0 % period, (y0 + 1) % period], lattice[(x0 + 1) % period, (y0 + 1) % period], tx);
                    value += Mathf.Lerp(a, b, ty) * octaves[o].Weight;
                }

                values[y * Size + x] = value;
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var v = (byte)Mathf.RoundToInt(Mathf.InverseLerp(min, max, values[i]) * 255f);
                pixels[i] = new Color32(v, v, v, 255);
            }

            texture.SetPixels32(pixels);
            File.WriteAllBytes(NoisePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(NoisePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(NoisePath);
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
