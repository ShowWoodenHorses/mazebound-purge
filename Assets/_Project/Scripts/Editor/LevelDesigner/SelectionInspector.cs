using System;
using System.Collections.Generic;
using System.Linq;
using Maze.Core.Authoring;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>
    /// Inspector for the selected cell and object: visual overrides (ТЗ §27, §94), ids, key/door links,
    /// definitions, patrols and map fragment regions.
    /// </summary>
    internal sealed class SelectionInspector
    {
        private static readonly string[] RotationNames = { "0°", "90°", "180°", "270°" };
        private static readonly int[] RotationValues = { 0, 1, 2, 3 };
        private const int DefaultIceHits = 3;

        private readonly LevelDesignerState _state;
        private readonly EditToolController _tools;
        private readonly Action<string> _notify;

        public SelectionInspector(LevelDesignerState state, EditToolController tools, Action<string> notify)
        {
            _state = state;
            _tools = tools;
            _notify = notify;
        }

        private LevelData Level => _state.Level;

        public void OnGUI()
        {
            if (!_state.SelectedCell.HasValue || !Level.Geometry.IsInside(_state.SelectedCell.Value))
                return;

            var cell = _state.SelectedCell.Value;
            EditorGUILayout.LabelField($"Cell {cell}  ·  {Level.Geometry.GetCell(cell)}", EditorStyles.boldLabel);
            DrawCellVisual(cell);

            var entities = Level.AllEntities().Where(e => e.Position == cell).ToList();
            if (entities.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                foreach (var entity in entities)
                    if (GUILayout.Toggle(entity.Id == _state.SelectedEntityId, entity.Id, EditorStyles.miniButton))
                        _state.SelectedEntityId = entity.Id;
                EditorGUILayout.EndHorizontal();
            }

            var selected = _state.SelectedEntity;
            if (selected != null)
                DrawEntity(selected);
        }

        // ------------------------------------------------------------ Cell

        private void DrawCellVisual(GridPosition cell)
        {
            var theme = Level.VisualTheme;
            if (theme == null)
                return;

            // Floor layer exists in every cell (also under walls); wall cells also have a wall layer.
            foreach (var layer in CellLayers.All)
            {
                if (!CellLayers.Exists(Level.Geometry.GetCell(cell), layer))
                    continue;

                var set = theme.GetSet(CellLayers.Kind(layer));
                var current = VisualResolver.ResolveCell(Level, cell, layer, out var source);
                EditorGUILayout.LabelField(layer + " visual", current.IsEmpty ? "no visual" : $"{current.VariantId} r{current.Rotation} [{source}]");

                VisualOverrideField(layer + " override", set, current, source == VisualSource.Override,
                    choice => Apply("Set Visual Override", () => LevelEditing.SetCellOverride(Level, cell, layer, choice)),
                    () => Apply("Clear Visual Override", () => LevelEditing.ClearCellOverride(Level, cell, layer)));
            }

            DrawDecor(cell, theme.GetSet(VisualKind.Decor));
        }

        /// <summary>"(automatic)" = auto placement decides; "(none)" = never decor here; a variant = placed by hand.</summary>
        private void DrawDecor(GridPosition cell, VisualSet set)
        {
            if (set == null || !CellLayers.Exists(Level.Geometry.GetCell(cell), CellLayer.Decor))
                return;

            var current = VisualResolver.ResolveDecor(Level, cell, out var source);
            EditorGUILayout.LabelField("Decor", current.IsEmpty
                ? (source == VisualSource.Override ? "none [Override]" : "none")
                : $"{current.VariantId} r{current.Rotation} [{source}]");

            var ids = set.Variants.Select(v => v.Id).ToList();
            var options = new[] { "(automatic)", "(none)" }.Concat(ids.Select(id => id ?? "<no id>")).ToArray();
            var isOverride = source == VisualSource.Override;
            var index = !isOverride ? 0 : current.IsEmpty ? 1 : Mathf.Max(0, ids.IndexOf(current.VariantId)) + 2;
            var selected = EditorGUILayout.Popup("Decor override", index, options);
            if (selected != index)
            {
                if (selected == 0)
                    Apply("Clear Decor", () => LevelEditing.ClearCellOverride(Level, cell, CellLayer.Decor));
                else
                    Apply("Set Decor", () => LevelEditing.SetCellOverride(Level, cell, CellLayer.Decor,
                        selected == 1 ? VisualChoice.None : new VisualChoice(ids[selected - 2], current.Rotation)));
                return;
            }

            if (current.IsEmpty)
                return;

            var hasPlacement = Level.VisualData.TryGetDecorPlacement(cell, out _);
            if (isOverride && !hasPlacement)
            {
                var rotation = EditorGUILayout.IntPopup("Decor rotation", current.Rotation, RotationNames, RotationValues);
                if (rotation != current.Rotation)
                    Apply("Rotate Decor", () => LevelEditing.SetDecorRotation(Level, cell, rotation));
            }

            // Placement (also dragged in the Scene view on the preview). Editing it makes auto decor manual.
            VisualResolver.ResolveDecorPose(Level, cell, current, out var offset, out var yaw);
            EditorGUI.BeginChangeCheck();
            var shift = EditorGUILayout.Vector2Field("Decor shift (X, Z)", new Vector2(offset.x, offset.z));
            var height = EditorGUILayout.Slider("Decor height", offset.y, DecorPlacement.MinHeight, DecorPlacement.MaxHeight);
            var turn = EditorGUILayout.Slider("Decor turn", yaw, 0f, 359.9f);
            if (EditorGUI.EndChangeCheck())
                Apply("Place Decor", () => LevelEditing.SetDecorPlacement(Level, cell, shift, height, turn));

            var nudge = NudgeButtons();
            if (nudge != Vector2.zero)
                Apply("Place Decor", () => LevelEditing.SetDecorPlacement(Level, cell, shift + nudge, height, turn));

            if (hasPlacement && GUILayout.Button("Reset decor placement"))
                Apply("Reset Decor Placement", () => LevelEditing.ResetDecorPlacement(Level, cell));
        }

        // ------------------------------------------------------------ Object

        private void DrawEntity(LevelEntityData entity)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(entity.GetType().Name.Replace("Data", string.Empty), EditorStyles.boldLabel);

            var newId = EditorGUILayout.DelayedTextField("Id", entity.Id);
            if (newId != entity.Id)
            {
                if (LevelEditing.IsFreeId(Level, newId))
                    Apply("Rename " + entity.Id, () => LevelEditing.Rename(Level, entity, newId));
                else
                    _notify($"Id '{newId}' is empty or already used.");
            }

            switch (entity)
            {
                case DoorData door: DrawDoor(door); break;
                case KeyData key: DrawKey(key); break;
                case ZombieSpawnData zombie: DrawZombie(zombie); break;
                case WeaponPickupData weapon: DrawWeapon(weapon); break;
                case MapFragmentData fragment: DrawFragment(fragment); break;
                case PlayerStartData start: DrawStart(start); break;
            }

            if (VisualKinds.TryGetForEntity(entity, out var kind, out _) && Level.VisualTheme != null)
            {
                var current = VisualResolver.ResolveObject(Level, entity, out var source);
                EditorGUILayout.LabelField("Visual", current.IsEmpty ? "no visual" : $"{current.VariantId} r{current.Rotation} [{source}]");
                VisualOverrideField("Override", Level.VisualTheme.GetSet(kind), current, source == VisualSource.Override,
                    choice => Apply("Set Visual Override", () => LevelEditing.SetObjectOverride(Level, entity, choice)),
                    () => Apply("Clear Visual Override", () => LevelEditing.ClearObjectOverride(Level, entity)));

                if (VisualKinds.IsPlaceable(entity))
                    DrawPlacement(entity, current);
            }

            EditorGUILayout.Space(2f);
            if (GUILayout.Button("Delete " + entity.Id))
            {
                Apply("Delete " + entity.Id, () => LevelEditing.Remove(Level, entity));
                _state.SelectedEntityId = null;
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDoor(DoorData door)
        {
            var keys = Level.Keys.ToList();
            var options = new[] { "(no key)" }.Concat(keys.Select(k =>
            {
                var owner = Level.Doors.FirstOrDefault(d => d != door && d.KeyId == k.Id);
                return owner == null ? k.Id : $"{k.Id}  (now opens {owner.Id})";
            })).ToArray();

            var index = door.RequiresKey ? keys.FindIndex(k => k.Id == door.KeyId) + 1 : 0;
            var selected = EditorGUILayout.Popup("Key", index, options);
            if (selected != index)
                Apply("Link Key", () => LevelEditing.LinkKey(Level, door, selected == 0 ? null : keys[selected - 1]));

            if (door.RequiresKey && keys.All(k => k.Id != door.KeyId))
                EditorGUILayout.HelpBox($"Key '{door.KeyId}' does not exist.", MessageType.Error);

            if (GUILayout.Button("Place a new key for this door"))
            {
                _tools.Tool = EditTool.Key;
                _tools.PendingKeyDoorId = door.Id;
                _notify("Click a floor cell to place the key.");
            }

            var open = EditorGUILayout.Toggle("Initially Open", door.IsInitiallyOpen);
            if (open != door.IsInitiallyOpen)
                Apply("Toggle Door Open", () => LevelEditing.SetDoorInitiallyOpen(door, open));

            var frozen = EditorGUILayout.Toggle(new GUIContent("Frozen",
                "The ice must be broken with melee hits before the door can be opened (starts closed)."), door.IsFrozen);
            if (frozen != door.IsFrozen)
                Apply("Toggle Frozen Door", () => LevelEditing.SetDoorIce(door, frozen ? DefaultIceHits : 0));
            if (door.IsFrozen)
            {
                var hits = EditorGUILayout.IntSlider("Ice Hits", door.IceHits, 1, 10);
                if (hits != door.IceHits)
                    Apply("Set Ice Hits", () => LevelEditing.SetDoorIce(door, hits));
            }
        }

        private void DrawKey(KeyData key)
        {
            var door = Level.Doors.FirstOrDefault(d => d.KeyId == key.Id);
            EditorGUILayout.LabelField("Opens", door != null ? door.Id : "no door (link it from a door)");
        }

        private void DrawZombie(ZombieSpawnData zombie)
        {
            EditorGUI.BeginChangeCheck();
            var definition = (ZombieDefinition)EditorGUILayout.ObjectField("Definition", zombie.Definition, typeof(ZombieDefinition), false);
            var facing = (Direction)EditorGUILayout.EnumPopup("Facing", zombie.Facing);
            if (EditorGUI.EndChangeCheck())
                Apply("Change Zombie", () => LevelEditing.SetZombie(Level, zombie, definition, facing));

            var patrol = Level.Patrols.FirstOrDefault(p => zombie.HasPatrol && p.Id == zombie.PatrolId);
            EditorGUILayout.LabelField("Patrol", patrol == null ? "none (idles at spawn)" : $"{patrol.Id}: {patrol.Points.Count} point(s)");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Edit Patrol"))
            {
                _tools.Tool = EditTool.Patrol;
                _notify("Click cells to add patrol points. Right-click removes the last one.");
            }

            using (new EditorGUI.DisabledScope(patrol == null))
            {
                if (GUILayout.Button("Clear Patrol"))
                    Apply("Clear Patrol", () => LevelEditing.ClearPatrol(Level, zombie));
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawWeapon(WeaponPickupData weapon)
        {
            var definition = (WeaponDefinition)EditorGUILayout.ObjectField("Definition", weapon.Definition, typeof(WeaponDefinition), false);
            if (definition != weapon.Definition)
                Apply("Change Weapon", () => LevelEditing.SetWeaponDefinition(Level, weapon, definition));
        }

        private void DrawStart(PlayerStartData start)
        {
            var index = IndexOf(Level.PlayerStarts, start);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button($"Test from Start #{index}") &&
                    !LevelPlayLauncher.Launch(Level, index, out var problem))
                    _notify(problem);
        }

        private static int IndexOf(IReadOnlyList<PlayerStartData> starts, PlayerStartData start)
        {
            for (var i = 0; i < starts.Count; i++)
                if (starts[i] == start) return i;
            return -1;
        }

        private void DrawFragment(MapFragmentData fragment)
        {
            var r = fragment.Region;
            EditorGUI.BeginChangeCheck();
            var rect = EditorGUILayout.RectIntField("Region", new RectInt(r.X, r.Y, r.Width, r.Height));
            if (EditorGUI.EndChangeCheck())
                Apply("Change Region", () => LevelEditing.SetFragmentRegion(fragment, new GridRect(rect.x, rect.y, rect.width, rect.height)));

            if (GUILayout.Button("Draw Region"))
            {
                _tools.Tool = EditTool.FragmentRegion;
                _notify("Drag a rectangle on the grid.");
            }
        }

        /// <summary>
        /// Shift / height / turn of a pickup's view inside its cell (also dragged in the Scene view on the preview);
        /// "Put on decor" lifts it onto the top of the cell's decor (e.g. a table).
        /// </summary>
        private void DrawPlacement(LevelEntityData entity, VisualChoice current)
        {
            VisualResolver.ResolveObjectPose(Level, entity, current, out var offset, out var yaw);
            var hasPlacement = Level.VisualData.TryGetObjectPlacement(entity.Id, out _);
            EditorGUI.BeginChangeCheck();
            var shift = EditorGUILayout.Vector2Field("Shift (X, Z)", new Vector2(offset.x, offset.z));
            var height = EditorGUILayout.Slider("Height", offset.y, DecorPlacement.MinHeight, DecorPlacement.MaxHeight);
            var turn = EditorGUILayout.Slider("Turn", yaw, 0f, 359.9f);
            if (EditorGUI.EndChangeCheck())
                Apply("Place " + entity.Id, () => LevelEditing.SetObjectPlacement(Level, entity, shift, height, turn));

            var nudge = NudgeButtons();
            if (nudge != Vector2.zero)
                Apply("Place " + entity.Id, () => LevelEditing.SetObjectPlacement(Level, entity, shift + nudge, height, turn));

            var onDecor = LevelEditing.IsOnDecor(Level, entity);
            if (onDecor)
                EditorGUILayout.HelpBox("On decor: shift and turn move the decor too (and the other way round).", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            var decor = VisualResolver.ResolveDecor(Level, entity.Position);
            using (new EditorGUI.DisabledScope(decor.IsEmpty))
            {
                if (GUILayout.Button(new GUIContent("Put on decor",
                        "Lift onto the top of this cell's decor (e.g. a table); from now on it moves and turns with the decor.")))
                    PutOnDecor(entity, decor, hasPlacement ? shift : (Vector2?)null, turn);
            }

            if (onDecor && GUILayout.Button(new GUIContent("Detach", "Keep the pose, stop moving with the decor.")))
                Apply("Detach " + entity.Id, () => LevelEditing.DetachFromDecor(Level, entity));

            using (new EditorGUI.DisabledScope(!hasPlacement))
            {
                if (GUILayout.Button("Reset placement"))
                    Apply("Reset Placement", () => LevelEditing.ResetObjectPlacement(Level, entity));
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Height = top of the decor model (measured) + the decor's own height; over its centre unless already shifted.</summary>
        private void PutOnDecor(LevelEntityData entity, VisualChoice decor, Vector2? shift, float turn)
        {
            var set = Level.VisualTheme.GetSet(VisualKind.Decor);
            var variant = set != null ? set.FindVariant(decor.VariantId) : null;
            if (variant == null)
                return;

            DecorHeights.Refresh(set);
            VisualResolver.ResolveDecorPose(Level, entity.Position, decor, out var decorOffset, out _);
            var top = variant.Height + decorOffset.y;
            var where = shift ?? new Vector2(decorOffset.x, decorOffset.z);
            Apply("Put " + entity.Id + " on Decor", () => LevelEditing.PutOnDecor(Level, entity, where, top, turn));
        }

        /// <summary>The most common fine adjustment: a quarter of a cell along X (East) or Z (North).</summary>
        private const float NudgeStep = 0.25f;

        /// <summary>Row "X −0.25 | X +0.25 | Z −0.25 | Z +0.25"; returns the shift clicked (zero if none).</summary>
        private static Vector2 NudgeButtons()
        {
            var nudge = Vector2.zero;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("X -0.25")) nudge = new Vector2(-NudgeStep, 0f);
            if (GUILayout.Button("X +0.25")) nudge = new Vector2(NudgeStep, 0f);
            if (GUILayout.Button("Z -0.25")) nudge = new Vector2(0f, -NudgeStep);
            if (GUILayout.Button("Z +0.25")) nudge = new Vector2(0f, NudgeStep);
            EditorGUILayout.EndHorizontal();
            return nudge;
        }

        // ------------------------------------------------------------ Helpers

        /// <summary>"(automatic)" = no override; picking a variant creates a manual override that has priority.</summary>
        private static void VisualOverrideField(string label, VisualSet set, VisualChoice current, bool hasOverride,
            Action<VisualChoice> setOverride, Action clearOverride)
        {
            if (set == null)
                return;

            var ids = set.Variants.Select(v => v.Id).ToList();
            var options = new[] { "(automatic)" }.Concat(ids.Select(id => id ?? "<no id>")).ToArray();
            var index = hasOverride ? ids.IndexOf(current.VariantId) + 1 : 0;

            var selected = EditorGUILayout.Popup(label, Mathf.Max(0, index), options);
            if (selected != Mathf.Max(0, index))
            {
                if (selected == 0)
                    clearOverride();
                else
                    setOverride(new VisualChoice(ids[selected - 1], current.Rotation));
                return;
            }

            if (!hasOverride)
                return;

            var rotation = EditorGUILayout.IntPopup("Rotation", current.Rotation, RotationNames, RotationValues);
            if (rotation != current.Rotation)
                setOverride(new VisualChoice(current.VariantId, rotation));
        }

        private void Apply(string undoName, Action change)
        {
            LevelEditorCommands.Modify(Level, undoName, change);
            _state.Revalidate();
        }
    }
}
