using System;
using Maze.Core.Authoring;
using Maze.Core.Grid;
using Maze.Core.Level;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
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

        public static bool GenerateNew(LevelData level)
        {
            if (HasDesign(level) && !EditorUtility.DisplayDialog("Generate New",
                    "Generate New replaces the geometry, removes every object and the whole visual design, " +
                    "including manual overrides.\n\nYou can undo it with Ctrl+Z.",
                    "Generate", "Cancel"))
                return false;

            Undo.RegisterCompleteObjectUndo(level, "Generate New Level");
            DecorHeights.Refresh(level.VisualTheme);
            try
            {
                LevelAuthoring.GenerateNew(level);
            }
            catch (ArgumentException exception)
            {
                EditorUtility.DisplayDialog("Generate New", exception.Message, "OK");
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
