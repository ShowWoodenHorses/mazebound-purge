using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Services;
using Purge.Gameplay.Level;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// The player's camera settings (stage 6e2, settings «Game»): distance ±N cells (<see cref="TopDownCamera.FollowHalfExtent"/>)
    /// and shake on/off (<see cref="TopDownCamera.FeelEnabled"/>). Applied at load and at once on every change — also
    /// from the pause. The stress test sets its own distance later (its step runs after).
    /// </summary>
    public sealed class CameraSettingsPresenter : ILevelLoadStep, IDisposable
    {
        private readonly TopDownCamera _camera;
        private readonly SettingsService _settings;
        private bool _subscribed;

        public CameraSettingsPresenter(TopDownCamera camera, SettingsService settings)
        {
            _camera = camera;
            _settings = settings;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_subscribed)
            {
                _settings.GameChanged += Apply;
                _subscribed = true;
            }

            Apply();
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _settings.GameChanged -= Apply;
            _subscribed = false;
        }

        private void Apply()
        {
            if (_camera == null) return;
            var game = _settings.Game;
            _camera.FollowHalfExtent = game.CameraDistance;
            _camera.FeelEnabled = game.CameraShake;
        }
    }
}
