using System;
using System.Linq;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.LevelDesigner
{
    internal enum EditTool
    {
        Select,
        Wall,
        Floor,
        Door,
        PlayerStart,
        Key,
        Zombie,
        Weapon,
        Medkit,
        Decor,
        Snowdrift,
        Erase,
    }

    /// <summary>What the Decor brush paints.</summary>
    internal enum DecorBrushMode
    {
        /// <summary>The chosen variant (a manual override).</summary>
        Variant,

        /// <summary>"No decor here" (an empty override: auto placement never puts decor there).</summary>
        None,

        /// <summary>Removes the manual choice: the cell gets the auto placed decor again.</summary>
        Automatic,
    }

    /// <summary>
    /// Applies the active tool to grid clicks/drags. One mouse stroke = one Undo step;
    /// the level is revalidated when the stroke ends (not on every painted cell).
    /// </summary>
    internal sealed class EditToolController : IGridInputHandler
    {
        private readonly LevelDesignerState _state;
        private readonly Action<string> _notify;
        private int _undoGroup;
        private bool _changed;

        public EditToolController(LevelDesignerState state, Action<string> notify)
        {
            _state = state;
            _notify = notify;
        }

        public EditTool Tool { get; set; } = EditTool.Select;

        /// <summary>False outside the Edit tab: clicks only select.</summary>
        public bool EditingEnabled { get; set; }

        public ZombieDefinition ZombieDefinition { get; set; }
        public Direction ZombieFacing { get; set; } = Direction.South;
        public WeaponDefinition WeaponDefinition { get; set; }

        public DecorBrushMode DecorMode { get; set; }
        public string DecorVariantId { get; set; }
        public int DecorRotation { get; set; }

        /// <summary>Door waiting for a key: the next key placed is linked to it.</summary>
        public string PendingKeyDoorId { get; set; }

        private EditTool ActiveTool => EditingEnabled ? Tool : EditTool.Select;
        private LevelData Level => _state.Level;

        public void OnMouseDown(GridPosition cell, Event e)
        {
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            _state.SelectedCell = cell;

            if (e.button == 1)
            {
                if (ActiveTool == EditTool.Decor)
                    PaintDecor(cell, DecorBrushMode.None);
                else if (ActiveTool == EditTool.Snowdrift)
                    PaintSurface(cell, CellSurface.None);
                return;
            }

            switch (ActiveTool)
            {
                case EditTool.Select:
                    SelectAt(cell);
                    break;
                case EditTool.Wall:
                case EditTool.Floor:
                case EditTool.Door:
                    Paint(cell);
                    break;
                case EditTool.Decor:
                    PaintDecor(cell, DecorMode);
                    break;
                case EditTool.Snowdrift:
                    PaintSurface(cell, CellSurface.Snowdrift);
                    break;
                case EditTool.Erase:
                    Erase(cell);
                    break;
                default:
                    Place(cell);
                    break;
            }
        }

        public void OnMouseDrag(GridPosition cell, Event e)
        {
            if (ActiveTool == EditTool.Decor && (e.button == 0 || e.button == 1))
            {
                PaintDecor(cell, e.button == 1 ? DecorBrushMode.None : DecorMode);
                return;
            }

            if (ActiveTool == EditTool.Snowdrift && (e.button == 0 || e.button == 1))
            {
                PaintSurface(cell, e.button == 1 ? CellSurface.None : CellSurface.Snowdrift);
                return;
            }

            if (e.button != 0)
                return;

            switch (ActiveTool)
            {
                case EditTool.Wall:
                case EditTool.Floor:
                case EditTool.Door:
                    Paint(cell);
                    break;
                case EditTool.Select:
                    if (_state.SelectedEntity != null)
                        _state.DragTarget = cell;
                    break;
                case EditTool.Erase:
                    Erase(cell);
                    break;
            }
        }

        public void OnMouseUp(GridPosition? cell, Event e)
        {
            if (ActiveTool == EditTool.Select && _state.DragTarget.HasValue && _state.SelectedEntity != null)
            {
                var entity = _state.SelectedEntity;
                var target = _state.DragTarget.Value;
                if (Level.Geometry.GetCell(target) == CellType.Wall && !(entity is DoorData))
                    _notify("Objects can only be moved to floor cells.");
                else
                    Apply("Move " + entity.Id, () =>
                    {
                        if (LevelEditing.Move(Level, entity, target))
                            _state.SelectedCell = target;
                    });
            }

            _state.DragTarget = null;

            if (_changed)
            {
                Undo.CollapseUndoOperations(_undoGroup);
                _state.Revalidate();
                _changed = false;
            }
        }

        // ------------------------------------------------------------ Tools

        /// <summary>Clicking the same cell again cycles through the objects in it.</summary>
        private void SelectAt(GridPosition cell)
        {
            var entities = Level.AllEntities().Where(e => e.Position == cell).ToList();
            if (entities.Count == 0)
            {
                _state.SelectedEntityId = null;
                return;
            }

            var current = entities.FindIndex(e => e.Id == _state.SelectedEntityId);
            _state.Select(entities[(current + 1) % entities.Count]);
        }

        private void Paint(GridPosition cell)
        {
            var type = Tool == EditTool.Wall ? CellType.Wall : Tool == EditTool.Floor ? CellType.Floor : CellType.Door;
            if (Level.Geometry.GetCell(cell) == type)
                return;

            Apply("Paint " + type, () => LevelEditing.SetCellType(Level, cell, type));
            if (type == CellType.Door)
                _state.Select(Level.Doors.FirstOrDefault(d => d.Position == cell));
        }

        /// <summary>Decor only on floor cells; other cells are skipped silently while dragging.</summary>
        private void PaintDecor(GridPosition cell, DecorBrushMode mode)
        {
            if (Level.Geometry.GetCell(cell) != CellType.Floor)
                return;

            var data = Level.VisualData;
            var hasOverride = data.TryGetCellOverride(cell, Purge.Core.Visual.CellLayer.Decor, out var current);
            switch (mode)
            {
                case DecorBrushMode.Automatic:
                    if (hasOverride)
                        Apply("Clear Decor", () => LevelEditing.ClearCellOverride(Level, cell, Purge.Core.Visual.CellLayer.Decor));
                    break;
                case DecorBrushMode.None:
                    if (!hasOverride || !current.IsEmpty)
                        Apply("Remove Decor", () => LevelEditing.SetCellOverride(Level, cell, Purge.Core.Visual.CellLayer.Decor,
                            Purge.Core.Visual.VisualChoice.None));
                    break;
                default:
                    if (string.IsNullOrEmpty(DecorVariantId))
                    {
                        _notify("Choose a decor variant in the tool options.");
                        return;
                    }

                    var choice = new Purge.Core.Visual.VisualChoice(DecorVariantId, DecorRotation);
                    if (!hasOverride || !current.Equals(choice))
                        Apply("Paint Decor", () => LevelEditing.SetCellOverride(Level, cell, Purge.Core.Visual.CellLayer.Decor, choice));
                    break;
            }
        }

        /// <summary>Ground only on floor cells; other cells are skipped silently while dragging.</summary>
        private void PaintSurface(GridPosition cell, CellSurface surface)
        {
            var geometry = Level.Geometry;
            if (geometry.GetCell(cell) != CellType.Floor || geometry.GetSurface(cell) == surface)
                return;

            Apply(surface == CellSurface.None ? "Remove Snowdrift" : "Paint Snowdrift",
                () => LevelEditing.SetSurface(Level, cell, surface));
        }

        private void Place(GridPosition cell)
        {
            if (!CanPlace(cell, out var reason))
            {
                _notify(reason);
                return;
            }

            LevelEntityData created = null;
            switch (Tool)
            {
                case EditTool.PlayerStart:
                    Apply("Add Player Start", () => created = LevelEditing.AddPlayerStart(Level, cell));
                    break;
                case EditTool.Medkit:
                    Apply("Add Medkit", () => created = LevelEditing.AddMedkit(Level, cell));
                    break;
                case EditTool.Key:
                    var door = Level.Doors.FirstOrDefault(d => d.Id == PendingKeyDoorId);
                    Apply("Add Key", () => created = LevelEditing.AddKey(Level, cell, door));
                    if (door != null)
                    {
                        PendingKeyDoorId = null;
                        Tool = EditTool.Select;
                    }
                    break;
                case EditTool.Zombie:
                    Apply("Add Zombie", () => created = LevelEditing.AddZombie(Level, cell, ZombieDefinition, ZombieFacing));
                    break;
                case EditTool.Weapon:
                    Apply("Add Weapon", () => created = LevelEditing.AddWeapon(Level, cell, WeaponDefinition));
                    break;
            }

            _state.Select(created);
        }

        private bool CanPlace(GridPosition cell, out string reason)
        {
            reason = null;
            if (Level.Geometry.GetCell(cell) != CellType.Floor)
            {
                reason = "Objects can only be placed on floor cells.";
                return false;
            }

            if (Tool == EditTool.Zombie && ZombieDefinition == null)
            {
                reason = "Choose a Zombie Definition in the tool options.";
                return false;
            }

            if (Tool == EditTool.Weapon && WeaponDefinition == null)
            {
                reason = "Choose a Weapon Definition in the tool options.";
                return false;
            }

            var others = Level.AllEntities().Where(e => e.Position == cell).ToList();
            if (Tool == EditTool.Zombie)
            {
                if (others.Any(e => e is PlayerStartData))
                    reason = "A zombie cannot spawn on a player start.";
            }
            else if (others.Any(e => !(e is ZombieSpawnData)))
            {
                reason = $"Cell already holds {others.First(e => !(e is ZombieSpawnData)).Id}.";
            }
            else if (Tool == EditTool.PlayerStart && others.Count > 0)
            {
                reason = "A player start cannot share a cell with a zombie.";
            }

            return reason == null;
        }

        /// <summary>Clears the cell to empty floor (see <see cref="LevelEditing.ClearCell"/>); empty cells are skipped.</summary>
        private void Erase(GridPosition cell)
        {
            if (!HasAnythingToErase(cell))
                return;

            Apply("Erase Cell", () => LevelEditing.ClearCell(Level, cell));
        }

        private bool HasAnythingToErase(GridPosition cell) =>
            Level.Geometry.IsInside(cell) &&
            (Level.Geometry.GetCell(cell) != CellType.Floor
             || Level.AllEntities().Any(e => e.Position == cell)
             || Level.Lights.Any(l => l.Cell == cell)
             || !Purge.Core.Visual.VisualResolver.ResolveDecor(Level, cell).IsEmpty);

        private void Apply(string undoName, Action change)
        {
            LevelEditorCommands.Modify(Level, undoName, change);
            _changed = true;
        }
    }
}
