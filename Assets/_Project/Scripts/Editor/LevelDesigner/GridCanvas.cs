using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Validation;
using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>Receives mouse input on grid cells; the active edit tool implements it.</summary>
    internal interface IGridInputHandler
    {
        void OnMouseDown(GridPosition cell, Event e);
        void OnMouseDrag(GridPosition cell, Event e);
        void OnMouseUp(GridPosition? cell, Event e);
    }

    /// <summary>
    /// Top-down 2D view of the level. Wheel = zoom; pan = Hand mode / held Space / middle mouse / Alt+LMB;
    /// otherwise LMB/RMB go to the active tool. Grid +Y is drawn upwards (North at the top).
    /// </summary>
    internal sealed class GridCanvas
    {
        private const float MinCellSize = 4f;
        private const float MaxCellSize = 64f;

        private static readonly Color Background = new Color(0.13f, 0.13f, 0.14f);
        private static readonly Color WallColor = new Color(0.27f, 0.27f, 0.30f);
        private static readonly Color FloorColor = new Color(0.80f, 0.78f, 0.72f);
        private static readonly Color DoorColor = new Color(0.55f, 0.36f, 0.18f);
        private static readonly Color SnowdriftColor = new Color(0.86f, 0.93f, 1f);
        private static readonly Color FragmentColor = new Color(0.65f, 0.35f, 0.95f);
        private static readonly Color DecorAutoColor = new Color(0.45f, 0.4f, 0.32f);
        private static readonly Color DecorManualColor = new Color(0.2f, 0.6f, 0.25f);
        private static readonly Color SelectedColor = new Color(1f, 0.85f, 0.1f);
        private static readonly Color ErrorColor = new Color(1f, 0.2f, 0.2f);
        private static readonly Color WarningColor = new Color(1f, 0.65f, 0.1f);

        private readonly Dictionary<GridPosition, (string Text, Color Color)> _glyphs = new Dictionary<GridPosition, (string, Color)>();
        private static readonly Color SelectedEntityColor = new Color(0.2f, 1f, 1f);
        private static readonly Color PatrolColor = new Color(1f, 0.35f, 0.35f, 0.55f);
        private static readonly Color SelectedPatrolColor = new Color(1f, 0.3f, 0.3f, 1f);

        private float _cellSize;
        private Vector2 _origin;
        private GUIStyle _glyphStyle;
        private GUIStyle _smallStyle;
        private bool _dragging;
        private bool _panning;
        private bool _spaceHeld;
        private GridPosition? _lastDragCell;

        /// <summary>True when hover changed and the window should repaint.</summary>
        public bool NeedsRepaint { get; private set; }

        /// <summary>Hand tool: LMB drags the view instead of using the edit tool.</summary>
        public bool HandMode { get; set; }

        public bool IsPanActive => HandMode || _spaceHeld;

        /// <summary>Call when the window loses focus: a Space release would otherwise be missed.</summary>
        public void ResetTemporaryModes()
        {
            _spaceHeld = false;
            _panning = false;
        }

        public void Draw(Rect rect, LevelDesignerState state, IGridInputHandler input)
        {
            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            NeedsRepaint = false;
            if (rect.width < 2f || rect.height < 2f)
                return;

            var geometry = state.Level.Geometry;
            GUI.BeginClip(rect);
            var local = new Rect(0f, 0f, rect.width, rect.height);

            if (state.FitRequested || _cellSize <= 0f)
            {
                Fit(local, geometry);
                state.FitRequested = false;
            }

            HandleInput(local, state, input, controlId);
            if (Event.current.type == EventType.Repaint)
                Paint(local, state);

            GUI.EndClip();
        }

        private void Fit(Rect local, LevelGeometry geometry)
        {
            const float margin = 12f;
            _cellSize = Mathf.Clamp(Mathf.Min((local.width - margin * 2f) / geometry.Width, (local.height - margin * 2f) / geometry.Height),
                MinCellSize, MaxCellSize);
            var size = new Vector2(geometry.Width * _cellSize, geometry.Height * _cellSize);
            _origin = new Vector2((local.width - size.x) * 0.5f, (local.height - size.y) * 0.5f);
        }

        private void HandleInput(Rect local, LevelDesignerState state, IGridInputHandler input, int controlId)
        {
            var e = Event.current;
            var geometry = state.Level.Geometry;

            if (HandlePan(local, controlId, e))
                return;

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.ScrollWheel when local.Contains(e.mousePosition):
                    var oldSize = _cellSize;
                    _cellSize = Mathf.Clamp(_cellSize * (e.delta.y > 0f ? 0.9f : 1.1f), MinCellSize, MaxCellSize);
                    _origin = e.mousePosition - (e.mousePosition - _origin) * (_cellSize / oldSize);
                    e.Use();
                    break;

                case EventType.MouseDrag when !_dragging && (e.button == 2 || (e.button == 0 && e.alt)):
                    _origin += e.delta;
                    e.Use();
                    break;

                case EventType.MouseDown when (e.button == 0 || e.button == 1) && !e.alt && local.Contains(e.mousePosition):
                    if (TryGetCell(e.mousePosition, geometry, out var clicked))
                    {
                        GUI.FocusControl(null);
                        GUIUtility.hotControl = controlId;
                        _dragging = true;
                        _lastDragCell = clicked;
                        input.OnMouseDown(clicked, e);
                    }

                    e.Use();
                    break;

                case EventType.MouseDrag when _dragging && GUIUtility.hotControl == controlId:
                    if (TryGetCell(e.mousePosition, geometry, out var dragged) && dragged != _lastDragCell)
                    {
                        _lastDragCell = dragged;
                        state.HoveredCell = dragged;
                        input.OnMouseDrag(dragged, e);
                    }

                    e.Use();
                    break;

                case EventType.MouseUp when _dragging && GUIUtility.hotControl == controlId:
                    _dragging = false;
                    GUIUtility.hotControl = 0;
                    input.OnMouseUp(TryGetCell(e.mousePosition, geometry, out var released) ? released : (GridPosition?)null, e);
                    e.Use();
                    break;

                case EventType.MouseMove:
                    GridPosition? hovered = local.Contains(e.mousePosition) && TryGetCell(e.mousePosition, geometry, out var cell)
                        ? cell
                        : (GridPosition?)null;
                    if (hovered != state.HoveredCell)
                    {
                        state.HoveredCell = hovered;
                        NeedsRepaint = true;
                    }

                    break;
            }
        }

        /// <summary>Hand mode and held Space: LMB drag moves the view. Returns true if the event was consumed.</summary>
        private bool HandlePan(Rect local, int controlId, Event e)
        {
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space && !EditorGUIUtility.editingTextField)
            {
                if (!_spaceHeld)
                    NeedsRepaint = true;
                _spaceHeld = true;
                e.Use();
                return true;
            }

            if (e.type == EventType.KeyUp && e.keyCode == KeyCode.Space)
            {
                _spaceHeld = false;
                NeedsRepaint = true;
                e.Use();
                return true;
            }

            if (IsPanActive || _panning)
                EditorGUIUtility.AddCursorRect(local, MouseCursor.Pan);

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when e.button == 0 && IsPanActive && local.Contains(e.mousePosition):
                    GUIUtility.hotControl = controlId;
                    _panning = true;
                    e.Use();
                    return true;

                case EventType.MouseDrag when _panning && GUIUtility.hotControl == controlId:
                    _origin += e.delta;
                    e.Use();
                    return true;

                case EventType.MouseUp when _panning && GUIUtility.hotControl == controlId:
                    _panning = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    return true;

                default:
                    return false;
            }
        }

        private bool TryGetCell(Vector2 point, LevelGeometry geometry, out GridPosition cell)
        {
            var x = Mathf.FloorToInt((point.x - _origin.x) / _cellSize);
            var row = Mathf.FloorToInt((point.y - _origin.y) / _cellSize);
            cell = new GridPosition(x, geometry.Height - 1 - row);
            return geometry.IsInside(cell);
        }

        private Rect CellRect(GridPosition cell, int height) =>
            new Rect(_origin.x + cell.X * _cellSize, _origin.y + (height - 1 - cell.Y) * _cellSize, _cellSize, _cellSize);

        private void Paint(Rect local, LevelDesignerState state)
        {
            var level = state.Level;
            var geometry = level.Geometry;
            EditorGUI.DrawRect(local, Background);

            var gap = _cellSize >= 8f ? 1f : 0f;
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var cell = geometry.ToPosition(i);
                var r = CellRect(cell, geometry.Height);
                if (!r.Overlaps(local))
                    continue;

                var type = geometry.GetCell(cell);
                var color = type == CellType.Floor && geometry.GetSurface(cell) == CellSurface.Snowdrift ? SnowdriftColor : CellColor(type);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width - gap, r.height - gap), color);
            }

            foreach (var fragment in level.MapFragments)
            {
                var region = fragment.Region;
                if (region.IsEmpty)
                    continue;

                var min = CellRect(new GridPosition(region.X, region.YMax - 1), geometry.Height);
                DrawOutline(new Rect(min.x, min.y, region.Width * _cellSize, region.Height * _cellSize), FragmentColor, 2f);
            }

            PaintPatrols(state);
            PaintDecor(level, local);
            PaintLights(level, local);
            PaintGlyphs(level, local);
            PaintIssues(state);

            if (state.PreviewRect.HasValue)
            {
                var region = state.PreviewRect.Value;
                var min = CellRect(new GridPosition(region.X, region.YMax - 1), geometry.Height);
                DrawOutline(new Rect(min.x, min.y, region.Width * _cellSize, region.Height * _cellSize), Color.white, 2f);
            }

            if (state.HoveredCell.HasValue)
                DrawOutline(CellRect(state.HoveredCell.Value, geometry.Height), Color.white, 1f);
            if (state.SelectedCell.HasValue)
                DrawOutline(CellRect(state.SelectedCell.Value, geometry.Height), SelectedColor, 2f);

            var selected = state.SelectedEntity;
            if (selected != null && geometry.IsInside(selected.Position))
            {
                var r = CellRect(selected.Position, geometry.Height);
                DrawOutline(new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f), SelectedEntityColor, 2f);
            }

            if (state.DragTarget.HasValue && geometry.IsInside(state.DragTarget.Value))
                DrawOutline(CellRect(state.DragTarget.Value, geometry.Height), SelectedEntityColor, 2f);
        }

        /// <summary>Decor: a small square in the cell's bottom-left corner (dark: auto placed, green: by hand).</summary>
        private void PaintDecor(LevelData level, Rect local)
        {
            if (_cellSize < 8f)
                return;

            var geometry = level.Geometry;
            var size = Mathf.Max(3f, _cellSize * 0.22f);
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var cell = geometry.ToPosition(i);
                var r = CellRect(cell, geometry.Height);
                if (!r.Overlaps(local))
                    continue;

                var decor = VisualResolver.ResolveDecor(level, cell, out var source);
                if (decor.IsEmpty)
                    continue;

                EditorGUI.DrawRect(new Rect(r.x + 2f, r.yMax - size - 3f, size, size),
                    source == VisualSource.Override ? DecorManualColor : DecorAutoColor);
            }
        }

        /// <summary>Light sources: a dot of the light's colour at its point (shifted towards its wall).</summary>
        private void PaintLights(LevelData level, Rect local)
        {
            var height = level.Geometry.Height;
            var radius = Mathf.Max(2f, _cellSize * 0.16f);
            foreach (var light in level.Lights)
            {
                var point = light.Point;
                var center = new Vector3(_origin.x + (point.x + 0.5f) * _cellSize, _origin.y + (height - 0.5f - point.y) * _cellSize);
                if (!local.Contains(center))
                    continue;

                Handles.color = Color.black;
                Handles.DrawSolidDisc(center, Vector3.forward, radius + 1f);
                var color = light.Color;
                color.a = 1f;
                Handles.color = color;
                Handles.DrawSolidDisc(center, Vector3.forward, radius);
            }
        }

        /// <summary>Patrol loops: spawn -> P1 -> ... -> Pn -> P1. The selected zombie's route is highlighted.</summary>
        private void PaintPatrols(LevelDesignerState state)
        {
            var level = state.Level;
            var height = level.Geometry.Height;
            if (_smallStyle == null)
                _smallStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.UpperLeft };

            foreach (var zombie in level.ZombieSpawns)
            {
                if (!zombie.HasPatrol)
                    continue;

                PatrolData patrol = null;
                foreach (var p in level.Patrols)
                    if (p.Id == zombie.PatrolId)
                        patrol = p;

                if (patrol == null || patrol.Points.Count == 0)
                    continue;

                var selected = zombie.Id == state.SelectedEntityId;
                var points = new List<Vector3> { CellRect(zombie.Position, height).center };
                foreach (var point in patrol.Points)
                    points.Add(CellRect(point, height).center);
                points.Add(CellRect(patrol.Points[0], height).center);

                Handles.color = selected ? SelectedPatrolColor : PatrolColor;
                Handles.DrawAAPolyLine(selected ? 4f : 2f, points.ToArray());

                if (!selected || _cellSize < 12f)
                    continue;

                _smallStyle.normal.textColor = SelectedPatrolColor;
                for (var i = 0; i < patrol.Points.Count; i++)
                {
                    var r = CellRect(patrol.Points[i], height);
                    GUI.Label(new Rect(r.x + 2f, r.y, r.width, r.height), (i + 1).ToString(), _smallStyle);
                }
            }
        }

        private void PaintGlyphs(LevelData level, Rect local)
        {
            _glyphs.Clear();
            foreach (var entity in level.AllEntities())
            {
                var glyph = EntityGlyphs.For(level, entity);
                _glyphs[entity.Position] = _glyphs.TryGetValue(entity.Position, out var existing)
                    ? (existing.Text.Length < 2 ? existing.Text + glyph.Text : existing.Text, existing.Color)
                    : glyph;
            }

            if (_glyphStyle == null)
                _glyphStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };

            _glyphStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(_cellSize * 0.6f), 6, 28);
            var height = level.Geometry.Height;

            foreach (var pair in _glyphs)
            {
                var r = CellRect(pair.Key, height);
                if (!r.Overlaps(local))
                    continue;

                if (_cellSize < 12f)
                {
                    var dot = _cellSize * 0.5f;
                    EditorGUI.DrawRect(new Rect(r.center.x - dot * 0.5f, r.center.y - dot * 0.5f, dot, dot), pair.Value.Color);
                    continue;
                }

                _glyphStyle.normal.textColor = Color.black;
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), pair.Value.Text, _glyphStyle);
                _glyphStyle.normal.textColor = pair.Value.Color;
                GUI.Label(r, pair.Value.Text, _glyphStyle);
            }
        }

        private void PaintIssues(LevelDesignerState state)
        {
            if (state.Report == null)
                return;

            var height = state.Level.Geometry.Height;
            foreach (var issue in state.Report.Issues)
            {
                if (!issue.Position.HasValue || issue.Severity == ValidationSeverity.Info || !state.Level.Geometry.IsInside(issue.Position.Value))
                    continue;

                var color = issue.Severity == ValidationSeverity.Error ? ErrorColor : WarningColor;
                var thickness = issue == state.FocusedIssue ? 3f : 1f;
                var r = CellRect(issue.Position.Value, height);
                DrawOutline(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), color, thickness);
            }
        }

        private static Color CellColor(CellType type)
        {
            switch (type)
            {
                case CellType.Floor: return FloorColor;
                case CellType.Door: return DoorColor;
                default: return WallColor;
            }
        }

        private static void DrawOutline(Rect r, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }
    }

    /// <summary>Letter + colour used to show an object on the 2D grid. Keys and locked doors use their pair colour.</summary>
    internal static class EntityGlyphs
    {
        public static (string Text, Color Color) For(LevelData level, LevelEntityData entity)
        {
            switch (entity)
            {
                case PlayerStartData _: return ("S", new Color(0.2f, 0.85f, 0.3f));
                case ExitData _: return ("E", new Color(0.2f, 0.85f, 1f));
                case DoorData door: return ("D", PairColor(level, door) ?? Color.white);
                case KeyData key: return ("K", PairColor(level, key) ?? Color.yellow);
                case ZombieSpawnData _: return ("Z", new Color(1f, 0.25f, 0.25f));
                case WeaponPickupData _: return ("W", new Color(1f, 0.6f, 0.15f));
                case MedkitData _: return ("+", new Color(1f, 0.4f, 0.8f));
                case MapFragmentData _: return ("M", new Color(0.75f, 0.5f, 1f));
                default: return ("?", Color.gray);
            }
        }

        /// <summary>Colour of the entity's resolved visual variant ColorTag, if it parses as a colour name or #hex.</summary>
        public static Color? PairColor(LevelData level, LevelEntityData entity)
        {
            var theme = level.VisualTheme;
            if (theme == null || !VisualKinds.TryGetForEntity(entity, out var kind, out _))
                return null;

            var set = theme.GetSet(kind);
            if (set == null)
                return null;

            var variant = set.FindVariant(VisualResolver.ResolveObject(level, entity).VariantId);
            return variant != null && variant.HasColor && VisualColors.TryGetColor(variant.ColorTag, out var color)
                ? color
                : (Color?)null;
        }
    }
}
