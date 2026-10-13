using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Core.Definitions;
using Maze.Core.Visual;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;
using Maze.Gameplay.Spatial;
using Maze.Gameplay.Visibility;
using Maze.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Pool;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Views of combat (ТЗ §79, §83), display only, cartoon style:
    /// <list type="bullet">
    /// <item>shot: a flash at the gun's muzzle, a shell thrown out to the right, a tracer that leaves the muzzle and
    /// joins the bullet's real line (gameplay bullets fly from the player's centre) within <see cref="ConvergeDistance"/>;</item>
    /// <item>bullet stopped by a wall or closed door: sparks and dust out of the wall; hit on a zombie (bullet or
    /// melee): a hit "star" (the model's flash is <see cref="ZombieViewPresenter"/>'s);</item>
    /// <item>melee: a swing stroke over the weapon's sector (<see cref="SwingArc"/>: arc and reach of the weapon).</item>
    /// </list>
    /// Melee visuals wait for the moment the attack clip hits (<see cref="PlayerViewPresenter.LastMeleeContactDelay"/>;
    /// the clip starts with a wind-up): the stroke starts <see cref="SwingLead"/> earlier so its head crosses the sector
    /// then, the hit stars appear then; the stroke goes the way the clip swings (<see cref="PlayerViewPresenter.LastMeleeSweep"/>,
    /// alternating without that data). Gameplay damage is not delayed.
    /// Effects start only in cells the player sees (ТЗ §54); tracers are hidden outside them. Prefabs come from
    /// <see cref="CombatVisualDefinition"/> (a missing prefab means no such effect) and are pooled; the pools are filled
    /// while loading and one of each is drawn by <see cref="LevelWarmup"/>, so the first shots create nothing.
    /// Must be registered after <see cref="PlayerWeaponPresenter"/>: the late tick reads the muzzle after the gun is posed.
    /// </summary>
    public sealed class CombatViewPresenter : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        /// <summary>Height of shots without a gun model (and of melee hits), units.</summary>
        private const float ShotHeight = 1.0f;

        private const float SwingHeight = 0.8f;

        /// <summary>Seconds the swing stroke starts before the clip's hit: its head is mid-sector at the hit.</summary>
        public const float SwingLead = 0.06f;

        /// <summary>Cells over which a tracer moves from the muzzle onto the bullet's real line.</summary>
        public const float ConvergeDistance = 1.5f;

        /// <summary>Where along the gun (grip → muzzle) shells come out.</summary>
        private const float EjectShare = 0.4f;

        public const int PrefilledBullets = 8;
        public const int PrefilledImpacts = 4;
        public const int PrefilledSwings = 2;
        private const int PrefilledFlashes = 3;
        private const int PrefilledShells = 8;
        private const int PrefilledHits = 4;

        private readonly CombatVisualDefinition _visual;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly BulletSystem _bullets;
        private readonly PlayerCombat _combat;
        private readonly PlayerSystem _player;
        private readonly VisibilitySystem _visibility;
        private readonly PlayerWeaponPresenter _weaponView;
        private readonly PlayerViewPresenter _playerView;

        private readonly Dictionary<Bullet, Tracer> _tracers = new Dictionary<Bullet, Tracer>();
        private readonly List<Tracer> _newTracers = new List<Tracer>();
        private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();
        private readonly List<GameObject> _warmup = new List<GameObject>();
        private readonly List<IDamageable> _meleeTargets = new List<IDamageable>();

        private ObjectPool<Tracer> _tracerPool;
        private EffectPool _flashPool;
        private EffectPool _shellPool;
        private EffectPool _impactPool;
        private EffectPool _hitPool;
        private EffectPool _swingPool;
        private bool _shotPending;
        private Vector2 _shotDirection;
        private int _swingCount;
        private bool _meleePending;
        private bool _meleeTimed;
        private bool _swingShown;
        private float _swingLeft;
        private float _meleeHitsLeft;
        private Vector2 _meleeDirection;
        private float _meleeSweep;
        private WeaponDefinition _meleeWeapon;
        private bool _subscribed;

        public CombatViewPresenter(CombatVisualDefinition visual, IAssetOwner assets, LevelViewRoot root, BulletSystem bullets,
            PlayerCombat combat, PlayerSystem player, VisibilitySystem visibility, PlayerWeaponPresenter weaponView,
            PlayerViewPresenter playerView)
        {
            _playerView = playerView;
            _visual = visual;
            _assets = assets;
            _root = root;
            _bullets = bullets;
            _combat = combat;
            _player = player;
            _visibility = visibility;
            _weaponView = weaponView;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public int ActiveBulletViews => _tracers.Count;

        /// <summary>Effects being shown now (flashes, shells, impacts, hits, swings).</summary>
        public int ActiveEffects => _effects.Count;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var (bullet, flash, shell, impact, hit, swing) = await UniTask.WhenAll(
                Load(_visual.Bullet, cancellation), Load(_visual.MuzzleFlash, cancellation), Load(_visual.ShellCasing, cancellation),
                Load(_visual.Impact, cancellation), Load(_visual.TargetHit, cancellation), Load(_visual.MeleeSwing, cancellation));
            cancellation.ThrowIfCancellationRequested();

            _tracerPool = CreateTracerPool(bullet);
            _flashPool = CreateEffectPool(flash, "MuzzleFlash", PrefilledFlashes);
            _shellPool = CreateEffectPool(shell, "ShellCasing", PrefilledShells);
            _impactPool = CreateEffectPool(impact, "Impact", PrefilledImpacts);
            _hitPool = CreateEffectPool(hit, "TargetHit", PrefilledHits);
            _swingPool = CreateEffectPool(swing, "MeleeSwing", PrefilledSwings);
            PrefillTracers();

            if (!_subscribed)
            {
                _bullets.Spawned += OnBulletSpawned;
                _bullets.Ended += OnBulletEnded;
                _combat.Attacked += OnAttacked;
                _subscribed = true;
            }
        }

        public void LateTick(float deltaTime)
        {
            if (_shotPending)
                ShowShot();
            if (_meleePending)
                UpdateMelee(deltaTime);

            foreach (var pair in _tracers)
                PlaceTracer(pair.Key, pair.Value);

            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                var active = _effects[i];
                active.Elapsed += deltaTime;
                var effect = active.Effect;
                if (active.Elapsed < effect.Duration)
                {
                    if (effect.Component != null) effect.Component.Animate(active.Elapsed / effect.Duration);
                    _effects[i] = active;
                    continue;
                }

                active.Pool.Release(effect);
                _effects.RemoveAt(i);
            }
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_warmup);

        public void Dispose()
        {
            if (_subscribed)
            {
                _bullets.Spawned -= OnBulletSpawned;
                _bullets.Ended -= OnBulletEnded;
                _combat.Attacked -= OnAttacked;
                _subscribed = false;
            }

            foreach (var tracer in _tracers.Values)
                UnityObjects.Destroy(tracer.View);
            _tracers.Clear();
            _newTracers.Clear();
            foreach (var active in _effects)
                UnityObjects.Destroy(active.Effect.View);
            _effects.Clear();
            _warmup.Clear();
            _shotPending = false;
            _meleePending = false;
            _meleeTargets.Clear();

            _tracerPool?.Dispose();
            _tracerPool = null;
            foreach (var pool in new[] { _flashPool, _shellPool, _impactPool, _hitPool, _swingPool })
                pool?.Dispose();
            _flashPool = _shellPool = _impactPool = _hitPool = _swingPool = null;
        }

        private UniTask<GameObject> Load(AssetReferenceGameObject reference, CancellationToken cancellation) =>
            reference != null && reference.RuntimeKeyIsValid()
                ? _assets.LoadAsync<GameObject>(reference, cancellation)
                : UniTask.FromResult<GameObject>(null);

        // ---- Pools ----

        private ObjectPool<Tracer> CreateTracerPool(GameObject prefab)
        {
            if (prefab == null)
                return null;

            return new ObjectPool<Tracer>(
                () =>
                {
                    var instance = UnityEngine.Object.Instantiate(prefab, _root.transform);
                    instance.name = "Bullet";
                    return new Tracer { View = instance };
                },
                tracer => tracer.View.SetActive(true),
                tracer => tracer.View.SetActive(false),
                tracer => UnityObjects.Destroy(tracer.View),
                collectionCheck: false, defaultCapacity: PrefilledBullets);
        }

        private void PrefillTracers()
        {
            if (_tracerPool == null) return;
            var taken = new Tracer[PrefilledBullets];
            for (var i = 0; i < taken.Length; i++)
                taken[i] = _tracerPool.Get();
            _warmup.Add(taken[0].View); // one of each kind is enough to draw
            foreach (var tracer in taken)
                _tracerPool.Release(tracer);
        }

        private EffectPool CreateEffectPool(GameObject prefab, string name, int prefill)
        {
            if (prefab == null)
                return null;

            var pool = new EffectPool(prefab, name, _root.transform, _visual.EffectDuration);
            var taken = new Effect[prefill];
            for (var i = 0; i < prefill; i++)
                taken[i] = pool.Get();
            _warmup.Add(taken[0].View);
            foreach (var effect in taken)
                pool.Release(effect);
            return pool;
        }

        // ---- Events ----

        private void OnBulletSpawned(Bullet bullet)
        {
            if (_tracerPool == null) return;
            var tracer = _tracerPool.Get();
            tracer.Spawn = bullet.Position;
            tracer.Origin = new Vector3(bullet.Position.x, ShotHeight, bullet.Position.y);
            tracer.FromMuzzle = false;
            _tracers[bullet] = tracer;
            _newTracers.Add(tracer);
            PlaceTracer(bullet, tracer);
        }

        private void OnBulletEnded(Bullet bullet, Vector2 point, BulletEnd reason)
        {
            var height = ShotHeight;
            if (_tracers.TryGetValue(bullet, out var tracer))
            {
                height = tracer.Origin.y;
                _tracers.Remove(bullet);
                _newTracers.Remove(tracer);
                _tracerPool.Release(tracer);
            }

            if (reason == BulletEnd.Blocked)
                Show(_impactPool, point, height, WallNormal(point, bullet.Direction));
            else if (reason == BulletEnd.Hit)
                Show(_hitPool, point, height, -bullet.Direction);
        }

        private void OnAttacked(WeaponRuntime weapon, Vector2 direction)
        {
            if (weapon.Slot == WeaponSlot.Ranged)
            {
                // The gun is posed for this frame only in the late tick: the flash and shell wait for it.
                _shotPending = true;
                _shotDirection = direction;
                return;
            }

            // The previous attack's visuals (if still waiting) are shown now; the clip timing is read in the late tick.
            if (_meleePending)
                FinishMelee();

            _meleePending = true;
            _meleeTimed = false;
            _meleeSweep = 0f;
            _swingShown = false;
            _meleeDirection = direction;
            _meleeWeapon = weapon.Definition;
            _meleeTargets.Clear();
            _meleeTargets.AddRange(_combat.LastMeleeHits);
        }

        private void UpdateMelee(float deltaTime)
        {
            if (!_meleeTimed)
            {
                var contact = _playerView != null ? _playerView.LastMeleeContactDelay : 0f;
                _meleeHitsLeft = contact;
                _swingLeft = Mathf.Max(0f, contact - SwingLead);
                _meleeSweep = _playerView != null ? _playerView.LastMeleeSweep : 0f;
                _meleeTimed = true;
            }
            else
            {
                _swingLeft -= deltaTime;
                _meleeHitsLeft -= deltaTime;
            }

            if (!_swingShown && _swingLeft <= 0f)
                ShowSwing();
            if (_meleeHitsLeft <= 0f)
                FinishMelee();
        }

        private void FinishMelee()
        {
            if (!_swingShown)
                ShowSwing();
            foreach (var target in _meleeTargets)
                Show(_hitPool, target.Position - _meleeDirection * (target.Radius * 0.8f), ShotHeight, -_meleeDirection);
            _meleeTargets.Clear();
            _meleePending = false;
        }

        /// <summary>Around the player where they are now: the stroke belongs to the swinging arms.</summary>
        private void ShowSwing()
        {
            _swingShown = true;
            var swing = Show(_swingPool, _player.Position, SwingHeight, _meleeDirection, play: false);
            if (swing == null) return;
            if (swing.Component != null && swing.Component.Swing != null)
            {
                var leftToRight = _meleeSweep != 0f ? _meleeSweep > 0f : _swingCount++ % 2 == 0;
                swing.Component.Swing.Setup(_meleeWeapon.MeleeArc, _meleeWeapon.MeleeRange, leftToRight);
            }
            swing.Play();
        }

        // ---- Placement ----

        private void ShowShot()
        {
            _shotPending = false;
            var model = _weaponView != null ? _weaponView.ShownModel : null;
            var muzzle = model != null ? model.Muzzle : null;
            var rootTransform = _root.transform;

            if (muzzle == null)
            {
                var forward = _player.Position + _shotDirection * 0.5f;
                Show(_flashPool, forward, ShotHeight, _shotDirection);
                _newTracers.Clear();
                return;
            }

            var muzzlePoint = rootTransform.InverseTransformPoint(muzzle.position);
            var barrel = rootTransform.InverseTransformDirection(muzzle.forward);
            ShowAt(_flashPool, muzzlePoint, Quaternion.LookRotation(barrel, Vector3.up));

            var gun = model.transform;
            var eject = rootTransform.InverseTransformPoint(Vector3.Lerp(gun.position, muzzle.position, EjectShare));
            var right = Vector3.ProjectOnPlane(rootTransform.InverseTransformDirection(gun.right), Vector3.up);
            if (right.sqrMagnitude > 1e-4f)
                ShowAt(_shellPool, eject, Quaternion.LookRotation(right, Vector3.up));

            foreach (var tracer in _newTracers)
            {
                tracer.Origin = muzzlePoint;
                tracer.FromMuzzle = true;
            }
            _newTracers.Clear();
        }

        private void PlaceTracer(Bullet bullet, Tracer tracer)
        {
            var height = tracer.Origin.y;
            var head = new Vector3(bullet.Position.x, height, bullet.Position.y);
            if (tracer.FromMuzzle)
            {
                var travelled = (bullet.Position - tracer.Spawn).magnitude;
                var line = new Vector3(tracer.Spawn.x, height, tracer.Spawn.y);
                head += (tracer.Origin - line) * (1f - Mathf.Clamp01(travelled / ConvergeDistance));
            }

            var toHead = head - tracer.Origin;
            var distance = toHead.magnitude;
            var forward = distance > 1e-3f ? toHead / distance : new Vector3(bullet.Direction.x, 0f, bullet.Direction.y);

            var transform = tracer.View.transform;
            transform.localPosition = head;
            if (forward.sqrMagnitude > 0f)
                transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
            if (_visual.TracerLength > 0f)
                transform.localScale = new Vector3(1f, 1f, Mathf.Max(Mathf.Min(_visual.TracerLength, distance), 0.01f));

            var visible = _visibility.IsVisible(Cell(bullet.Position));
            if (tracer.View.activeSelf != visible)
                tracer.View.SetActive(visible);
        }

        private Effect Show(EffectPool pool, Vector2 position, float height, Vector2 direction, bool play = true)
        {
            if (pool == null || !_visibility.IsVisible(Cell(position)))
                return null;

            var rotation = direction.sqrMagnitude > 0f
                ? Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y), Vector3.up)
                : Quaternion.identity;
            return ShowAt(pool, new Vector3(position.x, height, position.y), rotation, play);
        }

        private Effect ShowAt(EffectPool pool, Vector3 position, Quaternion rotation, bool play = true)
        {
            if (pool == null)
                return null;

            var effect = pool.Get();
            effect.View.transform.SetLocalPositionAndRotation(position, rotation);
            if (play) effect.Play();
            _effects.Add(new ActiveEffect { Effect = effect, Pool = pool });
            return effect;
        }

        /// <summary>
        /// Outward normal of the wall face a bullet stopped at: the cell edge (x or y = k + 0.5) the point lies closest
        /// to, facing back against the flight.
        /// </summary>
        private static Vector2 WallNormal(Vector2 point, Vector2 direction)
        {
            var edgeX = Mathf.Abs(point.x - 0.5f - Mathf.Round(point.x - 0.5f));
            var edgeY = Mathf.Abs(point.y - 0.5f - Mathf.Round(point.y - 0.5f));
            if (edgeX <= edgeY && Mathf.Abs(direction.x) > 1e-4f)
                return new Vector2(-Mathf.Sign(direction.x), 0f);
            if (Mathf.Abs(direction.y) > 1e-4f)
                return new Vector2(0f, -Mathf.Sign(direction.y));
            return -direction;
        }

        private static Core.Grid.GridPosition Cell(Vector2 position) => PlayerMovement.CellOf(position);

        private sealed class Tracer
        {
            public GameObject View;

            /// <summary>Where the gameplay bullet started (the player's centre), grid units.</summary>
            public Vector2 Spawn;

            /// <summary>Where the tracer starts (the muzzle, or the bullet's start at <see cref="ShotHeight"/>), view root space.</summary>
            public Vector3 Origin;

            public bool FromMuzzle;
        }

        private struct ActiveEffect
        {
            public Effect Effect;
            public EffectPool Pool;
            public float Elapsed;
        }

        private sealed class Effect
        {
            public GameObject View;
            public CombatEffect Component;
            public ParticleSystem[] Particles;
            public float Duration;

            public void Play()
            {
                if (Component != null) Component.Play();
                else CombatEffect.Restart(Particles);
            }
        }

        private sealed class EffectPool : IDisposable
        {
            private readonly ObjectPool<Effect> _pool;

            public EffectPool(GameObject prefab, string name, Transform parent, float defaultDuration)
            {
                _pool = new ObjectPool<Effect>(
                    () =>
                    {
                        var instance = UnityEngine.Object.Instantiate(prefab, parent);
                        instance.name = name;
                        var component = instance.GetComponent<CombatEffect>();
                        return new Effect
                        {
                            View = instance,
                            Component = component,
                            Particles = component == null ? CombatEffect.RootSystems(instance) : null,
                            Duration = component != null ? component.Duration : defaultDuration,
                        };
                    },
                    effect => effect.View.SetActive(true),
                    effect => effect.View.SetActive(false),
                    effect => UnityObjects.Destroy(effect.View),
                    collectionCheck: false, defaultCapacity: 8);
            }

            public Effect Get() => _pool.Get();
            public void Release(Effect effect) => _pool.Release(effect);
            public void Dispose() => _pool.Dispose();
        }
    }
}
