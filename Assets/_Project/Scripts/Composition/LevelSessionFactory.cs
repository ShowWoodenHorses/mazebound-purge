using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Application.Flow;
using Maze.Core.Common;
using Maze.Core.Level;
using Maze.Gameplay.Level;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace Maze.Composition
{
    /// <summary>
    /// Creates a level session: loads the Game scene additively and builds its <see cref="LevelLifetimeScope"/>
    /// as a child of the project scope. On any failure everything created so far is undone.
    /// </summary>
    public sealed class LevelSessionFactory : ILevelSessionFactory
    {
        private readonly LifetimeScope _parent;
        private readonly string _sceneName;

        public LevelSessionFactory(LifetimeScope parent, string sceneName)
        {
            _parent = parent;
            _sceneName = sceneName;
        }

        public async UniTask<ILevelSession> CreateAsync(LevelData level, IAssetOwner levelAssets, LevelLaunchOptions options,
            CancellationToken cancellation)
        {
            var scene = default(Scene);
            LevelLifetimeScope scope = null;
            try
            {
                cancellation.ThrowIfCancellationRequested();

                var loading = SceneManager.LoadSceneAsync(_sceneName, LoadSceneMode.Additive)
                              ?? throw new LevelLoadException($"Scene '{_sceneName}' is not in Build Settings.");
                // Not cancelled midway: a scene can only be unloaded after it has finished loading.
                await loading.ToUniTask();
                scene = SceneManager.GetSceneByName(_sceneName);
                cancellation.ThrowIfCancellationRequested();

                SceneManager.SetActiveScene(scene);
                scope = FindScope(scene)
                        ?? throw new LevelLoadException($"Scene '{_sceneName}' has no {nameof(LevelLifetimeScope)} at its root.");

                scope.parentReference.Object = _parent;
                using (LifetimeScope.Enqueue(builder =>
                       {
                           builder.RegisterInstance(level);
                           builder.RegisterInstance(levelAssets);
                           builder.RegisterInstance(options ?? LevelLaunchOptions.Default);
                       }))
                {
                    scope.Build();
                }

                var runtime = scope.Container.Resolve<LevelRuntime>();
                var exits = scope.Container.Resolve<ExitSystem>();
                var progress = scope.Container.Resolve<LevelProgress>();
                scope.Container.Resolve<PlayerDeathRule>(); // subscribes on creation; disposed with the scope
                GameLog.Info(LogChannel.LevelLoading, $"LevelScope for '{level.name}' created.");
                return new LevelSession(scope, scene, levelAssets, runtime, exits, progress);
            }
            catch
            {
                if (scope != null) scope.Dispose();
                if (scene.IsValid() && scene.isLoaded) await SceneManager.UnloadSceneAsync(scene).ToUniTask();
                levelAssets.Dispose();
                throw;
            }
        }

        private static LevelLifetimeScope FindScope(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<LevelLifetimeScope>(out var scope))
                    return scope;

            return null;
        }

        private sealed class LevelSession : ILevelSession
        {
            private readonly Scene _scene;
            private readonly IAssetOwner _assets;
            private readonly LevelRuntime _runtime;
            private readonly ExitSystem _exits;
            private readonly LevelProgress _progress;
            private LevelLifetimeScope _scope;
            private bool _released;

            public LevelSession(LevelLifetimeScope scope, Scene scene, IAssetOwner assets, LevelRuntime runtime, ExitSystem exits,
                LevelProgress progress)
            {
                _progress = progress;
                _scope = scope;
                _scene = scene;
                _assets = assets;
                _runtime = runtime;
                _exits = exits;
                _runtime.Finished += OnFinished;
                _exits.ExitReached += OnExitReached;
            }

            public event Action<LevelOutcome> Finished;
            public event Action ExitReached;

            public UniTask LoadAsync(CancellationToken cancellation) => _runtime.LoadAsync(cancellation);

            public void StartGameplay() => _runtime.StartGameplay();

            public void SetPaused(bool paused) => _runtime.SetPaused(paused);

            public void StopGameplay() => _runtime.Stop();

            public LevelResult GetResult(LevelOutcome outcome) => _progress.GetResult(outcome);

            public async UniTask UnloadAsync()
            {
                if (_released) return;
                DisposeScope();
                if (_scene.IsValid() && _scene.isLoaded)
                    await SceneManager.UnloadSceneAsync(_scene).ToUniTask();
                ReleaseAssets();
                // Frees what has no owner handle: static batching meshes, released prefabs' dependencies.
                await Resources.UnloadUnusedAssets().ToUniTask();
            }

            public void Dispose()
            {
                if (_released) return;
                DisposeScope();
                ReleaseAssets();
            }

            // ТЗ §9 order: dispose the LevelScope, then release level Addressables.
            private void DisposeScope()
            {
                _released = true;
                _runtime.Finished -= OnFinished;
                _exits.ExitReached -= OnExitReached;
                Finished = null;
                ExitReached = null;
                if (_scope != null) _scope.Dispose();
                _scope = null;
            }

            private void ReleaseAssets() => _assets.Dispose();

            private void OnFinished(LevelOutcome outcome) => Finished?.Invoke(outcome);

            private void OnExitReached(ExitData exit) => ExitReached?.Invoke();
        }
    }
}
