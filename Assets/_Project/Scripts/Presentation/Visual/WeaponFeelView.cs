using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Platform;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Core.Feel;
using Purge.Core.Grid;
using Purge.Core.Visual;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using Purge.Gameplay.Weapons;
using Purge.Presentation.Visual.Horde;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Display part of the shooting feel (<see cref="FeelModule"/>, scaled by <see cref="FeelChannels"/>): per attack —
    /// the camera kick and shake, the gun's spring recoil and the chest rocking back, the flash of light at the muzzle
    /// (<c>_PurgeShotLight*</c> globals of PurgeLighting.cginc), a vibration; per hit on a monster of the horde — its
    /// flash and dent (<see cref="HordeRenderer.SetHitLook"/>), splashes of its colour and a ring under it; per kill —
    /// a bigger splash, a shake and a vibration. Melee hits wait for the clip's contact
    /// (<see cref="PlayerViewPresenter.LastMeleeContactDelay"/>). Effects only in visible cells. Two particle systems
    /// fed by <c>Emit</c> (one draw call each), emitted in the tick, paused while the level does not tick.
    /// </summary>
    public sealed class WeaponFeelView : ILevelLoadStep, ILevelTickable, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private static readonly int ShotLightPositionId = Shader.PropertyToID("_PurgeShotLightPosition");
        private static readonly int ShotLightColorId = Shader.PropertyToID("_PurgeShotLightColor");

        /// <summary>Seconds the shot's light fades out over.</summary>
        private const float ShotLightTime = 0.07f;

        /// <summary>Height of the light when the gun has no muzzle mark, metres.</summary>
        private const float ShotLightHeight = 1.1f;

        private const float SplashHeight = 0.6f;
        private const float RingHeight = 0.03f;
        private const float RingLifetime = 0.22f;
        private const int MaxSplashes = 384;
        private const int MaxRings = 48;
        private const int MaxPending = 32;

        private readonly PlayerCombat _combat;
        private readonly CombatTargets _targets;
        private readonly PlayerSystem _player;
        private readonly PlayerViewPresenter _playerView;
        private readonly PlayerWeaponPresenter _weaponView;
        private readonly TopDownCamera _camera;
        private readonly LevelViewRoot _root;
        private readonly CombatVisualDefinition _visual;
        private readonly FeelChannels _channels;
        private readonly VibrationService _vibration;
        private readonly VisibilitySystem _visibility;
        private readonly HordeRenderer _hordeView;
        private readonly DeterministicRandom _random = new DeterministicRandom(Environment.TickCount);
        private readonly Pending[] _pending = new Pending[MaxPending];
        private readonly List<GameObject> _objects = new List<GameObject>();

        private ParticleSystem _splashes;
        private ParticleSystem _rings;
        private Color _splashColor = new Color(0.55f, 0.85f, 0.25f, 1f);
        private Vector4 _shotLightPosition;
        private Color _shotLightColor;
        private float _shotLightLeft;
        private bool _subscribed;
        private bool _ticked;
        private float _time;

        public WeaponFeelView(PlayerCombat combat, CombatTargets targets, PlayerSystem player, PlayerViewPresenter playerView,
            PlayerWeaponPresenter weaponView, TopDownCamera camera, LevelViewRoot root, CombatVisualDefinition visual,
            FeelChannels channels, VibrationService vibration, VisibilitySystem visibility = null, HordeRenderer hordeView = null)
        {
            _combat = combat;
            _targets = targets;
            _player = player;
            _playerView = playerView;
            _weaponView = weaponView;
            _camera = camera;
            _root = root;
            _visual = visual;
            _channels = channels;
            _vibration = vibration;
            _visibility = visibility;
            _hordeView = hordeView;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>Splash particles (tests); null without the material.</summary>
        public ParticleSystem Splashes => _splashes;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_visual != null && _visual.Splash != null && _splashes == null)
            {
                _splashes = WeatherParticles.Create("Feel Splashes", _root.transform, _visual.Splash, MaxSplashes,
                    fadeIn: 0.01f, fadeOut: 0.6f, grow: 0.5f);
                var main = _splashes.main;
                main.gravityModifier = 2.2f;
                _objects.Add(_splashes.gameObject);
            }

            if (_visual != null && _visual.HitRing != null && _rings == null)
            {
                _rings = WeatherParticles.Create("Feel Rings", _root.transform, _visual.HitRing, MaxRings,
                    fadeIn: 0.01f, fadeOut: 0.3f, grow: 2.2f);
                _rings.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                _objects.Add(_rings.gameObject);
            }

            if (_hordeView?.Visual != null) _splashColor = _hordeView.Visual.SplashColor;

            if (!_subscribed)
            {
                _subscribed = true;
                _combat.Attacked += OnAttacked;
                _targets.WeaponDamaged += OnWeaponDamaged;
            }

            ClearShotLight();
            return UniTask.CompletedTask;
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_objects);

        public void Tick(float deltaTime)
        {
            _ticked = true;
            _time += deltaTime;
            if (_splashes != null && !_splashes.isPlaying) _splashes.Play(); // the warmup stops every particle system
            if (_rings != null && !_rings.isPlaying) _rings.Play();

            for (var i = 0; i < MaxPending; i++)
            {
                if (!_pending[i].Active || _pending[i].At > _time) continue;
                var hit = _pending[i];
                _pending[i] = default;
                ShowHit(hit.Agent, hit.Position, hit.Direction, hit.Feel, hit.Killed);
            }

            if (_shotLightLeft > 0f)
            {
                _shotLightLeft = Mathf.Max(0f, _shotLightLeft - deltaTime);
                var share = _shotLightLeft / ShotLightTime;
                Shader.SetGlobalVector(ShotLightPositionId, _shotLightPosition);
                Shader.SetGlobalColor(ShotLightColorId, _shotLightColor * (share * share));
            }
        }

        public void LateTick(float deltaTime)
        {
            _vibration?.Tick();
            if (_ticked)
            {
                _ticked = false;
                return;
            }

            // Not ticking (pause, map): the particles hold still.
            if (_splashes != null && _splashes.isPlaying) _splashes.Pause();
            if (_rings != null && _rings.isPlaying) _rings.Pause();
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _subscribed = false;
                _combat.Attacked -= OnAttacked;
                _targets.WeaponDamaged -= OnWeaponDamaged;
            }

            _vibration?.Stop();
            ClearShotLight();
            foreach (var go in _objects)
                UnityObjects.Destroy(go);
            _objects.Clear();
            _splashes = null;
            _rings = null;
        }

        // ------------------------------------------------------------------ Attack

        private void OnAttacked(WeaponRuntime weapon, Vector2 direction)
        {
            var definition = weapon.Definition;
            var feel = definition.Feel;
            if (_camera != null)
            {
                _camera.Kick(direction, feel.CameraKick * _channels[FeelChannel.CameraKick]);
                _camera.Shake(feel.CameraShake * _channels[FeelChannel.CameraShake]);
            }

            if (!definition.IsMelee)
            {
                _weaponView?.Kick(feel.GunKick * _channels[FeelChannel.GunKick], feel.GunKickAngle * _channels[FeelChannel.GunKick],
                    feel.BodyKick * _channels[FeelChannel.BodyKick]);
                ShotLight(feel, direction);
            }

            Vibrate(feel.VibrateAttack, 0.35f);
        }

        private void ShotLight(FeelModule feel, Vector2 direction)
        {
            var intensity = feel.LightIntensity * _channels[FeelChannel.ShotLight];
            if (intensity <= 0f || feel.LightRadius <= 0f) return;
            var muzzle = _weaponView?.ShownModel != null && _weaponView.ShownModel.Muzzle != null
                ? _weaponView.ShownModel.Muzzle.position
                : new Vector3(_player.Position.x + direction.x * 0.6f, ShotLightHeight, _player.Position.y + direction.y * 0.6f);
            _shotLightPosition = new Vector4(muzzle.x, muzzle.y, muzzle.z, 1f / feel.LightRadius);
            _shotLightColor = feel.LightColor.linear * intensity;
            _shotLightLeft = ShotLightTime;
            Shader.SetGlobalVector(ShotLightPositionId, _shotLightPosition);
            Shader.SetGlobalColor(ShotLightColorId, _shotLightColor);
        }

        private static void ClearShotLight()
        {
            Shader.SetGlobalVector(ShotLightPositionId, new Vector4(0f, -1000f, 0f, 1f));
            Shader.SetGlobalColor(ShotLightColorId, Color.clear);
        }

        // ------------------------------------------------------------------ Hits

        private void OnWeaponDamaged(CombatTarget target, Vector2 at, Vector2 direction, WeaponDefinition weapon, bool killed)
        {
            var feel = weapon.Feel;
            if (target.Agent >= 0)
                _hordeView?.SetHitLook(target.Agent, feel.HitFlash * _channels[FeelChannel.HitFlash],
                    feel.Dent * _channels[FeelChannel.Dent]);

            var delay = weapon.IsMelee && _playerView != null ? _playerView.LastMeleeContactDelay : 0f;
            if (delay <= 0f)
            {
                ShowHit(target.Agent, at, direction, feel, killed);
                return;
            }

            for (var i = 0; i < MaxPending; i++)
                if (!_pending[i].Active)
                {
                    _pending[i] = new Pending
                    {
                        Active = true, At = _time + delay, Agent = target.Agent, Position = at, Direction = direction,
                        Feel = feel, Killed = killed,
                    };
                    return;
                }
        }

        private void ShowHit(int agent, Vector2 at, Vector2 direction, FeelModule feel, bool killed)
        {
            if (killed)
            {
                _camera?.Shake(feel.KillShake * _channels[FeelChannel.CameraShake]);
                Vibrate(feel.VibrateKill, 0.6f);
            }

            if (_visibility != null && _visibility.HasResult &&
                !_visibility.IsVisible(new GridPosition(Mathf.FloorToInt(at.x + 0.5f), Mathf.FloorToInt(at.y + 0.5f), _player.Floor)))
                return;

            var dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.up;
            var splash = _channels[FeelChannel.Splash];
            Splash(at, dir, Mathf.RoundToInt((killed ? feel.KillSplash : feel.HitSplash) * splash), killed);

            var ring = feel.HitRing * _channels[FeelChannel.HitRing];
            if (ring > 0f && _rings != null && !killed)
            {
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(at.x, RingHeight, at.y),
                    startSize = ring,
                    startLifetime = RingLifetime,
                    startColor = Color.Lerp(_splashColor, Color.white, 0.5f),
                    velocity = Vector3.zero,
                };
                _rings.Emit(emit, 1);
            }
        }

        /// <summary>Drops of the monster's colour thrown along the shot and up, falling back.</summary>
        private void Splash(Vector2 at, Vector2 direction, int count, bool killed)
        {
            if (_splashes == null || count <= 0) return;
            var speed = killed ? 3.2f : 2.2f;
            for (var n = 0; n < count; n++)
            {
                var angle = ((float)_random.NextDouble() - 0.5f) * (killed ? 2.6f : 1.4f);
                var cos = Mathf.Cos(angle);
                var sin = Mathf.Sin(angle);
                var spread = new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);
                var along = speed * (0.4f + 0.6f * (float)_random.NextDouble());
                var tint = 0.85f + 0.3f * (float)_random.NextDouble();
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(at.x, SplashHeight + 0.2f * (float)_random.NextDouble(), at.y),
                    velocity = new Vector3(spread.x * along, 1.5f + 2.5f * (float)_random.NextDouble(), spread.y * along),
                    startSize = (killed ? 0.16f : 0.11f) * (0.7f + 0.6f * (float)_random.NextDouble()),
                    startLifetime = 0.35f + 0.25f * (float)_random.NextDouble(),
                    startColor = new Color(_splashColor.r * tint, _splashColor.g * tint, _splashColor.b * tint, 1f),
                };
                _splashes.Emit(emit, 1);
            }
        }

        private void Vibrate(int milliseconds, float strength)
        {
            var scale = _channels[FeelChannel.Vibration];
            if (_vibration != null && milliseconds > 0 && scale > 0f)
                _vibration.Vibrate(Mathf.RoundToInt(milliseconds * scale), strength);
        }

        private struct Pending
        {
            public bool Active;
            public float At;
            public int Agent;
            public Vector2 Position;
            public Vector2 Direction;
            public FeelModule Feel;
            public bool Killed;
        }
    }
}
