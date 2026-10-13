using Maze.Core.Lighting;
using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Create Placeholder Lighting: the blob shadow material (shader Maze/BlobShadow, instancing on) and
    /// its assignment to the placeholder theme's lighting, plus light presets (torch, cold lamp) if the theme has none.
    /// An existing material and presets keep their tuned values.
    /// The rest of the lighting mood is tuned on the theme asset (Lighting).
    /// </summary>
    internal static class LightingAssetsBuilder
    {
        private const string Folder = "Assets/_Project/Art/Lighting";
        private const string BlobShadowPath = Folder + "/BlobShadow.mat";
        private const string ThemePath = "Assets/_Project/Data/Themes/PlaceholderTheme.asset";

        [MenuItem("Maze/Dev/Create Placeholder Lighting")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Lighting");

            var material = AssetDatabase.LoadAssetAtPath<Material>(BlobShadowPath);
            if (material == null)
            {
                var shader = Shader.Find("Maze/BlobShadow");
                if (shader == null)
                {
                    Debug.LogError("[Maze] Shader 'Maze/BlobShadow' not found.");
                    return;
                }

                material = new Material(shader) { name = "BlobShadow", enableInstancing = true };
                AssetDatabase.CreateAsset(material, BlobShadowPath);
            }

            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            if (theme != null && theme.Lighting.BlobShadowMaterial != material)
            {
                theme.Lighting.BlobShadowMaterial = material;
                EditorUtility.SetDirty(theme);
            }

            if (theme != null && theme.Lighting.LightPresets.Count == 0)
            {
                theme.Lighting.LightPresets.Add(new LightPreset
                {
                    Id = "torch", Weight = 3f, Color = new Color(1f, 0.6f, 0.28f), Radius = 4f, Intensity = 1.3f, Flicker = 0.6f,
                });
                theme.Lighting.LightPresets.Add(new LightPreset
                {
                    Id = "cold_lamp", Weight = 1f, Color = new Color(0.62f, 0.82f, 1f), Radius = 5f, Intensity = 0.9f, Flicker = 0.05f,
                });
                EditorUtility.SetDirty(theme);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Blob shadow material at {BlobShadowPath}" + (theme != null ? ", assigned to PlaceholderTheme." : "."));
        }
    }
}
