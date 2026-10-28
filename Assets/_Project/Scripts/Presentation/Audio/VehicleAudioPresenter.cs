using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Services;
using Purge.Core.Audio;
using Purge.Core.Common;
using Purge.Gameplay.Level;
using Purge.Gameplay.Vehicles;
using UnityEngine;

namespace Purge.Presentation.Audio
{
    /// <summary>
    /// Sounds of the vehicles (stage 9a), all of the one the player drives (they are in it: no distance, no pan): the
    /// door and the engine starting on getting in, the door on getting out; the engine — idling and driving loops
    /// crossfaded by the speed, the driving one's pitch rising with it; a roar on the throttle from a standstill; wall
    /// hits (louder the harder) and the scrape loop while sliding along a wall. Ramming (stage 9b): one thud a tick for
    /// all the monsters hit (louder the more), a bounce off the big ones, the engine stalling at 0 HP and the smoke loop
    /// while the wreck smokes (the blast is the wreck's explosion effect). Names: the look's engine sounds and the shared
    /// folders <c>Sounds/Vehicle/door</c>, <c>wall_hit</c>, <c>scrape_loop</c>, <c>monster_hit</c>, <c>bounce</c>,
    /// <c>stall</c>, <c>smoke_loop</c>.
    /// </summary>
    public sealed class VehicleAudioPresenter : ILevelLoadStep, ILevelTickable, IDisposable
    {
        public const string DoorSound = "door";
        public const string WallHitSound = "wall_hit";
        public const string ScrapeSound = "scrape_loop";
        public const string MonsterHitSound = "monster_hit";
        public const string BounceSound = "bounce";
        public const string StallSound = "stall";
        public const string SmokeSound = "smoke_loop";

        /// <summary>Monsters rammed in a tick for the thud at full volume.</summary>
        private const float LoudHits = 6f;

        /// <summary>Share of the top speed at which the idle has faded out and the drive loop is full.</summary>
        private const float DriveFadeShare = 0.25f;

        private const float MinDrivePitch = 0.75f;
        private const float MaxDrivePitch = 1.35f;
        private const float LoopFadeSpeed = 4f;

        /// <summary>Below this share of the top speed the throttle roars.</summary>
        private const float RevShare = 0.15f;

        private readonly AudioService _audio;
        private readonly VehicleSystem _vehicles;
        private readonly DeterministicRandom _random = new DeterministicRandom(Environment.TickCount);
        private Loop _idle;
        private Loop _drive;
        private Loop _scrape;
        private Loop _smoke;
        private bool _throttle;
        private bool _subscribed;

        public VehicleAudioPresenter(AudioService audio, VehicleSystem vehicles)
        {
            _audio = audio;
            _vehicles = vehicles;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        private AudioCatalog Catalog => _audio.Catalog;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed || Catalog == null || _vehicles.Vehicles.Count == 0) return UniTask.CompletedTask;
            _vehicles.Entered += OnEntered;
            _vehicles.Exited += OnExited;
            _vehicles.WallHit += OnWallHit;
            _vehicles.Rammed += OnRammed;
            _vehicles.Bounced += OnBounced;
            _vehicles.BrokeDown += OnBrokeDown;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            Stop(ref _idle);
            Stop(ref _drive);
            Stop(ref _scrape);
            Stop(ref _smoke);
            if (!_subscribed) return;
            _vehicles.Entered -= OnEntered;
            _vehicles.Exited -= OnExited;
            _vehicles.WallHit -= OnWallHit;
            _vehicles.Rammed -= OnRammed;
            _vehicles.Bounced -= OnBounced;
            _vehicles.BrokeDown -= OnBrokeDown;
            _subscribed = false;
        }

        public void Tick(float deltaTime)
        {
            if (!_subscribed) return;
            var vehicle = _vehicles.Current;
            var look = vehicle?.Definition.Look;
            var share = vehicle != null ? vehicle.SpeedShare : 0f;
            var drive = Mathf.Clamp01(share / DriveFadeShare);
            // A wreck's engine is dead: no idling, the driving loop fades with the rolling.
            var running = vehicle != null && !vehicle.Broken;
            Fade(ref _idle, look != null ? Catalog.VehicleSound(look.Idle) : null, running ? 1f - drive : 0f, 1f, deltaTime);
            Fade(ref _smoke, Catalog.VehicleSound(SmokeSound), vehicle != null && vehicle.Broken ? 1f : 0f, 1f, deltaTime);
            Fade(ref _drive, look != null ? Catalog.VehicleSound(look.Drive) : null, vehicle != null ? drive : 0f,
                Mathf.Lerp(MinDrivePitch, MaxDrivePitch, share), deltaTime);
            Fade(ref _scrape, Catalog.VehicleSound(ScrapeSound), vehicle != null && vehicle.IsScraping ? 1f : 0f,
                1f, deltaTime);

            var throttle = vehicle != null && vehicle.Throttle;
            if (throttle && !_throttle && share < RevShare && look != null)
                Play(Catalog.VehicleSound(look.Rev), 1f);
            _throttle = throttle;
        }

        private void OnEntered(Vehicle vehicle)
        {
            Play(Catalog.VehicleSound(DoorSound), 1f);
            Play(Catalog.VehicleSound(vehicle.Definition.Look?.Start), 1f);
        }

        private void OnExited(Vehicle vehicle) => Play(Catalog.VehicleSound(DoorSound), 1f);

        private void OnWallHit(Vehicle vehicle, float impact)
        {
            var hard = Mathf.Clamp01(impact / Mathf.Max(0.01f, vehicle.Definition.Engine.MaxSpeed));
            Play(Catalog.VehicleSound(WallHitSound), Mathf.Lerp(0.35f, 1f, hard));
        }

        private void OnRammed(Vehicle vehicle, int hits, int kills) =>
            Play(Catalog.VehicleSound(MonsterHitSound), Mathf.Lerp(0.45f, 1f, Mathf.Clamp01((hits - 1) / (LoudHits - 1f))));

        private void OnBounced(Vehicle vehicle, Vector2 at) => Play(Catalog.VehicleSound(BounceSound), 1f);

        private void OnBrokeDown(Vehicle vehicle)
        {
            if (vehicle == _vehicles.Current) Play(Catalog.VehicleSound(StallSound), 1f);
        }

        private void Play(SoundCue cue, float volume)
        {
            if (cue == null || cue.IsEmpty) return;
            _audio.Play(cue, SoundChannel.Level, volume);
        }

        /// <summary>The loop eases to <paramref name="target"/> volume; it starts when heard and stops when silent.</summary>
        private void Fade(ref Loop loop, SoundCue cue, float target, float pitch, float deltaTime)
        {
            if (cue == null || cue.IsEmpty) target = 0f;
            if (loop.Cue != cue && loop.Voice != 0)
            {
                // Another vehicle's sound (got into the other one): start over.
                Stop(ref loop);
            }

            loop.Volume = Mathf.MoveTowards(loop.Volume, target, LoopFadeSpeed * deltaTime);
            if (loop.Volume <= 0f)
            {
                Stop(ref loop);
                return;
            }

            if (!_audio.IsPlaying(loop.Voice))
            {
                loop.Cue = cue;
                loop.Voice = _audio.PlayLoop(cue, SoundChannel.Level, loop.Volume, 0f, (float)_random.NextDouble(), pitch);
            }
            else
            {
                _audio.SetVoice(loop.Voice, loop.Volume, 0f, pitch);
            }
        }

        private void Stop(ref Loop loop)
        {
            if (loop.Voice != 0) _audio.Stop(loop.Voice);
            loop.Voice = 0;
            loop.Cue = null;
            loop.Volume = 0f;
        }

        private struct Loop
        {
            public SoundCue Cue;
            public int Voice;
            public float Volume;
        }
    }
}
