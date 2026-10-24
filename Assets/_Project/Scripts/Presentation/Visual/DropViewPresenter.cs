using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Common;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Drops;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Visibility;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Looks of the items that appear during play (<see cref="ItemVisualCatalog"/>): monsters' drops lying on the ground
    /// (<see cref="Pickup.IsDropped"/>; they blink before their time is out; one that disappears unpicked — its time is
    /// out, the next wave starts — puffs and shrinks away, stage 6b) and thrown items flying along their arc. Every
    /// look the drop pool or the throwable slot can need is loaded with the level (its asset owner) and pooled hidden —
    /// nothing is loaded or created during play (a pool that runs dry creates one more and warns). Shown only in cells the
    /// player sees.
    /// </summary>
    public sealed class DropViewPresenter : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private const int PickupPoolSize = 8;
        private const int FlyingPoolSize = 4;
        private const float BlinkTime = 5f;
        private const float HandHeight = 1.1f;
        private const float SpinSpeed = 540f;
        private const float MagnetHeight = 0.5f;

        private readonly DropPool _pool;
        private readonly PickupSystem _pickups;
        private readonly BulletSystem _bullets;
        private readonly ThrowableSlot _slot;
        private readonly VisibilitySystem _visibility;
        private readonly ItemVisualCatalog _catalog;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly Dictionary<string, Kind> _kinds = new Dictionary<string, Kind>();
        private readonly List<Shown> _lying = new List<Shown>();
        private readonly List<Shown> _flying = new List<Shown>();
        private readonly List<Shown> _vanishing = new List<Shown>();
        private readonly List<ContentDefinition> _items = new List<ContentDefinition>();
        private readonly List<GameObject> _all = new List<GameObject>();
        private readonly CombatVisualDefinition _visual;
        private readonly PackEmitters _emitters;
        private PackEmitters.Emitter _puff;
        private Transform _parent;
        private bool _subscribed;
        private readonly LevelData _level;
        private readonly PackEffectsPlayer _effects;

        public DropViewPresenter(DropPool pool, PickupSystem pickups, BulletSystem bullets, ThrowableSlot slot,
            VisibilitySystem visibility, IAssetOwner assets, LevelViewRoot root, ItemVisualCatalog catalog = null,
            CombatVisualDefinition visual = null, PackEmitters emitters = null, LevelData level = null,
            PackEffectsPlayer effects = null)
        {
            _level = level;
            _effects = effects;
            _visual = visual;
            _emitters = emitters;
            _pool = pool;
            _pickups = pickups;
            _bullets = bullets;
            _slot = slot;
            _visibility = visibility;
            _catalog = catalog;
            _assets = assets;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public int LyingCount => _lying.Count;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_catalog == null) return;

            _pool.GetItems(_items);
            if (_slot.Definition != null) _items.Add(_slot.Definition);
            // The run's start weapon can be dropped by the player (stage 6e).
            if (_level != null && _level.StartWeapon != null) _items.Add(_level.StartWeapon);
            foreach (var item in _pool.Throwables) _items.Add(item);
            if (_items.Count == 0) return;

            _parent = new GameObject("Drops").transform;
            _parent.SetParent(_root != null ? _root.transform : null, false);
            foreach (var item in _items)
            {
                if (item == null || _kinds.ContainsKey(Key(item))) continue;
                var visual = _catalog.Find(item);
                var kind = new Kind();
                _kinds[Key(item)] = kind;
                if (visual == null || visual.Pickup == null || !visual.Pickup.RuntimeKeyIsValid())
                {
                    GameLog.Warning(LogChannel.Visual, $"Item '{item.Id}' has no look: it is invisible on the ground.");
                    continue;
                }

                kind.Pickup = await _assets.LoadAsync<GameObject>(visual.Pickup, cancellation);
                if (item is ThrowableDefinition)
                    kind.Flying = await _assets.LoadAsync<GameObject>(visual.Flying, cancellation);
                cancellation.ThrowIfCancellationRequested();
                // Weapons: the most monsters can leave of one + the player's two slots dropped.
                var poolSize = item is WeaponDefinition ? PickupSystem.MaxSameWeaponDropped + 2 : PickupPoolSize;
                for (var i = 0; i < poolSize; i++) kind.FreePickups.Push(Create(kind.Pickup, item.Id));
                if (kind.Flying != null)
                    for (var i = 0; i < FlyingPoolSize; i++) kind.FreeFlying.Push(Create(kind.Flying, item.Id + " (flying)"));
            }

            _pickups.Added += OnAdded;
            _pickups.Removed += OnRemoved;
            _bullets.Spawned += OnSpawned;
            _bullets.Ended += OnEnded;
            _subscribed = true;
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_all);

        public void LateTick(float deltaTime)
        {
            TickVanishing(deltaTime);
            var time = _pickups.Time;
            for (var i = 0; i < _lying.Count; i++)
            {
                var shown = _lying[i];
                var pickup = shown.Pickup;
                if (pickup.Attracted || shown.Lifted)
                {
                    // The magnet of the run's cards: it flies to the player a little above the floor (and lands back where
                    // it lay when the player could not take it after all).
                    shown.Object.transform.localPosition = new Vector3(pickup.Position.x,
                        (pickup.Attracted ? MagnetHeight : 0f) + pickup.Cell.Floor * GridPosition.FloorHeight, pickup.Position.y);
                    if (pickup.Attracted)
                    {
                        if (!shown.Object.activeSelf) shown.Object.SetActive(true);
                        if (!shown.Lifted) _lying[i] = Lift(shown, true);
                        continue;
                    }

                    _lying[i] = Lift(shown, false);
                }

                var seen = _visibility.IsVisible(pickup.Cell);
                var visible = seen;
                var left = shown.Pickup.ExpiresAt - time;
                if (visible && left < BlinkTime) visible = Mathf.Repeat(left, 0.3f) > 0.1f;
                if (shown.Object.activeSelf != visible) shown.Object.SetActive(visible);
                if (shown.RayLook != null) _lying[i] = TickRay(shown, seen);
            }

            foreach (var shown in _flying)
            {
                var bullet = shown.Bullet;
                var p = bullet.Progress;
                var definition = bullet.Throwable;
                var arc = definition != null ? definition.ArcHeight * bullet.Travel * 4f * p * (1f - p) : 0f;
                var height = Mathf.Lerp(HandHeight, 0.1f, p) + arc + bullet.Floor * GridPosition.FloorHeight;
                var transform = shown.Object.transform;
                transform.localPosition = new Vector3(bullet.Position.x, height, bullet.Position.y);
                transform.localRotation = Quaternion.LookRotation(new Vector3(bullet.Direction.x, 0f, bullet.Direction.y)) *
                                          Quaternion.Euler(bullet.Age * SpinSpeed, 0f, 0f);
                var visible = _visibility.IsVisible(new GridPosition(Mathf.FloorToInt(bullet.Position.x + 0.5f),
                    Mathf.FloorToInt(bullet.Position.y + 0.5f), bullet.Floor));
                if (shown.Object.activeSelf != visible) shown.Object.SetActive(visible);
            }
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _pickups.Added -= OnAdded;
                _pickups.Removed -= OnRemoved;
                _bullets.Spawned -= OnSpawned;
                _bullets.Ended -= OnEnded;
                _subscribed = false;
            }

            foreach (var go in _all) UnityObjects.Destroy(go);
            _all.Clear();
            _kinds.Clear();
            _lying.Clear();
            _flying.Clear();
            _vanishing.Clear();
            if (_parent != null) UnityObjects.Destroy(_parent.gameObject);
        }

        private void OnAdded(Pickup pickup)
        {
            if (!pickup.IsDropped || !TryTake(pickup.Item, false, out var go)) return;
            var transform = go.transform;
            transform.localPosition = new Vector3(pickup.Position.x, pickup.Cell.Floor * GridPosition.FloorHeight, pickup.Position.y);
            // A steady turn of its own (from the id), not a random one.
            transform.localRotation = Quaternion.Euler(0f, StableHash.Of(pickup.Id) % 360, 0f);
            go.SetActive(_visibility.IsVisible(pickup.Cell));
            var ray = _visual != null && _effects != null && pickup.Weapon != null
                ? _visual.LootRayOf(pickup.Weapon.Definition.Rarity)
                : null;
            _lying.Add(new Shown { Object = go, Pickup = pickup, Item = Key(pickup.Item), Scale = transform.localScale,
                RayLook = ray, Ray = -1 });
        }

        /// <summary>
        /// The ray of a rare weapon's rarity over it (stage 6e): follows it while its cell is seen (the magnet carries it),
        /// fades where it is otherwise.
        /// </summary>
        private Shown TickRay(Shown shown, bool seen)
        {
            if (!seen || shown.Pickup.Attracted)
            {
                StopRay(ref shown);
                return shown;
            }

            var position = shown.Object.transform.localPosition;
            if (shown.Ray < 0) shown.Ray = _effects.Follow(shown.RayLook, position, Quaternion.identity);
            else _effects.Move(shown.Ray, position, Quaternion.identity);
            return shown;
        }

        private void StopRay(ref Shown shown)
        {
            if (shown.Ray < 0) return;
            _effects.Unfollow(shown.Ray);
            shown.Ray = -1;
        }

        private void OnRemoved(Pickup pickup)
        {
            for (var i = 0; i < _lying.Count; i++)
            {
                var shown = _lying[i];
                if (shown.Pickup != pickup) continue;
                _lying.RemoveAt(i);
                StopRay(ref shown);
                if (!pickup.WasTaken && _visual != null && _visibility.IsVisible(pickup.Cell)) Vanish(shown);
                else Return(shown, false);
                return;
            }
        }

        /// <summary>Not picked up and seen: a puff, then it shrinks away (<see cref="CombatVisualDefinition.DropVanishTime"/>).</summary>
        private void Vanish(Shown shown)
        {
            _puff ??= _emitters?.Get(_visual.DropVanishOf());
            var position = shown.Object.transform.localPosition;
            _puff?.Emit(position + new Vector3(0f, 0.3f, 0f), _visual.DropVanishCount);
            shown.Object.SetActive(true);
            shown.Age = 0f;
            _vanishing.Add(shown);
        }

        private void TickVanishing(float deltaTime)
        {
            for (var i = _vanishing.Count - 1; i >= 0; i--)
            {
                var shown = _vanishing[i];
                shown.Age += deltaTime;
                var t = shown.Age / _visual.DropVanishTime;
                if (t >= 1f)
                {
                    shown.Object.transform.localScale = shown.Scale;
                    Return(shown, false);
                    _vanishing.RemoveAt(i);
                    continue;
                }

                shown.Object.transform.localScale = shown.Scale * (1f - t * t);
                _vanishing[i] = shown;
            }
        }

        private void OnSpawned(Bullet bullet)
        {
            if (bullet.Throwable == null || !TryTake(bullet.Throwable, true, out var go)) return;
            go.SetActive(false); // placed and shown in the late tick
            _flying.Add(new Shown { Object = go, Bullet = bullet, Item = Key(bullet.Throwable) });
        }

        private void OnEnded(Bullet bullet, Vector2 point, BulletEnd end)
        {
            if (bullet.Throwable == null) return;
            for (var i = 0; i < _flying.Count; i++)
            {
                if (_flying[i].Bullet != bullet) continue;
                Return(_flying[i], true);
                _flying.RemoveAt(i);
                return;
            }
        }

        private bool TryTake(ContentDefinition item, bool flying, out GameObject go)
        {
            go = null;
            if (item == null || !_kinds.TryGetValue(Key(item), out var kind)) return false;
            var free = flying ? kind.FreeFlying : kind.FreePickups;
            if (free.Count > 0)
            {
                go = free.Pop();
                return true;
            }

            var prefab = flying ? kind.Flying : kind.Pickup;
            if (prefab == null) return false;
            GameLog.Warning(LogChannel.Visual, $"Pool of '{item.Id}' looks ran dry: one more is created during play.");
            go = Create(prefab, item.Id);
            return true;
        }

        private void Return(in Shown shown, bool flying)
        {
            shown.Object.SetActive(false);
            if (!_kinds.TryGetValue(shown.Item, out var kind)) return;
            (flying ? kind.FreeFlying : kind.FreePickups).Push(shown.Object);
        }

        private GameObject Create(GameObject prefab, string name)
        {
            var go = UnityEngine.Object.Instantiate(prefab, _parent, false);
            go.name = name;
            go.SetActive(false);
            _all.Add(go);
            return go;
        }

        private static Shown Lift(Shown shown, bool lifted)
        {
            shown.Lifted = lifted;
            return shown;
        }

        /// <summary>By type and id: a definition may be a different copy in another bundle of a build.</summary>
        private static string Key(ContentDefinition item) => item.GetType().Name + "/" + item.Id;

        private sealed class Kind
        {
            public GameObject Pickup;
            public GameObject Flying;
            public readonly Stack<GameObject> FreePickups = new Stack<GameObject>();
            public readonly Stack<GameObject> FreeFlying = new Stack<GameObject>();
        }

        private struct Shown
        {
            public GameObject Object;
            public Pickup Pickup;
            public Bullet Bullet;
            public string Item;
            public Vector3 Scale;
            public float Age;

            /// <summary>Was flying to the player (the magnet): its place is set again.</summary>
            public bool Lifted;

            /// <summary>The ray of a rare weapon's rarity (stage 6e); null — none.</summary>
            public UnityEngine.AddressableAssets.AssetReferenceGameObject RayLook;

            /// <summary>Handle of the shown ray; -1 — not shown.</summary>
            public int Ray;
        }
    }
}
