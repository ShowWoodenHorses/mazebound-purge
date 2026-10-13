using Maze.Core.Authoring;
using Maze.Core.Level;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>
    /// Light sources of the edited level in the Scene view (over the preview): a sphere at the light's point,
    /// dragged along the floor plane, and a faint circle of its radius. Moving makes the light hand-placed;
    /// it belongs to the cell its point is in (the validator reports a light that ends up off the floor).
    /// </summary>
    internal static class LightSceneHandles
    {
        /// <summary>Height of the handles above the floor (about torch height).</summary>
        private const float Height = 1.2f;

        public static bool Draw(LevelDesignerState state)
        {
            if (!state.HasUsableLevel || state.Level.Lights.Count == 0)
                return false;

            var level = state.Level;
            var moved = false;
            foreach (var light in level.Lights)
            {
                var point = light.Point;
                var position = new Vector3(point.x, Height, point.y);
                var color = light.Color;
                color.a = 1f;

                Handles.color = new Color(color.r, color.g, color.b, 0.35f);
                Handles.DrawWireDisc(new Vector3(point.x, 0.05f, point.y), Vector3.up, light.Radius);
                Handles.DrawLine(position, new Vector3(point.x, 0f, point.y));

                Handles.color = color;
                var size = HandleUtility.GetHandleSize(position) * 0.12f;
                EditorGUI.BeginChangeCheck();
                var dragged = Handles.Slider2D(position, Vector3.up, Vector3.right, Vector3.forward, size,
                    Handles.SphereHandleCap, Vector2.zero);
                if (EditorGUI.EndChangeCheck())
                {
                    var target = new Vector2(dragged.x, dragged.z);
                    LevelEditorCommands.Modify(level, "Move Light", () => LevelEditing.MoveLight(light, target));
                    var objects = LevelPreviewBuilder.FindObjectsRoot();
                    if (objects != null)
                        LevelPreviewBuilder.PlaceLight(level, light, objects.Find(LevelPreviewBuilder.LightName(light.Id)));
                    moved = true;
                }

                Handles.Label(position + Vector3.up * size * 2f, light.Id, EditorStyles.miniLabel);
            }

            if (moved)
                state.Revalidate();
            return moved;
        }
    }
}
