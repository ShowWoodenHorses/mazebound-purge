using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Services;
using Maze.Core.Audio;
using Maze.Core.Common;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Level;
using Maze.Gameplay.Pickups;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Weapons;
using Maze.Gameplay.Zombies;
using Maze.Presentation.Visual;
using UnityEngine;

namespace Maze.Presentation.Audio
{
    /// <summary>
    /// Sounds of the level (ТЗ §71, display only): listens to gameplay events and plays the cues of the
    /// <see cref="AudioCatalog"/> where they happen. The listener is the player: a sound is quieter with the distance to
    /// the player and panned to its side (<see cref="SoundMath"/>); walls do not muffle it (gameplay sounds go through
    /// walls too). Hidden zombies are heard as well. Zombie hits and deaths sound with their view reaction (a melee
    /// hit at the clip's contact). Timers (zombie steps and groans, delayed reloads, torch loops) run in the level tick:
    /// nothing advances on pause. Must be registered after the gameplay systems and <see cref="ZombieViewPresenter"/>.
    /// </summary>
    public sealed class LevelAudioPresenter : ILevelLoadStep, ILevelTickable, IDisposable
    {
        /// <summary>Seconds between choosing which torches play.</summary>
        private const float TorchPickInterval = 0.25f;

        /// <summary>Volume change per second of a torch loop (smooth when the player moves or a torch changes).</summary>
        private const float TorchFadeSpeed = 2f;

        /// <summary>Most a reload sound is sped up to fit a short reload.</summary>
        private const float MaxReloadPitch = 1.5f;

        /// <summary>A move longer than this in one tick is a teleport, not steps.</summary>
        private const float MaxStepJump = 1f;

        private readonly AudioService _audio;
        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _health;
        private readonly PlayerCombat _combat;
        private readonly PlayerInteraction _interaction;
        private readonly WeaponSystem _weapons;
        private readonly BulletSystem _bullets;
        private readonly PickupSystem _pickups;
        private readonly ZombieSystem _zombies;
        private readonly ZombieViewPresenter _zombieViews;
        private readonly FrozenDoorsView _frozenDoors;
        private readonly SoundEventBus _sounds;
        private readonly DeterministicRandom _random = new DeterministicRandom(Environment.TickCount);
        private readonly List<ZombieSound> _zombieSounds = new List<ZombieSound>();
        private readonly List<Vector2> _torches = new List<Vector2>();

        private TorchSlot[] _torchSlots = Array.Empty<TorchSlot>();
        private float _torchPick;
        private bool _torchesStarted;
        private int _health0;
        private float _quietTime = float.PositiveInfinity;
        private WeaponRuntime _reloadWeapon;
        private int _reloadVoice;
        private float _reloadDelay = -1f;
        private float _reloadPitch = 1f;
        private bool _subscribed;

        public LevelAudioPresenter(AudioService audio, LevelData level, PlayerSystem player, PlayerHealth health, PlayerCombat combat,
            PlayerInteraction interaction, WeaponSystem weapons, BulletSystem bullets, PickupSystem pickups, ZombieSystem zombies,
            ZombieViewPresenter zombieViews, SoundEventBus sounds, FrozenDoorsView frozenDoors)
        {
            _frozenDoors = frozenDoors;
            _audio = audio;
            _level = level;
            _player = player;
            _health = health;
            _combat = combat;
            _interaction = interaction;
            _weapons = weapons;
            _bullets = bullets;
            _pickups = pickups;
            _zombies = zombies;
            _zombieViews = zombieViews;
            _sounds = sounds;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        private AudioCatalog Catalog => _audio.Catalog;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _health0 = _health.Current;
            _zombieSounds.Clear();
            foreach (var zombie in _zombies.Zombies)
                AddZombie(zombie);

            _torches.Clear();
            foreach (var light in _level.Lights)
                _torches.Add(new Vector2(light.Cell.X + light.Offset.x, light.Cell.Y + light.Offset.y));

            if (!_subscribed && Catalog != null)
            {
                _subscribed = true;
                _sounds.Emitted += OnSound;
                _combat.Attacked += OnAttacked;
                _combat.ReloadChanged += OnReloadChanged;
                _weapons.Changed += OnWeaponsChanged;
                _weapons.Switched += OnSwitched;
                _bullets.Ended += OnBulletEnded;
                _interaction.Interacted += OnInteracted;
                _pickups.Removed += OnPickupRemoved;
                _health.Changed += OnHealthChanged;
                _zombies.Spawned += AddZombie;
                _zombies.Attacked += OnZombieAttacked;
                _zombieViews.HitShown += OnZombieHitShown;
                _zombieViews.DeathShown += OnZombieDeathShown;
                _frozenDoors.IceHitShown += OnIceHitShown;
                _frozenDoors.IceBrokenShown += OnIceBrokenShown;
            }

            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            if (!_subscribed || !_player.IsSpawned)
                return;

            TickReload(deltaTime);
            TickZombies(deltaTime);
            TickTorches(deltaTime);
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _subscribed = false;
                _sounds.Emitted -= OnSound;
                _combat.Attacked -= OnAttacked;
                _combat.ReloadChanged -= OnReloadChanged;
                _weapons.Changed -= OnWeaponsChanged;
                _weapons.Switched -= OnSwitched;
                _bullets.Ended -= OnBulletEnded;
                _interaction.Interacted -= OnInteracted;
                _pickups.Removed -= OnPickupRemoved;
                _health.Changed -= OnHealthChanged;
                _zombies.Spawned -= AddZombie;
                _zombies.Attacked -= OnZombieAttacked;
                _zombieViews.HitShown -= OnZombieHitShown;
                _zombieViews.DeathShown -= OnZombieDeathShown;
                _frozenDoors.IceHitShown -= OnIceHitShown;
                _frozenDoors.IceBrokenShown -= OnIceBrokenShown;
            }

            foreach (var slot in _torchSlots)
                _audio.Stop(slot.Voice);
            _torchSlots = Array.Empty<TorchSlot>();
            _audio.Stop(_reloadVoice);
        }

        // ------------------------------------------------------------------ Player

        private void OnSound(SoundEvent sound)
        {
            if (sound.Type == SoundType.Step)
                PlayAt(Catalog.Footstep, sound.Position);
        }

        private void OnAttacked(WeaponRuntime weapon, Vector2 direction)
        {
            var cue = Catalog.WeaponAttack(weapon.Definition.SoundGroup) ??
                      (weapon.Slot == WeaponSlot.Melee ? Catalog.MeleeSwing : Catalog.Shot);
            PlayAt(cue, _player.Position);
        }

        private void OnHealthChanged(int current, int max)
        {
            if (current < _health0)
                PlayAt(current > 0 ? Catalog.PlayerHurt : Catalog.PlayerDeath, _player.Position);
            _health0 = current;
        }

        private void OnSwitched(WeaponSlot slot) => PlayAt(Catalog.SwitchWeapon, _player.Position);

        private void OnPickupRemoved(Pickup pickup)
        {
            var cue = pickup.Kind switch
            {
                PickupKind.Key => Catalog.PickupKey,
                PickupKind.Medkit => Catalog.PickupMedkit,
                PickupKind.Weapon => Catalog.PickupWeapon,
                _ => Catalog.PickupMapFragment,
            };
            PlayAt(cue, new Vector2(pickup.Cell.X, pickup.Cell.Y));
        }

        private void OnInteracted(InteractionResult result, DoorData door)
        {
            if (door == null) return;
            var cue = result switch
            {
                InteractionResult.DoorOpened => Catalog.DoorOpen,
                InteractionResult.DoorClosed => Catalog.DoorClose,
                InteractionResult.DoorUnlocked => Catalog.DoorUnlock,
                InteractionResult.DoorLocked => Catalog.DoorLocked,
                InteractionResult.DoorBlocked => Catalog.DoorLocked,
                InteractionResult.DoorFrozen => Catalog.DoorLocked,
                _ => null,
            };
            PlayAt(cue, new Vector2(door.Position.X, door.Position.Y));
        }

        private void OnIceHitShown(DoorData door) => PlayAt(Catalog.IceHit, new Vector2(door.Position.X, door.Position.Y));

        private void OnIceBrokenShown(DoorData door) => PlayAt(Catalog.IceBreak, new Vector2(door.Position.X, door.Position.Y));

        private void OnBulletEnded(Bullet bullet, Vector2 point, BulletEnd end)
        {
            if (end == BulletEnd.Blocked)
                PlayAt(Catalog.BulletImpact, point);
        }

        // ------------------------------------------------------------------ Reload

        private void OnReloadChanged(WeaponRuntime weapon)
        {
            if (weapon.IsReloading && weapon == _weapons.Active) StartReload(weapon);
            else if (!weapon.IsReloading && weapon == _reloadWeapon) _reloadWeapon = null; // Its sound ends by itself.
        }

        /// <summary>A switch away stops the reload sound; back to a weapon still reloading starts it again.</summary>
        private void OnWeaponsChanged()
        {
            var active = _weapons.Active;
            if (_reloadWeapon != null && active != _reloadWeapon)
            {
                _audio.Stop(_reloadVoice);
                _reloadVoice = 0;
                _reloadDelay = -1f;
                _reloadWeapon = null;
            }

            if (active != null && active.IsReloading && _reloadWeapon == null)
                StartReload(active);
        }

        /// <summary>
        /// Fits the sound to the reload time left: a shorter sound waits so that it ends with the reload (the gun is
        /// ready on its last click), a longer one is sped up (at most <see cref="MaxReloadPitch"/>).
        /// </summary>
        private void StartReload(WeaponRuntime weapon)
        {
            _audio.Stop(_reloadVoice);
            _reloadVoice = 0;
            _reloadWeapon = weapon;
            var cue = ReloadCue(weapon);
            var length = cue.IsEmpty || cue.Clips[0] == null ? 0f : cue.Clips[0].length;
            var left = weapon.ReloadRemaining;
            if (length <= 0f || left <= 0f)
            {
                _reloadDelay = -1f;
                return;
            }

            _reloadPitch = Mathf.Clamp(length / left, 1f, MaxReloadPitch);
            _reloadDelay = Mathf.Max(0f, left - length);
            if (_reloadDelay <= 0f) PlayReload();
        }

        private void TickReload(float deltaTime)
        {
            if (_reloadDelay <= 0f || _reloadWeapon == null) return;
            _reloadDelay -= deltaTime;
            if (_reloadDelay <= 0f) PlayReload();
        }

        private void PlayReload()
        {
            _reloadDelay = -1f;
            if (_reloadWeapon == null) return;
            _reloadVoice = _audio.Play(ReloadCue(_reloadWeapon), SoundChannel.Level, 1f, 0f, _reloadPitch);
        }

        private SoundCue ReloadCue(WeaponRuntime weapon) =>
            Catalog.WeaponReload(weapon.Definition.SoundGroup) ??
            (weapon.Definition.FireMode == FireMode.Automatic ? Catalog.ReloadMagazine : Catalog.ReloadSingle);

        // ------------------------------------------------------------------ Zombies

        private void AddZombie(ZombieRuntime zombie)
        {
            foreach (var known in _zombieSounds)
                if (known.Zombie == zombie)
                    return;

            _zombieSounds.Add(new ZombieSound
            {
                Zombie = zombie,
                Pitch = Catalog != null ? Catalog.ZombiePitch(zombie.Definition.Id) : 1f,
                LastPosition = zombie.Position,
                LastState = zombie.State,
                // The first groans are spread out, not all at the start.
                GroanIn = Catalog != null ? NextGroan() * (0.3f + 0.7f * (float)_random.NextDouble()) : 0f,
            });
        }

        private void TickZombies(float deltaTime)
        {
            var catalog = Catalog;
            var aware = false;
            var newlyAlert = false;
            for (var i = 0; i < _zombieSounds.Count; i++)
            {
                var sound = _zombieSounds[i];
                var zombie = sound.Zombie;
                if (!zombie.IsAlive) continue;

                var state = zombie.State;
                if (state == ZombieState.Alert || state == ZombieState.Chase || state == ZombieState.Attack) aware = true;
                if (state == ZombieState.Alert && sound.LastState != ZombieState.Alert)
                {
                    PlayAt(catalog.ZombieRoar, zombie.Position, sound.Pitch);
                    newlyAlert = true;
                }

                // Steps by the distance walked: shuffling on patrol, running in a chase.
                var moved = (zombie.Position - sound.LastPosition).magnitude;
                sound.LastPosition = zombie.Position;
                if (moved > MaxStepJump) moved = 0f;
                sound.Walked += moved;
                var running = state == ZombieState.Chase;
                var step = running ? catalog.ZombieRunStep : catalog.ZombieWalkStep;
                if (moved <= 0f) sound.Walked = Mathf.Min(sound.Walked, step * 0.5f); // Stopping: next step comes soon.
                else if (sound.Walked >= step)
                {
                    sound.Walked -= step;
                    PlayAt(running ? catalog.ZombieStepRun : catalog.ZombieStepWalk, zombie.Position);
                }

                // Groans now and then while not after the player.
                if (state == ZombieState.Idle || state == ZombieState.Patrol || state == ZombieState.Return)
                {
                    sound.GroanIn -= deltaTime;
                    if (sound.GroanIn <= 0f)
                    {
                        sound.GroanIn = NextGroan();
                        PlayAt(catalog.ZombieGroan, zombie.Position, sound.Pitch);
                    }
                }

                sound.LastState = state;
                _zombieSounds[i] = sound;
            }

            // One stinger per chase, not per zombie: again only after a quiet while.
            if (newlyAlert && _quietTime >= catalog.StingerRearm)
                _audio.Play(catalog.ChaseStinger, SoundChannel.Level);
            _quietTime = aware ? 0f : _quietTime + deltaTime;
        }

        private float NextGroan() =>
            Mathf.Lerp(Catalog.GroanIntervalMin, Catalog.GroanIntervalMax, (float)_random.NextDouble());

        private void OnZombieAttacked(ZombieRuntime zombie) => PlayAt(Catalog.ZombieAttack, zombie.Position, PitchOf(zombie));

        private void OnZombieHitShown(ZombieRuntime zombie) => PlayAt(Catalog.ZombieHurt, zombie.Position, PitchOf(zombie));

        private void OnZombieDeathShown(ZombieRuntime zombie) => PlayAt(Catalog.ZombieDeath, zombie.Position, PitchOf(zombie));

        private float PitchOf(ZombieRuntime zombie)
        {
            foreach (var sound in _zombieSounds)
                if (sound.Zombie == zombie)
                    return sound.Pitch;
            return 1f;
        }

        // ------------------------------------------------------------------ Torches

        /// <summary>
        /// The nearest light sources within the loop's range get a voice each (at most <see cref="AudioCatalog.MaxTorchLoops"/>);
        /// volume and pan follow the player smoothly.
        /// </summary>
        private void TickTorches(float deltaTime)
        {
            var cue = Catalog.TorchLoop;
            if (_torches.Count == 0 || cue.IsEmpty) return;

            if (!_torchesStarted)
            {
                _torchesStarted = true;
                var count = Mathf.Min(Mathf.Min(Catalog.MaxTorchLoops, cue.MaxVoices), _torches.Count);
                _torchSlots = new TorchSlot[count];
                for (var i = 0; i < count; i++)
                    _torchSlots[i] = new TorchSlot { Torch = -1 };
                _torchPick = 0f;
            }

            var listener = _player.Position;
            _torchPick -= deltaTime;
            if (_torchPick <= 0f)
            {
                _torchPick = TorchPickInterval;
                PickTorches(listener, cue.Range);
            }

            for (var i = 0; i < _torchSlots.Length; i++)
            {
                var slot = _torchSlots[i];
                var target = 0f;
                var pan = 0f;
                if (slot.Torch >= 0)
                {
                    var offset = _torches[slot.Torch] - listener;
                    target = cue.Attenuation(offset.magnitude);
                    pan = SoundMath.Pan(offset.x);
                }

                slot.Volume = Mathf.MoveTowards(slot.Volume, target, TorchFadeSpeed * deltaTime);
                if (slot.Volume <= 0f)
                {
                    _audio.Stop(slot.Voice);
                    slot.Voice = 0;
                }
                else if (!_audio.IsPlaying(slot.Voice))
                    slot.Voice = _audio.PlayLoop(cue, SoundChannel.Level, slot.Volume, pan, (float)_random.NextDouble());
                else
                    _audio.SetVoice(slot.Voice, slot.Volume, pan);
                _torchSlots[i] = slot;
            }
        }

        /// <summary>A slot keeps its torch while that one is still among the nearest; new torches take free slots.</summary>
        private void PickTorches(Vector2 listener, float range)
        {
            for (var i = 0; i < _torchSlots.Length; i++)
                _torchSlots[i].Wanted = -1;

            // The nearest torches in range, in order.
            for (var i = 0; i < _torchSlots.Length; i++)
            {
                var best = -1;
                var bestDistance = range * range;
                for (var t = 0; t < _torches.Count; t++)
                {
                    var distance = (_torches[t] - listener).sqrMagnitude;
                    if (distance >= bestDistance || IsWanted(t)) continue;
                    best = t;
                    bestDistance = distance;
                }

                if (best < 0) break;
                _torchSlots[i].Wanted = best;
            }

            // Slots whose torch is still wanted keep it; the rest take the remaining wanted torches.
            for (var i = 0; i < _torchSlots.Length; i++)
                if (_torchSlots[i].Torch >= 0 && !IsWanted(_torchSlots[i].Torch))
                    _torchSlots[i].Torch = -1;
            for (var i = 0; i < _torchSlots.Length; i++)
            {
                var wanted = _torchSlots[i].Wanted;
                if (wanted < 0 || IsAssigned(wanted)) continue;
                for (var s = 0; s < _torchSlots.Length; s++)
                    if (_torchSlots[s].Torch < 0)
                    {
                        _torchSlots[s].Torch = wanted;
                        break;
                    }
            }
        }

        private bool IsWanted(int torch)
        {
            foreach (var slot in _torchSlots)
                if (slot.Wanted == torch)
                    return true;
            return false;
        }

        private bool IsAssigned(int torch)
        {
            foreach (var slot in _torchSlots)
                if (slot.Torch == torch)
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>Plays a level sound at a point (grid units): quieter with the distance to the player, panned.</summary>
        private void PlayAt(SoundCue cue, Vector2 position, float pitch = 1f)
        {
            if (cue == null) return;
            var volume = 1f;
            var pan = 0f;
            if (cue.Range > 0f && _player.IsSpawned)
            {
                var offset = position - _player.Position;
                volume = cue.Attenuation(offset.magnitude);
                if (volume <= 0.01f) return;
                pan = SoundMath.Pan(offset.x);
            }

            _audio.Play(cue, SoundChannel.Level, volume, pan, pitch);
        }

        private struct ZombieSound
        {
            public ZombieRuntime Zombie;
            public float Pitch;
            public Vector2 LastPosition;
            public ZombieState LastState;
            public float Walked;
            public float GroanIn;
        }

        private struct TorchSlot
        {
            public int Torch;
            public int Wanted;
            public int Voice;
            public float Volume;
        }
    }
}
