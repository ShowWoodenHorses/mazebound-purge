using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Services;
using Purge.Core.Audio;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Weapons;
using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Presentation.Audio
{
    /// <summary>
    /// Sounds of the player's weapons and of what they hit (display only; ТЗ «Ощущение стрельбы»): the shot with the
    /// weapon's pitch and volume, continuous fire as a loop (start → loop → end), the tail once after a single shot or a
    /// burst, the pump or bolt after a shot, shells, the low-magazine tick and the empty click, the draw on a switch, a
    /// reload by parts at shares of its time (or one recording fitted to it), hits on monsters (the weapon's own hit —
    /// melee at the clip's contact, <see cref="PlayerViewPresenter.LastMeleeContactDelay"/>), kills (pop + splat), blasts
    /// by size and the flight loop of projectiles. Groups and parts — <see cref="AudioCatalog"/>. Timers run in the level
    /// tick (nothing on pause). Registered after the gameplay systems and <see cref="PlayerViewPresenter"/>.
    /// </summary>
    public sealed class WeaponAudioPresenter : ILevelLoadStep, ILevelTickable, IDisposable
    {
        /// <summary>Seconds of silence after the last attack (beyond its interval) that end a burst or continuous fire.</summary>
        private const float BurstGap = 0.12f;

        /// <summary>Seconds after the shot before the shells fall.</summary>
        private const float ShellsDelay = 0.3f;

        /// <summary>Most a one-recording reload is sped up to fit a short reload.</summary>
        private const float MaxReloadPitch = 1.5f;

        private const int MaxScheduled = 24;
        private const int MaxFlights = 4;

        private readonly AudioService _audio;
        private readonly PlayerSystem _player;
        private readonly PlayerCombat _combat;
        private readonly WeaponSystem _weapons;
        private readonly BulletSystem _bullets;
        private readonly CombatTargets _targets;
        private readonly HordeSystem _horde;
        private readonly PlayerViewPresenter _playerView;
        private readonly Scheduled[] _scheduled = new Scheduled[MaxScheduled];
        private readonly Flight[] _flights = new Flight[MaxFlights];

        private bool _subscribed;
        private float _time;

        // The burst / continuous fire in progress.
        private WeaponRuntime _firing;
        private float _lastAttack = float.NegativeInfinity;
        private int _loopVoice;
        private bool _burst;

        private WeaponRuntime _reloadWeapon;
        private int _reloadVoice;

        public WeaponAudioPresenter(AudioService audio, PlayerSystem player, PlayerCombat combat, WeaponSystem weapons,
            BulletSystem bullets, CombatTargets targets, HordeSystem horde = null, PlayerViewPresenter playerView = null)
        {
            _audio = audio;
            _player = player;
            _combat = combat;
            _weapons = weapons;
            _bullets = bullets;
            _targets = targets;
            _horde = horde;
            _playerView = playerView;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        private AudioCatalog Catalog => _audio.Catalog;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_subscribed && Catalog != null)
            {
                _subscribed = true;
                _combat.Attacked += OnAttacked;
                _combat.DryFired += OnDryFired;
                _combat.ReloadChanged += OnReloadChanged;
                _weapons.Changed += OnWeaponsChanged;
                _weapons.Switched += OnSwitched;
                _bullets.Spawned += OnBulletSpawned;
                _bullets.Ended += OnBulletEnded;
                _bullets.Exploded += OnExploded;
                _targets.WeaponDamaged += OnWeaponDamaged;
                if (_horde != null) _horde.Killed += OnKilled;
            }

            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _subscribed = false;
                _combat.Attacked -= OnAttacked;
                _combat.DryFired -= OnDryFired;
                _combat.ReloadChanged -= OnReloadChanged;
                _weapons.Changed -= OnWeaponsChanged;
                _weapons.Switched -= OnSwitched;
                _bullets.Spawned -= OnBulletSpawned;
                _bullets.Ended -= OnBulletEnded;
                _bullets.Exploded -= OnExploded;
                _targets.WeaponDamaged -= OnWeaponDamaged;
                if (_horde != null) _horde.Killed -= OnKilled;
            }

            _audio.Stop(_loopVoice);
            _audio.Stop(_reloadVoice);
            for (var i = 0; i < MaxFlights; i++)
                _audio.Stop(_flights[i].Voice);
        }

        public void Tick(float deltaTime)
        {
            if (!_subscribed) return;
            _time += deltaTime;
            TickBurst();
            TickScheduled();
            TickFlights();
        }

        // ------------------------------------------------------------------ Attacks

        private void OnAttacked(WeaponRuntime weapon, Vector2 direction)
        {
            var definition = weapon.Definition;
            var group = Catalog.FindWeaponSound(definition.SoundGroup);
            var pitch = definition.SoundPitch;

            if (_firing != null && _firing != weapon) EndBurst();
            var starting = _firing == null;
            _firing = weapon;
            _lastAttack = _time;

            if (group != null && group.IsContinuous)
            {
                if (starting || !_audio.IsPlaying(_loopVoice))
                {
                    if (starting) Play(group.Start, definition.SoundVolume, pitch);
                    _loopVoice = _audio.PlayLoop(group.Loop, SoundChannel.Level, definition.SoundVolume, 0f, 0f, pitch);
                }
            }
            else
            {
                var cue = Catalog.WeaponAttack(definition.SoundGroup) ?? (definition.IsMelee ? Catalog.MeleeSwing : Catalog.Shot);
                Play(cue, definition.SoundVolume, pitch);
                _burst = !starting;
            }

            if (group == null || definition.IsMelee) return;

            // A single shot: its tail at once; a burst gets one when it stops (TickBurst).
            if (definition.FireMode == FireMode.Single && !group.IsContinuous) Play(Catalog.Part(group.Tail), 1f, pitch);
            if (!string.IsNullOrEmpty(group.Cycle)) Schedule(Catalog.Part(group.Cycle), group.CycleDelay, pitch);
            if (!string.IsNullOrEmpty(group.Shells)) Schedule(Catalog.Part(group.Shells), ShellsDelay, 1f);

            var size = _combat.MagazineOf(definition);
            if (size >= 4 && weapon.Ammo > 0 && weapon.Ammo <= Mathf.CeilToInt(size * Catalog.LowAmmoShare))
                Play(Catalog.LowAmmo, 1f, 1f);
        }

        /// <summary>A burst or continuous fire ends after its interval plus <see cref="BurstGap"/> without an attack.</summary>
        private void TickBurst()
        {
            if (_firing == null) return;
            if (_time - _lastAttack <= _combat.CooldownOf(_firing.Definition) + BurstGap && _weapons.Active == _firing) return;
            EndBurst();
        }

        private void EndBurst()
        {
            var weapon = _firing;
            _firing = null;
            var group = Catalog.FindWeaponSound(weapon.Definition.SoundGroup);
            if (group == null) return;
            var pitch = weapon.Definition.SoundPitch;
            if (group.IsContinuous)
            {
                _audio.Stop(_loopVoice);
                _loopVoice = 0;
                Play(group.End, weapon.Definition.SoundVolume, pitch);
                return;
            }

            if (_burst && weapon.Definition.FireMode == FireMode.Automatic) Play(Catalog.Part(group.Tail), 1f, pitch);
            _burst = false;
        }

        private void OnDryFired(WeaponRuntime weapon) => Play(Catalog.EmptyClick, 1f, 1f);

        private void OnSwitched(WeaponSlot slot)
        {
            var active = _weapons.Active;
            var draw = active != null ? Catalog.Part(Catalog.FindWeaponSound(active.Definition.SoundGroup)?.Draw) : null;
            Play(draw ?? Catalog.SwitchWeapon, 1f, 1f);
        }

        // ------------------------------------------------------------------ Reload

        private void OnReloadChanged(WeaponRuntime weapon)
        {
            if (weapon.IsReloading && weapon == _weapons.Active) StartReload(weapon);
            else if (!weapon.IsReloading && weapon == _reloadWeapon) _reloadWeapon = null; // Its sounds end by themselves.
        }

        /// <summary>A switch away stops the reload sounds; back to a weapon still reloading starts them again.</summary>
        private void OnWeaponsChanged()
        {
            var active = _weapons.Active;
            if (_reloadWeapon != null && active != _reloadWeapon) StopReload();
            if (active != null && active.IsReloading && _reloadWeapon == null) StartReload(active);
        }

        private void StopReload()
        {
            _audio.Stop(_reloadVoice);
            _reloadVoice = 0;
            for (var i = 0; i < MaxScheduled; i++)
                if (_scheduled[i].Reload)
                    _scheduled[i] = default;
            _reloadWeapon = null;
        }

        /// <summary>
        /// Steps at their shares of the time left (a step at 1 ends with the reload). One recording: a shorter one waits
        /// so that it ends with the reload, a longer one is sped up (at most <see cref="MaxReloadPitch"/>).
        /// </summary>
        private void StartReload(WeaponRuntime weapon)
        {
            StopReload();
            _reloadWeapon = weapon;
            var left = weapon.ReloadRemaining;
            if (left <= 0f) return;

            var definition = weapon.Definition;
            var group = Catalog.FindWeaponSound(definition.SoundGroup);
            if (group != null && group.ReloadSteps.Count > 0 && (group.Reload == null || group.Reload.IsEmpty))
            {
                foreach (var step in group.ReloadSteps)
                {
                    var cue = Catalog.Part(step.Part);
                    if (cue == null) continue;
                    var at = Mathf.Min(step.At * left, Mathf.Max(0f, left - Length(cue)));
                    Schedule(cue, at, definition.SoundPitch, true);
                }

                return;
            }

            var whole = Catalog.WeaponReload(definition.SoundGroup) ??
                        (definition.FireMode == FireMode.Automatic ? Catalog.ReloadMagazine : Catalog.ReloadSingle);
            var length = Length(whole);
            if (length <= 0f) return;
            var pitch = Mathf.Clamp(length / left, 1f, MaxReloadPitch);
            Schedule(whole, Mathf.Max(0f, left - length), pitch, true);
        }

        private static float Length(SoundCue cue) => cue == null || cue.IsEmpty || cue.Clips[0] == null ? 0f : cue.Clips[0].length;

        // ------------------------------------------------------------------ Hits, kills, blasts

        private void OnWeaponDamaged(CombatTarget target, Vector2 at, Vector2 direction, WeaponDefinition weapon, bool killed)
        {
            var own = Catalog.Part(Catalog.FindWeaponSound(weapon.SoundGroup)?.Hit);
            var cue = own ?? Catalog.MonsterHit;
            var delay = weapon.IsMelee && _playerView != null ? _playerView.LastMeleeContactDelay : 0f;
            ScheduleAt(cue, delay, at, own != null ? weapon.SoundPitch : 1f);
        }

        private void OnKilled(int agent, Vector2 at)
        {
            PlayAt(Catalog.KillPop, at, 1f);
            PlayAt(Catalog.KillSplat, at, 1f);
        }

        private void OnExploded(Vector2 at, float radius, IExplosionLook source) => PlayAt(Catalog.ExplosionOf(radius), at, 1f);

        // ------------------------------------------------------------------ Projectile flight

        private void OnBulletSpawned(Bullet bullet)
        {
            if (bullet.Weapon == null) return;
            var cue = Catalog.Part(Catalog.FindWeaponSound(bullet.Weapon.SoundGroup)?.Flight);
            if (cue == null) return;
            var slot = -1;
            for (var i = 0; i < MaxFlights && slot < 0; i++)
                if (_flights[i].Bullet == null)
                    slot = i;
            if (slot < 0) return;
            _flights[slot] = new Flight { Bullet = bullet, BulletId = bullet.Id, Cue = cue, Voice = 0 };
        }

        private void OnBulletEnded(Bullet bullet, Vector2 point, BulletEnd end)
        {
            for (var i = 0; i < MaxFlights; i++)
                if (_flights[i].Bullet == bullet)
                {
                    _audio.Stop(_flights[i].Voice);
                    _flights[i] = default;
                }
        }

        private void TickFlights()
        {
            for (var i = 0; i < MaxFlights; i++)
            {
                ref var flight = ref _flights[i];
                if (flight.Bullet == null) continue;
                if (flight.Bullet.Id != flight.BulletId) // The pooled bullet went on to another shot.
                {
                    _audio.Stop(flight.Voice);
                    flight = default;
                    continue;
                }

                var (volume, pan) = Positional(flight.Cue, flight.Bullet.Position);
                if (flight.Voice == 0 || !_audio.IsPlaying(flight.Voice))
                    flight.Voice = volume > 0.01f ? _audio.PlayLoop(flight.Cue, SoundChannel.Level, volume, pan, 0f) : 0;
                else
                    _audio.SetVoice(flight.Voice, volume, pan);
            }
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>A sound of the player's own weapon: at the player, full volume (the player's gun is the loudest).</summary>
        private void Play(SoundCue cue, float volume, float pitch)
        {
            if (cue == null || volume <= 0f) return;
            _audio.Play(cue, SoundChannel.Level, volume, 0f, pitch);
        }

        private void PlayAt(SoundCue cue, Vector2 position, float pitch)
        {
            if (cue == null) return;
            var (volume, pan) = Positional(cue, position);
            if (volume > 0.01f) _audio.Play(cue, SoundChannel.Level, volume, pan, pitch);
        }

        private (float Volume, float Pan) Positional(SoundCue cue, Vector2 position)
        {
            if (cue.Range <= 0f || !_player.IsSpawned) return (1f, 0f);
            var offset = position - _player.Position;
            return (cue.Attenuation(offset.magnitude), SoundMath.Pan(offset.x));
        }

        private void Schedule(SoundCue cue, float delay, float pitch, bool reload = false)
        {
            if (cue == null) return;
            if (delay <= 0f && !reload)
            {
                Play(cue, 1f, pitch);
                return;
            }

            Add(new Scheduled { Cue = cue, At = _time + delay, Pitch = pitch, Reload = reload, Own = true });
        }

        private void ScheduleAt(SoundCue cue, float delay, Vector2 position, float pitch)
        {
            if (cue == null) return;
            if (delay <= 0f)
            {
                PlayAt(cue, position, pitch);
                return;
            }

            Add(new Scheduled { Cue = cue, At = _time + delay, Pitch = pitch, Position = position });
        }

        /// <summary>A full queue drops the new sound (a burst of many hits: the earlier ones are heard anyway).</summary>
        private void Add(Scheduled scheduled)
        {
            for (var i = 0; i < MaxScheduled; i++)
                if (_scheduled[i].Cue == null)
                {
                    _scheduled[i] = scheduled;
                    return;
                }
        }

        private void TickScheduled()
        {
            for (var i = 0; i < MaxScheduled; i++)
            {
                var scheduled = _scheduled[i];
                if (scheduled.Cue == null || scheduled.At > _time) continue;
                _scheduled[i] = default;
                if (!scheduled.Own) PlayAt(scheduled.Cue, scheduled.Position, scheduled.Pitch);
                else if (scheduled.Reload) _reloadVoice = _audio.Play(scheduled.Cue, SoundChannel.Level, 1f, 0f, scheduled.Pitch);
                else Play(scheduled.Cue, 1f, scheduled.Pitch);
            }
        }

        private struct Scheduled
        {
            public SoundCue Cue;
            public float At;
            public float Pitch;
            /// <summary>At the player (own weapon); otherwise at <see cref="Position"/>.</summary>
            public bool Own;
            public bool Reload;
            public Vector2 Position;
        }

        private struct Flight
        {
            public Bullet Bullet;
            public int BulletId;
            public SoundCue Cue;
            public int Voice;
        }
    }
}
