using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Level;

namespace Maze.Gameplay.Level
{
    public enum LevelRunState
    {
        Created = 0,
        Loading = 1,
        Ready = 2,
        Running = 3,
        Paused = 4,
        Stopped = 5,
        Disposed = 6,
    }

    /// <summary>
    /// Lifecycle of the active level inside its LevelScope: runs load steps (ТЗ §9), starts, pauses and stops
    /// gameplay, ticks simulation only while running and reports the outcome. Holds no gameplay rules itself.
    /// </summary>
    public sealed class LevelRuntime : IDisposable
    {
        private readonly ILevelLoadStep[] _steps;
        private readonly ILevelTickable[] _tickables;
        private readonly ILevelLateTickable[] _lateTickables;

        public LevelRuntime(LevelData level, IReadOnlyList<ILevelLoadStep> steps, IReadOnlyList<ILevelTickable> tickables,
            IReadOnlyList<ILevelLateTickable> lateTickables = null)
        {
            Level = level != null ? level : throw new ArgumentNullException(nameof(level));
            // OrderBy is stable: steps of one stage keep their registration order.
            _steps = (steps ?? Array.Empty<ILevelLoadStep>()).OrderBy(s => s.Stage).ToArray();
            _tickables = (tickables ?? Array.Empty<ILevelTickable>()).ToArray();
            _lateTickables = (lateTickables ?? Array.Empty<ILevelLateTickable>()).ToArray();
        }

        public LevelData Level { get; }
        public LevelRunState State { get; private set; } = LevelRunState.Created;
        public bool IsRunning => State == LevelRunState.Running;

        /// <summary>Raised once when gameplay ends by a game rule (exit reached, player died).</summary>
        public event Action<LevelOutcome> Finished;

        public async UniTask LoadAsync(CancellationToken cancellation)
        {
            if (State != LevelRunState.Created)
                throw new InvalidOperationException($"Level can be loaded only once (state: {State}).");

            State = LevelRunState.Loading;
            foreach (var step in _steps)
            {
                cancellation.ThrowIfCancellationRequested();
                await step.ExecuteAsync(cancellation);
            }

            cancellation.ThrowIfCancellationRequested();
            State = LevelRunState.Ready;
        }

        public void StartGameplay()
        {
            if (State != LevelRunState.Ready)
                throw new InvalidOperationException($"Gameplay can start only after loading (state: {State}).");

            State = LevelRunState.Running;
        }

        public void SetPaused(bool paused)
        {
            if (paused && State == LevelRunState.Running) State = LevelRunState.Paused;
            else if (!paused && State == LevelRunState.Paused) State = LevelRunState.Running;
        }

        public void Stop()
        {
            if (State != LevelRunState.Disposed)
                State = LevelRunState.Stopped;
        }

        public void Tick(float deltaTime)
        {
            if (State != LevelRunState.Running)
                return;

            // Stops mid-frame when a tickable ended the level (e.g. the player died).
            for (var i = 0; i < _tickables.Length && State == LevelRunState.Running; i++)
                _tickables[i].Tick(deltaTime);
        }

        /// <summary>View sync after simulation; runs while running or paused.</summary>
        public void LateTick(float deltaTime)
        {
            if (State != LevelRunState.Running && State != LevelRunState.Paused)
                return;

            for (var i = 0; i < _lateTickables.Length; i++)
                _lateTickables[i].LateTick(deltaTime);
        }

        /// <summary>Ends gameplay with an outcome. Ignored unless gameplay is running.</summary>
        public void Finish(LevelOutcome outcome)
        {
            if (State != LevelRunState.Running)
                return;

            State = LevelRunState.Stopped;
            Finished?.Invoke(outcome);
        }

        public void Dispose()
        {
            State = LevelRunState.Disposed;
            Finished = null;
        }
    }
}
