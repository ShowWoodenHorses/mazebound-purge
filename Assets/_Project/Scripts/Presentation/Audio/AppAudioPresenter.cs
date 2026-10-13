using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Flow;
using Maze.Application.Services;
using Maze.Core.Audio;
using Maze.Presentation.UI;
using UnityEngine;

namespace Maze.Presentation.Audio
{
    /// <summary>
    /// Application-wide sound (project scope): music by <see cref="GameFlow.State"/> (menu track in the menu, level
    /// track in a level, quieter on pause and under the result), sounds of the UI controls (<see cref="UiSound"/>)
    /// and of the flow — pause, map, "Finish level?", the result jingle and its stars one by one. Level sounds pause
    /// with the gameplay and stop when the level ends (the last one-shots, like a death, may finish).
    /// </summary>
    public sealed class AppAudioPresenter : IDisposable
    {
        /// <summary>Seconds between preview clicks while the sound volume slider moves.</summary>
        private const float PreviewInterval = 0.12f;

        private readonly AudioService _audio;
        private readonly GameFlow _flow;
        private readonly UIRoot _ui;
        private readonly SettingsService _settings;
        private CancellationTokenSource _result;
        private GameFlowState _state;
        private float _sfxVolume;
        private float _lastPreview;
        private bool _initialized;

        public AppAudioPresenter(AudioService audio, GameFlow flow, UIRoot ui, SettingsService settings)
        {
            _audio = audio;
            _flow = flow;
            _ui = ui;
            _settings = settings;
        }

        private AudioCatalog Catalog => _audio.Catalog;

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _state = _flow.State;
            _sfxVolume = _settings.SfxVolume;
            _flow.StateChanged += OnStateChanged;
            _ui.SoundRequested += OnSoundRequested;
            _settings.VolumeChanged += OnVolumeChanged;
        }

        public void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            CancelResult();
            _flow.StateChanged -= OnStateChanged;
            _settings.VolumeChanged -= OnVolumeChanged;
            if (_ui != null) _ui.SoundRequested -= OnSoundRequested;
        }

        private void OnStateChanged(GameFlowState state)
        {
            var previous = _state;
            _state = state;
            CancelResult();
            if (Catalog == null) return; // Not initialized yet (or failed): silence.

            switch (state)
            {
                case GameFlowState.MainMenu:
                    _audio.StopLevelSounds();
                    _audio.SetMusicShare(1f);
                    _audio.PlayMusic(Catalog.MenuMusic);
                    break;

                case GameFlowState.Loading:
                    _audio.StopLevelSounds();
                    _audio.SetMusicShare(1f);
                    _audio.PlayMusic(null);
                    break;

                case GameFlowState.Playing:
                    _audio.SetLevelPaused(false);
                    _audio.SetMusicShare(1f);
                    _audio.PlayMusic(Catalog.LevelMusic);
                    if (previous == GameFlowState.Paused || previous == GameFlowState.Tip) Play(Catalog.PauseResume);
                    else if (previous == GameFlowState.Map) Play(Catalog.MapClose);
                    break;

                case GameFlowState.Paused:
                case GameFlowState.Map:
                case GameFlowState.ExitConfirmation:
                case GameFlowState.Tip:
                    _audio.SetLevelPaused(true);
                    _audio.SetMusicShare(Catalog.PausedMusicVolume);
                    if (state == GameFlowState.Paused || state == GameFlowState.Tip) Play(Catalog.PauseOpen);
                    else if (state == GameFlowState.Map) Play(Catalog.MapOpen);
                    else
                    {
                        Play(Catalog.ExitReached);
                        Play(Catalog.ConfirmExit);
                    }
                    break;

                case GameFlowState.Completed:
                case GameFlowState.Failed:
                    _audio.SetLevelPaused(false); // The last sounds (death) finish…
                    _audio.StopLevelSounds(loopsOnly: true); // …the ambience stops.
                    _audio.SetMusicShare(Catalog.ResultMusicVolume);
                    _result = new CancellationTokenSource();
                    // The tutorial shows no stars: only the jingle.
                    var stars = _flow.IsTutorialLevel ? 0 : _flow.LastResult?.Stars ?? 0;
                    PlayResultAsync(state == GameFlowState.Completed, stars, _result.Token).Forget();
                    break;

                case GameFlowState.Error:
                    _audio.StopLevelSounds();
                    _audio.PlayMusic(null);
                    Play(Catalog.Denied);
                    break;
            }
        }

        /// <summary>
        /// Jingle after a short pause (the death or exit sound first), then a sound per star, pitch rising; each sound
        /// reveals its star on the result screen.
        /// </summary>
        private async UniTaskVoid PlayResultAsync(bool completed, int stars, CancellationToken cancellation)
        {
            var catalog = Catalog;
            if (await Delay(catalog.ResultDelay, cancellation)) return;
            Play(completed ? catalog.LevelComplete : catalog.LevelFailed);

            for (var i = 0; i < stars; i++)
            {
                if (await Delay(catalog.StarInterval, cancellation)) return;
                _audio.Play(catalog.Star, SoundChannel.Interface, 1f, 0f, 1f + catalog.StarPitchStep * i);
                _ui.Result.RevealNextStar(); // The star appears with its sound.
            }
        }

        /// <summary>True when cancelled.</summary>
        private static async UniTask<bool> Delay(float seconds, CancellationToken cancellation) =>
            await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, cancellation)
                .SuppressCancellationThrow();

        private void CancelResult()
        {
            if (_result == null) return;
            _result.Cancel();
            _result.Dispose();
            _result = null;
        }

        private void OnSoundRequested(UiSoundKind kind)
        {
            if (Catalog == null) return;
            switch (kind)
            {
                case UiSoundKind.Click: Play(Catalog.Click); break;
                case UiSoundKind.Back: Play(Catalog.Back); break;
                case UiSoundKind.Denied: Play(Catalog.Denied); break;
            }
        }

        /// <summary>Moving the sound volume slider plays a click at the new volume.</summary>
        private void OnVolumeChanged()
        {
            var sfx = _settings.SfxVolume;
            if (Mathf.Approximately(sfx, _sfxVolume)) return;
            _sfxVolume = sfx;
            if (Catalog == null || !_ui.Settings.IsVisible || Time.unscaledTime - _lastPreview < PreviewInterval) return;
            _lastPreview = Time.unscaledTime;
            Play(Catalog.Click);
        }

        private void Play(SoundCue cue) => _audio.Play(cue, SoundChannel.Interface);
    }
}
