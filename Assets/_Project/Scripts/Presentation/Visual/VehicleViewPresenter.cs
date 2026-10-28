using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Grid;
using Purge.Core.Visual;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Looks of the level's vehicles (stage 9a): the model of each definition's look (loaded with the level, one per
    /// vehicle), an entity view (hidden outside the player's view like any object; a driven one is where the player is).
    /// Every late tick it follows its vehicle: position, heading, the wheels roll by the distance covered and the front
    /// ones turn with the wheel (children named *Wheel*, the front ones *Wheel_f*). While the player drives, the camera
    /// pulls back with the speed (the look's camera zoom at full speed), eased. A broken-down one smokes (the look's
    /// smoke, followed over the body, stage 9b); a blown-up one is gone.
    /// </summary>
    public sealed class VehicleViewPresenter : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        /// <summary>Degrees the front wheels turn at full lock.</summary>
        private const float WheelLock = 30f;

        /// <summary>Seconds the camera takes to settle on its new distance.</summary>
        private const float ZoomSmoothTime = 0.6f;

        /// <summary>Where the smoke rises: metres up, and forward of the centre (over the engine) × the half length.</summary>
        private const float SmokeHeight = 1f;
        private const float SmokeForward = 0.55f;

        private readonly VehicleSystem _vehicles;
        private readonly EntityViewRegistry _entities;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly TopDownCamera _camera;
        private readonly PackEffectsPlayer _effects;
        private readonly List<View> _views = new List<View>();
        private readonly List<GameObject> _all = new List<GameObject>();
        private float _zoom;
        private float _zoomVelocity;

        public VehicleViewPresenter(VehicleSystem vehicles, EntityViewRegistry entities, IAssetOwner assets, LevelViewRoot root,
            TopDownCamera camera = null, PackEffectsPlayer effects = null)
        {
            _vehicles = vehicles;
            _entities = entities;
            _assets = assets;
            _root = root;
            _camera = camera;
            _effects = effects;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_vehicles.Vehicles.Count == 0) return;
            var parent = new GameObject("Vehicles").transform;
            parent.SetParent(_root != null ? _root.transform : null, false);
            var prefabs = new Dictionary<string, GameObject>();
            foreach (var vehicle in _vehicles.Vehicles)
            {
                var prefab = await Load(vehicle.Definition.Look?.Prefab, prefabs, cancellation);
                if (prefab == null) continue;
                var go = UnityEngine.Object.Instantiate(prefab, parent, false);
                go.name = vehicle.Id;
                var view = new View(vehicle, go);
                _views.Add(view);
                _all.Add(go);
                Pose(view, 0f);
                _entities.Add(view.Entity);
            }
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_all);

        public void LateTick(float deltaTime)
        {
            for (var i = 0; i < _views.Count; i++)
                Pose(_views[i], deltaTime);
            Zoom(deltaTime);
        }

        public void Dispose()
        {
            if (_camera != null) _camera.ExtraHalfExtent = 0f;
            _views.Clear();
            _all.Clear();
        }

        private void Zoom(float deltaTime)
        {
            if (_camera == null) return;
            var current = _vehicles.Current;
            var look = current?.Definition.Look;
            var target = look != null ? look.CameraZoom * current.SpeedShare : 0f;
            _zoom = deltaTime > 0f
                ? Mathf.SmoothDamp(_zoom, target, ref _zoomVelocity, ZoomSmoothTime, Mathf.Infinity, deltaTime)
                : target;
            _camera.ExtraHalfExtent = _zoom;
        }

        private void Pose(View view, float deltaTime)
        {
            var vehicle = view.Vehicle;
            if (view.Gone) return;
            if (vehicle.State == VehicleState.Destroyed)
            {
                view.Gone = true;
                StopSmoke(view);
                _entities.Remove(view.Entity.EntityId);
                view.Root.SetActive(false);
                return;
            }

            var transform = view.Root.transform;
            var position = new Vector3(vehicle.Position.x, vehicle.Floor * GridPosition.FloorHeight, vehicle.Position.y);
            var travelled = (new Vector2(position.x, position.z) - new Vector2(transform.localPosition.x, transform.localPosition.z))
                .magnitude * Mathf.Sign(vehicle.Speed);
            transform.localPosition = position;
            transform.localRotation = Quaternion.Euler(0f, vehicle.Heading * Mathf.Rad2Deg, 0f);

            if (deltaTime > 0f)
            {
                var steer = vehicle.State == VehicleState.Driven ? vehicle.Steer * WheelLock : 0f;
                view.Steer = Mathf.MoveTowards(view.Steer, steer, WheelLock * 4f * deltaTime);
                foreach (var wheel in view.Wheels)
                {
                    wheel.Spin = Mathf.Repeat(wheel.Spin + travelled / Mathf.Max(0.05f, wheel.Radius) * Mathf.Rad2Deg, 360f);
                    var turn = wheel.Front ? Quaternion.Euler(0f, view.Steer, 0f) : Quaternion.identity;
                    // In the body's frame: a mirrored wheel of the pack still rolls forward.
                    wheel.Transform.localRotation = turn * Quaternion.Euler(wheel.Spin, 0f, 0f) * wheel.Rest;
                }
            }

            var cell = PlayerMovement.CellOf(vehicle.Position, vehicle.Floor);
            if (cell != view.Entity.Cell) _entities.Move(view.Entity, cell);
            if (vehicle.Broken) Smoke(view, position);
        }

        /// <summary>The wreck's smoke over its engine (started when its cell is seen, carried every frame).</summary>
        private void Smoke(View view, Vector3 position)
        {
            var smoke = view.Vehicle.Definition.Look?.Smoke;
            if (_effects == null || smoke == null) return;
            var vehicle = view.Vehicle;
            var front = vehicle.Forward * (vehicle.HalfLength * SmokeForward);
            var at = position + new Vector3(front.x, SmokeHeight, front.y);
            if (view.Smoke < 0) view.Smoke = _effects.Follow(smoke, at, Quaternion.identity);
            else _effects.Move(view.Smoke, at, Quaternion.identity);
        }

        private void StopSmoke(View view)
        {
            if (_effects != null && view.Smoke >= 0) _effects.Unfollow(view.Smoke);
            view.Smoke = -1;
        }

        private async UniTask<GameObject> Load(AssetReferenceGameObject reference, Dictionary<string, GameObject> loaded,
            CancellationToken cancellation)
        {
            if (reference == null || !reference.RuntimeKeyIsValid()) return null;
            var key = reference.AssetGUID;
            if (loaded.TryGetValue(key, out var prefab)) return prefab;
            prefab = await _assets.LoadAsync<GameObject>(reference, cancellation);
            cancellation.ThrowIfCancellationRequested();
            loaded[key] = prefab;
            return prefab;
        }

        private sealed class Wheel
        {
            public Transform Transform;
            public Quaternion Rest;
            public float Radius;
            public bool Front;
            public float Spin;
        }

        private sealed class View
        {
            public View(Vehicle vehicle, GameObject root)
            {
                Vehicle = vehicle;
                Root = root;
                Entity = new EntityView(vehicle.Id, VisualKind.Vehicle, PlayerMovement.CellOf(vehicle.Position, vehicle.Floor), root);
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    var name = child.name;
                    var at = name.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase);
                    if (child == root.transform || at < 0) continue;
                    var renderer = child.GetComponent<Renderer>();
                    Wheels.Add(new Wheel
                    {
                        Transform = child,
                        Rest = child.localRotation,
                        Radius = renderer != null ? renderer.bounds.extents.y : 0.4f,
                        Front = name.IndexOf("_f", at, StringComparison.OrdinalIgnoreCase) >= 0,
                    });
                }
            }

            public readonly Vehicle Vehicle;
            public readonly GameObject Root;
            public readonly EntityView Entity;
            public readonly List<Wheel> Wheels = new List<Wheel>();
            public float Steer;

            /// <summary>The followed smoke's handle (-1 — none).</summary>
            public int Smoke = -1;

            /// <summary>Blown up: hidden, out of the registry.</summary>
            public bool Gone;
        }
    }
}
