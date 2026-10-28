using System;
using System.Linq;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Vehicles;
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
                case EditTool.Barrel:
                    tools.BarrelKind = (BarrelKind)EditorGUILayout.EnumPopup("Kind", tools.BarrelKind);
                    break;
                case EditTool.Chest:
                    tools.ChestKind = (ChestKind)EditorGUILayout.EnumPopup("Kind", tools.ChestKind);
                    break;
                case EditTool.Vehicle:
                    tools.VehicleDefinition = (VehicleDefinition)EditorGUILayout.ObjectField("Definition", tools.VehicleDefinition,
                        typeof(VehicleDefinition), false);
                    tools.VehicleHeading = HeadingPopup(tools.VehicleHeading);
                    break;
                case EditTool.Spawn:
                    EditorGUILayout.LabelField("Spawn points", state.Level.MonsterSpawns.Count.ToString());
                    break;
                case EditTool.Arena:
                    ArenaOptions(state, tools);
                    break;
                case EditTool.LockArea:
                    EditorGUILayout.LabelField("Locked areas", state.Level.Marks.LockedAreas.Count.ToString());
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

        private static void ArenaOptions(LevelDesignerState state, EditToolController tools)
        {
            tools.ArenaRing = Mathf.Clamp(EditorGUILayout.IntField("Ring", tools.ArenaRing), 0, 8);
            var arena = state.Level.Arena;
            if (arena.IsEmpty)
            {
                EditorGUILayout.LabelField("Arena", "none");
                return;
            }

            var wide = arena.Entries.Count(e => e.Width >= Purge.Core.Validation.LevelValidator.MinArenaEntryWidth);
            EditorGUILayout.LabelField("Arena", $"{arena.Rect}, ring {arena.Ring}, entries {wide} of {arena.Entries.Count}");
            if (GUILayout.Button("Remove Arena"))
            {
                LevelEditorCommands.Modify(state.Level, "Remove Arena", () => LevelEditing.RemoveArena(state.Level));
                state.Revalidate();
            }
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
                case EditTool.LockArea: return "Lock Area";
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
                case EditTool.Ground: return "Click or drag on floor cells to lay bare ground (trails): look and footsteps only; the cell gets a ground floor of the theme with a soft edge and no auto decor. Right-click or right-drag removes it.";
                case EditTool.Grass: return "Click or drag on floor cells to grow grass: fire spreads over it; the cell gets grass decor of the theme (tall grass). Right-click or right-drag removes it.";
                case EditTool.Obstacle: return "Click or drag on floor cells to place the obstacle (Data/Obstacles: what it blocks — movement, bullets, sight — is set in its asset). Right-click or right-drag removes obstacles. The cell shows a Decor variant of the theme bound to the obstacle.";
                case EditTool.Spawn: return "Click a free floor cell to add a spawn point of the waves' monsters (M); right-click removes it. Keep them out of the start's view (more than 10 cells) with a 3x3 path to the arena.";
                case EditTool.Arena: return "Drag a rectangle: the open cells of the boss arena (A). The ring of the given width around it is drawn by hand with Wall; its entries are the gaps in the ring (at least 3, each 3 cells wide). The arena and ring are marked as edited, so Generate keeps them.";
                case EditTool.Barrel: return "Click a floor cell to place an explosive barrel of the chosen kind (B). Gameplay comes in stage 7в.";
                case EditTool.Chest: return "Click a floor cell to place a chest of the chosen kind (C). Gameplay comes in stage 7в.";
                case EditTool.Vehicle: return "Click a floor cell to place the chosen vehicle's centre (V); its body (tinted) must fit on free floor. Leave it room ahead to speed up (8+ cells).";
                case EditTool.LockArea: return "Drag a rectangle: Generate leaves its cells as they are (blue tint). Right-click inside an area removes it.";
                case EditTool.Erase: return "Click or drag to clear cells: objects, lights, snowdrifts, grass, obstacles and decor are removed, walls, void and doors become floor.";
                default: return "Click a floor cell to place.";
            }
        }

        private static readonly string[] HeadingNames = { "N 0°", "NE 45°", "E 90°", "SE 135°", "S 180°", "SW 225°", "W 270°", "NW 315°" };

        /// <summary>A vehicle's heading: one of the 8 directions, degrees clockwise from North.</summary>
        internal static float HeadingPopup(float heading)
        {
            var index = Mathf.RoundToInt(Mathf.Repeat(heading, 360f) / 45f) % 8;
            return EditorGUILayout.Popup("Heading", index, HeadingNames) * 45f;
        }
    }
}
