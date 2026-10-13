using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Application.Flow;
using Maze.Application.Levels;
using Maze.Application.Save;
using Maze.Application.Services;
using Maze.Core.Authoring;
using Maze.Core.Level;
using Maze.Gameplay.Level;
using Maze.Tests.EditMode.Common;
using Maze.Tests.EditMode.Visual;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Flow
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
            _invalidLevel = ScriptableObject.CreateInstance<LevelData>(); // no starts, no exits

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
        public IEnumerator ExitReached_PausesAndAsks_NoResumes_YesCompletes() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            var session = _sessions.Created[0];

            session.RaiseExitReached();
            Assert.AreEqual(GameFlowState.ExitConfirmation, _flow.State);
            Assert.IsTrue(session.Paused, "Gameplay waits for the answer.");

            _flow.ConfirmExit(false);
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.IsFalse(session.Paused);

            session.RaiseExitReached();
            _flow.ConfirmExit(true);
            Assert.AreEqual(GameFlowState.Completed, _flow.State);
            Assert.IsTrue(session.Stopped);
        });

        [UnityTest]
        public IEnumerator Completion_CalculatesStars_SavesProgress_BeforeResultWindow() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            var session = _sessions.Created[0];
            session.Kills = 2;
            session.TotalZombies = 2;
            session.Fragments = 1;
            session.TotalFragments = 3;
            var savedBeforeResult = false;
            _flow.StateChanged += state =>
            {
                if (state == GameFlowState.Completed) savedBeforeResult = _progress.Recorded.Count == 1;
            };

            session.RaiseExitReached();
            _flow.ConfirmExit(true);

            Assert.IsTrue(savedBeforeResult, "ТЗ §89: Calculate Stars → Save → Result Window.");
            Assert.AreEqual(ValidId, _progress.Recorded[0].Key);
            var result = _flow.LastResult.Value;
            Assert.IsTrue(result.Completed);
            Assert.AreEqual(2, result.Stars, "Exit + all zombies; fragments 1/3.");
            Assert.AreEqual(result.Stars, _progress.Recorded[0].Value.Stars);
        });

        [UnityTest]
        public IEnumerator Death_ShowsResult_SavesNothing_RetryClearsResult() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            _sessions.Created[0].Kills = 1;

            _sessions.Created[0].RaiseFinished(LevelOutcome.Failed);

            Assert.AreEqual(GameFlowState.Failed, _flow.State);
            Assert.AreEqual(0, _progress.Recorded.Count, "ТЗ §85: an unfinished run is not saved.");
            Assert.AreEqual(0, _flow.LastResult.Value.Stars);

            await _flow.RetryLevel();
            Assert.IsNull(_flow.LastResult);
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

            session.RaiseExitReached();
            Assert.AreEqual(GameFlowState.Map, _flow.State, "Nothing happens while paused.");

            _flow.CloseMap();
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.IsFalse(session.Paused);

            _flow.PauseGameplay();
            _flow.OpenMap();
            Assert.AreEqual(GameFlowState.Paused, _flow.State, "Not from the pause menu.");
        });

        [UnityTest]
        public IEnumerator ExitReached_WhilePaused_IsIgnored() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            await _flow.StartLevel(ValidId);
            _flow.PauseGameplay();

            _sessions.Created[0].RaiseExitReached();
            Assert.AreEqual(GameFlowState.Paused, _flow.State);
        });

        [UnityTest]
        public IEnumerator LaunchOptions_ArePassedToLevel_AndKeptOnRetry() => UniTask.ToCoroutine(async () =>
        {
            await BootToMenu();
            var options = new LevelLaunchOptions(startIndex: 1);
            await _flow.StartLevel(ValidId, options);
            Assert.AreSame(options, _sessions.LastOptions);

            _flow.FailLevel();
            await _flow.RetryLevel();
            Assert.AreSame(options, _sessions.LastOptions, "Test from Start #N restarts at the same start.");

            await _flow.StartLevel(OtherId);
            Assert.AreSame(LevelLaunchOptions.Default, _sessions.LastOptions);
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
            public bool IsUnlocked(string levelId) => true;
            public int GetStars(string levelId) => 0;
            public float GetBestTime(string levelId) => 0f;
            public bool IsMapLayerUnlocked(string levelId, MapUnlock layer) => false;
            public void UnlockMapLayer(string levelId, MapUnlock layer) { }
            public void RecordCompletion(string levelId, LevelResult result) =>
                Recorded.Add(new KeyValuePair<string, LevelResult>(levelId, result));
            public void ResetProgress() => Recorded.Clear();
        }

        private sealed class FakeSessionFactory : ILevelSessionFactory
        {
            public readonly List<FakeSession> Created = new List<FakeSession>();
            public Func<CancellationToken, UniTask> LoadBehaviour;
            public Action OnCreate;

            public LevelLaunchOptions LastOptions;

            public UniTask<ILevelSession> CreateAsync(LevelData level, IAssetOwner levelAssets, LevelLaunchOptions options,
                CancellationToken cancellation)
            {
                OnCreate?.Invoke();
                LastOptions = options;
                var session = new FakeSession(level, levelAssets, LoadBehaviour);
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
            public event Action ExitReached;

            public void RaiseFinished(LevelOutcome outcome) => Finished?.Invoke(outcome);

            public void RaiseExitReached() => ExitReached?.Invoke();

            public async UniTask LoadAsync(CancellationToken cancellation)
            {
                if (_load != null) await _load(cancellation);
                Loaded = true;
            }

            public void StartGameplay() => Started = true;
            public void SetPaused(bool paused) => Paused = paused;
            public void StopGameplay() => Stopped = true;

            public int Kills, TotalZombies, Fragments, TotalFragments;

            public LevelResult GetResult(LevelOutcome outcome) =>
                new LevelResult(outcome == LevelOutcome.Completed, Kills, TotalZombies, Fragments, TotalFragments);

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
