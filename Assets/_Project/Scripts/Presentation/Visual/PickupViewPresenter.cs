using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Level;
using Maze.Gameplay.Pickups;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Keeps pickup views in sync with <see cref="PickupSystem"/>: removes the view of a taken item and creates the
    /// view of a dropped weapon with its own saved visual (never a new choice). Visibility picks up new views
    /// through <see cref="EntityViewRegistry.Added"/>. Must be registered after <see cref="LevelVisualSystem"/>.
    /// </summary>
    public sealed class PickupViewPresenter : ILevelLoadStep, IDisposable
    {
        private readonly LevelData _level;
        private readonly PickupSystem _pickups;
        private readonly EntityViewRegistry _views;
        private readonly LevelVisualSystem _visuals;
        private readonly LevelViewRoot _root;
        private bool _subscribed;

        public PickupViewPresenter(LevelData level, PickupSystem pickups, EntityViewRegistry views,
            LevelVisualSystem visuals, LevelViewRoot root)
        {
            _level = level;
            _pickups = pickups;
            _views = views;
            _visuals = visuals;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_subscribed)
            {
                _pickups.Added += OnAdded;
                _pickups.Removed += OnRemoved;
                _subscribed = true;
            }

            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _pickups.Added -= OnAdded;
            _pickups.Removed -= OnRemoved;
            _subscribed = false;
        }

        private void OnRemoved(Pickup pickup) => _views.Remove(pickup.Id);

        private void OnAdded(Pickup pickup)
        {
            if (_visuals.EntityViews == null || !VisualKinds.TryGetForEntity(pickup.Source, out var kind, out _))
                return;

            var view = _visuals.EntityViews.Create(pickup.Id, kind, VisualResolver.ResolveObject(_level, pickup.Source),
                pickup.Cell, _root.transform);
            if (view != null) _views.Add(view);
            else GameLog.Warning(LogChannel.Visual, $"Dropped item '{pickup.Id}' has no visual.");
        }
    }
}
