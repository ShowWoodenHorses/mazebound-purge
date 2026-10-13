using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Core.Common;
using Maze.Core.Level;
using Maze.Gameplay.Level;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Builds the visual representation of the level (ТЗ §31, load stage BuildVisuals): loads the prefabs of
    /// the saved variants, combines static geometry into chunks and creates views of placed objects.
    /// Zombie views are created by the zombie system, the player view by the player system.
    /// </summary>
    public sealed class LevelVisualSystem : ILevelLoadStep, IDisposable
    {
        private readonly LevelData _level;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly EntityViewRegistry _entities;
        private readonly TopDownCamera _camera;

        public LevelVisualSystem(LevelData level, IAssetOwner assets, LevelViewRoot root, EntityViewRegistry entities,
            TopDownCamera camera)
        {
            _level = level;
            _assets = assets;
            _root = root;
            _entities = entities;
            _camera = camera;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>Available after loading.</summary>
        public LevelGeometryView Geometry { get; private set; }

        /// <summary>Creates views of objects that appear later (zombies, dropped items). Available after loading.</summary>
        public EntityViewFactory EntityViews { get; private set; }

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var prefabs = new VisualPrefabLibrary();
            await prefabs.LoadAsync(_level, _assets, cancellation);
            cancellation.ThrowIfCancellationRequested();

            Geometry = new GeometryBuilder(prefabs).Build(_level, _root.transform, staticBatching: true);
            Geometry.Bind();

            EntityViews = new EntityViewFactory(prefabs);
            var missingObjects = 0;
            foreach (var entity in _level.AllEntities())
            {
                if (entity is PlayerStartData || entity is ZombieSpawnData)
                    continue;

                var view = EntityViews.Create(_level, entity, _root.transform);
                if (view != null) _entities.Add(view);
                else missingObjects++;
            }

            var geometry = _level.Geometry;
            _camera.Frame(new Bounds(
                new Vector3((geometry.Width - 1) * 0.5f, 0f, (geometry.Height - 1) * 0.5f),
                new Vector3(geometry.Width, 1f, geometry.Height)));

            GameLog.Info(LogChannel.Visual,
                $"Level '{_level.name}' visuals built: {prefabs.Count} prefab(s), {Geometry.Chunks.Count} chunk(s), " +
                $"{_entities.Count} object view(s).");
            if (Geometry.MissingVisuals > 0 || missingObjects > 0)
                GameLog.Warning(LogChannel.Visual,
                    $"Level '{_level.name}': {Geometry.MissingVisuals} cell layer(s) and {missingObjects} object(s) have no visual.");
        }

        public void Dispose()
        {
            Geometry?.Dispose();
            Geometry = null;
            EntityViews = null;
        }
    }
}
