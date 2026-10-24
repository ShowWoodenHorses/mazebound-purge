using Purge.Core.Generation;
using Purge.Core.Level;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.LevelDesigner
{
    /// <summary>Generation settings, the biome recipe, "Generate" (keeps hand edits) and "Generate New" (ТЗ §12–16, stage 7).</summary>
    internal sealed class GenerationPanel
    {
        public void OnGUI(LevelDesignerState state)
        {
            var level = state.Level;
            var settings = level.Generation;

            EditorGUILayout.LabelField("Generation Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var recipe = (BiomeRecipe)EditorGUILayout.ObjectField(new GUIContent("Biome Recipe",
                    "Steps of the biome generator (Data/Recipes). Empty = an open field with a border wall (dev levels)."),
                settings.Recipe, typeof(BiomeRecipe), false);
            var width = EditorGUILayout.IntField("Width", settings.Width);
            var height = EditorGUILayout.IntField("Height", settings.Height);
            var layoutSeed = SeedField("Layout Seed", settings.LayoutSeed);
            var visualSeed = SeedField("Visual Seed", settings.VisualSeed);
            var obstacles = settings.ObstacleDensity;
            if (recipe == null)
                obstacles = EditorGUILayout.Slider(new GUIContent("Obstacle Density",
                    "Single-cell pillars inside the field (cells with both coordinates even); 0 = open field."),
                    settings.ObstacleDensity, 0f, 1f);

            if (EditorGUI.EndChangeCheck())
            {
                LevelEditorCommands.Modify(level, "Change Generation Settings", () =>
                {
                    settings.Recipe = recipe;
                    settings.Width = width;
                    settings.Height = height;
                    settings.LayoutSeed = layoutSeed;
                    settings.VisualSeed = visualSeed;
                    settings.ObstacleDensity = obstacles;
                });
            }

            var canGenerate = true;
            var minSize = settings.Recipe != null ? BiomeGenerator.MinSize : OpenFieldGenerator.MinSize;
            if (settings.Width < minSize || settings.Height < minSize)
            {
                EditorGUILayout.HelpBox($"Minimum size is {minSize}x{minSize}.", MessageType.Error);
                canGenerate = false;
            }

            if (settings.Recipe != null)
                foreach (var problem in BiomeGenerator.Validate(settings.Recipe))
                    EditorGUILayout.HelpBox(problem, MessageType.Warning);

            var geometry = level.Geometry;
            if (geometry != null && (geometry.Width != settings.Width || geometry.Height != settings.Height))
                EditorGUILayout.HelpBox($"Current level is {geometry.Width}x{geometry.Height}. New size applies on the next generation " +
                                        "(hand-edit marks are dropped).", MessageType.Info);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!canGenerate))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(settings.Recipe == null))
                    if (GUILayout.Button(new GUIContent("Generate", "Again with the recipe; hand-edited cells and locked areas stay."),
                            GUILayout.Height(30f)) && LevelEditorCommands.Generate(level))
                        state.OnLevelReplaced();

                if (GUILayout.Button(new GUIContent("Generate New", "Everything from scratch, marks and locked areas too."),
                        GUILayout.Height(30f)) && LevelEditorCommands.GenerateNew(level))
                    state.OnLevelReplaced();
            }

            foreach (var problem in LevelEditorCommands.LastProblems)
                EditorGUILayout.HelpBox(problem, MessageType.Warning);

            var marks = level.Marks;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Hand-edited cells: {marks.ManualCount}, locked areas: {marks.LockedAreas.Count}");
                using (new EditorGUI.DisabledScope(marks.ManualCount == 0))
                    if (GUILayout.Button("Clear Marks", GUILayout.Width(90f)) && LevelEditorCommands.ClearManualMarks(level))
                        state.Revalidate();
            }

            EditorGUILayout.HelpBox(
                "Generate runs the biome recipe again and keeps the cells edited by hand (every edit marks its cell) and the " +
                "locked areas, with their objects and visual overrides. Generate New is destructive: geometry, all objects, " +
                "the visual design, marks and locked areas are replaced. Neither runs automatically. Without a recipe both " +
                "make an open field with a border wall and one start in the centre.", MessageType.None);

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
