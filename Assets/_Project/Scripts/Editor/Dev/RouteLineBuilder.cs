using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Route Line: the material of the route hint line (<c>Maze/RouteLine</c>; colours, widths and
    /// pulses are set per theme in <see cref="ThemeRoute"/> and passed from code) and its assignment to every
    /// <see cref="VisualTheme"/> that has none yet. The material is overwritten (GUID kept); tuned theme values stay.
    /// </summary>
    internal static class RouteLineBuilder
    {
        private const string Folder = "Assets/_Project/Art/Effects";
        private const string MaterialPath = Folder + "/fx_route_line.mat";

        [MenuItem("Maze/Dev/Build Route Line")]
        public static void Build()
        {
            var shader = Shader.Find("Maze/RouteLine");
            if (shader == null)
            {
                Debug.LogError("[Maze] Shader 'Maze/RouteLine' is required.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Effects");

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "fx_route_line" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.shader = shader;
            material.renderQueue = -1; // the shader's: after the fog and the vision zones
            EditorUtility.SetDirty(material);

            var assigned = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(VisualTheme)))
            {
                var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(AssetDatabase.GUIDToAssetPath(guid));
                if (theme == null || theme.Route.LineMaterial != null) continue;
                theme.Route.LineMaterial = material;
                EditorUtility.SetDirty(theme);
                assigned++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Route line material built: {MaterialPath}; assigned to {assigned} theme(s).");
        }
    }
}
