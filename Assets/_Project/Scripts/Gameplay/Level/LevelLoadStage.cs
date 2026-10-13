using System.Threading;
using Cysharp.Threading.Tasks;

namespace Maze.Gameplay.Level
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

        public LevelLaunchOptions(int? startIndex = null, int? seed = null)
        {
            StartIndex = startIndex;
            Seed = seed;
        }

        /// <summary>Forced player start (Test from Start #N). Null = random start.</summary>
        public int? StartIndex { get; }

        /// <summary>Seed for gameplay randomness of this run. Null = new seed every run.</summary>
        public int? Seed { get; }
    }

    public enum LevelOutcome
    {
        Completed = 0,
        Failed = 1,
    }
}
