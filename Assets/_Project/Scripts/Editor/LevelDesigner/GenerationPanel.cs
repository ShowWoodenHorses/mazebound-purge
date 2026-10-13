using Maze.Core.Generation;
using Maze.Core.Level;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>Generation settings and the destructive "Generate New" command (ТЗ §12–16).</summary>
    internal sealed class GenerationPanel
    {
        public void OnGUI(LevelDesignerState state)
        {
            var level = state.Level;
            var settings = level.Generation;

            EditorGUILayout.LabelField("Generation Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var width = EditorGUILayout.IntField("Width", settings.Width);
            var height = EditorGUILayout.IntField("Height", settings.Height);
            var mazeSeed = SeedField("Maze Seed", settings.MazeSeed);
            var visualSeed = SeedField("Visual Seed", settings.VisualSeed);
            var algorithm = (MazeAlgorithm)EditorGUILayout.EnumPopup("Algorithm", settings.MazeAlgorithm);
            var loopDensity = EditorGUILayout.Slider("Loop Density", settings.LoopDensity, 0f, 1f);
            var starts = Mathf.Max(1, EditorGUILayout.IntField("Player Starts", settings.InitialPlayerStartCount));
            var exits = Mathf.Max(1, EditorGUILayout.IntField("Exits", settings.InitialExitCount));

            if (EditorGUI.EndChangeCheck())
            {
                LevelEditorCommands.Modify(level, "Change Generation Settings", () =>
                {
                    settings.Width = width;
                    settings.Height = height;
                    settings.MazeSeed = mazeSeed;
                    settings.VisualSeed = visualSeed;
                    settings.MazeAlgorithm = algorithm;
                    settings.LoopDensity = loopDensity;
                    settings.InitialPlayerStartCount = starts;
                    settings.InitialExitCount = exits;
                });
            }

            var canGenerate = true;
            if (!settings.HasOddSize)
            {
                EditorGUILayout.HelpBox("Width and Height must be odd (e.g. 21 x 21): border wall + 1-cell passages and walls.",
                    MessageType.Error);
                canGenerate = false;
            }

            if (settings.Width < MazeGenerator.MinSize || settings.Height < MazeGenerator.MinSize)
            {
                EditorGUILayout.HelpBox($"Minimum size is {MazeGenerator.MinSize}x{MazeGenerator.MinSize}.", MessageType.Error);
                canGenerate = false;
            }

            var geometry = level.Geometry;
            if (geometry != null && (geometry.Width != settings.Width || geometry.Height != settings.Height))
                EditorGUILayout.HelpBox($"Current level is {geometry.Width}x{geometry.Height}. New size applies on Generate New.", MessageType.Info);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!canGenerate))
            {
                if (GUILayout.Button("Generate New", GUILayout.Height(30f)) && LevelEditorCommands.GenerateNew(level))
                    state.OnLevelReplaced();
            }

            EditorGUILayout.HelpBox(
                "Generate New is destructive: geometry, all objects and the visual design (including overrides) are replaced. " +
                "It never runs automatically.", MessageType.None);

            TutorialGUI(state);
        }

        /// <summary>
        /// The tutorial script of the level: a level with one is the game's tutorial (Build / Sync puts it on the main
        /// menu's Tutorial button, apart from the numbered levels). Steps are edited in the script's inspector.
        /// </summary>
        private static void TutorialGUI(LevelDesignerState state)
        {
            var level = state.Level;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tutorial", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var script = (TutorialScript)EditorGUILayout.ObjectField(new GUIContent("Tutorial Script",
                    "Only for the tutorial level: steps with texts, goals and targets. Build / Sync then lists the level " +
                    "as the tutorial (main menu button), not as a numbered level."),
                level.Tutorial, typeof(TutorialScript), false);
            if (EditorGUI.EndChangeCheck())
            {
                LevelEditorCommands.Modify(level, "Change Tutorial Script", () => level.Tutorial = script);
                state.Revalidate();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (level.Tutorial == null && GUILayout.Button("Create Tutorial Script"))
                {
                    var path = AssetDatabase.GenerateUniqueAssetPath(
                        System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(level)) + "/" + level.name + "_Tutorial.asset");
                    var created = ScriptableObject.CreateInstance<TutorialScript>();
                    AssetDatabase.CreateAsset(created, path);
                    LevelEditorCommands.Modify(level, "Create Tutorial Script", () => level.Tutorial = created);
                    AssetDatabase.SaveAssets();
                    state.Revalidate();
                }

                if (level.Tutorial != null && GUILayout.Button("Edit Steps"))
                    Selection.activeObject = level.Tutorial;
            }
        }

        private static int SeedField(string label, int value)
        {
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.IntField(label, value);
            if (GUILayout.Button("Random", GUILayout.Width(60f)))
            {
                value = LevelEditorCommands.NewRandomSeed();
                GUI.changed = true;
            }

            EditorGUILayout.EndHorizontal();
            return value;
        }
    }
}
