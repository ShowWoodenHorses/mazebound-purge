using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Level;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Level;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Mirrors DoorSystem state on door views (ТЗ §37): <see cref="DoorVisual"/> if the prefab has one, otherwise the
    /// view's renderers are hidden while the door is open. Never touches the view's active flag — that belongs to
    /// visibility. Must be registered after <see cref="LevelVisualSystem"/> (same load stage, registration order).
    /// </summary>
    public sealed class DoorViewPresenter : ILevelLoadStep, IDisposable
    {
        private readonly DoorSystem _doors;
        private readonly EntityViewRegistry _views;
        private bool _subscribed;

        public DoorViewPresenter(DoorSystem doors, EntityViewRegistry views)
        {
            _doors = doors;
            _views = views;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            foreach (var door in _doors.Doors)
                Apply(door);

            if (!_subscribed)
            {
                _doors.DoorChanged += OnDoorChanged;
                _doors.DoorUnlocked += Apply;
                _subscribed = true;
            }

            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _doors.DoorChanged -= OnDoorChanged;
            _doors.DoorUnlocked -= Apply;
            _subscribed = false;
        }

        private void OnDoorChanged(DoorData door, bool open) => Apply(door);

        private void Apply(DoorData door)
        {
            if (!_views.TryGet(door.Id, out var view) || view.GameObject == null)
                return;

            var open = _doors.IsOpen(door.Id);
            var visual = view.GameObject.GetComponentInChildren<DoorVisual>(true);
            if (visual != null)
            {
                visual.SetState(open, _doors.IsLocked(door.Id));
                return;
            }

            foreach (var renderer in view.GameObject.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = !open;
        }
    }
}
