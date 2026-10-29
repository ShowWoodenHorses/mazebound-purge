using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Purge.Application.Flow;
using Purge.Application.Save;
using Purge.Composition;
using Purge.Core.Balance;
using Purge.Core.Cards;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Editor.Dev;
using Purge.Gameplay.Balance;
using Purge.Gameplay.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VContainer;

namespace Purge.Editor.Balance
{
    /// <summary>
    /// The balance bot's batch (stage 10a): Purge → Balance → Run Bot plays every level × difficulty × run of
    /// <see cref="BotPlan"/> in Play mode, one after another through <see cref="GameFlow"/> (menu → level → result →
    /// menu), the bot in place of the player (<see cref="LevelLaunchOptions.Bot"/>). Each finished run is appended to
    /// Tools/Balance/Bot/&lt;batch&gt;/runs.csv and waves.csv at once, so an interrupted batch keeps its runs and
    /// Continue Bot Batch plays only the missing ones. The save of the editor is kept aside and put back when Play mode
    /// ends (the runs' money and progress are not the player's). Real time: no time scale.
    /// </summary>
    [InitializeOnLoad]
    internal static class BotRunner
    {
        public const string PlanPath = "Assets/_Project/Data/Balance/BotPlan.asset";
        private const string Root = "Tools/Balance/Bot";
        private const string RunsFile = "runs.csv";
        private const string WavesFile = "waves.csv";
        private const string SaveBackupFile = "save_backup.txt";
        private const string ActiveKey = "Purge.BotRunner.Active";
        private const string StartSceneKey = "Purge.BotRunner.StartSceneSet";
        private const string BatchPref = "Purge.BotRunner.Batch";
        private const double StartTimeout = 60d;

        private static readonly string[] DefaultCards =
        {
            "Card_Damage", "Card_FireRate", "Card_Crit", "Card_Health", "Card_Magazine", "Card_Reload", "Card_Speed",
            "Card_HealOnKill", "Card_Explosives", "Card_Throwables", "Card_Magnet", "Card_Burning",
        };

        private static readonly string RunsHeader =
            "level,difficulty,run,seed,outcome,minutes,waves_cleared,waves," +
            "kills_mass,kills_officer,kills_elite,kills_boss,kpm_mass,kpm_officer,kpm_elite,kpm_boss," +
            "money,kill_money,wave_money,xp_level,experience,damage_taken,healed,throws,chests,weapons_taken," +
            "weapon1,weapon2,cards";

        private static readonly string WavesHeader =
            "level,difficulty,run,wave,start_s,duration_s,cleared,kills_mass,kills_officer,kills_elite,kills_boss," +
            "money,experience,damage_taken,xp_level,hp,stand_s,flee_s,errand_s,approach_s,arena_s,shoot_s";

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private static double _deadline;
        private static BotPlan _plan;
        private static string _batch;
        private static List<Job> _jobs;
        private static Job _current;
        private static BotRun _run;
        private static bool _leaving;
        private static bool _hooked;
        private static bool _runInBackground;

        static BotRunner()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Purge/Balance/Run Bot", priority = 0)]
        private static void RunBot()
        {
            var plan = LoadOrCreatePlan();
            if (!CanStart(plan)) return;
            var batch = Path.Combine(Root, DateTime.Now.ToString("yyyyMMdd-HHmm", Invariant));
            Directory.CreateDirectory(batch);
            File.WriteAllText(Path.Combine(batch, RunsFile), RunsHeader + "\n");
            File.WriteAllText(Path.Combine(batch, WavesFile), WavesHeader + "\n");
            EditorPrefs.SetString(BatchPref, batch);
            Launch(batch);
        }

        [MenuItem("Purge/Balance/Continue Bot Batch", priority = 1)]
        private static void ContinueBatch()
        {
            var batch = EditorPrefs.GetString(BatchPref, null);
            if (string.IsNullOrEmpty(batch) || !File.Exists(Path.Combine(batch, RunsFile)))
            {
                EditorUtility.DisplayDialog("Bot", "No batch to continue: run the bot first.", "OK");
                return;
            }

            if (!CanStart(LoadOrCreatePlan())) return;
            Launch(batch);
        }

        [MenuItem("Purge/Balance/Stop Bot", priority = 2)]
        private static void StopBot()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            Debug.Log("[Purge] Bot: stopped; the unfinished run is played again on Continue Bot Batch.");
            EditorApplication.isPlaying = false;
        }

        [MenuItem("Purge/Balance/Bot Plan", priority = 20)]
        private static void SelectPlan()
        {
            Selection.activeObject = LoadOrCreatePlan();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        private static bool CanStart(BotPlan plan)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Bot", "Stop Play mode first.", "OK");
                return false;
            }

            if (plan.Levels.Count == 0 || plan.Levels.Any(level => level == null) || plan.Difficulties.Any(d => d == null))
            {
                EditorUtility.DisplayDialog("Bot", "The bot plan needs levels (and no empty rows): " + PlanPath, "OK");
                Selection.activeObject = plan;
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RuntimeScenesBuilder.BootstrapPath) == null)
            {
                EditorUtility.DisplayDialog("Bot", "No Bootstrap scene: run Purge → Dev → Build Runtime Scenes.", "OK");
                return false;
            }

            return true;
        }

        private static void Launch(string batch)
        {
            var backup = Path.Combine(batch, SaveBackupFile);
            if (!File.Exists(backup))
                File.WriteAllText(backup, PlayerPrefs.HasKey(PlayerPrefsSaveStorage.Key) ? "1\n" + PlayerPrefs.GetString(PlayerPrefsSaveStorage.Key) : "0\n");

            SessionState.SetBool(ActiveKey, true);
            if (EditorSceneManager.playModeStartScene == null)
            {
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(RuntimeScenesBuilder.BootstrapPath);
                SessionState.SetBool(StartSceneKey, true);
            }

            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    if (SessionState.GetBool(ActiveKey, false)) Begin();
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    Unhook();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    if (!SessionState.GetBool(ActiveKey, false)) return;
                    SessionState.EraseBool(ActiveKey);
                    RestoreSave(EditorPrefs.GetString(BatchPref, null));
                    if (SessionState.GetBool(StartSceneKey, false))
                    {
                        EditorSceneManager.playModeStartScene = null;
                        SessionState.EraseBool(StartSceneKey);
                    }

                    break;
            }
        }

        private static void Begin()
        {
            _plan = LoadOrCreatePlan();
            _batch = EditorPrefs.GetString(BatchPref, null);
            _jobs = PendingJobs(_plan, _batch);
            _current = null;
            _run = null;
            _leaving = false;
            _deadline = EditorApplication.timeSinceStartup + StartTimeout;
            _runInBackground = UnityEngine.Application.runInBackground;
            UnityEngine.Application.runInBackground = true; // the batch goes on while the editor is in the background
            Debug.Log($"[Purge] Bot: {_jobs.Count} run(s) to play → {_batch}");
            EditorApplication.update += Drive;
            _hooked = true;
        }

        private static void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            EditorApplication.update -= Drive;
            UnityEngine.Application.runInBackground = _runInBackground;
        }

        /// <summary>Editor update while the batch plays: one step of menu → level → result → menu at a time.</summary>
        private static void Drive()
        {
            try
            {
                Step();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish("an exception");
            }
        }

        private static void Step()
        {
            var flow = FindFlow();
            if (flow == null || flow.State == GameFlowState.None || flow.State == GameFlowState.Initializing)
            {
                if (EditorApplication.timeSinceStartup > _deadline) Finish("the application did not start");
                return;
            }

            switch (flow.State)
            {
                case GameFlowState.MainMenu:
                    if (_current != null) return; // a run's result was written, the menu is opening
                    StartNext(flow);
                    return;

                case GameFlowState.Loading:
                    return;

                case GameFlowState.Completed:
                case GameFlowState.Failed:
                    if (_current == null || _leaving) return;
                    Record(flow.State == GameFlowState.Completed ? "won" : _run.Report.Died ? "died" : "failed");
                    Leave(flow);
                    return;

                case GameFlowState.Error:
                    if (_current != null && !_leaving) Record("error");
                    Finish("an error: " + flow.ErrorMessage);
                    return;

                case GameFlowState.Paused:
                    flow.ResumeGameplay();
                    return;
            }

            // Playing (the bot takes the cards itself before their window; no revive window — it never opens).
            if (_current != null && !_leaving && _run.Report.Duration > _plan.MaxMinutes * 60f)
            {
                Record("timeout");
                Leave(flow);
            }
        }

        private static void StartNext(GameFlow flow)
        {
            if (_jobs.Count == 0)
            {
                Finish(null);
                return;
            }

            _current = _jobs[0];
            _jobs.RemoveAt(0);
            _run = new BotRun(_plan);
            _leaving = false;
            Debug.Log($"[Purge] Bot: {_current.Level.name} · {DifficultyName(_current.Difficulty)} · run {_current.Run + 1} " +
                      $"(seed {_current.Seed}); {_jobs.Count} left.");
            var options = new LevelLaunchOptions(seed: _current.Seed, difficulty: _current.Difficulty, bot: _run);
            flow.StartLevel(_current.Level.name, options).Forget();
        }

        private static void Leave(GameFlow flow)
        {
            _leaving = true;
            LeaveAsync(flow).Forget();
        }

        private static async UniTaskVoid LeaveAsync(GameFlow flow)
        {
            await flow.ExitToMenu();
            _current = null;
            _run = null;
            _leaving = false;
        }

        private static void Finish(string problem)
        {
            Unhook();
            if (problem == null) Debug.Log($"[Purge] Bot: the batch is done → {_batch}");
            else Debug.LogError($"[Purge] Bot: stopped by {problem}. Continue Bot Batch plays the rest.");
            EditorApplication.isPlaying = false;
        }

        // ── Jobs ────────────────────────────────────────────────────────────────────────────────────────────────────

        private sealed class Job
        {
            public LevelData Level;
            public DifficultyDefinition Difficulty;
            public int Run;
            public int Seed;

            public string Key => $"{Level.name},{DifficultyName(Difficulty)},{Run + 1}";
        }

        /// <summary>Every level × difficulty × run of the plan not yet in the batch's runs.csv.</summary>
        private static List<Job> PendingJobs(BotPlan plan, string batch)
        {
            var done = new HashSet<string>();
            var runs = Path.Combine(batch, RunsFile);
            if (File.Exists(runs))
                foreach (var line in File.ReadAllLines(runs).Skip(1))
                {
                    var parts = line.Split(',');
                    if (parts.Length > 3) done.Add($"{parts[0]},{parts[1]},{parts[2]}");
                }

            var jobs = new List<Job>();
            var difficulties = plan.Difficulties.Count > 0 ? plan.Difficulties : new DifficultyDefinition[] { null };
            foreach (var level in plan.Levels)
            foreach (var difficulty in difficulties)
                for (var run = 0; run < plan.Runs; run++)
                {
                    var job = new Job { Level = level, Difficulty = difficulty, Run = run, Seed = plan.Seed + run };
                    if (!done.Contains(job.Key)) jobs.Add(job);
                }

            return jobs;
        }

        private static string DifficultyName(DifficultyDefinition difficulty) =>
            difficulty != null ? difficulty.Level.ToString() : "Default";

        // ── CSV ─────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Record(string outcome)
        {
            var report = _run.Report;
            var minutes = Mathf.Max(report.Duration, 1f) / 60f;
            var kills = report.Kills;
            var waveMoney = report.Money - Mathf.FloorToInt(report.KillMoney);
            var row = new StringBuilder(_current.Key)
                .Append(',').Append(_current.Seed)
                .Append(',').Append(outcome)
                .Append(',').Append(F(report.Duration / 60f))
                .Append(',').Append(report.WavesCleared)
                .Append(',').Append(report.WaveCount);
            foreach (var count in kills) row.Append(',').Append(count);
            foreach (var count in kills) row.Append(',').Append(F(count / minutes));
            row.Append(',').Append(report.Money)
                .Append(',').Append(F(report.KillMoney))
                .Append(',').Append(Mathf.Max(0, waveMoney))
                .Append(',').Append(report.XpLevel)
                .Append(',').Append(F(report.Experience))
                .Append(',').Append(report.DamageTaken)
                .Append(',').Append(report.Healed)
                .Append(',').Append(report.Throws)
                .Append(',').Append(report.ChestsOpened)
                .Append(',').Append(report.WeaponsTaken)
                .Append(',').Append(report.Weapons[0])
                .Append(',').Append(report.Weapons[1])
                .Append(',').Append(string.Join(";", report.Cards));
            File.AppendAllText(Path.Combine(_batch, RunsFile), row + "\n");

            var waves = new StringBuilder();
            foreach (var wave in report.Waves)
            {
                waves.Append(_current.Key)
                    .Append(',').Append(wave.Index + 1)
                    .Append(',').Append(F(wave.Start))
                    .Append(',').Append(F(wave.Duration >= 0f ? wave.Duration : report.Duration - wave.Start))
                    .Append(',').Append(wave.Duration >= 0f ? 1 : 0);
                foreach (var count in wave.Kills) waves.Append(',').Append(count);
                waves.Append(',').Append(wave.Money)
                    .Append(',').Append(F(wave.Experience))
                    .Append(',').Append(wave.DamageTaken)
                    .Append(',').Append(wave.XpLevel)
                    .Append(',').Append(wave.Health);
                foreach (var seconds in wave.Seconds) waves.Append(',').Append(F(seconds));
                waves.Append(',').Append(F(wave.ShootSeconds)).Append('\n');
            }

            File.AppendAllText(Path.Combine(_batch, WavesFile), waves.ToString());
            Debug.Log($"[Purge] Bot: {_current.Key} — {outcome}, {F(report.Duration / 60f)} min, waves {report.WavesCleared}/{report.WaveCount}, " +
                      $"money {report.Money}, level {report.XpLevel}.");
        }

        private static string F(float value) => value.ToString("0.##", Invariant);

        // ── Save and plan ───────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The editor's save as it was before the batch (the runs wrote money and progress into it).</summary>
        private static void RestoreSave(string batch)
        {
            var backup = string.IsNullOrEmpty(batch) ? null : Path.Combine(batch, SaveBackupFile);
            if (backup == null || !File.Exists(backup)) return;
            var text = File.ReadAllText(backup);
            if (text.StartsWith("1\n", StringComparison.Ordinal)) PlayerPrefs.SetString(PlayerPrefsSaveStorage.Key, text.Substring(2));
            else PlayerPrefs.DeleteKey(PlayerPrefsSaveStorage.Key);
            PlayerPrefs.Save();
            Debug.Log("[Purge] Bot: the editor's save is back as before the batch.");
        }

        private static BotPlan LoadOrCreatePlan()
        {
            var plan = AssetDatabase.LoadAssetAtPath<BotPlan>(PlanPath);
            if (plan != null) return plan;

            plan = ScriptableObject.CreateInstance<BotPlan>();
            var levels = new[] { "Level_Run", "Level_Forest_01" }.Select(FindAsset<LevelData>).Where(level => level != null);
            var difficulties = AssetDatabase.FindAssets("t:DifficultyDefinition", new[] { "Assets/_Project/Data/Difficulty" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<DifficultyDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .OrderBy(difficulty => difficulty.Level);
            var cards = DefaultCards.Select(FindAsset<CardDefinition>).Where(card => card != null);
            plan.Configure(levels, difficulties, 3, cards);
            Directory.CreateDirectory(Path.GetDirectoryName(PlanPath));
            AssetDatabase.CreateAsset(plan, PlanPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Purge] Bot: created the plan " + PlanPath);
            return plan;
        }

        private static T FindAsset<T>(string name) where T : UnityEngine.Object
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.name == name) return asset;
            }

            return null;
        }

        private static GameFlow FindFlow()
        {
            // Editor tooling only: the runtime itself never searches for objects.
            var scope = UnityEngine.Object.FindFirstObjectByType<ProjectLifetimeScope>();
            return scope != null && scope.Container != null ? scope.Container.Resolve<GameFlow>() : null;
        }
    }
}
