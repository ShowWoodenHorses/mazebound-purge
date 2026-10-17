using System;
using System.Collections.Generic;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Definitions
{
    /// <summary>
    /// A module of a weapon (<see cref="WeaponDefinition.Modules"/>): the weapon is assembled from them in the inspector
    /// (ТЗ: «Контент — это данные»). Exactly one attack (<see cref="WeaponAttack"/>); a ranged attack needs
    /// <see cref="AmmoModule"/>; the rest is optional. The gameplay of each module is written once (PlayerCombat).
    /// </summary>
    [Serializable]
    public abstract class WeaponModule : ContentModule
    {
    }

    /// <summary>How the weapon attacks. Every weapon has exactly one.</summary>
    [Serializable]
    public abstract class WeaponAttack : WeaponModule
    {
        [Tooltip("Seconds between attacks (a stream: between its damage ticks).")]
        [SerializeField, Min(0.02f)] private float _interval = 0.3f;

        [Tooltip("Repeats while the attack is held; otherwise once per press.")]
        [SerializeField] private bool _automatic;

        public float Interval { get => _interval; internal set => _interval = value; }
        public bool Automatic { get => _automatic; internal set => _automatic = value; }

        /// <summary>How far aim assist looks for a target, cells.</summary>
        public abstract float AimRange { get; }

        /// <summary>A close-combat attack: no ammo, hits around the player.</summary>
        public virtual bool IsMelee => false;

        public override void Validate(List<string> problems)
        {
            if (_interval < 0.02f) problems.Add("interval is below 0.02 s.");
        }
    }

    /// <summary>Bullets flying in a straight line: one, or a fan of pellets (shotgun).</summary>
    [Serializable, ModuleInfo("Bullets", Category = "Attack")]
    public sealed class BulletAttack : WeaponAttack
    {
        [Tooltip("Damage of one bullet (one pellet).")]
        [SerializeField, Min(0f)] private float _damage = 10f;
        [Tooltip("Cells per second.")]
        [SerializeField, Min(1f)] private float _speed = 28f;
        [Tooltip("Cells a bullet flies before it disappears.")]
        [SerializeField, Min(0.5f)] private float _range = 14f;
        [Tooltip("Bullets per shot: more than one is a fan of pellets.")]
        [SerializeField, Min(1)] private int _pellets = 1;
        [Tooltip("Degrees. One bullet: random deviation up to ±spread/2. Pellets: the whole fan width.")]
        [SerializeField, Range(0f, 90f)] private float _spread = 2f;
        [Tooltip("Monsters a bullet passes through before it stops (0 = stops at the first).")]
        [SerializeField, Min(0)] private int _pierce;

        public float Damage { get => _damage; internal set => _damage = value; }
        public float Speed { get => _speed; internal set => _speed = value; }
        public float Range { get => _range; internal set => _range = value; }
        public int Pellets { get => _pellets; internal set => _pellets = value; }
        public float Spread { get => _spread; internal set => _spread = value; }
        public int Pierce { get => _pierce; internal set => _pierce = value; }
        public override float AimRange => Mathf.Min(_range, 9f);
    }

    /// <summary>A continuous cone in front of the player (flamethrower): every monster in it takes damage each tick.</summary>
    [Serializable, ModuleInfo("Stream (cone)", Category = "Attack")]
    public sealed class StreamAttack : WeaponAttack
    {
        [Tooltip("Damage per second to every monster in the cone.")]
        [SerializeField, Min(0f)] private float _damagePerSecond = 45f;
        [Tooltip("Cells from the player.")]
        [SerializeField, Min(0.5f)] private float _range = 3.5f;
        [Tooltip("Full width of the cone, degrees.")]
        [SerializeField, Range(5f, 120f)] private float _angle = 30f;

        public StreamAttack()
        {
            Interval = 0.1f;
            Automatic = true;
        }

        public float DamagePerSecond { get => _damagePerSecond; internal set => _damagePerSecond = value; }
        public float Range { get => _range; internal set => _range = value; }
        public float Angle { get => _angle; internal set => _angle = value; }
        public float DamagePerTick => _damagePerSecond * Interval;
        public override float AimRange => _range;
    }

    /// <summary>Lightning that hits the first monster in front and jumps to the nearest ones (tesla).</summary>
    [Serializable, ModuleInfo("Chain lightning", Category = "Attack")]
    public sealed class ChainAttack : WeaponAttack
    {
        [Tooltip("Damage to the first monster.")]
        [SerializeField, Min(0f)] private float _damage = 25f;
        [Tooltip("Reach to the first monster, cells.")]
        [SerializeField, Min(0.5f)] private float _range = 6f;
        [Tooltip("Half-angle of the cone the first monster is looked for in, degrees.")]
        [SerializeField, Range(1f, 90f)] private float _aimAngle = 25f;
        [Tooltip("Jumps after the first monster.")]
        [SerializeField, Min(0)] private int _jumps = 3;
        [Tooltip("Reach of one jump, cells.")]
        [SerializeField, Min(0.5f)] private float _jumpRange = 2.5f;
        [Tooltip("Damage multiplier per jump.")]
        [SerializeField, Range(0f, 1f)] private float _jumpFalloff = 0.75f;

        public float Damage { get => _damage; internal set => _damage = value; }
        public float Range { get => _range; internal set => _range = value; }
        public float AimAngle { get => _aimAngle; internal set => _aimAngle = value; }
        public int Jumps { get => _jumps; internal set => _jumps = value; }
        public float JumpRange { get => _jumpRange; internal set => _jumpRange = value; }
        public float JumpFalloff { get => _jumpFalloff; internal set => _jumpFalloff = value; }
        public override float AimRange => _range;
    }

    /// <summary>
    /// A projectile that explodes on a monster, a wall or at the end of its flight (grenade launcher): damage to every
    /// monster in the radius not hidden behind a wall, less towards the edge.
    /// </summary>
    [Serializable, ModuleInfo("Explosive projectile", Category = "Attack")]
    public sealed class ExplosiveAttack : WeaponAttack
    {
        [Tooltip("Cells per second.")]
        [SerializeField, Min(1f)] private float _speed = 14f;
        [Tooltip("Cells the projectile flies before it explodes by itself.")]
        [SerializeField, Min(0.5f)] private float _range = 10f;
        [Tooltip("Extra damage to the monster hit directly.")]
        [SerializeField, Min(0f)] private float _directDamage = 20f;
        [Tooltip("Damage at the centre of the explosion.")]
        [SerializeField, Min(0f)] private float _explosionDamage = 60f;
        [Tooltip("Radius of the explosion, cells.")]
        [SerializeField, Min(0.3f)] private float _radius = 2f;
        [Tooltip("Share of the damage at the edge of the radius.")]
        [SerializeField, Range(0f, 1f)] private float _edgeDamage = 0.3f;

        public ExplosiveAttack() => Interval = 0.9f;

        public float Speed { get => _speed; internal set => _speed = value; }
        public float Range { get => _range; internal set => _range = value; }
        public float DirectDamage { get => _directDamage; internal set => _directDamage = value; }
        public float ExplosionDamage { get => _explosionDamage; internal set => _explosionDamage = value; }
        public float Radius { get => _radius; internal set => _radius = value; }
        public float EdgeDamage { get => _edgeDamage; internal set => _edgeDamage = value; }
        public override float AimRange => Mathf.Min(_range, 9f);
    }

    /// <summary>A swing: every monster in a sector in front of the player, not behind a wall.</summary>
    [Serializable, ModuleInfo("Melee swing", Category = "Attack")]
    public sealed class MeleeAttack : WeaponAttack
    {
        [SerializeField, Min(0f)] private float _damage = 15f;
        [Tooltip("Reach from the player's centre to the target's edge, cells.")]
        [SerializeField, Min(0.1f)] private float _range = 1.3f;
        [Tooltip("Width of the sector in front of the player, degrees.")]
        [SerializeField, Range(10f, 360f)] private float _arc = 100f;
        [Tooltip("How far a swing is heard by monsters, cells.")]
        [SerializeField, Min(0f)] private float _soundRadius = 4f;

        public MeleeAttack()
        {
            Interval = 0.8f;
            Automatic = true;
        }

        public float Damage { get => _damage; internal set => _damage = value; }
        public float Range { get => _range; internal set => _range = value; }
        public float Arc { get => _arc; internal set => _arc = value; }
        public float SoundRadius { get => _soundRadius; internal set => _soundRadius = value; }
        public override float AimRange => _range;
        public override bool IsMelee => true;
    }

    /// <summary>Ammo type, magazine and reload of a ranged weapon.</summary>
    [Serializable, ModuleInfo("Ammo and magazine", Category = "Ammo")]
    public sealed class AmmoModule : WeaponModule
    {
        [SerializeField] private AmmoDefinition _ammo;
        [SerializeField, Min(1)] private int _magazine = 10;
        [Tooltip("Seconds.")]
        [SerializeField, Min(0f)] private float _reloadTime = 1.5f;
        [Tooltip("Rounds one attack takes (a stream: one damage tick).")]
        [SerializeField, Min(1)] private int _perAttack = 1;
        [Tooltip("The reserve never runs out (the base pistol).")]
        [SerializeField] private bool _infiniteReserve;
        [Tooltip("How far a shot is heard, cells; 0 = the ammo type's.")]
        [SerializeField, Min(0f)] private float _soundRadius;

        public AmmoDefinition Ammo { get => _ammo; internal set => _ammo = value; }
        public int Magazine { get => _magazine; internal set => _magazine = value; }
        public float ReloadTime { get => _reloadTime; internal set => _reloadTime = value; }
        public int PerAttack { get => _perAttack; internal set => _perAttack = value; }
        public bool InfiniteReserve { get => _infiniteReserve; internal set => _infiniteReserve = value; }
        public float SoundRadius { get => _soundRadius; internal set => _soundRadius = value; }

        public override void Validate(List<string> problems)
        {
            if (_ammo == null && !_infiniteReserve) problems.Add("no ammo type.");
            if (_perAttack > _magazine) problems.Add("one attack takes more than the magazine holds.");
        }
    }

    /// <summary>Makes shots quieter (ТЗ «Шум от калибра»: radius of the caliber × silencer).</summary>
    [Serializable, ModuleInfo("Silencer", Category = "Ammo")]
    public sealed class SilencerModule : WeaponModule
    {
        [Tooltip("Multiplier of the shot's sound radius.")]
        [SerializeField, Range(0f, 1f)] private float _noise = 0.4f;

        public float Noise { get => _noise; internal set => _noise = value; }
    }

    /// <summary>
    /// How the weapon slows the player (ТЗ «Подвижность»). Only the weapon in hands counts: switch to the pistol and
    /// run. Without the module the weapon does not slow.
    /// </summary>
    [Serializable, ModuleInfo("Mobility", Category = "Movement")]
    public sealed class MobilityModule : WeaponModule
    {
        [Tooltip("Speed multiplier while the weapon is in hands (pistol 1, rifle 0.9, flamethrower 0.75, minigun 0.6).")]
        [SerializeField, Range(0f, 1.5f)] private float _inHands = 1f;
        [Tooltip("Extra multiplier while firing (minigun in a burst 0.4; 0 = stands while firing).")]
        [SerializeField, Range(0f, 1f)] private float _whileFiring = 1f;

        public float InHands { get => _inHands; internal set => _inHands = value; }
        public float WhileFiring { get => _whileFiring; internal set => _whileFiring = value; }
    }
}
