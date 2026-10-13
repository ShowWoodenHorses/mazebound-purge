using System;
using System.IO;
using Maze.Core.Visual;
using Maze.Presentation.Visual;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Weather: textures and materials (<c>Maze/Particle</c>, alpha) of the snowflake and the breath
    /// puff in <c>Art/Effects</c>, and the ice of frozen doors (<c>Art/Effects/IceOverlay.prefab</c>: chunky blocks on
    /// <c>Maze/Lit</c>, <see cref="FrozenDoorIce"/> pieces from the top down), assigned to <see cref="ThemeWeather"/> of the
    /// snow theme. The first run also turns the zombies' frost on; tuned numbers of the theme are kept on later runs.
    /// Assets are overwritten (GUIDs kept).
    /// </summary>
    internal static class WeatherBuilder
    {
        private const string Folder = "Assets/_Project/Art/Effects";
        private const string ThemePath = "Assets/_Project/Data/Themes/SnowTheme.asset";
        private const float DefaultZombieFrost = 0.55f;

        [MenuItem("Maze/Dev/Build Weather")]
        public static void Build()
        {
            if (Shader.Find("Maze/Particle") == null)
            {
                Debug.LogError("[Maze] Shader 'Maze/Particle' not found.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Effects");

            var snow = SaveMaterial("fx_snowflake", SaveTexture("fx_snowflake", 32, Snowflake));
            var breath = SaveMaterial("fx_breath", SaveTexture("fx_breath", 64, Puff));
            var ice = BuildIceOverlay();

            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            if (theme != null)
            {
                var weather = theme.Weather;
                weather.SnowMaterial = snow;
                weather.BreathMaterial = breath;
                weather.IceOverlay = new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ice)));
                if (weather.ZombieFrost <= 0f)
                    weather.ZombieFrost = DefaultZombieFrost;
                EditorUtility.SetDirty(theme);
            }
            else
            {
                Debug.LogWarning($"[Maze] {ThemePath} not found (Maze → Dev → Build Biome Themes): materials are not assigned.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Weather built in {Folder}" + (theme != null ? ", assigned to SnowTheme." : "."));
        }

        // ---- Ice of frozen doors ----

        private const float IceHeight = 1.4f;
        private const float IceThickness = 0.44f;

        /// <summary>
        /// 3 × 2 chunky blocks across the doorway (door rotation 0 = along X), slightly tilted and uneven; pieces fall
        /// off from the top row down. Built-in cube mesh, no colliders (no physics in the project).
        /// </summary>
        private static GameObject BuildIceOverlay()
        {
            var material = SaveIceMaterial();
            var root = new GameObject("IceOverlay");
            var overlay = root.AddComponent<FrozenDoorIce>();
            var pieces = new System.Collections.Generic.List<GameObject>();
            const int columns = 3, rows = 2;
            var random = new Maze.Core.Common.DeterministicRandom(17); // fixed shape
            for (var row = rows - 1; row >= 0; row--)
            for (var column = 0; column < columns; column++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.name = $"Piece_{row}_{column}";
                cube.transform.SetParent(root.transform, false);
                var width = 1.04f / columns;
                var height = IceHeight / rows;
                float Jitter(float amount) => ((float)random.NextDouble() * 2f - 1f) * amount;
                cube.transform.localPosition = new Vector3(-0.52f + width * (column + 0.5f) + Jitter(0.03f),
                    height * (row + 0.5f) + Jitter(0.03f), Jitter(0.04f));
                cube.transform.localRotation = Quaternion.Euler(Jitter(6f), Jitter(8f), Jitter(6f));
                cube.transform.localScale = new Vector3(width * (1.08f + Jitter(0.06f)), height * (1.06f + Jitter(0.08f)),
                    IceThickness * (1f + Jitter(0.12f)));
                var renderer = cube.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                pieces.Add(cube.gameObject);
            }

            overlay.Pieces = pieces.ToArray();
            var path = $"{Folder}/IceOverlay.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static Material SaveIceMaterial()
        {
            var path = $"{Folder}/ice_overlay.mat";
            var shader = Shader.Find("Maze/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "ice_overlay" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_Color", new Color(0.7f, 0.87f, 1f));
            material.SetFloat("_Glossiness", 0.85f);
            material.SetFloat("_Metallic", 0f);
            material.SetColor("_EmissionColor", new Color(0.06f, 0.1f, 0.14f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material SaveMaterial(string name, Texture texture)
        {
            var path = $"{Folder}/{name}.mat";
            var shader = Shader.Find("Maze/Particle");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D SaveTexture(string name, int size, Func<float, float, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha((x + 0.5f) / size, (y + 0.5f) / size)));
            texture.SetPixels(pixels);
            texture.Apply();

            var path = $"{Folder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.maxTextureSize = size;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---- Shapes (u, v in 0..1) → alpha ----

        /// <summary>Round flake: solid core, soft edge (small on screen — a crisp dot reads better than a crystal).</summary>
        private static float Snowflake(float u, float v)
        {
            var r = new Vector2(u * 2f - 1f, v * 2f - 1f).magnitude;
            return Mathf.Clamp01((1f - r) * 2.2f);
        }

        /// <summary>Breath cloud: a soft dot with a lumpy, uneven edge.</summary>
        private static float Puff(float u, float v)
        {
            var p = new Vector2(u * 2f - 1f, v * 2f - 1f);
            var lumps = Mathf.PerlinNoise(u * 4f + 3.1f, v * 4f + 7.7f) * 0.45f;
            var r = Mathf.Clamp01(p.magnitude + lumps - 0.2f);
            var a = 1f - r;
            return a * a;
        }
    }
}
