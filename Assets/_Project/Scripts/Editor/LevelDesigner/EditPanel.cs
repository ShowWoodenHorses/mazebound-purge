using System;
using System.Linq;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Visual;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.LevelDesigner
{
    /// <summary>Tool palette and per-tool options for manual editing (ТЗ §15).</summary>
    internal sealed class EditPanel
    {
        private static readonly EditTool[] Tools = (EditTool[])Enum.GetValues(typeof(EditTool));
        private static readonly string[] ToolNames = Tools.Select(Label).ToArray();

        public void OnGUI(LevelDesignerState state, EditToolController tools)
        {
            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);
            var index = Array.IndexOf(Tools, tools.Tool);
            var selected = GUILayout.SelectionGrid(index, ToolNames, 3);
            if (selected != index)
            {
                tools.Tool = Tools[selected];
                tools.PendingKeyDoorId = null;
            }

            EditorGUILayout.Space();
            switch (tools.Tool)
            {
                case EditTool.Zombie:
                    tools.ZombieDefinition = (ZombieDefinition)EditorGUILayout.ObjectField("Definition", tools.ZombieDefinition, typeof(ZombieDefinition), false);
                    tools.ZombieFacing = (Direction)EditorGUILayout.EnumPopup("Facing", tools.ZombieFacing);
                    break;
                case EditTool.Weapon:
                    tools.WeaponDefinition = (WeaponDefinition)EditorGUILayout.ObjectField("Definition", tools.WeaponDefinition, typeof(WeaponDefinition), false);
                    break;
                case EditTool.Decor:
                    DecorOptions(state, tools);
                    break;
                case EditTool.Obstacle:
                    tools.ObstacleDefinition = (ObstacleDefinition)EditorGUILayout.ObjectField("Obstacle", tools.ObstacleDefinition, typeof(ObstacleDefinition), false);
                    if (tools.ObstacleDefinition != null)
                        EditorGUILayout.LabelField("Blocks", Blocks(tools.ObstacleDefinition), EditorStyles.miniLabel);
                    break;
                case EditTool.Key when tools.PendingKeyDoorId != null:
                    EditorGUILayout.HelpBox($"The next key you place opens '{tools.PendingKeyDoorId}'.", MessageType.Info);
                    break;
            }

            EditorGUILayout.HelpBox(Help(tools.Tool), MessageType.None);
        }

        private static readonly string[] RotationNames = { "0°", "90°", "180°", "270°" };

        private static void DecorOptions(LevelDesignerState state, EditToolController tools)
        {
            tools.DecorMode = (DecorBrushMode)EditorGUILayout.EnumPopup("Paint", tools.DecorMode);
            if (tools.DecorMode != DecorBrushMode.Variant)
                return;

            var set = state.Level.VisualTheme != null ? state.Level.VisualTheme.GetSet(VisualKind.Decor) : null;
            if (set == null || set.Variants.Count == 0)
            {
                EditorGUILayout.HelpBox("The theme has no Decor set (Purge > Dev > Build Decor).", MessageType.Warning);
                return;
            }

            var limit = state.Level.Generation.MaxAutoDecorHeight;
            var variants = set.Variants.ToList();
            var names = variants.Select(v => $"{v.Id}  ({v.Height:0.00} m{(v.Height > limit ? ", by hand only" : string.Empty)})").ToArray();
            var index = Mathf.Max(0, variants.FindIndex(v => v.Id == tools.DecorVariantId));
            index = EditorGUILayout.Popup("Variant", index, names);
            tools.DecorVariantId = variants[index].Id;
            tools.DecorRotation = EditorGUILayout.Popup("Rotation", tools.DecorRotation, RotationNames);
        }

        private static string Blocks(ObstacleDefinition obstacle)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (obstacle.BlocksMovement) parts.Add("movement");
            if (obstacle.BlocksBullets) parts.Add("bullets");
            if (obstacle.BlocksSight) parts.Add("sight");
            if (obstacle.SpeedFactor < 1f) parts.Add($"slows ×{obstacle.SpeedFactor:0.##}");
            if (obstacle.Flammable) parts.Add("burns");
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        private static string Label(EditTool tool)
        {
            switch (tool)
            {
                case EditTool.PlayerStart: return "Start";
                default: return tool.ToString();
            }
        }

        private static string Help(EditTool tool)
        {
            switch (tool)
            {
                case EditTool.Select: return "Click a cell to select it and its object (click again to cycle objects). Drag an object to move it. Delete key removes the selected object.";
                case EditTool.Wall: return "Click or drag to paint walls.";
                case EditTool.Floor: return "Click or drag to paint floor. Painting over a door removes the door.";
                case EditTool.Door: return "Click to place a door cell. Then link a key in the inspector below.";
                case EditTool.Key: return "Click a floor cell to place a key. Link it to a door from the door's inspector (one key = one door).";
                case EditTool.Decor: return "Click or drag on floor cells to paint decor (Paint: a variant, None = no decor, Automatic = back to auto placement). Right-click or right-drag removes decor. Decor is only visual.";
                case EditTool.Snowdrift: return "Click or drag on floor cells to make snowdrifts: the player and zombies move slower there, zombie routes go around them when the detour is short. Right-click or right-drag removes them. The cell gets a drift floor of the theme (if it has one) and no auto decor.";
                case EditTool.Obstacle: return "Click or drag on floor cells to place the obstacle (Data/Obstacles: what it blocks — movement, bullets, sight — is set in its asset). Right-click or right-drag removes obstacles. The cell shows a Decor variant of the theme bound to the obstacle.";
                case EditTool.Erase: return "Click or drag to clear cells: objects, lights, snowdrifts, obstacles and decor are removed, walls, void and doors become floor.";
                default: return "Click a floor cell to place.";
            }
        }
    }
}
