using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Animator contract of zombie prefabs (parameter names). The controller is built by
    /// Maze → Dev → Build Zombie Animations; <see cref="ZombieViewPresenter"/> drives it. Every parameter is optional.
    /// </summary>
    public static class ZombieAnimatorParameters
    {
        /// <summary>Float 0..1: movement this frame relative to the current speed (0 = standing).</summary>
        public const string Speed = "Speed";
        /// <summary>Bool: chasing the player — run instead of walk.</summary>
        public const string Chasing = "Chasing";
        /// <summary>Bool: noticed the player and roars before the chase (ZombieState.Alert).</summary>
        public const string Alert = "Alert";
        /// <summary>Float: playback multiplier of the walk clip (see <see cref="LocomotionAnimation"/>).</summary>
        public const string WalkPlayback = "WalkPlayback";
        /// <summary>Float: playback multiplier of the run clip.</summary>
        public const string RunPlayback = "RunPlayback";
        /// <summary>Float, read-only data: ground speed of the walk clip at the model's scale, m/s (set by the builder).</summary>
        public const string WalkGroundSpeed = "WalkGroundSpeed";
        /// <summary>Float, read-only data: ground speed of the run clip at the model's scale, m/s (set by the builder).</summary>
        public const string RunGroundSpeed = "RunGroundSpeed";
        /// <summary>Float 0 or 1: which idle animation plays.</summary>
        public const string IdleVariant = "IdleVariant";
        /// <summary>Trigger: an attack (a hit on the player).</summary>
        public const string Attack = "Attack";
        /// <summary>Int: which attack animation to play (cycles 0..<see cref="AttackVariants"/>-1).</summary>
        public const string AttackIndex = "AttackIndex";
        /// <summary>Float: playback multiplier of attacks, 1 / attack interval (attack states last 1 s at 1).</summary>
        public const string AttackSpeed = "AttackSpeed";
        /// <summary>Trigger: took damage and survived (no clip yet; a controller may add one).</summary>
        public const string Hit = "Hit";
        /// <summary>Bool: the zombie is dead.</summary>
        public const string Dead = "Dead";

        public const int AttackVariants = 3;
        public const int IdleVariants = 2;

        public static readonly int SpeedHash = Animator.StringToHash(Speed);
        public static readonly int ChasingHash = Animator.StringToHash(Chasing);
        public static readonly int AlertHash = Animator.StringToHash(Alert);
        public static readonly int WalkPlaybackHash = Animator.StringToHash(WalkPlayback);
        public static readonly int RunPlaybackHash = Animator.StringToHash(RunPlayback);
        public static readonly int WalkGroundSpeedHash = Animator.StringToHash(WalkGroundSpeed);
        public static readonly int RunGroundSpeedHash = Animator.StringToHash(RunGroundSpeed);
        public static readonly int IdleVariantHash = Animator.StringToHash(IdleVariant);
        public static readonly int AttackHash = Animator.StringToHash(Attack);
        public static readonly int AttackIndexHash = Animator.StringToHash(AttackIndex);
        public static readonly int AttackSpeedHash = Animator.StringToHash(AttackSpeed);
        public static readonly int HitHash = Animator.StringToHash(Hit);
        public static readonly int DeadHash = Animator.StringToHash(Dead);
    }
}
