using System;
using System.Linq;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
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
                EditorGUILayout.HelpBox("The theme has no Decor set (Maze > Dev > Build Decor).", MessageType.Warning);
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

        private static string Label(EditTool tool)
        {
            switch (tool)
            {
                case EditTool.PlayerStart: return "Start";
                case EditTool.MapFragment: return "Fragment";
                case EditTool.FragmentRegion: return "Region";
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
                case EditTool.MapFragment: return "Click a floor cell to place a map fragment, then drag the region it reveals.";
                case EditTool.FragmentRegion: return "Drag a rectangle to set the region of the selected map fragment. Regions must not overlap.";
                case EditTool.Patrol: return "Select a zombie, then click cells to add patrol points (loop A→B→…→A). Right-click removes the last point.";
                case EditTool.Decor: return "Click or drag on floor cells to paint decor (Paint: a variant, None = no decor, Automatic = back to auto placement). Right-click or right-drag removes decor. Decor is only visual.";
                case EditTool.Snowdrift: return "Click or drag on floor cells to make snowdrifts: the player and zombies move slower there, zombie routes go around them when the detour is short. Right-click or right-drag removes them. The cell gets a drift floor of the theme (if it has one) and no auto decor.";
                case EditTool.Erase: return "Click or drag to clear cells: objects, lights, snowdrifts and decor are removed, walls and doors become floor.";
                default: return "Click a floor cell to place.";
            }
        }
    }
}
