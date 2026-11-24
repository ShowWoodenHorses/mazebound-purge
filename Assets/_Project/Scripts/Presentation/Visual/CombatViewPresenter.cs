using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Visual;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Visibility;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Pool;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Views of combat (ТЗ §79, §83), display only, cartoon style:
    /// <list type="bullet">
    /// <item>shot: a flash at the gun's muzzle, a shell thrown out to the right, a tracer that leaves the muzzle and
    /// joins the bullet's real line (gameplay bullets fly from the player's centre) within <see cref="ConvergeDistance"/>;</item>
    /// <item>bullet stopped by a wall or closed door: sparks and dust out of the wall; hit on a zombie (bullet or
    /// melee): a hit "star" (the model's flash is <see cref="ZombieViewPresenter"/>'s);</item>
    /// <item>melee: a swing stroke over the weapon's sector (<see cref="SwingArc"/>: arc and reach of the weapon);</item>
    /// <item>stage Э2 (pack effects, <see cref="PackEffectsPlayer"/>): a stream's looping look held at the muzzle while it
    /// fires (no flash then), smoke out of the muzzle after a burst of bullets; shells only from cartridge guns.</item>
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
        private static readonly int ClipPlaneId = Shader.PropertyToID("_ClipPlane");

        /// <summary>Where along the gun (grip → muzzle) shells come out.</summary>
        private const float EjectShare = 0.4f;

        /// <summary>Seconds after a stream's tick (beyond its interval) its look keeps playing: no flicker between ticks.</summary>
        private const float StreamGap = 0.15f;

        /// <summary>Seconds of no shot (beyond the interval) that end a burst — as the weapon's sound tail.</summary>
        private const float BurstGap = 0.12f;

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
        private readonly PackEffectsPlayer _packEffects;
        private readonly Deployables _deployables;
        private readonly DeployablesView _deployablesView;
        private readonly CombatTargets _targets;

        private readonly Dictionary<Bullet, Tracer> _tracers = new Dictionary<Bullet, Tracer>();
        private readonly List<Tracer> _newTracers = new List<Tracer>();
        private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();
        private readonly List<GameObject> _warmup = new List<GameObject>();
        private readonly List<IDamageable> _meleeTargets = new List<IDamageable>();

        private ObjectPool<Tracer> _tracerPool;
        private ObjectPool<Tracer> _rocketPool;
        private ObjectPool<Tracer> _fireballPool;
        private EffectPool _flashPool;
        private EffectPool _shellPool;
        private EffectPool _impactPool;
        private EffectPool _hitPool;
        private EffectPool _swingPool;
        private bool _shotPending;
        private Vector2 _shotDirection;
        private WeaponDefinition _shotWeapon;
        private int _swingCount;
        private bool _meleePending;
        private bool _meleeTimed;
        private bool _swingShown;
        private float _swingLeft;
        private float _meleeHitsLeft;
        private Vector2 _meleeDirection;
        private float _meleeSweep;
        private WeaponDefinition _meleeWeapon;
        private AssetReferenceGameObject _streamLook;
        private float _streamReach;
        private float _streamLeft;
        private WeaponDefinition _burstWeapon;
        private int _burstShots;
        private float _burstLeft;
        private bool _subscribed;

        public CombatViewPresenter(CombatVisualDefinition visual, IAssetOwner assets, LevelViewRoot root, BulletSystem bullets,
            PlayerCombat combat, PlayerSystem player, VisibilitySystem visibility, PlayerWeaponPresenter weaponView,
            PlayerViewPresenter playerView, PackEffectsPlayer packEffects = null, CombatTargets targets = null,
            Deployables deployables = null, DeployablesView deployablesView = null)
        {
            _targets = targets;
            _deployables = deployables;
            _deployablesView = deployablesView;
            _playerView = playerView;
            _packEffects = packEffects;
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
            var (bullet, flash, shell, impact, hit, swing, rocket, fireball) = await UniTask.WhenAll(
                Load(_visual.Bullet, cancellation), Load(_visual.MuzzleFlash, cancellation), Load(_visual.ShellCasing, cancellation),
                Load(_visual.Impact, cancellation), Load(_visual.TargetHit, cancellation), Load(_visual.MeleeSwing, cancellation),
                Load(_visual.Rocket, cancellation), Load(_visual.Fireball, cancellation));
            cancellation.ThrowIfCancellationRequested();

            _tracerPool = CreateTracerPool(bullet);
            _rocketPool = CreateTracerPool(rocket, rocket: true);
            _fireballPool = CreateTracerPool(fireball, fireball: true);
            _flashPool = CreateEffectPool(flash, "MuzzleFlash", PrefilledFlashes);
            _shellPool = CreateEffectPool(shell, "ShellCasing", PrefilledShells);
            _impactPool = CreateEffectPool(impact, "Impact", PrefilledImpacts);
            _hitPool = CreateEffectPool(hit, "TargetHit", PrefilledHits);
            _swingPool = CreateEffectPool(swing, "MeleeSwing", PrefilledSwings);
            PrefillTracers(_tracerPool, PrefilledBullets);
            PrefillTracers(_rocketPool, 4);
            PrefillTracers(_fireballPool, 4);

            if (!_subscribed)
            {
                _bullets.Spawned += OnBulletSpawned;
                _bullets.Ended += OnBulletEnded;
                if (_deployables != null) _deployables.TurretFired += OnTurretFired;
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
            UpdateStream(deltaTime);
            UpdateBurst(deltaTime);

            foreach (var pair in _tracers)
                PlaceTracer(pair.Key, pair.Value, startFireball: true);

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
                if (_deployables != null) _deployables.TurretFired -= OnTurretFired;
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
            _streamLook = null;
            _burstWeapon = null;

            _tracerPool?.Dispose();
            _rocketPool?.Dispose();
            _fireballPool?.Dispose();
            _tracerPool = null;
            _rocketPool = _fireballPool = null;
            foreach (var pool in new[] { _flashPool, _shellPool, _impactPool, _hitPool, _swingPool })
                pool?.Dispose();
            _flashPool = _shellPool = _impactPool = _hitPool = _swingPool = null;
        }

        private UniTask<GameObject> Load(AssetReferenceGameObject reference, CancellationToken cancellation) =>
            reference != null && reference.RuntimeKeyIsValid()
                ? _assets.LoadAsync<GameObject>(reference, cancellation)
                : UniTask.FromResult<GameObject>(null);

        // ---- Pools ----

        private ObjectPool<Tracer> CreateTracerPool(GameObject prefab, bool rocket = false, bool fireball = false)
        {
            if (prefab == null)
                return null;

            return new ObjectPool<Tracer>(
                () =>
                {
                    var instance = UnityEngine.Object.Instantiate(prefab, _root.transform);
                    instance.name = "Bullet";
                    var renderer = instance.GetComponentInChildren<Renderer>();
                    var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
                    var head = fireball ? instance.GetComponent<ParticleSystem>() : null;
                    if (head != null)
                    {
                        var emission = head.emission;
                        emission.enabled = false;
                        var shape = head.shape;
                        shape.enabled = false;
                        var size = head.sizeOverLifetime;
                        size.enabled = false;
                        var color = head.colorOverLifetime;
                        color.enabled = false;
                    }
                    var fireballTrail = fireball ? new List<Renderer>() : null;
                    if (fireball)
                    {
                        foreach (var system in particles)
                        {
                            var main = system.main;
                            main.playOnAwake = false;
                            if (system != head && system.TryGetComponent<ParticleSystemRenderer>(out var trailRenderer))
                                fireballTrail.Add(trailRenderer);
                        }
                    }
                    return new Tracer
                    {
                        View = instance,
                        OriginalScale = instance.transform.localScale,
                        Renderer = renderer,
                        OriginalMaterial = renderer != null ? renderer.sharedMaterial : null,
                        Tint = renderer != null ? new MaterialPropertyBlock() : null,
                        Particles = particles,
                        FireballHead = head,
                        FireballTrail = fireballTrail?.ToArray(),
                        Trail = rocket ? CreateRocketTrail(instance, renderer) : null,
                    };
                },
                tracer => tracer.View.SetActive(true),
                tracer => tracer.View.SetActive(false),
                tracer => UnityObjects.Destroy(tracer.View),
                collectionCheck: false, defaultCapacity: PrefilledBullets);
        }

        private TrailRenderer CreateRocketTrail(GameObject rocket, Renderer renderer)
        {
            if (_visual.BoltMaterial == null) return null;
            var exhaust = new GameObject("Rocket Exhaust");
            exhaust.transform.SetParent(rocket.transform, false);
            if (renderer != null)
                exhaust.transform.localPosition = new Vector3(0f, 0f, renderer.localBounds.min.z);
            var trail = exhaust.AddComponent<TrailRenderer>();
            trail.sharedMaterial = _visual.BoltMaterial;
            trail.time = 0.07f;
            trail.minVertexDistance = 0.035f;
            trail.startWidth = 0.13f;
            trail.endWidth = 0.015f;
            trail.startColor = new Color(1f, 0.82f, 0.48f, 0.9f);
            trail.endColor = new Color(1f, 0.34f, 0.09f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            return trail;
        }

        private void PrefillTracers(ObjectPool<Tracer> pool, int count)
        {
            if (pool == null) return;
            var taken = new Tracer[count];
            for (var i = 0; i < taken.Length; i++)
                taken[i] = pool.Get();
            _warmup.Add(taken[0].View); // one of each kind is enough to draw
            foreach (var tracer in taken)
                pool.Release(tracer);
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
            if (_tracerPool == null || bullet.Throwable != null) return; // thrown items: DropViewPresenter
            var effects = bullet.Weapon?.Effects;
            var special = effects?.RocketProjectile == true ? _rocketPool : effects?.FireProjectile == true ? _fireballPool : null;
            var pool = special ?? _tracerPool;
            var tracer = pool.Get();
            tracer.Pool = pool;
            tracer.Model = special != null;
            tracer.Spawn = bullet.Position;
            tracer.Origin = new Vector3(bullet.Position.x, BulletHeight(bullet), bullet.Position.y);
            tracer.FromMuzzle = false;
            tracer.KeepMuzzleHeight = false;
            tracer.Electric = bullet.Weapon?.Effects?.ElectricProjectile == true;
            tracer.Fire = bullet.Weapon?.Effects?.FireProjectile == true;
            tracer.FireballPending = tracer.FireballTrail != null;
            if (tracer.Model)
            {
                if (tracer.Trail != null)
                {
                    tracer.Trail.emitting = false;
                    tracer.Trail.Clear();
                }
                foreach (var particles in tracer.Particles)
                {
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    if (!tracer.FireballPending) particles.Play(false);
                }
            }
            else if (tracer.Renderer != null)
            {
                tracer.Tint.Clear();
                tracer.Renderer.sharedMaterial = tracer.Electric && _visual.ChainBolt != null ? _visual.ChainBolt : tracer.OriginalMaterial;
                if (tracer.Fire) tracer.Tint.SetColor("_Color", new Color(2f, 0.65f, 0.12f, 1f));
                tracer.Renderer.SetPropertyBlock(tracer.Tint);
            }
            _tracers[bullet] = tracer;
            if (!bullet.IsLobbed) _newTracers.Add(tracer);
            PlaceTracer(bullet, tracer);
        }

        private void OnTurretFired(DeployedItem turret, Bullet bullet)
        {
            if (!_tracers.TryGetValue(bullet, out var tracer)) return;
            _newTracers.Remove(tracer);
            if (_deployablesView == null || !_deployablesView.TryGetMuzzle(turret, out var muzzle)) return;

            var root = _root.transform;
            tracer.Origin = root.InverseTransformPoint(muzzle.position);
            tracer.FromMuzzle = true;
            tracer.KeepMuzzleHeight = true;
            PlaceTracer(bullet, tracer);
            if (_visibility.IsVisible(PlayerMovement.CellOf(turret.Position, turret.Floor)))
                ShowAt(_flashPool, tracer.Origin, Quaternion.LookRotation(root.InverseTransformDirection(muzzle.forward)));
        }

        private void OnBulletEnded(Bullet bullet, Vector2 point, BulletEnd reason)
        {
            if (bullet.Throwable != null) return;
            var height = BulletHeight(bullet);
            if (_tracers.TryGetValue(bullet, out var tracer))
            {
                height = tracer.Origin.y;
                _tracers.Remove(bullet);
                _newTracers.Remove(tracer);
                tracer.Pool.Release(tracer);
            }

            if (reason == BulletEnd.Blocked)
                Show(_impactPool, point, height, WallNormal(point, bullet.Direction), bullet.Floor);
            else if (reason == BulletEnd.Hit)
                Show(_hitPool, point, height, -bullet.Direction, bullet.Floor);
        }

        private void OnAttacked(WeaponRuntime weapon, Vector2 direction)
        {
            var definition = weapon.Definition;
            if (definition.Attack is StreamAttack stream && definition.Effects?.ElectricCone != true &&
                HoldsStream(definition, stream))
                return; // the stream's look is the whole shot: no flash, no shell
            if (definition.Attack is BulletAttack bullets)
                CountBurst(definition, bullets);

            if (!definition.IsMelee)
            {
                // The gun is posed for this frame only in the late tick: the flash and shell wait for it.
                _shotPending = true;
                _shotDirection = direction;
                _shotWeapon = weapon.Definition;
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

        // ---- Streams and bursts (stage Э2) ----

        /// <summary>A stream with a loaded pack look keeps it held until the attacks stop.</summary>
        private bool HoldsStream(WeaponDefinition weapon, StreamAttack stream)
        {
            var look = _visual.StreamOf(weapon);
            if (_packEffects == null || !_packEffects.IsLoaded(look)) return false;
            if (_streamLook != null && _streamLook.AssetGUID != look.AssetGUID) _packEffects.Release(_streamLook);
            _streamLook = look;
            _streamReach = stream.Range;
            _streamLeft = weapon.Cooldown + StreamGap;
            return true;
        }

        private void UpdateStream(float deltaTime)
        {
            if (_streamLook == null) return;
            _streamLeft -= deltaTime;
            if (_streamLeft > 0f && Muzzle(out var position, out var rotation, flat: true))
            {
                var reach = ReachToWall(position, rotation, out var cut);
                _packEffects.Hold(_streamLook, position, rotation, reach, cut);
                return;
            }

            _packEffects.Release(_streamLook);
            _streamLook = null;
        }

        /// <summary>
        /// The stream's reach from the muzzle, cut short by the first wall ahead (display only); <paramref name="cut"/> —
        /// how far from the muzzle the wall's face is (everything beyond is not drawn), 0 = no wall.
        /// </summary>
        private float ReachToWall(Vector3 muzzle, Quaternion rotation, out float cut)
        {
            cut = 0f;
            if (_targets == null) return _streamReach;
            var forward = rotation * Vector3.forward;
            var direction = new Vector2(forward.x, forward.z);
            if (direction.sqrMagnitude < 1e-6f) return _streamReach;
            direction.Normalize();
            // From the player's centre (the muzzle may already be in the wall the player hugs).
            var origin = _player.Position;
            var muzzleAhead = Vector2.Dot(new Vector2(muzzle.x, muzzle.z) - origin, direction);
            var length = _streamReach + Mathf.Max(0f, muzzleAhead);
            var hit = _targets.Spatial.QuerySegment(origin, origin + direction * length, _player.Floor);
            if (hit.Kind != SegmentHitKind.Blocker) return _streamReach;
            var toWall = hit.T * length - muzzleAhead;
            cut = Mathf.Max(0.05f, toWall + WallOverlap);
            return Mathf.Clamp(toWall + WallOverlap, MinStreamReach, _streamReach);
        }

        /// <summary>The flame is drawn this far into the wall's cell (its face is the cell's edge, the wall mesh starts there).</summary>
        private const float WallOverlap = 0.05f;
        private const float MinStreamReach = 0.35f;

        private void CountBurst(WeaponDefinition weapon, BulletAttack bullets)
        {
            if (_burstWeapon != weapon) _burstShots = 0;
            _burstWeapon = weapon;
            _burstShots += Mathf.Max(1, bullets.Pellets);
            _burstLeft = weapon.Cooldown + BurstGap;
        }

        /// <summary>A burst long enough smokes out of the muzzle when it stops.</summary>
        private void UpdateBurst(float deltaTime)
        {
            if (_burstWeapon == null) return;
            _burstLeft -= deltaTime;
            if (_burstLeft > 0f) return;
            var smoke = _burstShots >= _visual.BarrelSmokeShots ? _visual.BarrelSmokeOf(_burstWeapon) : null;
            if (smoke != null && _packEffects != null && Muzzle(out var position, out var rotation, flat: false))
                _packEffects.Play(smoke, position, rotation);
            _burstWeapon = null;
            _burstShots = 0;
        }

        /// <summary>The posed gun's muzzle (view root space), else in front of the player at shot height.</summary>
        private bool Muzzle(out Vector3 position, out Quaternion rotation, bool flat)
        {
            var model = _weaponView != null ? _weaponView.ShownModel : null;
            var muzzle = model != null ? model.Muzzle : null;
            var aim = _combat.AimDirection;
            var forward = new Vector3(aim.x, 0f, aim.y);
            if (muzzle != null)
            {
                var rootTransform = _root.transform;
                position = rootTransform.InverseTransformPoint(model.MuzzlePosition);
                var barrel = rootTransform.InverseTransformDirection(muzzle.forward);
                if (flat) barrel = Vector3.ProjectOnPlane(barrel, Vector3.up);
                if (barrel.sqrMagnitude > 1e-4f) forward = barrel;
            }
            else
            {
                var front = _player.Position + aim * 0.5f;
                position = new Vector3(front.x, ShotHeight, front.y);
            }

            rotation = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
            return _player.IsSpawned;
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
                Show(_hitPool, target.Position - _meleeDirection * (target.Radius * 0.8f), _player.ViewHeight + ShotHeight, -_meleeDirection, _player.Floor);
            _meleeTargets.Clear();
            _meleePending = false;
        }

        /// <summary>Around the player where they are now: the stroke belongs to the swinging arms.</summary>
        private void ShowSwing()
        {
            _swingShown = true;
            var swing = Show(_swingPool, _player.Position, _player.ViewHeight + SwingHeight, _meleeDirection, _player.Floor, play: false);
            if (swing == null) return;
            if (swing.Component != null && swing.Component.Swing != null)
            {
                var leftToRight = _meleeSweep != 0f ? _meleeSweep > 0f : _swingCount++ % 2 == 0;
                var melee = _meleeWeapon.Attack as MeleeAttack;
                swing.Component.Swing.Setup(melee != null ? melee.Arc : 100f, melee != null ? melee.Range : 1.2f, leftToRight);
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
            var electric = _shotWeapon?.Attack is BeamAttack or ChainAttack || _shotWeapon?.Effects?.ElectricCone == true ||
                _shotWeapon?.Effects?.ElectricProjectile == true || _shotWeapon?.Effects?.FrostStream == true;

            if (muzzle == null)
            {
                var forward = _player.Position + _shotDirection * 0.5f;
                var flat = new Vector3(_shotDirection.x, 0f, _shotDirection.y);
                var height = _player.ViewHeight + ShotHeight;
                if (!electric && !PlayPackFlash(new Vector3(forward.x, height, forward.y),
                        flat.sqrMagnitude > 0f ? Quaternion.LookRotation(flat, Vector3.up) : Quaternion.identity))
                    Show(_flashPool, forward, height, _shotDirection, _player.Floor);
                foreach (var tracer in _newTracers)
                {
                    if (!tracer.FireballPending) continue;
                    tracer.Origin = new Vector3(forward.x, height, forward.y);
                    tracer.FromMuzzle = true;
                }
                _newTracers.Clear();
                return;
            }

            var muzzlePoint = rootTransform.InverseTransformPoint(model.MuzzlePosition);
            var barrel = rootTransform.InverseTransformDirection(muzzle.forward);
            var barrelRotation = Quaternion.LookRotation(barrel, Vector3.up);
            if (!electric && !PlayPackFlash(muzzlePoint, barrelRotation))
                ShowAt(_flashPool, muzzlePoint, barrelRotation);

            var gun = model.transform;
            var eject = rootTransform.InverseTransformPoint(Vector3.Lerp(gun.position, muzzle.position, EjectShare));
            var right = Vector3.ProjectOnPlane(rootTransform.InverseTransformDirection(gun.right), Vector3.up);
            // Only guns that fire cartridges throw out shells (not streams or lightning).
            var shells = _shotWeapon != null && _shotWeapon.ArsenalClass != ArsenalClass.Energy &&
                (_shotWeapon.Attack is BulletAttack || _shotWeapon.Attack is ExplosiveAttack { FlightTime: <= 0f });
            if (shells && right.sqrMagnitude > 1e-4f)
                ShowAt(_shellPool, eject, Quaternion.LookRotation(right, Vector3.up));

            foreach (var tracer in _newTracers)
            {
                tracer.Origin = muzzlePoint;
                tracer.FromMuzzle = true;
            }
            _newTracers.Clear();
        }

        /// <summary>The weapon's own pack flash, else the default of its hold; false — none (the own flash is shown).</summary>
        private bool PlayPackFlash(Vector3 position, Quaternion rotation)
        {
            if (_packEffects == null || _shotWeapon == null) return false;
            var own = _shotWeapon.Effects != null ? _shotWeapon.Effects.MuzzleFlash : null;
            var flash = CombatVisualDefinition.IsSet(own) ? own : _visual.MuzzleFlashOf(_shotWeapon.Hold);
            return flash != null && _packEffects.Play(flash, position, rotation);
        }

        private void PlaceTracer(Bullet bullet, Tracer tracer, bool startFireball = false)
        {
            if (bullet.IsLobbed)
            {
                var progress = bullet.Progress;
                var arc = 4f * progress * (1f - progress);
                var position = new Vector3(bullet.Position.x,
                    bullet.Floor * GridPosition.FloorHeight + ShotHeight * (1f - progress) + 3f * arc,
                    bullet.Position.y);
                tracer.View.transform.localPosition = position;
                tracer.View.transform.localRotation = Quaternion.LookRotation(
                    new Vector3(bullet.Direction.x, 1f - 2f * progress, bullet.Direction.y));
                var size = 0.4f + 0.5f * arc;
                tracer.View.transform.localScale = new Vector3(size, size, size);
                tracer.View.SetActive(_visibility.IsVisible(PlayerMovement.CellOf(bullet.Position, bullet.Floor)));
                return;
            }

            var travelled = Mathf.Max(0f, bullet.Travel - bullet.RangeLeft);
            var length = tracer.Fire ? 0.22f : tracer.Electric ? 0.7f : _visual.TracerLength;
            var head = TracerPoint(bullet, tracer, bullet.Position, travelled);
            var tailDistance = Mathf.Min(Mathf.Max(length, 0.01f), travelled);
            var tail = TracerPoint(bullet, tracer, bullet.Position - bullet.Direction * tailDistance, travelled - tailDistance);
            var toHead = head - tail;
            var distance = toHead.magnitude;
            var forward = distance > 1e-3f ? toHead / distance : new Vector3(bullet.Direction.x, 0f, bullet.Direction.y);

            var transform = tracer.View.transform;
            transform.localPosition = head;
            if (forward.sqrMagnitude > 0f)
                transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
            var width = tracer.Fire ? 3.2f : tracer.Electric ? 2.2f : 1f;
            transform.localScale = tracer.Model
                ? tracer.OriginalScale * (tracer.Fire ? 0.5f : 1f)
                : length > 0f
                ? new Vector3(width, width, Mathf.Max(distance, 0.01f))
                : tracer.OriginalScale;

            var visible = _visibility.IsVisible(PlayerMovement.CellOf(bullet.Position, bullet.Floor));
            if (tracer.Trail != null)
            {
                if (!visible) tracer.Trail.Clear();
                tracer.Trail.emitting = visible && travelled > 0.05f;
            }
            if (tracer.View.activeSelf != visible)
                tracer.View.SetActive(visible);
            if (visible && startFireball && tracer.FireballPending)
                StartFireball(bullet, tracer, head);
        }

        private void StartFireball(Bullet bullet, Tracer tracer, Vector3 head)
        {
            tracer.FireballPending = false;
            var root = _root.transform;
            var muzzle = root.TransformPoint(tracer.Origin);
            var backward = -root.TransformDirection(new Vector3(bullet.Direction.x, 0f, bullet.Direction.y));
            var plane = new Vector4(backward.x, backward.y, backward.z, Vector3.Dot(backward, muzzle));
            foreach (var renderer in tracer.FireballTrail)
            {
                renderer.GetPropertyBlock(tracer.Tint);
                tracer.Tint.SetVector(ClipPlaneId, plane);
                renderer.SetPropertyBlock(tracer.Tint);
            }

            tracer.View.transform.localPosition = tracer.Origin;
            foreach (var particles in tracer.Particles)
                particles.Play(false);
            if (tracer.FireballHead != null)
                tracer.FireballHead.Emit(new ParticleSystem.EmitParams
                {
                    position = Vector3.zero,
                    velocity = Vector3.zero,
                    startSize = 0.58f,
                    startLifetime = 2f,
                    startColor = new Color(1f, 0.48f, 0.14f, 1f),
                }, 1);
            tracer.View.transform.localPosition = head;
        }

        private static Vector3 TracerPoint(Bullet bullet, Tracer tracer, Vector2 point, float travelled)
        {
            var height = tracer.KeepMuzzleHeight ? tracer.Origin.y : BulletHeight(bullet);
            var position = new Vector3(point.x, height, point.y);
            if (!tracer.FromMuzzle) return position;
            var simulatedOrigin = new Vector3(tracer.Spawn.x, height, tracer.Spawn.y);
            var offset = tracer.Origin - simulatedOrigin;
            var barrelLead = Mathf.Max(0f, Vector2.Dot(new Vector2(offset.x, offset.z), bullet.Direction));
            return position + offset * (1f - Mathf.Clamp01(travelled / (ConvergeDistance + barrelLead)));
        }

        private Effect Show(EffectPool pool, Vector2 position, float height, Vector2 direction, int floor, bool play = true)
        {
            if (pool == null || !_visibility.IsVisible(PlayerMovement.CellOf(position, floor)))
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

        private static float BulletHeight(Bullet bullet) => bullet.Floor * GridPosition.FloorHeight + ShotHeight;

        private sealed class Tracer
        {
            public GameObject View;
            public Vector3 OriginalScale;
            public Renderer Renderer;
            public Material OriginalMaterial;
            public MaterialPropertyBlock Tint;
            public ParticleSystem[] Particles;
            public ParticleSystem FireballHead;
            public Renderer[] FireballTrail;
            public bool FireballPending;
            public TrailRenderer Trail;
            public ObjectPool<Tracer> Pool;
            public bool Model;
            public bool Electric;
            public bool Fire;

            /// <summary>Where the gameplay bullet started (the player's centre), grid units.</summary>
            public Vector2 Spawn;

            /// <summary>Where the tracer starts (the muzzle, or the bullet's start at <see cref="ShotHeight"/>), view root space.</summary>
            public Vector3 Origin;

            public bool FromMuzzle;
            public bool KeepMuzzleHeight;
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
