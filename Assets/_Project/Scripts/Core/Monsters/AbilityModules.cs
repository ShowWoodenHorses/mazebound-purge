using System;
using System.Collections.Generic;
using Purge.Core.Effects;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Monsters
{
    /// <summary>How the warning of an ability looks on the floor (ТЗ: опасная способность сначала показывает зону/линию).</summary>
    public enum AbilityWarningShape
    {
        /// <summary>Lines from the monster along the shot (one per projectile of a fan).</summary>
        Line = 0,
        /// <summary>A circle where it lands.</summary>
        Circle = 1,
        /// <summary>A sector in front of the monster.</summary>
        Sector = 2,
        /// <summary>Nothing on the floor (the rage: the monster roars).</summary>
        None = 3,
        /// <summary>Small circles on the spots it acts on (the corpses a raise lifts).</summary>
        Points = 4,
    }

    /// <summary>
    /// An ability of a monster (ТЗ: «способности — модули с перезарядкой и условиями»; one system for monsters and
    /// bosses). The monster casts it when it chases the player, the cooldown is over and the conditions hold (distance,
    /// line of sight, HP); then it stands and the warning shows for <see cref="Warning"/> seconds — aimed where the player
    /// was when the cast began, so it can be dodged — and the ability goes off. Order in the list = priority.
    /// </summary>
    [Serializable]
    public abstract class AbilityModule : ContentModule
    {
        [Tooltip("Seconds between casts (from the moment it goes off).")]
        [SerializeField, Min(0.1f)] private float _cooldown = 3f;
        [Tooltip("Seconds after the monster noticed the player before the first cast.")]
        [SerializeField, Min(0f)] private float _firstDelay = 1f;
        [Tooltip("Casts only when the player is at least this far, cells.")]
        [SerializeField, Min(0f)] private float _minRange;
        [Tooltip("Casts only when the player is at most this far, cells.")]
        [SerializeField, Min(0.5f)] private float _maxRange = 8f;
        [Tooltip("Casts only when nothing that stops bullets is between the monster and the player.")]
        [SerializeField] private bool _needsSight = true;
        [Tooltip("Casts only when the monster's HP is at most this share (1 = always).")]
        [SerializeField, Range(0.05f, 1f)] private float _healthBelow = 1f;
        [Tooltip("Seconds the warning shows before it goes off (ТЗ: 0.5–1).")]
        [SerializeField, Range(0.2f, 2f)] private float _warning = 0.8f;
        [Tooltip("Seconds the monster still stands after it went off.")]
        [SerializeField, Min(0f)] private float _recovery = 0.35f;
        [Tooltip("Played where the monster stands when the cast begins (the warning): a roar, a spell. Folders Sounds/Monster.")]
        [SerializeField, Audio.MonsterSound] private string _castSound;
        [Tooltip("Played when it goes off: a shot, a spit. Folders Sounds/Monster.")]
        [SerializeField, Audio.MonsterSound] private string _releaseSound;

        public string CastSound { get => _castSound; internal set => _castSound = value; }
        public string ReleaseSound { get => _releaseSound; internal set => _releaseSound = value; }
        public float Cooldown { get => _cooldown; internal set => _cooldown = value; }
        public float FirstDelay { get => _firstDelay; internal set => _firstDelay = value; }
        public float MinRange { get => _minRange; internal set => _minRange = value; }
        public float MaxRange { get => _maxRange; internal set => _maxRange = value; }
        public bool NeedsSight { get => _needsSight; internal set => _needsSight = value; }
        public float HealthBelow { get => _healthBelow; internal set => _healthBelow = value; }
        public float Warning { get => _warning; internal set => _warning = value; }
        public float Recovery { get => _recovery; internal set => _recovery = value; }

        public abstract AbilityWarningShape Shape { get; }
    }

    /// <summary>How a projectile ability flies.</summary>
    public enum ProjectileForm
    {
        /// <summary>One projectile in a straight line (an arrow).</summary>
        Straight = 0,
        /// <summary>Over everything along an arc into the cell where the player stood (spit): the effects happen there.</summary>
        Arc = 1,
        /// <summary>Several straight ones in a fan (icicles).</summary>
        Fan = 2,
    }

    /// <summary>
    /// Shoots (ТЗ «снаряд: прямой / дугой / веером»). Straight projectiles fly over monsters and stop at what stops
    /// bullets or at the player: damage, a state, then the impact effects where they ended. An arc lands in the cell the
    /// player stood in when the cast began (walls cut the aim short): damage to the player in
    /// <see cref="ArcRadius"/>, then the impact effects (an acid puddle). Projectiles cannot be shot down — only dodged.
    /// </summary>
    [Serializable, ModuleInfo("Projectile", Category = "Ability", AllowMultiple = true)]
    public sealed class ProjectileAbility : AbilityModule
    {
        [SerializeField] private ProjectileForm _form;
        [Tooltip("Fan: projectiles in the fan.")]
        [SerializeField, Range(1, 9)] private int _count = 3;
        [Tooltip("Fan: degrees between the outer projectiles.")]
        [SerializeField, Range(5f, 120f)] private float _fanAngle = 30f;
        [Tooltip("Cells per second.")]
        [SerializeField, Min(1f)] private float _speed = 12f;
        [Tooltip("Damage to the player hit.")]
        [SerializeField, Min(0f)] private float _damage = 15f;
        [Tooltip("Straight and fan: how far it flies, cells.")]
        [SerializeField, Min(1f)] private float _range = 12f;
        [Tooltip("Arc: the player this close to where it lands takes the damage, cells.")]
        [SerializeField, Min(0.2f)] private float _arcRadius = 0.9f;
        [Tooltip("Arc: height of the arc, × its length.")]
        [SerializeField, Range(0f, 1f)] private float _arcHeight = 0.3f;
        [Tooltip("A state put on the player hit (none = only damage).")]
        [SerializeField] private StatusDefinition _status;
        [SerializeField, Min(0.1f)] private float _statusDuration = 2f;
        [Tooltip("What happens where it ends (a puddle of acid…); its zones are the monsters' — they hurt the player too.")]
        [SerializeReference, ModuleList] private List<EffectModule> _impact = new List<EffectModule>();

        public ProjectileForm Form { get => _form; internal set => _form = value; }
        public int Count { get => _form == ProjectileForm.Fan ? Mathf.Max(1, _count) : 1; internal set => _count = value; }
        public float FanAngle { get => _fanAngle; internal set => _fanAngle = value; }
        public float Speed { get => _speed; internal set => _speed = value; }
        public float Damage { get => _damage; internal set => _damage = value; }
        public float Range { get => _range; internal set => _range = value; }
        public float ArcRadius { get => _arcRadius; internal set => _arcRadius = value; }
        public float ArcHeight { get => _arcHeight; internal set => _arcHeight = value; }
        public StatusDefinition Status { get => _status; internal set => _status = value; }
        public float StatusDuration { get => _statusDuration; internal set => _statusDuration = value; }
        public IReadOnlyList<EffectModule> Impact => _impact;

        public override AbilityWarningShape Shape => _form == ProjectileForm.Arc ? AbilityWarningShape.Circle : AbilityWarningShape.Line;

        /// <summary>Radius of the landing circle the warning shows (the larger of the damage and the effects).</summary>
        public float LandingRadius
        {
            get
            {
                var radius = _arcRadius;
                foreach (var effect in _impact)
                    if (effect != null) radius = Mathf.Max(radius, effect.Radius);
                return radius;
            }
        }

        internal void SetImpact(params EffectModule[] effects) => _impact = new List<EffectModule>(effects);

        public override void Validate(List<string> problems)
        {
            Purge.Core.Modules.Modules.Validate(_impact, "impact", problems);
        }
    }

    /// <summary>
    /// A blow in front of the monster (ТЗ «удар»): after the warning sector, the player inside it and not behind a wall
    /// takes the damage and the state.
    /// </summary>
    [Serializable, ModuleInfo("Strike", Category = "Ability", AllowMultiple = true)]
    public sealed class StrikeAbility : AbilityModule
    {
        [Tooltip("Reach of the blow from the monster's centre, cells.")]
        [SerializeField, Min(0.5f)] private float _reach = 2f;
        [Tooltip("Width of the sector, degrees (360 = all around).")]
        [SerializeField, Range(10f, 360f)] private float _angle = 90f;
        [SerializeField, Min(0f)] private float _damage = 25f;
        [SerializeField] private StatusDefinition _status;
        [SerializeField, Min(0.1f)] private float _statusDuration = 1f;

        public float Reach { get => _reach; internal set => _reach = value; }
        public float Angle { get => _angle; internal set => _angle = value; }
        public float Damage { get => _damage; internal set => _damage = value; }
        public StatusDefinition Status { get => _status; internal set => _status = value; }
        public float StatusDuration { get => _statusDuration; internal set => _statusDuration = value; }

        public override AbilityWarningShape Shape => AbilityWarningShape.Sector;
    }

    /// <summary>
    /// A charge (ТЗ «рывок»; the Brute's run-up): after the warning line it rushes along it — up to
    /// <see cref="Distance"/>, stopping at a wall or a closed door. The player it runs into takes the damage and is
    /// knocked away (no stun); the rank-and-file in its way are shoved aside unhurt (the crowd impulse).
    /// </summary>
    [Serializable, ModuleInfo("Charge", Category = "Ability", AllowMultiple = true)]
    public sealed class ChargeAbility : AbilityModule
    {
        [Tooltip("How far it rushes at most, cells.")]
        [SerializeField, Min(1f)] private float _distance = 7f;
        [Tooltip("Cells per second while it rushes.")]
        [SerializeField, Min(1f)] private float _speed = 10f;
        [SerializeField, Min(0f)] private float _damage = 20f;
        [Tooltip("How far the player it hit is knocked, cells.")]
        [SerializeField, Min(0f)] private float _knockback = 2f;
        [Tooltip("How hard the monsters in its way are shoved aside, cells per second (no damage).")]
        [SerializeField, Min(0f)] private float _crowdPush = 7f;

        public float Distance { get => _distance; internal set => _distance = value; }
        public float Speed { get => _speed; internal set => _speed = value; }
        public float Damage { get => _damage; internal set => _damage = value; }
        public float Knockback { get => _knockback; internal set => _knockback = value; }
        public float CrowdPush { get => _crowdPush; internal set => _crowdPush = value; }

        public override AbilityWarningShape Shape => AbilityWarningShape.Line;
    }

    /// <summary>
    /// A shove all around (ТЗ «отталкивание»): after the warning circle around the monster, the player inside it and not
    /// behind a wall takes the damage and the state and is knocked away from the monster.
    /// </summary>
    [Serializable, ModuleInfo("Push", Category = "Ability", AllowMultiple = true)]
    public sealed class PushAbility : AbilityModule
    {
        [Tooltip("Reach from the monster's centre, cells.")]
        [SerializeField, Min(0.5f)] private float _radius = 2.2f;
        [SerializeField, Min(0f)] private float _damage = 10f;
        [Tooltip("How far the player is knocked, cells.")]
        [SerializeField, Min(0f)] private float _knockback = 2.5f;
        [SerializeField] private StatusDefinition _status;
        [SerializeField, Min(0.1f)] private float _statusDuration = 1f;

        public float Radius { get => _radius; internal set => _radius = value; }
        public float Damage { get => _damage; internal set => _damage = value; }
        public float Knockback { get => _knockback; internal set => _knockback = value; }
        public StatusDefinition Status { get => _status; internal set => _status = value; }
        public float StatusDuration { get => _statusDuration; internal set => _statusDuration = value; }

        public override AbilityWarningShape Shape => AbilityWarningShape.Circle;
    }

    /// <summary>
    /// A blow on an area after a delay (ТЗ «удар по площади с задержкой»; the Shaman's magic): the warning circle shows
    /// under the player for <see cref="AbilityModule.Warning"/> s — step out of it; then the player inside it and not
    /// behind a wall takes the damage and the state, and the impact effects happen at its centre (hostile: an explosion
    /// hurts the player too, a zone is the monsters'). The biome's flavour is the data: a fireball is an explosion, a
    /// poison cloud a zone, a freeze a state.
    /// </summary>
    [Serializable, ModuleInfo("Area strike", Category = "Ability", AllowMultiple = true)]
    public sealed class AreaStrikeAbility : AbilityModule
    {
        [Tooltip("The player this close to the centre takes the damage and the state, cells.")]
        [SerializeField, Min(0.3f)] private float _radius = 1.6f;
        [SerializeField, Min(0f)] private float _damage;
        [SerializeField] private StatusDefinition _status;
        [SerializeField, Min(0.1f)] private float _statusDuration = 1f;
        [Tooltip("What happens at the centre (an explosion, a zone…); hostile — its explosion and zones hurt the player.")]
        [SerializeReference, ModuleList] private List<EffectModule> _impact = new List<EffectModule>();
        [Tooltip("Look only (stage Э3): an effect of Art/Effects that falls from above onto the centre at the end of the " +
                 "warning — a fireball, a blob of poison, a lump of ice; empty = nothing falls.")]
        [SerializeField] private UnityEngine.AddressableAssets.AssetReferenceGameObject _fall;
        [Tooltip("Seconds it falls: the last part of the warning.")]
        [SerializeField, Range(0.1f, 1.5f)] private float _fallTime = 0.45f;

        public float Radius { get => _radius; internal set => _radius = value; }
        public float Damage { get => _damage; internal set => _damage = value; }
        public StatusDefinition Status { get => _status; internal set => _status = value; }
        public float StatusDuration { get => _statusDuration; internal set => _statusDuration = value; }
        public IReadOnlyList<EffectModule> Impact => _impact;
        public UnityEngine.AddressableAssets.AssetReferenceGameObject Fall { get => _fall; internal set => _fall = value; }
        public float FallTime { get => _fallTime; internal set => _fallTime = value; }

        /// <summary>Radius of the warning circle (the larger of the blow and the effects).</summary>
        public float WarningRadius
        {
            get
            {
                var radius = _radius;
                foreach (var effect in _impact)
                    if (effect != null) radius = Mathf.Max(radius, effect.Radius);
                return radius;
            }
        }

        internal void SetImpact(params EffectModule[] effects) => _impact = new List<EffectModule>(effects);

        public override AbilityWarningShape Shape => AbilityWarningShape.Circle;

        public override void Validate(List<string> problems)
        {
            Purge.Core.Modules.Modules.Validate(_impact, "impact", problems);
        }
    }

    /// <summary>
    /// Raises the fallen (ТЗ: Шаман поднимает трупы): the rank-and-file and officers that died within
    /// <see cref="Radius"/> of the monster in the last <see cref="MaxAge"/> s get up again where they fell — the same
    /// monsters, at most <see cref="Count"/> a cast, the nearest first. No fallen near — no cast. The warning marks the
    /// spots; they climb out for <see cref="RiseTime"/> s (they can be hit meanwhile) and chase the player at once.
    /// </summary>
    [Serializable, ModuleInfo("Raise the dead", Category = "Ability", AllowMultiple = false)]
    public sealed class RaiseAbility : AbilityModule
    {
        public const int MaxCount = 5;

        [Tooltip("Lifts the fallen this close to the monster, cells.")]
        [SerializeField, Min(1f)] private float _radius = 7f;
        [Tooltip("At most this many a cast (ТЗ: не больше 5 за раз).")]
        [SerializeField, Range(1, MaxCount)] private int _count = MaxCount;
        [Tooltip("Only those that died at most this many seconds ago.")]
        [SerializeField, Min(1f)] private float _maxAge = 20f;
        [Tooltip("Seconds they climb out before they walk.")]
        [SerializeField, Min(0.2f)] private float _riseTime = 2f;

        public float Radius { get => _radius; internal set => _radius = value; }
        public int Count { get => Mathf.Clamp(_count, 1, MaxCount); internal set => _count = value; }
        public float MaxAge { get => _maxAge; internal set => _maxAge = value; }
        public float RiseTime { get => _riseTime; internal set => _riseTime = value; }

        public override AbilityWarningShape Shape => AbilityWarningShape.Points;
    }

    /// <summary>
    /// Rage (ТЗ «ярость по порогу HP»; the Brute, later bosses): once the HP falls to <see cref="AbilityModule.HealthBelow"/>
    /// (30 % by default) the monster stops and roars for the warning, then deals × <see cref="DamageFactor"/> damage
    /// (its attacks and abilities) and moves × <see cref="SpeedFactor"/> until it dies. Once per life.
    /// </summary>
    [Serializable, ModuleInfo("Rage", Category = "Ability", AllowMultiple = false)]
    public sealed class RageAbility : AbilityModule
    {
        [Tooltip("× the damage of its attacks and abilities once enraged.")]
        [SerializeField, Range(1f, 4f)] private float _damageFactor = 1.5f;
        [Tooltip("× its speed once enraged (1 = as before).")]
        [SerializeField, Range(0.5f, 2f)] private float _speedFactor = 1f;

        public RageAbility()
        {
            HealthBelow = 0.3f;
            NeedsSight = false;
            FirstDelay = 0f;
            MaxRange = 99f;
            Warning = 1f;
            Recovery = 0.2f;
        }

        public float DamageFactor { get => _damageFactor; internal set => _damageFactor = value; }
        public float SpeedFactor { get => _speedFactor; internal set => _speedFactor = value; }

        public override AbilityWarningShape Shape => AbilityWarningShape.None;
    }
}
