using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Level;
using Maze.Gameplay.Level;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Maze.Tests.EditMode.Flow
{
    public class LevelRuntimeTests
    {
        private LevelData _level;
        private List<string> _log;

        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            _log = new List<string>();
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_level);

        [UnityTest]
        public IEnumerator Load_RunsStepsInStageOrder_KeepingRegistrationOrderWithinStage() => UniTask.ToCoroutine(async () =>
        {
            var runtime = new LevelRuntime(_level, new ILevelLoadStep[]
            {
                new Step(LevelLoadStage.SpawnZombies, "zombies", _log),
                new Step(LevelLoadStage.BuildVisuals, "visuals-a", _log),
                new Step(LevelLoadStage.BuildRuntime, "runtime", _log),
                new Step(LevelLoadStage.BuildVisuals, "visuals-b", _log),
            }, null);

            await runtime.LoadAsync(CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "runtime", "visuals-a", "visuals-b", "zombies" }, _log);
            Assert.AreEqual(LevelRunState.Ready, runtime.State);
        });

        [Test]
        public void Load_Cancelled_StopsBeforeNextStep()
        {
            using var cancellation = new CancellationTokenSource();
            var runtime = new LevelRuntime(_level, new ILevelLoadStep[]
            {
                new Step(LevelLoadStage.BuildRuntime, "first", _log, cancellation.Cancel),
                new Step(LevelLoadStage.SpawnPlayer, "second", _log),
            }, null);

            Assert.Throws<OperationCanceledException>(() => runtime.LoadAsync(cancellation.Token).GetAwaiter().GetResult());
            CollectionAssert.AreEqual(new[] { "first" }, _log);
            Assert.AreNotEqual(LevelRunState.Ready, runtime.State);
        }

        [Test]
        public void Tick_OnlyWhileRunning_NotWhenPausedOrStopped()
        {
            var ticker = new Ticker();
            var runtime = new LevelRuntime(_level, null, new ILevelTickable[] { ticker });
            runtime.Tick(1f);
            runtime.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            runtime.Tick(1f);
            Assert.AreEqual(0, ticker.Count, "Not started yet.");

            runtime.StartGameplay();
            runtime.Tick(1f);
            Assert.AreEqual(1, ticker.Count);

            runtime.SetPaused(true);
            runtime.Tick(1f);
            Assert.AreEqual(1, ticker.Count, "Paused.");

            runtime.SetPaused(false);
            runtime.Tick(1f);
            Assert.AreEqual(2, ticker.Count);

            runtime.Stop();
            runtime.Tick(1f);
            Assert.AreEqual(2, ticker.Count, "Stopped.");
        }

        [Test]
        public void Finish_RaisesOnce_AndStopsGameplay()
        {
            var runtime = new LevelRuntime(_level, null, null);
            runtime.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            runtime.StartGameplay();
            var outcomes = new List<LevelOutcome>();
            runtime.Finished += outcomes.Add;

            runtime.Finish(LevelOutcome.Failed);
            runtime.Finish(LevelOutcome.Completed);

            CollectionAssert.AreEqual(new[] { LevelOutcome.Failed }, outcomes);
            Assert.AreEqual(LevelRunState.Stopped, runtime.State);
        }

        [Test]
        public void StartGameplay_BeforeLoad_Throws()
        {
            var runtime = new LevelRuntime(_level, null, null);
            Assert.Throws<InvalidOperationException>(runtime.StartGameplay);
        }

        [Test]
        public void Load_Twice_Throws()
        {
            var runtime = new LevelRuntime(_level, null, null);
            runtime.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => runtime.LoadAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        private sealed class Step : ILevelLoadStep
        {
            private readonly string _name;
            private readonly List<string> _log;
            private readonly Action _onExecute;

            public Step(LevelLoadStage stage, string name, List<string> log, Action onExecute = null)
            {
                Stage = stage;
                _name = name;
                _log = log;
                _onExecute = onExecute;
            }

            public LevelLoadStage Stage { get; }

            public UniTask ExecuteAsync(CancellationToken cancellation)
            {
                _log.Add(_name);
                _onExecute?.Invoke();
                return UniTask.CompletedTask;
            }
        }

        private sealed class Ticker : ILevelTickable
        {
            public int Count { get; private set; }
            public void Tick(float deltaTime) => Count++;
        }
    }
}
