using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Boosters;
using Purge.Core.Definitions;

namespace Purge.Gameplay.Level
{
    /// <summary>Level loading stages after the LevelScope is created, in ТЗ §9 order.</summary>
    public enum LevelLoadStage
    {
        BuildRuntime = 0,
        InitializeSystems = 1,
        BuildNavigation = 2,
        InitializeVisibility = 3,
        BuildVisuals = 4,
        InitializeUI = 5,
        SpawnPlayer = 6,
        SpawnZombies = 7,

        /// <summary>Last: everything is created; first-draw costs are paid while the loading screen is shown.</summary>
        Warmup = 8,
    }

    /// <summary>
    /// A piece of level initialization registered in the LevelScope. <see cref="LevelRuntime"/> runs all steps
    /// ordered by <see cref="Stage"/>, then by registration order. Any layer may contribute a step
    /// (e.g. Presentation builds visuals), Gameplay only knows the contract.
    /// </summary>
    public interface ILevelLoadStep
    {
        LevelLoadStage Stage { get; }

        UniTask ExecuteAsync(CancellationToken cancellation);
    }

    /// <summary>Per-frame level simulation. Ticked by <see cref="LevelRuntime"/> only while gameplay runs (not paused).</summary>
    public interface ILevelTickable
    {
        void Tick(float deltaTime);
    }

    /// <summary>
    /// Per-frame sync of views with simulation state (transforms, animators, camera). Runs after every
    /// <see cref="ILevelTickable"/> of the frame, also while paused (nothing moves then, but views stay consistent).
    /// </summary>
    public interface ILevelLateTickable
    {
        void LateTick(float deltaTime);
    }

    /// <summary>How a level is started. Default: random player start (ТЗ §61).</summary>
    public sealed class LevelLaunchOptions
    {
        public static readonly LevelLaunchOptions Default = new LevelLaunchOptions();

        private static readonly IReadOnlyList<BoosterDefinition> NoBoosters = Array.Empty<BoosterDefinition>();

        public LevelLaunchOptions(int? startIndex = null, int? seed = null, Core.Monsters.DifficultyDefinition difficulty = null,
            Run.RunSnapshot resume = null, Balance.BotRun bot = null, WeaponDefinition startWeapon = null,
            ThrowableDefinition startThrowable = null, IReadOnlyList<BoosterDefinition> boosters = null, bool closeArena = true)
        {
            StartIndex = startIndex;
            Seed = resume != null ? resume.Seed : seed;
            Difficulty = difficulty;
            Resume = resume;
            Bot = bot;
            StartWeapon = startWeapon;
            StartThrowable = startThrowable;
            Boosters = boosters ?? NoBoosters;
            CloseArena = closeArena;
        }

        /// <summary>The balance bot plays this run (stage 10a) and writes its statistics there; null — the player.</summary>
        public Balance.BotRun Bot { get; }

        /// <summary>A saved run to continue (stage 6e): its seed, state and the wave to start with; null = a new run.</summary>
        public Run.RunSnapshot Resume { get; }

        /// <summary>Forced player start (Test from Start #N). Null = random start.</summary>
        public int? StartIndex { get; }

        /// <summary>Seed for gameplay randomness of this run. Null = new seed every run.</summary>
        public int? Seed { get; }

        /// <summary>Difficulty of the run (stage 6); null = the level horde's own.</summary>
        public Core.Monsters.DifficultyDefinition Difficulty { get; }

        /// <summary>The weapon of slot 1 a new run starts with (stage 11б, the preparation); null = the basic pistol only.</summary>
        public WeaponDefinition StartWeapon { get; }

        /// <summary>The throwable a new run starts with (one; stage 11б); null = none.</summary>
        public ThrowableDefinition StartThrowable { get; }

        /// <summary>
        /// Boosters of the run (stage 11б): a new run applies them at its start, a continued one keeps what lasts (the
        /// medkit at the start, free rerolls left come from its save).
        /// </summary>
        public IReadOnlyList<BoosterDefinition> Boosters { get; }

        /// <summary>
        /// A boss wave closes the arena with gates once the player is inside (stage 8a; the choice «Закрытая арена» of
        /// stage 11б, from the normal difficulty). On by default: development launches keep the arena closed.
        /// </summary>
        public bool CloseArena { get; }

        /// <summary>The same launch with another seed.</summary>
        public LevelLaunchOptions WithSeed(int seed) =>
            new LevelLaunchOptions(StartIndex, seed, Difficulty, Resume, Bot, StartWeapon, StartThrowable, Boosters, CloseArena);

        /// <summary>The first booster of the run with a module of this kind.</summary>
        public bool TryGetBoost<T>(out T module) where T : BoosterModule
        {
            foreach (var booster in Boosters)
                if (booster != null && booster.TryGet(out module))
                    return true;
            module = null;
            return false;
        }
    }

    public enum LevelOutcome
    {
        Completed = 0,
        Failed = 1,
    }
}
