using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Services;
using Purge.Core.Audio;
using Purge.Core.Common;
using Purge.Gameplay.Level;
using Purge.Gameplay.Objects;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Presentation.Audio
{
    /// <summary>
    /// Sounds of the level objects (stage 7в): a chest opens (its lid; a challenge's chest too), a locked chest without
    /// its key, the safe dome's loop near it while it stands (fades in and out). Barrels' blasts sound as explosions of
    /// effects (<c>LevelAudioPresenter</c>), the chest key as a key picked up.
    /// </summary>
    public sealed class LevelObjectsAudioPresenter : ILevelLoadStep, ILevelTickable, IDisposable
    {
        private const float LoopFadeSpeed = 2.5f;

        private readonly AudioService _audio;
        private readonly PlayerSystem _player;
        private readonly PlayerInteraction _interaction;
        private readonly ChestSystem _chests;
        private readonly RefugeSystem _refuge;
        private readonly DeterministicRandom _random = new DeterministicRandom(Environment.TickCount);
        private int _domeVoice;
        private float _domeVolume;
        private bool _subscribed;

        public LevelObjectsAudioPresenter(AudioService audio, PlayerSystem player, PlayerInteraction interaction,
            ChestSystem chests, RefugeSystem refuge)
        {
            _audio = audio;
            _player = player;
            _interaction = interaction;
            _chests = chests;
            _refuge = refuge;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        private AudioCatalog Catalog => _audio.Catalog;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed || Catalog == null) return UniTask.CompletedTask;
            _chests.Opened += OnOpened;
            _interaction.Interacted += OnInteracted;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            _audio.Stop(_domeVoice);
            _domeVoice = 0;
            if (!_subscribed) return;
            _chests.Opened -= OnOpened;
            _interaction.Interacted -= OnInteracted;
            _subscribed = false;
        }

        public void Tick(float deltaTime)
        {
            var cue = Catalog?.DomeLoop;
            if (cue == null || cue.IsEmpty || !_refuge.Exists || !_player.IsSpawned) return;
            var offset = _refuge.Center - _player.Position;
            var distance = Mathf.Max(0f, offset.magnitude - _refuge.Edge);
            var target = _refuge.IsUp && _player.Floor == _refuge.Floor ? cue.Attenuation(distance) : 0f;
            _domeVolume = Mathf.MoveTowards(_domeVolume, target, LoopFadeSpeed * deltaTime);
            if (_domeVolume <= 0f)
            {
                _audio.Stop(_domeVoice);
                _domeVoice = 0;
                return;
            }

            var pan = distance > 0f ? SoundMath.Pan(offset.x) : 0f;
            if (!_audio.IsPlaying(_domeVoice))
                _domeVoice = _audio.PlayLoop(cue, SoundChannel.Level, _domeVolume, pan, (float)_random.NextDouble());
            else
                _audio.SetVoice(_domeVoice, _domeVolume, pan);
        }

        private void OnOpened(Chest chest) => PlayAt(Catalog.ChestOpen, chest.Position);

        private void OnInteracted(InteractionResult result, Core.Level.DoorData door)
        {
            if (result != InteractionResult.ChestLocked) return;
            var chest = _chests.FindUsable();
            PlayAt(Catalog.ChestLocked, chest != null ? chest.Position : _player.Position);
        }

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
