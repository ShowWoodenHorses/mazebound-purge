using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Visual;
using Purge.Gameplay.Level;
using Purge.Gameplay.Objects;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Looks of the level's objects (stage 7в): barrels and chests — the models of their kinds' assets (loaded with the
    /// level, one per object), entity views (<see cref="EntityViewRegistry"/>: hidden outside the player's view like any
    /// object). A barrel that blew up is gone; a chest that opens lifts its lid (a child named *Lid*), an opened one stays
    /// open. Objects are destroyed with the level root (the registry disposes their views).
    /// </summary>
    public sealed class LevelObjectsPresenter : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private const float LidAngle = -105f;
        private const float LidTime = 0.35f;

        /// <summary>Wider side of the models, cells.</summary>
        private const float BarrelSize = 0.6f;
        private const float ChestSize = 0.85f;

        private readonly BarrelSystem _barrels;
        private readonly ChestSystem _chests;
        private readonly EntityViewRegistry _entities;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly List<Lid> _lids = new List<Lid>();
        private readonly List<GameObject> _all = new List<GameObject>();
        private Transform _parent;
        private bool _subscribed;

        public LevelObjectsPresenter(BarrelSystem barrels, ChestSystem chests, EntityViewRegistry entities, IAssetOwner assets,
            LevelViewRoot root)
        {
            _barrels = barrels;
            _chests = chests;
            _entities = entities;
            _assets = assets;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_barrels.Barrels.Count == 0 && _chests.Chests.Count == 0) return;
            _parent = new GameObject("Level Objects").transform;
            _parent.SetParent(_root != null ? _root.transform : null, false);
            var prefabs = new Dictionary<string, GameObject>();

            foreach (var barrel in _barrels.Barrels)
            {
                var prefab = await Load(barrel.Definition != null ? barrel.Definition.Prefab : null, prefabs, cancellation);
                var view = Place(prefab, barrel.Id, VisualKind.Barrel, barrel.Cell, "Barrel " + barrel.Data.Kind, BarrelSize);
                if (view == null) GameLog.Warning(LogChannel.Visual, $"Barrel '{barrel.Id}' has no model.");
            }

            foreach (var chest in _chests.Chests)
            {
                var prefab = await Load(chest.Definition != null ? chest.Definition.Prefab : null, prefabs, cancellation);
                var view = Place(prefab, chest.Id, VisualKind.Chest, chest.Cell, "Chest " + chest.Kind, ChestSize);
                if (view == null)
                {
                    GameLog.Warning(LogChannel.Visual, $"Chest '{chest.Id}' has no model.");
                    continue;
                }

                var lid = FindLid(view.transform);
                if (lid == null) continue;
                var entry = new Lid { Chest = chest, Transform = lid, Closed = lid.localRotation };
                if (chest.State == ChestState.Open || chest.State == ChestState.Challenge) entry.Open = LidTime; // a continued run
                _lids.Add(entry);
                Pose(entry);
            }

            _barrels.Exploded += OnExploded;
            _subscribed = true;
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_all);

        public void LateTick(float deltaTime)
        {
            for (var i = 0; i < _lids.Count; i++)
            {
                var lid = _lids[i];
                var opened = lid.Chest.State == ChestState.Open || lid.Chest.State == ChestState.Challenge;
                var target = opened ? LidTime : lid.Chest.State == ChestState.Opening ? LidTime * 0.15f * lid.Chest.Progress : 0f;
                if (Mathf.Approximately(lid.Open, target)) continue;
                lid.Open = Mathf.MoveTowards(lid.Open, target, deltaTime);
                _lids[i] = lid;
                Pose(lid);
            }
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _barrels.Exploded -= OnExploded;
                _subscribed = false;
            }

            _lids.Clear();
            _all.Clear();
        }

        // ------------------------------------------------------------------ Helpers

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

        /// <summary>
        /// The model fitted to the cell (the packs' models differ in size): its wider side becomes
        /// <paramref name="size"/> cells, standing on the floor.
        /// </summary>
        private GameObject Place(GameObject prefab, string id, VisualKind kind, GridPosition cell, string name, float size)
        {
            if (prefab == null) return null;
            var go = UnityEngine.Object.Instantiate(prefab, _parent, false);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                var wide = Mathf.Max(bounds.size.x, bounds.size.z);
                if (wide > 1e-3f) go.transform.localScale = prefab.transform.localScale * (size / wide);
            }

            go.transform.localPosition = new Vector3(cell.X, cell.Floor * GridPosition.FloorHeight, cell.Y);
            // A stable turn per object (never random): the same chest looks the same in every run.
            go.transform.localRotation = Quaternion.Euler(0f, (StableHash.Of(id) % 4) * 90f, 0f);
            _all.Add(go);
            _entities.Add(new EntityView(id, kind, cell, go));
            return go;
        }

        private static Transform FindLid(Transform root)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child != root && child.name.IndexOf("Lid", StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
            return null;
        }

        private static void Pose(Lid lid)
        {
            var t = Mathf.Clamp01(lid.Open / LidTime);
            var eased = 1f - (1f - t) * (1f - t);
            lid.Transform.localRotation = lid.Closed * Quaternion.Euler(LidAngle * eased, 0f, 0f);
        }

        private void OnExploded(Barrel barrel) => _entities.Remove(barrel.Id);

        private struct Lid
        {
            public Chest Chest;
            public Transform Transform;
            public Quaternion Closed;
            public float Open;
        }
    }
}
