using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Level;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Level;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Mirrors DoorSystem state on door views (ТЗ §37): <see cref="DoorVisual"/> if the prefab has one, otherwise the
    /// view's renderers are hidden while the door is open. A broken door (stage 5d, <see cref="DoorSystem.DoorBroken"/>)
    /// shows its closed leaf and falls flat away from the monster that broke it — a quarter turn about its foot in
    /// <see cref="FallTime"/> s — and lies there as decor. Never touches the view's active flag — that belongs to
    /// visibility. Must be registered after <see cref="LevelVisualSystem"/> (same load stage, registration order).
    /// </summary>
    public sealed class DoorViewPresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        public const float FallTime = 0.35f;

        /// <summary>A fallen leaf lies this high above the floor at its lowest, m (no z-fighting with the floor).</summary>
        private const float FloorGap = 0.01f;

        private readonly DoorSystem _doors;
        private readonly EntityViewRegistry _views;
        private readonly List<Fall> _falls = new List<Fall>();
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
                _doors.DoorBroken += OnDoorBroken;
                _subscribed = true;
            }

            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            for (var f = _falls.Count - 1; f >= 0; f--)
            {
                var fall = _falls[f];
                fall.Time = Mathf.Min(FallTime, fall.Time + deltaTime);
                if (fall.Transform == null)
                {
                    _falls.RemoveAt(f);
                    continue;
                }

                // Falls faster and faster, as a plank does.
                var t = fall.Time / FallTime;
                var turn = Quaternion.AngleAxis(90f * t * t, fall.Axis);
                fall.Transform.SetPositionAndRotation(fall.Pivot + turn * (fall.StartPosition - fall.Pivot) + Vector3.up * (fall.Lift * t),
                    turn * fall.StartRotation);
                if (fall.Time >= FallTime) _falls.RemoveAt(f);
            }
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _doors.DoorChanged -= OnDoorChanged;
            _doors.DoorUnlocked -= Apply;
            _doors.DoorBroken -= OnDoorBroken;
            _subscribed = false;
        }

        private void OnDoorChanged(DoorData door, bool open) => Apply(door);

        private void Apply(DoorData door)
        {
            if (!_views.TryGet(door.Id, out var view) || view.GameObject == null)
                return;

            var broken = _doors.IsBroken(door.Id);
            var open = _doors.IsOpen(door.Id) && !broken; // a broken door shows its leaf lying on the floor
            var visual = view.GameObject.GetComponentInChildren<DoorVisual>(true);
            if (visual != null)
            {
                if (broken) visual.SetBroken();
                else visual.SetState(open, _doors.IsLocked(door.Id));
                return;
            }

            foreach (var renderer in view.GameObject.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = !open;
        }

        private void OnDoorBroken(DoorData door, Vector2 from)
        {
            if (!_views.TryGet(door.Id, out var view) || view.GameObject == null) return;
            var transform = view.GameObject.transform;
            var center = new Vector3(door.Position.X, transform.position.y, door.Position.Y);
            // Across the passage (the leaf's normal), away from the one that broke it.
            var normal = transform.forward;
            normal.y = 0f;
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.forward;
            var away = center - new Vector3(from.x, center.y, from.y);
            if (Vector3.Dot(away, normal) < 0f) normal = -normal;

            // Half the leaf's thickness: lying flat, it would sink that deep into the floor.
            var thickness = 0.1f;
            var bounds = RendererBounds(view.GameObject, out var any);
            if (any) thickness = Mathf.Abs(Vector3.Dot(bounds.size, normal));
            _falls.Add(new Fall
            {
                Transform = transform,
                StartPosition = transform.position,
                StartRotation = transform.rotation,
                Pivot = center,
                Axis = Vector3.Cross(Vector3.up, normal),
                Lift = Mathf.Min(0.3f, thickness * 0.5f) + FloorGap,
            });
        }

        private static Bounds RendererBounds(GameObject root, out bool any)
        {
            var bounds = new Bounds();
            any = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!any) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                any = true;
            }

            return bounds;
        }

        private sealed class Fall
        {
            public Transform Transform;
            public Vector3 StartPosition;
            public Quaternion StartRotation;
            public Vector3 Pivot;
            public Vector3 Axis;
            public float Lift;
            public float Time;
        }
    }
}
