using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Animator contract of the player prefab (parameter names). The controller is built by
    /// Maze → Dev → Build Player Animations; <see cref="PlayerViewPresenter"/> drives it. Every parameter is optional:
    /// a controller without one simply does not get it.
    /// </summary>
    public static class PlayerAnimatorParameters
    {
        /// <summary>Float 0..1: movement speed relative to the maximum.</summary>
        public const string Speed = "Speed";
        /// <summary>Int: weapon in hands, see <see cref="WeaponNone"/>, <see cref="WeaponMelee"/>, <see cref="WeaponRanged"/>.</summary>
        public const string Weapon = "Weapon";
        /// <summary>Trigger: melee attack.</summary>
        public const string Attack = "Attack";
        /// <summary>Int: which melee attack animation to play (cycles 0..<see cref="AttackVariants"/>-1).</summary>
        public const string AttackIndex = "AttackIndex";
        /// <summary>Float: playback multiplier of melee attacks, 1 / cooldown (attack states last 1 s at 1).</summary>
        public const string AttackSpeed = "AttackSpeed";
        /// <summary>
        /// Float defaults "AttackContact0".."AttackContact2", never set at runtime: when the weapon hits in melee attack
        /// clip i, as a share of the clip (0..1). Measured by Build Player Animations (fastest right hand movement);
        /// views show the swing and hit reactions then (gameplay damage is applied at once).
        /// </summary>
        public const string AttackContact = "AttackContact";
        /// <summary>
        /// Float defaults "AttackSweep0".."AttackSweep2", never set at runtime: which way the weapon moves when melee
        /// clip i hits, +1 = to the character's right (left to right), -1 = to its left. Measured by Build Player Animations.
        /// </summary>
        public const string AttackSweep = "AttackSweep";
        /// <summary>Trigger: a shot.</summary>
        public const string Shoot = "Shoot";
        /// <summary>Bool: the ranged weapon in hands is automatic.</summary>
        public const string Automatic = "Automatic";
        /// <summary>Bool: the weapon in hands is reloading.</summary>
        public const string Reloading = "Reloading";
        /// <summary>Float: playback multiplier of the reload, 1 / reload time (the reload state lasts 1 s at 1).</summary>
        public const string ReloadSpeed = "ReloadSpeed";
        /// <summary>Trigger: the player took damage.</summary>
        public const string Hit = "Hit";
        /// <summary>Trigger: a successful Interact (door, weapon pickup).</summary>
        public const string Use = "Use";
        /// <summary>Bool: the player is dead.</summary>
        public const string Dead = "Dead";

        public const int WeaponNone = 0;
        public const int WeaponMelee = 1;
        public const int WeaponRanged = 2;

        public const int AttackVariants = 3;

        /// <summary>Layer with reload, hit and use reactions over the upper body.</summary>
        public const string UpperBodyLayer = "UpperBody";

        /// <summary>State tag: the left hand leaves the gun (reload, hit, use) — no left hand IK.</summary>
        public const string NoHandIK = "NoHandIK";
        /// <summary>State tag: no gun pose at all (death) — the clip as is.</summary>
        public const string NoWeaponPose = "NoWeaponPose";

        public static readonly int NoHandIKTag = Animator.StringToHash(NoHandIK);
        public static readonly int NoWeaponPoseTag = Animator.StringToHash(NoWeaponPose);

        public static readonly int SpeedHash = Animator.StringToHash(Speed);
        public static readonly int WeaponHash = Animator.StringToHash(Weapon);
        public static readonly int AttackHash = Animator.StringToHash(Attack);
        public static readonly int AttackIndexHash = Animator.StringToHash(AttackIndex);
        public static readonly int AttackSpeedHash = Animator.StringToHash(AttackSpeed);
        public static readonly int[] AttackContactHashes =
        {
            Animator.StringToHash(AttackContact + 0), Animator.StringToHash(AttackContact + 1), Animator.StringToHash(AttackContact + 2),
        };
        public static readonly int[] AttackSweepHashes =
        {
            Animator.StringToHash(AttackSweep + 0), Animator.StringToHash(AttackSweep + 1), Animator.StringToHash(AttackSweep + 2),
        };
        public static readonly int ShootHash = Animator.StringToHash(Shoot);
        public static readonly int AutomaticHash = Animator.StringToHash(Automatic);
        public static readonly int ReloadingHash = Animator.StringToHash(Reloading);
        public static readonly int ReloadSpeedHash = Animator.StringToHash(ReloadSpeed);
        public static readonly int HitHash = Animator.StringToHash(Hit);
        public static readonly int UseHash = Animator.StringToHash(Use);
        public static readonly int DeadHash = Animator.StringToHash(Dead);
    }
}
