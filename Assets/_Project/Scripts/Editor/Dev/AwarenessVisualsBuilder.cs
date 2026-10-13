using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Awareness Visuals: the materials of zombie vision zones (<c>Maze/Particle</c>, alpha, no
    /// texture — vertex colour × the tint set per zone; drawn after the fog) and of noise waves (<c>Maze/Ring</c>), and
    /// their assignment to the placeholder theme's <see cref="ThemeAwareness"/>. Materials are overwritten (GUIDs
    /// kept); the tuned colours and timings of the theme stay.
    /// </summary>
    internal static class AwarenessVisualsBuilder
    {
        private const string Folder = "Assets/_Project/Art/Effects";
        private const string ZonePath = Folder + "/fx_vision_zone.mat";
        private const string NoisePath = Folder + "/fx_noise_ring.mat";
        private const string ThemePath = "Assets/_Project/Data/Themes/PlaceholderTheme.asset";

        [MenuItem("Maze/Dev/Build Awareness Visuals")]
        public static void Build()
        {
            var particle = Shader.Find("Maze/Particle");
            var ring = Shader.Find("Maze/Ring");
            if (particle == null || ring == null)
            {
                Debug.LogError("[Maze] Shaders 'Maze/Particle' and 'Maze/Ring' are required.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Effects");

            var zone = LoadOrCreate(ZonePath, particle);
            zone.SetTexture("_MainTex", null);
            zone.SetColor("_Color", Color.white);
            zone.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            zone.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            zone.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            zone.renderQueue = -1; // the shader's: after the fog
            EditorUtility.SetDirty(zone);

            var noise = LoadOrCreate(NoisePath, ring);
            noise.SetFloat("_Fill", 0.12f); // a faint fill: the line shows the reach
            EditorUtility.SetDirty(noise);

            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            if (theme != null)
            {
                theme.Awareness.ZoneMaterial = zone;
                theme.Awareness.NoiseMaterial = noise;
                EditorUtility.SetDirty(theme);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Awareness visuals built in {Folder}" + (theme != null ? ", assigned to PlaceholderTheme." : "."));
        }

        private static Material LoadOrCreate(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            return material;
        }
    }
}
