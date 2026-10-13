using System.Collections.Generic;
using System.Linq;
using Maze.Core.Authoring;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>
    /// Scene view handles that place something inside its cell: the cell's frame, a square to shift (≤ half a cell),
    /// an arrow to lift, a circle to turn (Ctrl snaps by 15°) and a label with the values.
    /// </summary>
    internal static class PlacementHandles
    {
        public static readonly Color AutoColor = new Color(0.75f, 0.68f, 0.55f);
        public static readonly Color ManualColor = new Color(0.35f, 0.9f, 0.4f);

        /// <summary>A small cube marker; true when clicked.</summary>
        public static bool Marker(Vector3 position, Color color)
        {
            var size = HandleUtility.GetHandleSize(position) * 0.06f;
            Handles.color = color;
            return Handles.Button(position + Vector3.up * size, Quaternion.identity, size, size * 1.4f, Handles.CubeHandleCap);
        }

        /// <summary>True when changed; <paramref name="offset"/> is relative to the cell centre (y = height).</summary>
        public static bool Edit(GridPosition cell, string label, Color color, ref Vector3 offset, ref float yaw)
        {
            var center = cell.ToWorld();
            var position = center + offset;
            var half = DecorPlacement.MaxOffset;
            Handles.color = new Color(color.r, color.g, color.b, 0.5f);
            Handles.DrawAAPolyLine(2f,
                center + new Vector3(-half, 0.02f, -half), center + new Vector3(half, 0.02f, -half),
                center + new Vector3(half, 0.02f, half), center + new Vector3(-half, 0.02f, half),
                center + new Vector3(-half, 0.02f, -half));
            if (offset.y > 0.01f)
                Handles.DrawDottedLine(position, new Vector3(position.x, 0f, position.z), 2f);

            var size = HandleUtility.GetHandleSize(position);
            Handles.Label(position + Vector3.up * size * 0.45f,
                $"{label}  {offset.x:0.00}, {offset.z:0.00}  h {offset.y:0.00}  {yaw:0}°", EditorStyles.miniLabel);

            Handles.color = color;
            EditorGUI.BeginChangeCheck();
            var moved = Handles.Slider2D(position, Vector3.up, Vector3.right, Vector3.forward, size * 0.12f,
                Handles.RectangleHandleCap, Vector2.zero);
            if (EditorGUI.EndChangeCheck())
            {
                offset = new Vector3(moved.x - center.x, offset.y, moved.z - center.z);
                return true;
            }

            Handles.color = Handles.yAxisColor;
            EditorGUI.BeginChangeCheck();
            var lifted = Handles.Slider(position, Vector3.up, size * 0.6f, Handles.ArrowHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                offset = new Vector3(offset.x, lifted.y - center.y, offset.z);
                return true;
            }

            Handles.color = new Color(0.3f, 0.6f, 1f);
            EditorGUI.BeginChangeCheck();
            var turned = Handles.Disc(Quaternion.Euler(0f, yaw, 0f), position, Vector3.up, size * 0.45f, false, 15f);
            if (EditorGUI.EndChangeCheck())
            {
                yaw = turned.eulerAngles.y;
                return true;
            }

            return false;
        }

        /// <summary>Preview instances under <paramref name="root"/> by name (empty without a preview).</summary>
        public static void Collect(Transform root, Dictionary<string, Transform> result)
        {
            result.Clear();
            if (root == null)
                return;
            foreach (Transform child in root)
                result[child.name] = child;
        }
    }

    /// <summary>
    /// Decor of the edited level in the Scene view (over the preview). Every decor has a small cube marker (beige: auto,
    /// green: manual); a click selects its cell. The decor of the selected cell — unless an object of that cell is
    /// selected — gets <see cref="PlacementHandles"/>. Changes go to the level at once (Undo) and move the preview
    /// instance, no Build Preview needed; auto placed decor becomes manual.
    /// </summary>
    internal static class DecorSceneHandles
    {
        private static readonly Dictionary<string, Transform> Instances = new Dictionary<string, Transform>();

        public static bool Draw(LevelDesignerState state)
        {
            var level = state.Level;
            var geometry = level.Geometry;
            PlacementHandles.Collect(LevelPreviewBuilder.FindDecorRoot(), Instances);
            var editCell = state.SelectedEntity != null && VisualKinds.IsPlaceable(state.SelectedEntity)
                ? (GridPosition?)null
                : state.SelectedCell;
            var changed = false;

            for (var i = 0; i < geometry.CellCount; i++)
            {
                var cell = geometry.ToPosition(i);
                var decor = VisualResolver.ResolveDecor(level, cell, out var source);
                if (decor.IsEmpty)
                    continue;

                VisualResolver.ResolveDecorPose(level, cell, decor, out var offset, out var yaw);

                // Keeps the preview in step with the level (also after Undo / Redo).
                if (Event.current.type == EventType.Repaint &&
                    Instances.TryGetValue(LevelPreviewBuilder.DecorName(cell), out var instance))
                    LevelPreviewBuilder.PlaceDecor(level, cell, decor, instance);

                if (editCell == cell)
                {
                    if (!PlacementHandles.Edit(cell, decor.VariantId, PlacementHandles.ManualColor, ref offset, ref yaw))
                        continue;

                    LevelEditorCommands.Modify(level, "Place Decor",
                        () => LevelEditing.SetDecorPlacement(level, cell, new Vector2(offset.x, offset.z), offset.y, yaw));
                    if (Instances.TryGetValue(LevelPreviewBuilder.DecorName(cell), out var moved))
                        LevelPreviewBuilder.PlaceDecor(level, cell, VisualResolver.ResolveDecor(level, cell), moved);
                    changed = true;
                }
                else if (PlacementHandles.Marker(cell.ToWorld() + offset,
                             source == VisualSource.Override ? PlacementHandles.ManualColor : PlacementHandles.AutoColor))
                {
                    state.SelectedCell = cell;
                    state.SelectedEntityId = null;
                    changed = true;
                }
            }

            return changed;
        }
    }

    /// <summary>
    /// Pickups (keys, medkits, weapons, map fragments) in the Scene view: a marker each (green once placed by hand);
    /// a click selects the object; the selected one gets <see cref="PlacementHandles"/> — e.g. lift a key onto a table.
    /// Purely visual: the pickup still belongs to its cell.
    /// </summary>
    internal static class ObjectSceneHandles
    {
        private static readonly Color PickupColor = new Color(0.4f, 0.8f, 1f);
        private static readonly Dictionary<string, Transform> Instances = new Dictionary<string, Transform>();

        public static bool Draw(LevelDesignerState state)
        {
            var level = state.Level;
            PlacementHandles.Collect(LevelPreviewBuilder.FindObjectsRoot(), Instances);
            var changed = false;

            foreach (var entity in level.AllEntities().Where(VisualKinds.IsPlaceable).ToList())
            {
                var choice = VisualResolver.ResolveObject(level, entity);
                VisualResolver.ResolveObjectPose(level, entity, choice, out var offset, out var yaw);
                Instances.TryGetValue(entity.Id, out var instance);
                if (Event.current.type == EventType.Repaint)
                    LevelPreviewBuilder.PlaceObject(level, entity, instance);

                if (state.SelectedEntityId == entity.Id)
                {
                    if (!PlacementHandles.Edit(entity.Position, entity.Id, PickupColor, ref offset, ref yaw))
                        continue;

                    LevelEditorCommands.Modify(level, "Place " + entity.Id,
                        () => LevelEditing.SetObjectPlacement(level, entity, new Vector2(offset.x, offset.z), offset.y, yaw));
                    LevelPreviewBuilder.PlaceObject(level, entity, instance);
                    changed = true;
                }
                else if (PlacementHandles.Marker(entity.Position.ToWorld() + offset,
                             level.VisualData.TryGetObjectPlacement(entity.Id, out _) ? PlacementHandles.ManualColor : PickupColor))
                {
                    state.Select(entity);
                    changed = true;
                }
            }

            return changed;
        }
    }

    /// <summary>
    /// The Level Designer's tools in the Scene view: light sources, decor and pickups. The level is revalidated when
    /// a drag ends (not on every frame of it).
    /// </summary>
    internal static class LevelSceneHandles
    {
        private static bool _pendingRevalidate;

        public static bool Draw(LevelDesignerState state)
        {
            if (!state.HasUsableLevel)
                return false;

            var lights = LightSceneHandles.Draw(state);
            var placed = state.Level.VisualTheme != null &&
                         (DecorSceneHandles.Draw(state) | ObjectSceneHandles.Draw(state));
            _pendingRevalidate |= placed;

            if (_pendingRevalidate && GUIUtility.hotControl == 0)
            {
                _pendingRevalidate = false;
                state.Revalidate();
                return true;
            }

            return lights || placed;
        }
    }
}
