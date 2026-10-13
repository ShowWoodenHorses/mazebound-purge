using System;
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
    /// <summary>Theme, Regenerate Visuals, light sources, overrides and distribution diagnostics (ТЗ §93–96).</summary>
    internal sealed class VisualsPanel
    {
        private bool _clearOverrides;
        private bool _showSets = true;
        private bool _showDistribution = true;
        private bool _showOverrides = true;
        private bool _showLights = true;
        private bool _showLightList;
        private bool _showDecor = true;

        /// <summary>Background of the lights in the selected cell in the Light List.</summary>
        private static readonly Color SelectedLightRow = new Color(0.24f, 0.48f, 0.90f, 0.35f);

        public void OnGUI(LevelDesignerState state)
        {
            var level = state.Level;

            EditorGUILayout.LabelField("Visual Theme", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var theme = (VisualTheme)EditorGUILayout.ObjectField("Theme", level.VisualTheme, typeof(VisualTheme), false);
            if (EditorGUI.EndChangeCheck())
            {
                LevelEditorCommands.Modify(level, "Change Visual Theme", () => level.VisualTheme = theme);
                state.Revalidate();
            }

            if (level.VisualTheme == null)
            {
                EditorGUILayout.HelpBox("Assign a VisualTheme (Create > Maze > Visual > Visual Theme).", MessageType.Warning);
                return;
            }

            DrawSets(level.VisualTheme);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Regenerate Visuals", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var visualSeed = EditorGUILayout.IntField("Visual Seed", level.Generation.VisualSeed);
            var randomize = GUILayout.Button("Random Visual Seed");
            if (EditorGUI.EndChangeCheck() || randomize)
            {
                var seed = randomize ? LevelEditorCommands.NewRandomSeed() : visualSeed;
                LevelEditorCommands.Modify(level, "Change Visual Seed", () => level.Generation.VisualSeed = seed);
            }

            _clearOverrides = EditorGUILayout.ToggleLeft("Also clear manual overrides", _clearOverrides);
            if (GUILayout.Button("Regenerate Visuals", GUILayout.Height(26f)) &&
                LevelEditorCommands.RegenerateVisuals(level, _clearOverrides))
                state.Revalidate();

            EditorGUILayout.HelpBox("Changing the Visual Seed does not change the level until you press Regenerate Visuals.",
                MessageType.None);

            DrawLights(state);
            DrawDecor(state);
            DrawOverrides(state);
            DrawDistribution(level);
        }

        private void DrawSets(VisualTheme theme)
        {
            _showSets = EditorGUILayout.Foldout(_showSets, "Visual Sets", true);
            if (!_showSets)
                return;

            using (new EditorGUI.DisabledScope(true))
            {
                foreach (VisualKind kind in Enum.GetValues(typeof(VisualKind)))
                    EditorGUILayout.ObjectField(kind.ToString(), theme.GetSet(kind), typeof(VisualSet), false);
            }

            EditorGUILayout.HelpBox("Edit sets and weights in the Inspector of the theme / set assets.", MessageType.None);
        }

        /// <summary>
        /// Light sources: auto placement (density, Place Lights; also part of Regenerate Visuals) and manual edits.
        /// Editing a light makes it hand-placed, so auto placement keeps it.
        /// </summary>
        private void DrawLights(LevelDesignerState state)
        {
            var level = state.Level;
            var auto = level.Lights.Count(l => l.IsGenerated);

            EditorGUILayout.Space();
            _showLights = EditorGUILayout.Foldout(_showLights,
                $"Lights ({level.Lights.Count}: {auto} auto, {level.Lights.Count - auto} manual)", true);
            if (!_showLights)
                return;

            EditorGUI.BeginChangeCheck();
            var density = EditorGUILayout.Slider("Density", level.Generation.LightDensity, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
                LevelEditorCommands.Modify(level, "Change Light Density", () => level.Generation.LightDensity = density);

            if (level.VisualTheme.Lighting.LightPresets.Count == 0)
                EditorGUILayout.HelpBox("The theme has no light presets (theme asset > Lighting > Light Presets): " +
                                        "auto placement places nothing.", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Place Lights"))
                Change(state, "Place Lights", () => LevelEditing.PlaceLights(level));

            var cell = state.SelectedCell;
            var canAdd = cell.HasValue && level.Geometry.IsInside(cell.Value) &&
                         level.Geometry.GetCell(cell.Value) == CellType.Floor;
            using (new EditorGUI.DisabledScope(!canAdd))
            {
                if (GUILayout.Button("Add at Selected Cell"))
                    Change(state, "Add Light", () => LevelEditing.AddLight(level, cell.Value));
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Place Lights replaces auto placed lights (Visual Seed, density) and keeps manual ones. " +
                                    "Drag lights in the Scene view on the preview.", MessageType.None);

            _showLightList = EditorGUILayout.Foldout(_showLightList, "Light List", true);
            if (!_showLightList)
                return;

            LightSourceData toRemove = null;
            foreach (var light in level.Lights)
            {
                var row = EditorGUILayout.BeginHorizontal();
                if (state.SelectedCell == light.Cell && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(row, SelectedLightRow);
                if (GUILayout.Button(light.Id, EditorStyles.linkLabel, GUILayout.Width(70f)))
                    state.SelectedCell = light.Cell;
                EditorGUILayout.LabelField(light.IsGenerated ? "auto" : "manual", GUILayout.Width(46f));

                EditorGUI.BeginChangeCheck();
                var color = EditorGUILayout.ColorField(GUIContent.none, light.Color, true, false, false, GUILayout.Width(50f));
                EditorGUIUtility.labelWidth = 14f;
                var radius = EditorGUILayout.FloatField("R", light.Radius, GUILayout.Width(52f));
                EditorGUIUtility.labelWidth = 14f;
                var intensity = EditorGUILayout.FloatField("I", light.Intensity, GUILayout.Width(52f));
                EditorGUIUtility.labelWidth = 14f;
                var flicker = EditorGUILayout.Slider("F", light.Flicker, 0f, 1f);
                EditorGUIUtility.labelWidth = 0f;
                if (EditorGUI.EndChangeCheck())
                    LevelEditorCommands.Modify(level, "Edit Light", () => LevelEditing.SetLight(light, color, radius, intensity, flicker));

                DrawLightFixture(level, light);
                if (GUILayout.Button("X", GUILayout.Width(22f)))
                    toRemove = light;
                EditorGUILayout.EndHorizontal();
            }

            if (toRemove != null)
            {
                Change(state, "Remove Light", () => LevelEditing.RemoveLight(level, toRemove));
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>
        /// Decor: auto placement settings of the level (density, the height limit for auto placement) and Place Decor
        /// (also part of Regenerate Visuals). Manual decor is painted with the Decor tool (Edit tab).
        /// </summary>
        private void DrawDecor(LevelDesignerState state)
        {
            var level = state.Level;
            var geometry = level.Geometry;
            int auto = 0, manual = 0;
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var choice = VisualResolver.ResolveDecor(level, geometry.ToPosition(i), out var source);
                if (choice.IsEmpty) continue;
                if (source == VisualSource.Override) manual++;
                else auto++;
            }

            EditorGUILayout.Space();
            _showDecor = EditorGUILayout.Foldout(_showDecor, $"Decor ({auto + manual}: {auto} auto, {manual} manual)", true);
            if (!_showDecor)
                return;

            var set = level.VisualTheme.GetSet(VisualKind.Decor);
            if (set == null)
            {
                EditorGUILayout.HelpBox("The theme has no Decor set (Maze > Dev > Build Decor).", MessageType.Info);
                return;
            }

            EditorGUI.BeginChangeCheck();
            var density = EditorGUILayout.Slider("Density", level.Generation.DecorDensity, 0f, 1f);
            var maxHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Max Auto Height (m)", "Taller decor is never placed automatically, only with the Decor tool."),
                level.Generation.MaxAutoDecorHeight));
            if (EditorGUI.EndChangeCheck())
                LevelEditorCommands.Modify(level, "Change Decor Settings", () =>
                {
                    level.Generation.DecorDensity = density;
                    level.Generation.MaxAutoDecorHeight = maxHeight;
                });

            if (GUILayout.Button("Place Decor"))
            {
                DecorHeights.Refresh(set);
                Change(state, "Place Decor", () => LevelEditing.PlaceDecor(level));
            }

            var limit = level.Generation.MaxAutoDecorHeight;
            var autoVariants = set.Variants.Count(v => v.Weight > 0 && v.Category == VisualCategory.General && v.Height <= limit);
            EditorGUILayout.HelpBox(
                $"Place Decor replaces auto placed decor (Visual Seed, density) and keeps manual decor. {autoVariants} of " +
                $"{set.Variants.Count} variants are low enough for auto placement; taller ones only with the Decor tool.",
                autoVariants == 0 ? MessageType.Warning : MessageType.None);
        }

        private static void Change(LevelDesignerState state, string undoName, Action change)
        {
            LevelEditorCommands.Modify(state.Level, undoName, change);
            state.Revalidate();
        }

        private void DrawOverrides(LevelDesignerState state)
        {
            var level = state.Level;
            var data = level.VisualData;
            var total = data.CellOverrides.Count + data.ObjectOverrides.Count;

            EditorGUILayout.Space();
            _showOverrides = EditorGUILayout.Foldout(_showOverrides, $"Manual Overrides ({total})", true);
            if (!_showOverrides || total == 0)
                return;

            CellVisualOverride cellToClear = null;
            string objectToClear = null;

            foreach (var entry in data.CellOverrides)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(entry.Position.ToString(), EditorStyles.linkLabel, GUILayout.Width(70f)))
                    state.SelectedCell = entry.Position;
                EditorGUILayout.LabelField($"{entry.Layer}: {entry.Choice}");
                if (GUILayout.Button("Clear", GUILayout.Width(50f)))
                    cellToClear = entry;
                EditorGUILayout.EndHorizontal();
            }

            foreach (var entry in data.ObjectOverrides)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(entry.EntityId, GUILayout.Width(70f));
                EditorGUILayout.LabelField(entry.Choice.ToString());
                if (GUILayout.Button("Clear", GUILayout.Width(50f)))
                    objectToClear = entry.EntityId;
                EditorGUILayout.EndHorizontal();
            }

            if (cellToClear != null)
                ClearOverride(state, () => data.ClearCellOverride(cellToClear.Position, cellToClear.Layer));
            if (objectToClear != null)
                ClearOverride(state, () => data.ClearObjectOverride(objectToClear));
        }

        private static void ClearOverride(LevelDesignerState state, Action clear)
        {
            LevelEditorCommands.Modify(state.Level, "Clear Visual Override", clear);
            state.Revalidate();
            GUIUtility.ExitGUI();
        }

        private void DrawDistribution(LevelData level)
        {
            EditorGUILayout.Space();
            _showDistribution = EditorGUILayout.Foldout(_showDistribution, "Visual Distribution", true);
            if (!_showDistribution)
                return;

            foreach (var layer in CellLayers.All)
            {
                var kind = CellLayers.Kind(layer);
                var set = level.VisualTheme.GetSet(kind);
                if (set == null)
                    continue;

                EditorGUILayout.LabelField(layer == CellLayer.Floor ? "Floor (all cells, incl. under walls)" : "Wall",
                    EditorStyles.miniBoldLabel);
                var counts = VisualResolver.CountCellVariants(level, layer);

                // Percentages are relative to the variant's category, where weights apply.
                foreach (var group in set.Variants.GroupBy(v => v.Category))
                {
                    var categoryCells = group.Sum(v => counts.TryGetValue(v.Id ?? string.Empty, out var c) ? c : 0);
                    var categoryWeight = group.Where(v => v.Weight > 0).Sum(v => v.Weight);

                    foreach (var variant in group)
                    {
                        counts.TryGetValue(variant.Id ?? string.Empty, out var count);
                        var actual = categoryCells > 0 ? 100f * count / categoryCells : 0f;
                        var expected = categoryWeight > 0 && group.Key != VisualCategory.Special ? 100f * variant.Weight / categoryWeight : 0f;
                        var label = group.Key == VisualCategory.General ? variant.Id : $"{variant.Id} [{group.Key}]";
                        EditorGUILayout.LabelField(label, $"{count,5}   {actual,5:0.0}%   (weight {expected:0.0}%)");
                    }
                }

                var unknown = counts.Where(p => set.FindVariant(p.Key) == null).Sum(p => p.Value);
                if (unknown > 0)
                    EditorGUILayout.HelpBox($"{unknown} {kind} cell(s) have no or unknown visual.", MessageType.Warning);
            }
        }

        /// <summary>Fixture variant of a light (theme's Light set); "-" = the set default. Rebuild the preview to see it.</summary>
        private static void DrawLightFixture(LevelData level, LightSourceData light)
        {
            var set = level.VisualTheme.GetSet(VisualKind.Light);
            if (set == null || set.Variants.Count == 0)
                return;

            var ids = new List<string> { "-" };
            ids.AddRange(set.Variants.Select(v => v.Id));
            var current = Mathf.Max(0, ids.IndexOf(light.Visual.VariantId));
            var chosen = EditorGUILayout.Popup(current, ids.ToArray(), GUILayout.Width(110f));
            if (chosen != current)
                LevelEditorCommands.Modify(level, "Change Light Fixture",
                    () => LevelEditing.SetLightVisual(light, chosen == 0 ? null : ids[chosen]));
        }
    }
}
