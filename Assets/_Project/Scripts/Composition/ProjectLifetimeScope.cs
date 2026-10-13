using Maze.Application.Assets;
using Maze.Application.Flow;
using Maze.Application.Platform;
using Maze.Application.Levels;
using Maze.Application.Save;
using Maze.Application.Services;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Player;
using Maze.Presentation.Audio;
using Maze.Presentation.Localization;
using Maze.Presentation.UI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Maze.Composition
{
    /// <summary>
    /// Root scope in the Bootstrap scene (ТЗ §6.1): application-wide services only, no level state.
    /// The Bootstrap scene stays loaded for the whole session; levels are loaded additively.
    /// </summary>
    public sealed class ProjectLifetimeScope : LifetimeScope
    {
        [SerializeField] private UIRoot _ui;
        [Tooltip("Scene with the LevelLifetimeScope, loaded additively for every level. Must be in Build Settings.")]
        [SerializeField] private string _levelSceneName = "Game";

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<AddressablesService>(Lifetime.Singleton).As<IAddressablesService>();

            // IApplicationService initialization order = registration order. Settings are read from the save.
            // The site the build runs on (its SDK): first, saves and language depend on it. A platform build defines
            // its symbol (MazeBuilder: MAZE_YANDEX, MAZE_CRAZYGAMES, MAZE_VK, MAZE_GAMEDISTRIBUTION; Android for RuStore: MAZE_RUSTORE); no
            // symbol = no site.
#if MAZE_YANDEX
            builder.Register<YandexPlatform>(Lifetime.Singleton).As<IGamePlatform>();
#elif MAZE_CRAZYGAMES
            builder.Register<CrazyGamesPlatform>(Lifetime.Singleton).As<IGamePlatform>();
#elif MAZE_VK
            builder.Register<VkPlatform>(Lifetime.Singleton).As<IGamePlatform>();
#elif MAZE_GAMEDISTRIBUTION
            builder.Register<GameDistributionPlatform>(Lifetime.Singleton).As<IGamePlatform>();
#elif MAZE_RUSTORE
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
#if MAZE_YANDEX
            builder.Register<YandexMetrikaSink>(Lifetime.Singleton).As<IAnalyticsSink>();
            builder.Register<YandexAdProvider>(Lifetime.Singleton).As<IAdProvider>();
#elif MAZE_CRAZYGAMES
            builder.Register<CrazyGamesAdProvider>(Lifetime.Singleton).As<IAdProvider>(); // its own statistics: no sink
#elif MAZE_VK
            builder.Register<VkAdProvider>(Lifetime.Singleton).As<IAdProvider>(); // VK's own statistics: no sink
#elif MAZE_GAMEDISTRIBUTION
            builder.Register<GameDistributionAdProvider>(Lifetime.Singleton).As<IAdProvider>(); // analytics forbidden: no sink
#elif MAZE_RUSTORE
            builder.Register<MyTrackerSink>(Lifetime.Singleton).As<IAnalyticsSink>();
            builder.Register<VkAdsProvider>(Lifetime.Singleton).As<IAdProvider>();
#else
            builder.Register<SimulatedAdProvider>(Lifetime.Singleton).As<IAdProvider>();
#endif
            builder.Register<SoundSuspension>(Lifetime.Singleton);
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
