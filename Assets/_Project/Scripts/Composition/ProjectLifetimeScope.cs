using Purge.Application.Assets;
using Purge.Application.Flow;
using Purge.Application.Platform;
using Purge.Application.Levels;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Player;
using Purge.Presentation.Audio;
using Purge.Presentation.Localization;
using Purge.Presentation.Menu;
using Purge.Presentation.UI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Purge.Composition
{
    /// <summary>
    /// Root scope in the Bootstrap scene (ТЗ §6.1): application-wide services only, no level state.
    /// The Bootstrap scene stays loaded for the whole session; levels are loaded additively.
    /// </summary>
    public sealed class ProjectLifetimeScope : LifetimeScope
    {
        [SerializeField] private UIRoot _ui;
        [Tooltip("The main menu's 3D character stage (stage 11a); optional.")]
        [SerializeField] private MenuPodiumView _podium;
        [Tooltip("Scene with the LevelLifetimeScope, loaded additively for every level. Must be in Build Settings.")]
        [SerializeField] private string _levelSceneName = "Game";

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<AddressablesService>(Lifetime.Singleton).As<IAddressablesService>();

            // IApplicationService initialization order = registration order. Settings are read from the save.
            // The site the build runs on (its SDK): first, saves and language depend on it. A platform build defines
            // its symbol (PurgeBuilder: PURGE_YANDEX, PURGE_CRAZYGAMES, PURGE_VK, PURGE_GAMEDISTRIBUTION; Android for RuStore: PURGE_RUSTORE); no
            // symbol = no site.
#if PURGE_YANDEX
            builder.Register<YandexPlatform>(Lifetime.Singleton).As<IGamePlatform>();
#elif PURGE_CRAZYGAMES
            builder.Register<CrazyGamesPlatform>(Lifetime.Singleton).As<IGamePlatform>();
#elif PURGE_VK
            builder.Register<VkPlatform>(Lifetime.Singleton).As<IGamePlatform>();
#elif PURGE_GAMEDISTRIBUTION
            builder.Register<GameDistributionPlatform>(Lifetime.Singleton).As<IGamePlatform>();
#elif PURGE_RUSTORE
            builder.Register<RuStorePlatform>(Lifetime.Singleton).As<IGamePlatform>();
#else
            builder.Register<GenericPlatform>(Lifetime.Singleton).As<IGamePlatform>();
#endif
            builder.Register<PlatformService>(Lifetime.Singleton).AsSelf().As<IApplicationService>();
            builder.Register<PlayerPrefsSaveStorage>(Lifetime.Singleton).As<ISaveStorage>();
            builder.Register<SaveService>(Lifetime.Singleton).AsSelf().As<IApplicationService>();
            builder.Register<SettingsService>(Lifetime.Singleton).AsSelf().As<IApplicationService, IAimSettings>();
            builder.Register<LocalizationService>(Lifetime.Singleton).AsSelf().As<IApplicationService>();
            builder.Register<AudioService>(Lifetime.Singleton).AsSelf().As<IApplicationService>();
            builder.Register<UiPointer>(Lifetime.Singleton).As<IUiPointer>();
            builder.Register<InputService>(Lifetime.Singleton).As<IInputService, IPlayerInput, IApplicationService>();
            builder.Register<SharedDefinitionsService>(Lifetime.Singleton).AsSelf().As<IApplicationService>();
            builder.Register<LevelCatalogService>(Lifetime.Singleton).As<ILevelCatalog, IApplicationService>();
            builder.Register<ProgressService>(Lifetime.Singleton).As<IProgressService>();

            // Platform: analytics sinks and the ad provider of the build's platform (SDK integrations go here).
            builder.Register<LogAnalyticsSink>(Lifetime.Singleton).As<IAnalyticsSink>();
#if PURGE_YANDEX
            builder.Register<YandexMetrikaSink>(Lifetime.Singleton).As<IAnalyticsSink>();
            builder.Register<YandexAdProvider>(Lifetime.Singleton).As<IAdProvider>();
#elif PURGE_CRAZYGAMES
            builder.Register<CrazyGamesAdProvider>(Lifetime.Singleton).As<IAdProvider>(); // its own statistics: no sink
#elif PURGE_VK
            builder.Register<VkAdProvider>(Lifetime.Singleton).As<IAdProvider>(); // VK's own statistics: no sink
#elif PURGE_GAMEDISTRIBUTION
            builder.Register<GameDistributionAdProvider>(Lifetime.Singleton).As<IAdProvider>(); // analytics forbidden: no sink
#elif PURGE_RUSTORE
            builder.Register<MyTrackerSink>(Lifetime.Singleton).As<IAnalyticsSink>();
            builder.Register<VkAdsProvider>(Lifetime.Singleton).As<IAdProvider>();
#else
            builder.Register<SimulatedAdProvider>(Lifetime.Singleton).As<IAdProvider>();
#endif
            builder.Register<SoundSuspension>(Lifetime.Singleton);
            builder.Register<Purge.Application.Platform.VibrationService>(Lifetime.Singleton);
            builder.Register<Purge.Core.Feel.FeelChannels>(Lifetime.Singleton);
            builder.Register<AnalyticsService>(Lifetime.Singleton).AsSelf().As<IApplicationService>();
            builder.Register<AdsService>(Lifetime.Singleton).AsSelf().As<IApplicationService>();
            builder.Register<LevelRunStats>(Lifetime.Singleton);
            builder.Register<GameAnalytics>(Lifetime.Singleton);
            builder.Register<AccountService>(Lifetime.Singleton);

            builder.Register<ILevelSessionFactory>(_ => new LevelSessionFactory(this, _levelSceneName), Lifetime.Singleton);
            builder.Register<GameFlow>(Lifetime.Singleton);
            builder.Register<PauseController>(Lifetime.Singleton);
            builder.Register<AppFocusController>(Lifetime.Singleton);
            builder.Register<PlatformEvents>(Lifetime.Singleton);

            builder.RegisterInstance(_ui);
            builder.Register<ScreenRouter>(Lifetime.Singleton);
            builder.Register<SettingsPresenter>(Lifetime.Singleton);
            builder.Register<LocalizationPresenter>(Lifetime.Singleton);
            builder.Register<AppAudioPresenter>(Lifetime.Singleton);
            // The podium is optional (test scenes have none).
            builder.Register(resolver => new MenuPodiumPresenter(resolver.Resolve<GameFlow>(),
                resolver.Resolve<SharedDefinitionsService>(), resolver.Resolve<IProgressService>(),
                resolver.Resolve<IAddressablesService>(), _podium), Lifetime.Singleton);

            builder.RegisterEntryPoint<ApplicationEntryPoint>();
            builder.RegisterEntryPoint<AudioTickDriver>();
        }
    }

    /// <summary>Ticks <see cref="AudioService"/> (fades, music loops) with unscaled time, also while the game is paused.</summary>
    public sealed class AudioTickDriver : ITickable
    {
        private readonly AudioService _audio;

        public AudioTickDriver(AudioService audio)
        {
            _audio = audio;
        }

        public void Tick() => _audio.Tick(Time.unscaledDeltaTime);
    }
}
