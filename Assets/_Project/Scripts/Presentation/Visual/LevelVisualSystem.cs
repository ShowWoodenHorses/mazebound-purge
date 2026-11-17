using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Common;
using Purge.Core.Level;
using Purge.Gameplay.Level;
using UnityEngine;

namespace Purge.Presentation.Visual
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
        private LevelGeometryView[] _floors;
        private int _activeFloor;

        public LevelGeometryView Geometry => _floors?[_activeFloor];

        internal LevelGeometryView ShowFloor(int floor)
        {
            if (floor == _activeFloor) return Geometry;
            Geometry.Root.gameObject.SetActive(false);
            _activeFloor = floor;
            Geometry.Root.gameObject.SetActive(true);
            return Geometry;
        }

        /// <summary>Creates views of objects that appear later (zombies, dropped items). Available after loading.</summary>
        public EntityViewFactory EntityViews { get; private set; }

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var prefabs = new VisualPrefabLibrary();
            await prefabs.LoadAsync(_level, _assets, cancellation);
            cancellation.ThrowIfCancellationRequested();

            var stairRef = _level.VisualTheme != null ? _level.VisualTheme.Stairs : null;
            var stairs = _level.Geometry.Stairs.Count > 0 && stairRef != null && stairRef.RuntimeKeyIsValid()
                ? await _assets.LoadAsync<GameObject>(stairRef, cancellation) : null;
            cancellation.ThrowIfCancellationRequested();
            var builder = new GeometryBuilder(prefabs, stairs: stairs);
            _floors = new LevelGeometryView[_level.Geometry.FloorCount];
            for (var floor = 0; floor < _floors.Length; floor++)
            {
                var view = builder.Build(_level, _root.transform, staticBatching: true, floor: floor);
                _floors[floor] = view;
                view.Bind();
                view.SetAllVisible(false);
                view.ApplyVisibility();
                view.Root.gameObject.SetActive(floor == _activeFloor);
            }

            EntityViews = new EntityViewFactory(prefabs);
            var missingObjects = 0;
            foreach (var entity in _level.AllEntities())
            {
                if (entity is PlayerStartData || entity is BarrelData || entity is ChestData ||
                    entity is VehicleData)
                    continue; // barrels, chests, vehicles: their look comes with their gameplay (stages 7в, 9a)

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
            if (_floors != null)
                foreach (var floor in _floors) floor?.Dispose();
            _floors = null;
            _activeFloor = 0;
            EntityViews = null;
        }
    }
}
