using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Services;
using Purge.Core.Audio;
using Purge.Core.Common;
using Purge.Core.Level;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using UnityEngine;

namespace Purge.Presentation.Audio
{
    /// <summary>
    /// Sounds of the horde and the doors (stage 6e2), 2D by distance to the player like the rest
    /// (<see cref="SoundCue.Attenuation"/>):
    /// abilities — <see cref="Core.Monsters.AbilityModule.CastSound"/> when a cast begins and
    /// <see cref="Core.Monsters.AbilityModule.ReleaseSound"/> when it goes off (named sounds of the catalog, data of the
    /// ability); a caller's call (<see cref="SoundType.Call"/>), ricochets off armored fronts, a champion appearing;
    /// doors opening and closing (by anyone — the player's own too, <see cref="DoorSystem.DoorChanged"/>), struck and
    /// broken by monsters. Bosses (stage 8a): the stinger and the roar when one comes, heard anywhere; its hurt roar now
    /// and then (<see cref="BossHurtInterval"/>); the long death; the arena's gates clanging down and up.
    /// </summary>
    public sealed class HordeAudioPresenter : ILevelLoadStep, IDisposable
    {
        private readonly AudioService _audio;
        private readonly PlayerSystem _player;
        private readonly HordeSystem _horde;
        private readonly MonsterAbilities _abilities;
        private readonly CombatTargets _targets;
        private readonly DoorSystem _doors;
        private readonly SoundEventBus _sounds;
        private readonly BossSystem _bosses;
        private float _nextBossHurt;
        private bool _subscribed;

        /// <summary>A boss roars from a hit at most this often, s.</summary>
        public const float BossHurtInterval = 3f;

        // Sounds/Monster/<name>/ (stage 8a).
        public const string BossRoar = "boss_roar", BossStinger = "boss_stinger", BossDeath = "boss_death",
            BossHurt = "boss_hurt", ArenaGate = "arena_gate";

        public HordeAudioPresenter(AudioService audio, PlayerSystem player, HordeSystem horde, MonsterAbilities abilities,
            CombatTargets targets, DoorSystem doors, SoundEventBus sounds, BossSystem bosses = null)
        {
            _bosses = bosses;
            _audio = audio;
            _player = player;
            _horde = horde;
            _abilities = abilities;
            _targets = targets;
            _doors = doors;
            _sounds = sounds;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        private AudioCatalog Catalog => _audio.Catalog;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed) return UniTask.CompletedTask;
            _subscribed = true;
            _abilities.CastStarted += OnCastStarted;
            _abilities.Released += OnReleased;
            _horde.ChampionMade += OnChampionMade;
            _targets.ArmoredHit += OnArmoredHit;
            _doors.DoorChanged += OnDoorChanged;
            _doors.DoorHit += OnDoorHit;
            _doors.DoorBroken += OnDoorBroken;
            _sounds.Emitted += OnSound;
            if (_bosses != null)
            {
                _bosses.Appeared += OnBossAppeared;
                _bosses.Defeated += OnBossDefeated;
                _bosses.GatesChanged += OnGatesChanged;
                _targets.Damaged += OnDamaged;
            }

            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _subscribed = false;
            _abilities.CastStarted -= OnCastStarted;
            _abilities.Released -= OnReleased;
            _horde.ChampionMade -= OnChampionMade;
            _targets.ArmoredHit -= OnArmoredHit;
            _doors.DoorChanged -= OnDoorChanged;
            _doors.DoorHit -= OnDoorHit;
            _doors.DoorBroken -= OnDoorBroken;
            _sounds.Emitted -= OnSound;
            if (_bosses != null)
            {
                _bosses.Appeared -= OnBossAppeared;
                _bosses.Defeated -= OnBossDefeated;
                _bosses.GatesChanged -= OnGatesChanged;
                _targets.Damaged -= OnDamaged;
            }
        }

        private void OnBossAppeared(int slot)
        {
            // Heard wherever the player is: a boss is always known.
            Play(Catalog.MonsterSound(BossStinger));
            Play(Catalog.MonsterSound(BossRoar));
            _nextBossHurt = Time.unscaledTime + BossHurtInterval;
        }

        private void OnBossDefeated(int slot, Vector2 at) => Play(Catalog.MonsterSound(BossDeath));

        private void OnGatesChanged(bool closed)
        {
            if (_bosses.Entries.Count > 0) Play(Catalog.MonsterSound(ArenaGate));
        }

        private void OnDamaged(CombatTarget target, Vector2 at, float amount, Vector2 direction, bool killed)
        {
            if (killed || target.Agent < 0 || target.Agent != _bosses.Boss || Time.unscaledTime < _nextBossHurt) return;
            _nextBossHurt = Time.unscaledTime + BossHurtInterval;
            PlayAt(Catalog.MonsterSound(BossHurt), _horde.Positions[target.Agent]);
        }

        private void Play(SoundCue cue)
        {
            if (cue != null && !cue.IsEmpty) _audio.Play(cue, SoundChannel.Level, 1f, 0f);
        }

        private void OnCastStarted(int slot, int ability) => PlayAbility(slot, ability, false);

        private void OnReleased(int slot, int ability) => PlayAbility(slot, ability, true);

        private void PlayAbility(int slot, int ability, bool released)
        {
            var abilities = _horde.DefinitionOf(slot)?.Abilities;
            if (abilities == null || (uint)ability >= (uint)abilities.Count || abilities[ability] == null) return;
            var module = abilities[ability];
            PlayAt(Catalog.MonsterSound(released ? module.ReleaseSound : module.CastSound), _horde.Positions[slot]);
        }

        private void OnChampionMade(int slot) => PlayAt(Catalog.ChampionAppear, _horde.Positions[slot]);

        private void OnArmoredHit(int slot, Vector2 at) => PlayAt(Catalog.ArmorHit, at);

        private void OnSound(SoundEvent sound)
        {
            if (sound.Type == SoundType.Call) PlayAt(Catalog.MonsterCall, sound.Position);
        }

        /// <summary>A broken door has its own sound (<see cref="OnDoorBroken"/>), not the opening.</summary>
        private void OnDoorChanged(DoorData door, bool open)
        {
            if (open && _doors.IsBroken(door.Id)) return;
            PlayAt(open ? Catalog.DoorOpen : Catalog.DoorClose, At(door));
        }

        private void OnDoorHit(DoorData door, Vector2 from) => PlayAt(Catalog.DoorHit, At(door));

        private void OnDoorBroken(DoorData door, Vector2 from) => PlayAt(Catalog.DoorBroken, At(door));

        private static Vector2 At(DoorData door) => new Vector2(door.Position.X, door.Position.Y);

        private void PlayAt(SoundCue cue, Vector2 position)
        {
            if (cue == null || cue.IsEmpty) return;
            var volume = 1f;
            var pan = 0f;
            if (cue.Range > 0f && _player.IsSpawned)
            {
                var offset = position - _player.Position;
                volume = cue.Attenuation(offset.magnitude);
                if (volume <= 0.01f) return;
                pan = SoundMath.Pan(offset.x);
            }

            _audio.Play(cue, SoundChannel.Level, volume, pan);
        }
    }
}
