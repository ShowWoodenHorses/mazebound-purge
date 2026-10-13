using Maze.Application.Platform;
using Maze.Application.Services;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Navigation;
using Maze.Gameplay.Level;
using Maze.Gameplay.Map;
using Maze.Gameplay.Pickups;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Spatial;
using Maze.Gameplay.Tutorial;
using Maze.Gameplay.Visibility;
using Maze.Gameplay.Weapons;
using Maze.Gameplay.Zombies;
using Maze.Presentation.Audio;
using Maze.Presentation.Map;
using Maze.Presentation.UI;
using Maze.Presentation.Visual;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Maze.Composition
{
    /// <summary>
    /// Scope of the active level (ТЗ §7), placed in the Game scene with Auto Run off. Built by
    /// <see cref="LevelSessionFactory"/> as a child of the ProjectLifetimeScope, which also registers
    /// <see cref="LevelData"/>, the level's <c>IAssetOwner</c> and <see cref="LevelLaunchOptions"/>.
    /// Disposing it disposes every level system.
    /// Registration order matters within a load stage and for ticking: gameplay before its views.
    /// </summary>
    public sealed class LevelLifetimeScope : LifetimeScope
    {
        [SerializeField] private LevelViewRoot _viewRoot;
        [SerializeField] private TopDownCamera _camera;

        protected override void Awake()
        {
            if (autoRun)
            {
                Debug.LogWarning("[Maze] LevelLifetimeScope must not auto-run: it is built by LevelSessionFactory.", this);
                autoRun = false;
            }

            base.Awake();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            // Shared definitions live in the project scope.
            builder.Register(resolver => resolver.Resolve<SharedDefinitionsService>().Player, Lifetime.Singleton);
            builder.Register(resolver => resolver.Resolve<SharedDefinitionsService>().PlayerVisual, Lifetime.Singleton);
            builder.Register(resolver => resolver.Resolve<SharedDefinitionsService>().CombatVisual, Lifetime.Singleton);
            builder.Register(resolver => resolver.Resolve<SharedDefinitionsService>().WeaponVisuals, Lifetime.Singleton);

            // Gameplay
            builder.Register(resolver => new LevelGrid(resolver.Resolve<LevelData>().Geometry), Lifetime.Singleton);
            builder.Register<DoorSystem>(Lifetime.Singleton);
            builder.Register<LevelPassability>(Lifetime.Singleton);
            builder.Register<OccupancyMap>(Lifetime.Singleton);
            builder.Register<SoundEventBus>(Lifetime.Singleton);
            builder.Register<SpatialQueryService>(Lifetime.Singleton).AsSelf().As<ISpatialQueryService>();
            builder.Register<PlayerHealth>(Lifetime.Singleton);
            builder.Register<PlayerInventory>(Lifetime.Singleton);
            builder.Register<MapSystem>(Lifetime.Singleton);
            // Tick order: weapon switching → attack (stops this tick's movement) → movement → steps → interaction → bullets.
            builder.Register<WeaponSystem>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<PlayerCombat>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<PlayerSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            builder.Register<PlayerFootsteps>(Lifetime.Singleton).As<ILevelTickable>();
            builder.Register<PickupSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<PlayerInteraction>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<BulletSystem>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<ExitSystem>(Lifetime.Singleton);
            builder.Register<NavigationSystem>(Lifetime.Singleton);
            builder.Register<ZombieSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            builder.Register<VisibilitySystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<LevelProgress>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<RouteHintSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            // Stage SpawnZombies, registered after ZombieSystem: every target exists when the first step starts.
            builder.Register<TutorialSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<LevelAnalytics>(Lifetime.Singleton).As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>();
            builder.Register<LevelRuntime>(Lifetime.Singleton);
            builder.Register<PlayerDeathRule>(Lifetime.Singleton);
            builder.RegisterEntryPoint<LevelTickDriver>();

            // Presentation
            builder.RegisterComponent(_viewRoot);
            builder.RegisterComponent(_camera);
            builder.Register<EntityViewRegistry>(Lifetime.Singleton);
            builder.Register<LevelVisualSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<LightFixturesView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<VisibilityController>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<FogOfWarView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<LevelLightMap>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<DoorViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<FrozenDoorsView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            builder.Register<PickupViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<HudPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<MapPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<TutorialPresenter>(Lifetime.Singleton).AsSelf()
                .As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>();
            builder.Register<PlayerViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<PlayerWeaponPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            // After the weapon presenter: the late tick reads the muzzle of the posed gun.
            builder.Register<CombatViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            builder.Register<ZombieViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            // After the gameplay systems and the zombie views: it listens to them and ticks after the simulation.
            builder.Register<LevelAudioPresenter>(Lifetime.Singleton).As<ILevelLoadStep, ILevelTickable>();
            builder.Register<FootprintsView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            // Weather: emitted in the tick (nothing on pause), paused in the late tick.
            builder.Register<SnowfallView>(Lifetime.Singleton).AsSelf()
                .As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>().As<IViewWarmup>();
            builder.Register<BreathView>(Lifetime.Singleton).AsSelf()
                .As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>().As<IViewWarmup>();
            builder.Register<VisionZonesView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            builder.Register<NoiseWavesView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            builder.Register<RouteLineView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            // After the zombie views: shows their hits when they show.
            builder.Register<CombatFeedbackView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            builder.Register<BlobShadows>(Lifetime.Singleton);
            // After the player view: the lantern follows it in the same late tick.
            builder.Register<LevelLighting>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<LevelWarmup>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
        }
    }

    /// <summary>
    /// Drives <see cref="LevelRuntime"/> from the player loop: simulation in Update, view sync in LateUpdate.
    /// The runtime decides whether to tick (not while loading, paused or stopped).
    /// </summary>
    public sealed class LevelTickDriver : ITickable, ILateTickable
    {
        private readonly LevelRuntime _runtime;

        public LevelTickDriver(LevelRuntime runtime)
        {
            _runtime = runtime;
        }

        public void Tick() => _runtime.Tick(Time.deltaTime);

        public void LateTick() => _runtime.LateTick(Time.deltaTime);
    }
}
