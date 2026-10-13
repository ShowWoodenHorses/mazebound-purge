using Cysharp.Threading.Tasks;
using Maze.Application.Flow;
using Maze.Composition;
using Maze.Core.Level;
using Maze.Editor.Dev;
using Maze.Gameplay.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VContainer;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>
    /// Test / Play from the Level Designer (ТЗ §16, §61 "Test from Start #N"): validates and syncs the level, enters
    /// Play mode from the Bootstrap scene (whatever scenes are open) and, once the application reaches the main menu,
    /// starts the level through <see cref="GameFlow"/> — the same path as the menu, only with a forced start.
    /// The request survives the domain reload of entering Play mode in <see cref="SessionState"/>.
    /// Retry keeps the start; leaving to the menu works as usual.
    /// </summary>
    [InitializeOnLoad]
    internal static class LevelPlayLauncher
    {
        private const string LevelKey = "Maze.LevelPlayLauncher.Level";
        private const string StartKey = "Maze.LevelPlayLauncher.Start";
        private const string StartSceneKey = "Maze.LevelPlayLauncher.StartSceneSet";
        private const int RandomStart = -1;
        private const double TimeoutSeconds = 30d;

        private static double _deadline;

        static LevelPlayLauncher()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <param name="startIndex">Index in <see cref="LevelData.PlayerStarts"/>; null = random start, as in the game.</param>
        public static bool Launch(LevelData level, int? startIndex, out string problem)
        {
            problem = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                problem = "Already in Play mode: stop it first.";
                return false;
            }

            if (startIndex.HasValue && (startIndex < 0 || startIndex >= level.PlayerStarts.Count))
            {
                problem = $"The level has no start #{startIndex}.";
                return false;
            }

            var report = EditorLevelValidator.Validate(level);
            if (!report.IsValid)
            {
                problem = $"The level has {report.ErrorCount} validation error(s): fix them first (Validate tab).";
                return false;
            }

            // The game loads levels through Addressables and the catalog.
            if (!LevelSync.Sync(level))
            {
                problem = "Build / Sync failed.";
                return false;
            }

            var bootstrap = AssetDatabase.LoadAssetAtPath<SceneAsset>(RuntimeScenesBuilder.BootstrapPath);
            if (bootstrap == null)
            {
                problem = "No Bootstrap scene: run Maze → Dev → Build Runtime Scenes.";
                return false;
            }

            SessionState.SetString(LevelKey, level.name);
            SessionState.SetInt(StartKey, startIndex ?? RandomStart);
            if (EditorSceneManager.playModeStartScene == null)
            {
                EditorSceneManager.playModeStartScene = bootstrap;
                SessionState.SetBool(StartSceneKey, true);
            }

            Debug.Log($"[Maze] Test level '{level.name}' from " + (startIndex.HasValue ? $"start #{startIndex}." : "a random start."));
            EditorApplication.isPlaying = true;
            return true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    if (!string.IsNullOrEmpty(SessionState.GetString(LevelKey, null)))
                    {
                        _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
                        EditorApplication.update += WaitForMenu;
                    }
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    Cancel();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    Cancel();
                    // Pressing Play normally starts from the open scenes again.
                    if (SessionState.GetBool(StartSceneKey, false))
                    {
                        EditorSceneManager.playModeStartScene = null;
                        SessionState.EraseBool(StartSceneKey);
                    }
                    break;
            }
        }

        private static void WaitForMenu()
        {
            var flow = FindFlow();
            if (flow == null || flow.State == GameFlowState.None || flow.State == GameFlowState.Initializing)
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                {
                    Debug.LogError("[Maze] Test level: the application did not reach the main menu.");
                    Cancel();
                }

                return;
            }

            var levelId = SessionState.GetString(LevelKey, null);
            var start = SessionState.GetInt(StartKey, RandomStart);
            Cancel();
            if (flow.State != GameFlowState.MainMenu)
            {
                Debug.LogError($"[Maze] Test level: the application is in state {flow.State}, not in the main menu.");
                return;
            }

            var options = start == RandomStart ? LevelLaunchOptions.Default : new LevelLaunchOptions(startIndex: start);
            flow.StartLevel(levelId, options).Forget();
        }

        private static GameFlow FindFlow()
        {
            // Editor tooling only: the runtime itself never searches for objects.
            var scope = Object.FindFirstObjectByType<ProjectLifetimeScope>();
            return scope != null && scope.Container != null ? scope.Container.Resolve<GameFlow>() : null;
        }

        private static void Cancel()
        {
            EditorApplication.update -= WaitForMenu;
            SessionState.EraseString(LevelKey);
            SessionState.EraseInt(StartKey);
        }
    }
}
