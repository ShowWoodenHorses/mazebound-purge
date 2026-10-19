using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Application.Flow;
using Purge.Application.Levels;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Core.Authoring;
using Purge.Core.Level;
using Purge.Gameplay.Level;
using Purge.Gameplay.Run;
using Purge.Tests.EditMode.Common;
using Purge.Tests.EditMode.Visual;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Flow
{
    public class GameFlowTests
    {
        private const string ValidId = "valid";
        private const string OtherId = "other";
        private const string InvalidId = "invalid";
        private const string MissingAssetId = "missing_asset";

        private VisualFixture _fixture;
        private LevelData _invalidLevel;
        private LevelCatalog _catalogAsset;
        private FakeAddressables _addressables;
        private FakeSessionFactory _sessions;
        private FakeProgress _progress;
        private List<string> _initialized;
        private GameFlow _flow;
        private List<GameFlowState> _states;

        [SetUp]
        public void SetUp()
        {
            _fixture = new VisualFixture();
            LevelAuthoring.GenerateNew(_fixture.Level);
            _invalidLevel = ScriptableObject.CreateInstance<LevelData>(); // no player start

            _catalogAsset = ScriptableObject.CreateInstance<LevelCatalog>();
            _catalogAsset.AddOrUpdate(ValidId, "Levels/valid", "Valid");
            _catalogAsset.AddOrUpdate(OtherId, "Levels/other", "Other");
            _catalogAsset.AddOrUpdate(InvalidId, "Levels/invalid", "Invalid");
            _catalogAsset.AddOrUpdate(MissingAssetId, "Levels/missing", "Missing");

            _addressables = new FakeAddressables();
            _addressables.Assets["Levels/valid"] = _fixture.Level;
            _addressables.Assets["Levels/other"] = _fixture.Level;
            _addressables.Assets["Levels/invalid"] = _invalidLevel;

            _sessions = new FakeSessionFactory();
            _progress = new FakeProgress();
            _initialized = new List<string>();
            _flow = CreateFlow(new RecordingService("A", _initialized), new RecordingService("B", _initialized));
        }

        [TearDown]
        public void TearDown()
        {
            _flow.Dispose();
            _fixture.Dispose();
            Object.DestroyImmediate(_invalidLevel);
            Object.DestroyImmediate(_catalogAsset);
        }

        private GameFlow CreateFlow(params IApplicationService[] services)
        {
            var flow = new GameFlow(services, new FakeCatalog(_catalogAsset), _addressables, _sessions, _progress);
            _states = new List<GameFlowState>();
            flow.StateChanged += _states.Add;
            return flow;
        }

        private async UniTask BootToMenu()
        {
            await _flow.InitializeApplication();
            await _flow.OpenMainMenu();
        }

        // ------------------------------------------------------------ Initialization

        [UnityTest]
        public IEnumerator Initialize_RunsServicesInRegistrationOrder_ThenMainMenu() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();

            CollectionAssert.AreEqual(new[] { "A", "B" }, _initialized);
            CollectionAssert.AreEqual(new[] { GameFlowState.Initializing, GameFlowState.MainMenu }, _states);
        });

        [UnityTest]
        public IEnumerator Initialize_ServiceFails_EntersErrorState() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.ignoreFailingMessages = true;
            _flow.Dispose();
            _flow = CreateFlow(new RecordingService("A", _initialized, fail: true));

            await _flow.InitializeApplication();

            Assert.AreEqual(GameFlowState.Error, _flow.State);
            StringAssert.Contains("A failed", _flow.ErrorMessage);
        });

        // ------------------------------------------------------------ Start level

        [UnityTest]
        public IEnumerator StartLevel_LoadsLevelAndStartsGameplay() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);

            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.AreEqual(ValidId, _flow.CurrentLevelId);
            var session = _sessions.Created[0];
            Assert.AreSame(_fixture.Level, session.Level);
            Assert.IsTrue(session.Loaded && session.Started);
            Assert.AreEqual(1, _addressables.ActiveHandleCount, "The level asset is held by the level owner.");
            CollectionAssert.AreEqual(
                new[] { GameFlowState.Initializing, GameFlowState.MainMenu, GameFlowState.Loading, GameFlowState.Playing }, _states);
        });

        [UnityTest]
        public IEnumerator ExitToMenu_UnloadsLevel_AndReleasesItsAssets() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            await _flow.ExitToMenu();

            Assert.AreEqual(GameFlowState.MainMenu, _flow.State);
            Assert.IsTrue(_sessions.Created[0].Unloaded);
            Assert.IsFalse(_flow.HasLevel);
            Assert.AreEqual(0, _addressables.OwnerCount);
            Assert.AreEqual(0, _addressables.ActiveHandleCount);
        });

        [UnityTest]
        public IEnumerator StartLevel_WhilePlaying_UnloadsOldLevelBeforeCreatingNew() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            var first = _sessions.Created[0];
            _sessions.OnCreate = () => Assert.IsTrue(first.Unloaded, "Old LevelScope must be disposed first (ТЗ §9).");

            await _flow.StartLevel(OtherId);

            Assert.AreEqual(2, _sessions.Created.Count);
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.AreEqual(OtherId, _flow.CurrentLevelId);
            Assert.AreEqual(1, _addressables.OwnerCount);
        });

        [UnityTest]
        public IEnumerator RetryLevel_ReloadsSameLevel() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            _flow.FailLevel();
            await _flow.RetryLevel();

            Assert.AreEqual(2, _sessions.Created.Count);
            Assert.IsTrue(_sessions.Created[0].Unloaded);
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.AreEqual(ValidId, _flow.CurrentLevelId);
        });

        // ------------------------------------------------------------ Errors (ТЗ §109)

        [UnityTest]
        public IEnumerator StartLevel_UnknownLevel_EntersError() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.ignoreFailingMessages = true;
            await BootToMenu();
            await _flow.StartLevel("nope");

            Assert.AreEqual(GameFlowState.Error, _flow.State);
            StringAssert.Contains("nope", _flow.ErrorMessage);
            Assert.AreEqual(0, _addressables.OwnerCount);
        });

        [UnityTest]
        public IEnumerator StartLevel_MissingAsset_EntersError_WithoutLeakedHandles() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.ignoreFailingMessages = true;
            await BootToMenu();
            await _flow.StartLevel(MissingAssetId);

            Assert.AreEqual(GameFlowState.Error, _flow.State);
            Assert.AreEqual(0, _sessions.Created.Count);
            Assert.AreEqual(0, _addressables.OwnerCount);
        });

        [UnityTest]
        public IEnumerator StartLevel_InvalidLevel_EntersError_BeforeCreatingScope() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.ignoreFailingMessages = true;
            await BootToMenu();
            await _flow.StartLevel(InvalidId);

            Assert.AreEqual(GameFlowState.Error, _flow.State);
            StringAssert.Contains("validation error", _flow.ErrorMessage);
            Assert.AreEqual(0, _sessions.Created.Count);
            Assert.AreEqual(0, _addressables.OwnerCount);
        });

        [UnityTest]
        public IEnumerator StartLevel_LoadStepFails_UnloadsPartialLevel_AndEntersError() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.ignoreFailingMessages = true;
            await BootToMenu();
            _sessions.LoadBehaviour = _ => throw new InvalidOperationException("visual build failed");

            await _flow.StartLevel(ValidId);

            Assert.AreEqual(GameFlowState.Error, _flow.State);
            StringAssert.Contains("visual build failed", _flow.ErrorMessage);
            Assert.IsTrue(_sessions.Created[0].Unloaded, "Partially initialized level must be destroyed.");
            Assert.IsFalse(_flow.HasLevel);
            Assert.AreEqual(0, _addressables.OwnerCount);

            await _flow.ExitToMenu();
            Assert.AreEqual(GameFlowState.MainMenu, _flow.State);
        });

        // ------------------------------------------------------------ Cancellation

        [UnityTest]
        public IEnumerator ExitToMenu_WhileLoading_CancelsLoad_AndUnloadsLevel() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            var gate = new UniTaskCompletionSource();
            _sessions.LoadBehaviour = cancellation => gate.Task.AttachExternalCancellation(cancellation);

            var loading = _flow.StartLevel(ValidId);
            Assert.AreEqual(GameFlowState.Loading, _flow.State);

            await _flow.ExitToMenu();
            await loading;

            Assert.AreEqual(GameFlowState.MainMenu, _flow.State);
            Assert.IsTrue(_sessions.Created[0].Unloaded);
            Assert.IsFalse(_sessions.Created[0].Started);
            Assert.AreEqual(0, _addressables.OwnerCount);
        });

        [UnityTest]
        public IEnumerator StartLevel_WhileLoading_CancelsFirstLoad() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            var gate = new UniTaskCompletionSource();
            _sessions.LoadBehaviour = cancellation => gate.Task.AttachExternalCancellation(cancellation);
            var first = _flow.StartLevel(ValidId);

            _sessions.LoadBehaviour = null;
            await _flow.StartLevel(OtherId);
            await first;

            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.AreEqual(OtherId, _flow.CurrentLevelId);
            Assert.IsTrue(_sessions.Created[0].Unloaded);
            Assert.IsFalse(_sessions.Created[0].Started);
            Assert.IsTrue(_sessions.Created[1].Started);
            Assert.AreEqual(1, _addressables.OwnerCount);
        });

        // ------------------------------------------------------------ Gameplay states

        [UnityTest]
        public IEnumerator PauseAndResume_ArePassedToLevel() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            var session = _sessions.Created[0];

            _flow.PauseGameplay();
            Assert.AreEqual(GameFlowState.Paused, _flow.State);
            Assert.IsTrue(session.Paused);

            _flow.ResumeGameplay();
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.IsFalse(session.Paused);
        });

        [UnityTest]
        public IEnumerator Pause_OutsideGameplay_IsIgnored() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            _flow.PauseGameplay();
            Assert.AreEqual(GameFlowState.MainMenu, _flow.State);
        });

        [UnityTest]
        public IEnumerator LevelFinished_MovesToResultState_AndStopsGameplay() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            var session = _sessions.Created[0];

            session.RaiseFinished(LevelOutcome.Completed);

            Assert.AreEqual(GameFlowState.Completed, _flow.State);
            Assert.IsTrue(session.Stopped);
            Assert.IsTrue(_flow.HasLevel, "The level stays loaded behind the result screen.");
        });

        [UnityTest]
        public IEnumerator Completion_SavesRun_BeforeResultWindow() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            var session = _sessions.Created[0];
            session.Kills = 1;
            session.Money = 30;
            var savedBeforeResult = false;
            _flow.StateChanged += state =>
            {
                if (state == GameFlowState.Completed) savedBeforeResult = _progress.Recorded.Count == 1;
            };

            session.RaiseFinished(LevelOutcome.Completed);

            Assert.IsTrue(savedBeforeResult, "ТЗ §89: result → save → result window.");
            Assert.AreEqual(ValidId, _progress.Recorded[0].Key);
            var result = _flow.LastResult.Value;
            Assert.IsTrue(result.Completed);
            Assert.AreEqual(30, _progress.Money);
        });

        [UnityTest]
        public IEnumerator Death_SavesMoney_DoubledByRewarded_RetryClearsResult() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            _sessions.Created[0].Kills = 1;
            _sessions.Created[0].Money = 12;

            _sessions.Created[0].RaiseFinished(LevelOutcome.Failed);

            Assert.AreEqual(GameFlowState.Failed, _flow.State);
            Assert.AreEqual(1, _progress.Recorded.Count, "ТЗ: при смерти собранное остаётся.");
            Assert.IsFalse(_flow.LastResult.Value.Completed);
            Assert.AreEqual(12, _progress.Money);

            Assert.IsTrue(_flow.CanDoubleReward);
            Assert.IsTrue(await _flow.DoubleRewardAsync(CancellationToken.None), "No ad service: ×2 is free.");
            Assert.AreEqual(24, _progress.Money);
            Assert.IsFalse(_flow.CanDoubleReward, "Once per result.");
            Assert.IsFalse(await _flow.DoubleRewardAsync(CancellationToken.None));

            await _flow.RetryLevel();
            Assert.IsNull(_flow.LastResult);
            Assert.AreEqual(24, _progress.Money);
        });

        [UnityTest]
        public IEnumerator DeathWindow_PausesUntilRevive_GivingUpEndsLevel() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            var session = _sessions.Created[0];

            _flow.OpenDeath();
            Assert.AreEqual(GameFlowState.Death, _flow.State);
            Assert.IsTrue(session.Paused);
            _flow.CloseDeath();
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.IsFalse(session.Paused);

            _flow.OpenDeath();
            session.RaiseFinished(LevelOutcome.Failed); // given up in the window
            Assert.AreEqual(GameFlowState.Failed, _flow.State);
        });

        [UnityTest]
        public IEnumerator LeavingRunFromPause_KeepsItsSave_ContinueResumesIt() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            _sessions.Runs = true;
            await _flow.StartLevel(ValidId);
            var seed = _sessions.LastOptions.Seed;
            Assert.IsTrue(seed.HasValue, "A new run gets one seed for all its systems.");
            _sessions.Created[0].Money = 7;
            _sessions.Created[0].RaiseCheckpoint(new RunSnapshot { Seed = seed.Value, Wave = 1, Money = 4 });
            Assert.AreEqual(ValidId, _progress.RunLevelId, "A checkpoint goes to the save at once.");
            _flow.PauseGameplay();

            await _flow.ExitToMenu();

            Assert.AreEqual(0, _progress.Money, "Left from the pause: the money stays in the run's save.");
            Assert.AreEqual(0, _progress.Recorded.Count, "No result for a left run.");
            Assert.IsTrue(_flow.HasSavedRun);

            await _flow.ContinueRun();
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.AreSame(_progress.Run, _sessions.LastOptions.Resume);
            Assert.AreEqual(seed, _sessions.LastOptions.Seed, "The continued run keeps its seed.");

            _flow.FailLevel();
            Assert.IsFalse(_progress.HasRun, "ТЗ: смерть удаляет сейв забега.");
            await _flow.RetryLevel();
            Assert.IsNull(_sessions.LastOptions.Resume, "Retry is a new run, not the continued one again.");
        });

        [UnityTest]
        public IEnumerator NewRun_GivesUpTheSavedOne_ItsMoneyKept() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            _sessions.Runs = true;
            await _flow.StartLevel(ValidId);
            _sessions.Created[0].RaiseCheckpoint(new RunSnapshot { Wave = 0, Money = 4 });
            _sessions.Created[0].Money = 9;
            _flow.PauseGameplay();
            Assert.IsTrue(_flow.IsRunActive);

            await _flow.RetryLevel();
            Assert.AreEqual(9, _progress.Money, "Retry from the pause gives up the run: its money now.");
            Assert.IsFalse(_progress.HasRun);

            _sessions.Created[1].RaiseCheckpoint(new RunSnapshot { Wave = 2, Money = 6 });
            await _flow.ExitToMenu();
            await _flow.StartLevel(ValidId);
            Assert.AreEqual(15, _progress.Money, "A new run from the menu gives up the saved one: its saved money.");
            Assert.IsFalse(_progress.HasRun);
        });

        [UnityTest]
        public IEnumerator LeavingLevelWithoutWaves_KeepsNoMoney() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            _sessions.Created[0].Money = 7;
            _flow.PauseGameplay();

            await _flow.ExitToMenu();

            Assert.AreEqual(0, _progress.Money);
            Assert.IsFalse(_flow.HasSavedRun);
        });

        [UnityTest]
        public IEnumerator Map_PausesGameplay_OnlyFromPlaying() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            _flow.OpenMap();
            Assert.AreEqual(GameFlowState.MainMenu, _flow.State, "No level.");

            await _flow.StartLevel(ValidId);
            var session = _sessions.Created[0];
            _flow.OpenMap();
            Assert.AreEqual(GameFlowState.Map, _flow.State);
            Assert.IsTrue(session.Paused, "ТЗ §60: the map pauses gameplay.");


            _flow.CloseMap();
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.IsFalse(session.Paused);

            _flow.PauseGameplay();
            _flow.OpenMap();
            Assert.AreEqual(GameFlowState.Paused, _flow.State, "Not from the pause menu.");
        });

        [UnityTest]
        public IEnumerator LaunchOptions_ArePassedToLevel_AndKeptOnRetry() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            var options = new LevelLaunchOptions(startIndex: 1, seed: 5);
            await _flow.StartLevel(ValidId, options);
            Assert.AreSame(options, _sessions.LastOptions);

            _flow.FailLevel();
            await _flow.RetryLevel();
            Assert.AreSame(options, _sessions.LastOptions, "Test from Start #N restarts at the same start.");

            await _flow.StartLevel(OtherId);
            Assert.IsNull(_sessions.LastOptions.StartIndex);
            Assert.IsTrue(_sessions.LastOptions.Seed.HasValue, "A new seed for a run without a forced one.");
        });

        // ------------------------------------------------------------ Fakes

        private sealed class RecordingService : IApplicationService
        {
            private readonly List<string> _log;
            private readonly bool _fail;

            public RecordingService(string name, List<string> log, bool fail = false)
            {
                Name = name;
                _log = log;
                _fail = fail;
            }

            public string Name { get; }

            public UniTask InitializeAsync(CancellationToken cancellation)
            {
                if (_fail) throw new InvalidOperationException(Name + " failed");
                _log.Add(Name);
                return UniTask.CompletedTask;
            }
        }

        private sealed class FakeCatalog : ILevelCatalog
        {
            private readonly LevelCatalog _catalog;
            public FakeCatalog(LevelCatalog catalog) => _catalog = catalog;
            public IReadOnlyList<LevelCatalogEntry> Levels => _catalog.Levels;
            public IReadOnlyList<LevelCatalogEntry> DevLevels => System.Array.Empty<LevelCatalogEntry>();
            public LevelCatalogEntry Tutorial => _catalog.Tutorial;
            public LevelCatalogEntry Find(string levelId) => _catalog.Find(levelId);
        }

        private sealed class FakeProgress : IProgressService
        {
            public readonly List<KeyValuePair<string, LevelResult>> Recorded = new List<KeyValuePair<string, LevelResult>>();

            public int TotalKills => 0;
            public int Money { get; private set; }
            public bool IsUnlocked(string levelId) => true;
            public bool IsCompleted(string levelId) => false;

            public void RecordRun(string levelId, LevelResult result, int difficulty = 0)
            {
                Recorded.Add(new KeyValuePair<string, LevelResult>(levelId, result));
                Money += result.Money;
            }

            public void AddMoney(int amount) => Money += amount;
            public void ResetProgress() => Recorded.Clear();

            public RunSnapshot Run;
            public string RunLevel;
            public bool HasRun => Run != null;
            public string RunLevelId => Run != null ? RunLevel : null;
            public RunSnapshot LoadRun() => Run;

            public void SaveRun(string levelId, int difficulty, RunSnapshot run)
            {
                RunLevel = levelId;
                Run = run;
            }

            public void ClearRun() => Run = null;

            public void AbandonRun(int money)
            {
                Money += money;
                Run = null;
            }
        }

        private sealed class FakeSessionFactory : ILevelSessionFactory
        {
            public readonly List<FakeSession> Created = new List<FakeSession>();
            public Func<CancellationToken, UniTask> LoadBehaviour;
            public Action OnCreate;

            public LevelLaunchOptions LastOptions;

            /// <summary>Sessions created from now on are runs with waves.</summary>
            public bool Runs;

            public UniTask<ILevelSession> CreateAsync(LevelData level, IAssetOwner levelAssets, LevelLaunchOptions options,
                CancellationToken cancellation)
            {
                OnCreate?.Invoke();
                LastOptions = options;
                var session = new FakeSession(level, levelAssets, LoadBehaviour) { IsRun = Runs };
                Created.Add(session);
                return UniTask.FromResult<ILevelSession>(session);
            }
        }

        private sealed class FakeSession : ILevelSession
        {
            private readonly IAssetOwner _assets;
            private readonly Func<CancellationToken, UniTask> _load;

            public FakeSession(LevelData level, IAssetOwner assets, Func<CancellationToken, UniTask> load)
            {
                Level = level;
                _assets = assets;
                _load = load;
            }

            public LevelData Level { get; }
            public bool Loaded { get; private set; }
            public bool Started { get; private set; }
            public bool Paused { get; private set; }
            public bool Stopped { get; private set; }
            public bool Unloaded { get; private set; }

            public event Action<LevelOutcome> Finished;
            public event Action<RunSnapshot> RunCheckpoint;

            /// <summary>The level has waves (a run with a save).</summary>
            public bool IsRun { get; set; }

            public void RaiseFinished(LevelOutcome outcome) => Finished?.Invoke(outcome);

            public void RaiseCheckpoint(RunSnapshot run) => RunCheckpoint?.Invoke(run);


            public async UniTask LoadAsync(CancellationToken cancellation)
            {
                if (_load != null) await _load(cancellation);
                Loaded = true;
            }

            public void StartGameplay() => Started = true;
            public void SetPaused(bool paused) => Paused = paused;
            public void StopGameplay() => Stopped = true;

            public int Kills, Money;

            public LevelResult GetResult(LevelOutcome outcome) =>
                new LevelResult(outcome == LevelOutcome.Completed, Kills, money: Money);

            public UniTask UnloadAsync()
            {
                Dispose();
                return UniTask.CompletedTask;
            }

            public void Dispose()
            {
                Unloaded = true;
                _assets.Dispose();
            }
        }
    }
}
