using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Core.Level;
using Maze.Gameplay.Level;

namespace Maze.Application.Flow
{
    /// <summary>
    /// The active level: its LevelScope, scene and level-specific assets. Created by
    /// <see cref="ILevelSessionFactory"/>; the composition root knows how scopes are built, GameFlow does not.
    /// </summary>
    public interface ILevelSession : IDisposable
    {
        /// <summary>Raised when gameplay ends by a game rule.</summary>
        event Action<LevelOutcome> Finished;

        /// <summary>The player entered an exit cell; GameFlow asks "Finish level?" (ТЗ §62).</summary>
        event Action ExitReached;

        /// <summary>Runs the level load steps (ТЗ §9: build runtime … spawn zombies).</summary>
        UniTask LoadAsync(CancellationToken cancellation);

        void StartGameplay();
        void SetPaused(bool paused);
        void StopGameplay();

        /// <summary>Stars and counters of the current run (ТЗ §87), for the given outcome.</summary>
        LevelResult GetResult(LevelOutcome outcome);

        /// <summary>
        /// Full unload: dispose the LevelScope, unload its scene, release level Addressables.
        /// Not cancellable: cleanup always completes. <see cref="IDisposable.Dispose"/> is the synchronous
        /// shutdown variant (application quit) that skips the scene unload.
        /// </summary>
        UniTask UnloadAsync();
    }

    public interface ILevelSessionFactory
    {
        /// <summary>
        /// Creates the LevelScope for <paramref name="level"/>. Takes ownership of <paramref name="levelAssets"/>
        /// (the owner that loaded the level): the session releases it, and so does the factory on failure.
        /// </summary>
        UniTask<ILevelSession> CreateAsync(LevelData level, IAssetOwner levelAssets, LevelLaunchOptions options,
            CancellationToken cancellation);
    }

    public sealed class LevelLoadException : Exception
    {
        public LevelLoadException(string message) : base(message) { }
    }
}
