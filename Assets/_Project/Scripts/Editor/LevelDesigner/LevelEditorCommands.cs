using System;
using Purge.Core.Authoring;
using Purge.Core.Grid;
using Purge.Core.Level;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.LevelDesigner
{
    /// <summary>
    /// Every change the tool makes to a LevelData goes through here: Undo record, mutation, SetDirty.
    /// Destructive commands ask for confirmation (ТЗ §15, §95).
    /// </summary>
    internal static class LevelEditorCommands
    {
        public const string DefaultLevelFolder = "Assets/_Project/Data/Levels";

        public static void Modify(LevelData level, string undoName, Action change)
        {
            Undo.RecordObject(level, undoName);
            change();
            EditorUtility.SetDirty(level);
        }

        /// <summary>Problems the last generation reported (arena did not fit…); shown by the Generation panel.</summary>
        public static System.Collections.Generic.IReadOnlyList<string> LastProblems { get; private set; } =
            Array.Empty<string>();

        public static bool GenerateNew(LevelData level)
        {
            if (HasDesign(level) && !EditorUtility.DisplayDialog("Generate New",
                    "Generate New replaces the geometry, removes every object and the whole visual design, " +
                    "including manual overrides, hand-edit marks and locked areas.\n\nYou can undo it with Ctrl+Z.",
                    "Generate", "Cancel"))
                return false;

            return Run(level, "Generate New Level", () => LevelAuthoring.GenerateNew(level));
        }

        /// <summary>Generates again with the level's recipe, keeping hand-edited cells and locked areas.</summary>
        public static bool Generate(LevelData level)
        {
            var kept = level.Marks.ManualCount;
            if (HasDesign(level) && !EditorUtility.DisplayDialog("Generate",
                    $"Generate replaces everything except {kept} hand-edited cell(s) and {level.Marks.LockedAreas.Count} " +
                    "locked area(s): they keep their cells, objects and visual overrides.\n\nYou can undo it with Ctrl+Z.",
                    "Generate", "Cancel"))
                return false;

            return Run(level, "Generate Level", () => LevelAuthoring.Generate(level));
        }

        public static bool ClearManualMarks(LevelData level)
        {
            if (!EditorUtility.DisplayDialog("Clear Hand-Edit Marks",
                    $"{level.Marks.ManualCount} cell(s) are marked as edited by hand. Without the marks, Generate may " +
                    "change them again. Locked areas stay.", "Clear", "Cancel"))
                return false;

            Modify(level, "Clear Hand-Edit Marks", () => LevelEditing.ClearManualMarks(level));
            return true;
        }

        private static bool Run(LevelData level, string undoName, Func<Purge.Core.Generation.GenerationResult> generate)
        {
            Undo.RegisterCompleteObjectUndo(level, undoName);
            DecorHeights.Refresh(level.VisualTheme);
            try
            {
                LastProblems = generate().Problems;
            }
            catch (ArgumentException exception)
            {
                EditorUtility.DisplayDialog(undoName, exception.Message, "OK");
                return false;
            }

            EditorUtility.SetDirty(level);
            return true;
        }

        public static bool RegenerateVisuals(LevelData level, bool clearOverrides)
        {
            var overrides = level.VisualData.CellOverrides.Count + level.VisualData.ObjectOverrides.Count;
            var message = "Regenerate Visuals recreates the automatic visual assignments from the VisualSeed. " +
                          "Geometry and objects are not changed.\n\n" +
                          (clearOverrides
                              ? $"All {overrides} manual override(s) will be removed."
                              : $"{overrides} manual override(s) will be kept.");

            if (!EditorUtility.DisplayDialog("Regenerate Visuals", message, "Regenerate", "Cancel"))
                return false;

            Undo.RegisterCompleteObjectUndo(level, clearOverrides ? "Regenerate Visuals + Clear Overrides" : "Regenerate Visuals");
            DecorHeights.Refresh(level.VisualTheme);
            LevelAuthoring.RegenerateVisuals(level, clearOverrides);
            EditorUtility.SetDirty(level);
            return true;
        }

        public static LevelData CreateLevelAsset()
        {
            var folder = AssetDatabase.IsValidFolder(DefaultLevelFolder) ? DefaultLevelFolder : "Assets";
            var path = EditorUtility.SaveFilePanelInProject("Create Level", "Level_01", "asset", "Choose where to save the new level.", folder);
            if (string.IsNullOrEmpty(path))
                return null;

            var level = ScriptableObject.CreateInstance<LevelData>();
            AssetDatabase.CreateAsset(level, path);
            AssetDatabase.SaveAssets();
            return level;
        }

        public static int NewRandomSeed() => new System.Random().Next(int.MinValue, int.MaxValue);

        private static bool HasDesign(LevelData level)
        {
            foreach (var _ in level.AllEntities())
                return true;

            var geometry = level.Geometry;
            if (geometry == null || !geometry.IsConsistent)
                return false;

            for (var i = 0; i < geometry.CellCount; i++)
                if (geometry.GetCell(geometry.ToPosition(i)) != CellType.Wall)
                    return true;

            return false;
        }
    }
}
