using Maze.Presentation.Visual;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Global shader keywords survive leaving play mode. If a level was not disposed cleanly, the visibility
    /// keyword would stay on and the editor preview (Level Designer) would hide geometry; the snowdrift keyword would
    /// keep the game's (destroyed) mask. Reset them in edit mode; the preview gets its drifts back on Build Preview.
    /// </summary>
    [InitializeOnLoad]
    internal static class GeometryShaderEditorGuard
    {
        static GeometryShaderEditorGuard()
        {
            Reset();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    Reset();
            };
        }

        private static void Reset()
        {
            Shader.DisableKeyword(GeometryShader.VisibilityKeyword);
            Shader.DisableKeyword(GeometryShader.SnowdriftKeyword);
        }
    }
}
