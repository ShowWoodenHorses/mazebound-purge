using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Flow;
using Purge.Application.Levels;
using Purge.Application.Save;
using Purge.Application.Platform;
using Purge.Core.Common;
using Purge.Presentation.Audio;
using Purge.Presentation.Localization;
using Purge.Presentation.Menu;
using Purge.Presentation.UI;
using VContainer.Unity;

namespace Purge.Composition
{
    /// <summary>Starts the application once the ProjectLifetimeScope is built: UI, sound, input, services, main menu.</summary>
    public sealed class ApplicationEntryPoint : IAsyncStartable
    {
        private readonly GameFlow _flow;
        private readonly ScreenRouter _screens;
        private readonly PauseController _pause;
        private readonly SettingsPresenter _settings;
        private readonly AppAudioPresenter _audio;
        private readonly LocalizationPresenter _localization;
        private readonly GameAnalytics _analytics;
        private readonly AppFocusController _focus;
        private readonly PlatformEvents _platformEvents;
        private readonly PlatformService _platform;
        private readonly ILevelCatalog _catalog;
        private readonly IProgressService _progress;
        private readonly MenuPodiumPresenter _podium;

        public ApplicationEntryPoint(GameFlow flow, ScreenRouter screens, PauseController pause, SettingsPresenter settings,
            AppAudioPresenter audio, LocalizationPresenter localization, GameAnalytics analytics, AppFocusController focus,
            PlatformEvents platformEvents, PlatformService platform, ILevelCatalog catalog, IProgressService progress,
            MenuPodiumPresenter podium)
        {
            _podium = podium;
            _progress = progress;
            _catalog = catalog;
            _platform = platform;
            _platformEvents = platformEvents;
            _focus = focus;
            _analytics = analytics;
            _localization = localization;
            _audio = audio;
            _flow = flow;
            _screens = screens;
            _pause = pause;
            _settings = settings;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            GameLog.Info(LogChannel.Bootstrap, "Bootstrap started.");
#if UNITY_ANDROID
            // Unity caps mobile players at 30 FPS by default (decision 2026-10-14: 60 on Android, see Docs/StressTest.md).
            UnityEngine.Application.targetFrameRate = 60;
#endif
            _localization.Initialize();
            _screens.Initialize();
            _podium.Initialize();
            _settings.Initialize();
            _audio.Initialize();
            _pause.Initialize();
            _analytics.Initialize();
            _focus.Initialize();
            _platformEvents.Initialize();

            await _flow.InitializeApplication();
            if (_flow.State == GameFlowState.Error || cancellation.IsCancellationRequested) return;
            // A new player on a site that wants it lands in gameplay straight away: no menu at all (its music would
            // start and be unloaded at once; in a browser waiting for the first click that breaks the deferred play).
            if (FirstLevelForNewPlayer() is { } first) await _flow.StartLevel(first);
            else await _flow.OpenMainMenu();
        }

        /// <summary>
        /// A site where players land in gameplay at once (<see cref="PlatformTraits.StartsInGameplay"/>): a player who has
        /// completed nothing starts with the tutorial (or the first level) instead of the menu.
        /// </summary>
        private string FirstLevelForNewPlayer()
        {
            if (!_platform.Traits.StartsInGameplay) return null;
            var tutorial = _catalog.Tutorial;
            if (tutorial != null && _progress.IsCompleted(tutorial.LevelId)) return null;
            foreach (var level in _catalog.Levels)
                if (_progress.IsCompleted(level.LevelId))
                    return null;
            return tutorial?.LevelId ?? (_catalog.Levels.Count > 0 ? _catalog.Levels[0].LevelId : null);
        }
    }
}
