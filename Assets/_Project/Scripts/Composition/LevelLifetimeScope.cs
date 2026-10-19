using Purge.Application.Platform;
using Purge.Application.Services;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Drops;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Waves;
using Purge.Gameplay.Navigation;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Tutorial;
using Purge.Gameplay.Visibility;
using Purge.Gameplay.Weapons;
using Purge.Gameplay.Zombies;
using Purge.Presentation.Audio;
using Purge.Presentation.Map;
using Purge.Presentation.Stress;
using Purge.Presentation.UI;
using Purge.Presentation.Visual;
using Purge.Presentation.Visual.Horde;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Purge.Composition
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
                Debug.LogWarning("[Purge] LevelLifetimeScope must not auto-run: it is built by LevelSessionFactory.", this);
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
            builder.Register(resolver => resolver.Resolve<SharedDefinitionsService>().Content, Lifetime.Singleton);
            builder.Register(resolver => resolver.Resolve<SharedDefinitionsService>().ItemVisuals, Lifetime.Singleton);

            // Gameplay
            builder.Register(resolver => new LevelGrid(resolver.Resolve<LevelData>().Geometry), Lifetime.Singleton);
            builder.Register<DoorSystem>(Lifetime.Singleton);
            builder.Register<LevelPassability>(Lifetime.Singleton);
            builder.Register<OccupancyMap>(Lifetime.Singleton);
            builder.Register<SoundEventBus>(Lifetime.Singleton);
            builder.Register<SpatialQueryService>(Lifetime.Singleton).AsSelf().As<ISpatialQueryService>();
            builder.Register<PlayerHealth>(Lifetime.Singleton);
            builder.Register<PlayerInventory>(Lifetime.Singleton);
            builder.Register<AmmoStock>(Lifetime.Singleton);
            builder.Register<CombatTargets>(Lifetime.Singleton);
            builder.Register<ThrowableSlot>(Lifetime.Singleton);
            builder.Register<EffectSystem>(Lifetime.Singleton);
            // What monsters may drop: known at load (InitializeSystems), before the views load the items' looks.
            builder.Register<DropPool>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            // Tick order: weapon switching → throwing (a click that throws is not a shot) → attack (sets this tick's aim
            // and speed) → movement → steps → pickups (lifetimes) → interaction → bullets.
            // States on the player first: slow and stun act on this tick's attack and movement.
            builder.Register<PlayerStatusSystem>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<WeaponSystem>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<PlayerThrowing>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<PlayerCombat>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<WeaponFeelSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<PlayerSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            builder.Register<PlayerFootsteps>(Lifetime.Singleton).As<ILevelTickable>();
            builder.Register<PickupSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            builder.Register<PlayerInteraction>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<BulletSystem>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            builder.Register<NavigationSystem>(Lifetime.Singleton);
            builder.Register<ZombieSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            // Mass monsters and the stress test weapons (inactive unless the level has StressTest settings).
            builder.Register<HordeSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            // Abilities of its monsters (stage 5c): casts on the fresh positions, monster projectiles.
            builder.Register<MonsterAbilities>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            // After the horde: sized by it, tick on its fresh buckets (states act on the monsters from the next tick).
            builder.Register<StatusSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            builder.Register<ZoneSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            builder.Register<DropSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<MonsterDeathEffects>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            // Champions' affixes (stage 5e2): trails, death blasts, a hit's state, the vampire.
            builder.Register<ChampionSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            // The run's waves (stage 6): spawn the horde at the level's spawn points after its tick.
            builder.Register<WaveSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            builder.Register<StressWeapons>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable>();
            builder.Register<CombatSandbox>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<VisibilitySystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<LevelProgress>(Lifetime.Singleton).AsSelf().As<ILevelTickable>();
            // Stage SpawnZombies, registered after ZombieSystem: every target exists when the first step starts.
            builder.Register<TutorialSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<LevelAnalytics>(Lifetime.Singleton).As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>();
            builder.Register<LevelRuntime>(Lifetime.Singleton);
            builder.Register<PlayerDeathRule>(Lifetime.Singleton);
            builder.Register<AllKilledRule>(Lifetime.Singleton);
            builder.Register<WavesClearedRule>(Lifetime.Singleton);
            builder.RegisterEntryPoint<LevelTickDriver>();

            // Presentation
            builder.RegisterComponent(_viewRoot);
            builder.RegisterComponent(_camera);
            builder.Register<CameraAimPointer>(Lifetime.Singleton).As<IAimPointer>();
            builder.Register<EntityViewRegistry>(Lifetime.Singleton);
            builder.Register<LevelVisualSystem>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<LightFixturesView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<VisibilityController>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<FogOfWarView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<LevelLightMap>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<DoorViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<FrozenDoorsView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            builder.Register<PickupViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<DropViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            builder.Register<ThrowAimView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<ZoneView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>();
            builder.Register<HudPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<MapPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep>();
            builder.Register<TutorialPresenter>(Lifetime.Singleton).AsSelf()
                .As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>();
            builder.Register<PlayerViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<PlayerWeaponPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            // Pack effects (stage Э) named by content: loaded before the combat views and effects that play them.
            builder.Register<PackEffectsPlayer>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            // Mass effects in pack looks (stage Э2): fire on cells, burning monsters, hit sparks.
            builder.Register<PackEmitters>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, IViewWarmup>();
            // After the weapon presenter: the late tick reads the muzzle of the posed gun.
            builder.Register<CombatViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            builder.Register<ZombieViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<HordeRenderer>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            // Officers, elite, bosses (Animator) and what their abilities show.
            builder.Register<MonsterViewPresenter>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            builder.Register<AbilityWarningView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<MonsterProjectileView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            // Dust of climbing out, puffs of sinking corpses, the rage's flash, what falls onto area strikes (stage Э3).
            builder.Register<MonsterEffectsView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<PlayerStatusView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable>();
            builder.Register<WeaponFeelView>(Lifetime.Singleton).AsSelf()
                .As<ILevelLoadStep, ILevelTickable, ILevelLateTickable, IViewWarmup>();
            builder.Register<WeaponEffectsView>(Lifetime.Singleton).AsSelf()
                .As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>().As<IViewWarmup>();
            builder.Register<StressEffectsView>(Lifetime.Singleton).AsSelf()
                .As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>().As<IViewWarmup>();
            // After the horde views: measures them and drives the stress test overlay.
            builder.Register<StressController>(Lifetime.Singleton).AsSelf()
                .As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>();
            // After the gameplay systems and the zombie views: it listens to them and ticks after the simulation.
            builder.Register<LevelAudioPresenter>(Lifetime.Singleton).As<ILevelLoadStep, ILevelTickable>();
            builder.Register<WeaponAudioPresenter>(Lifetime.Singleton).As<ILevelLoadStep, ILevelTickable>();
            builder.Register<FootprintsView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            // Weather: emitted in the tick (nothing on pause), paused in the late tick.
            builder.Register<SnowfallView>(Lifetime.Singleton).AsSelf()
                .As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>().As<IViewWarmup>();
            builder.Register<BreathView>(Lifetime.Singleton).AsSelf()
                .As<ILevelLoadStep, ILevelTickable, ILevelLateTickable>().As<IViewWarmup>();
            builder.Register<VisionZonesView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
            builder.Register<NoiseWavesView>(Lifetime.Singleton).AsSelf().As<ILevelLoadStep, ILevelLateTickable, IViewWarmup>();
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
