using System.Collections.Generic;
using Purge.Core.Definitions;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Core.Visual
{
    /// <summary>
    /// Shared combat visuals, Addressable at <see cref="Address"/>. Display only (ТЗ §79): prefabs are pooled and
    /// never decide hits. Any reference may be empty — that effect is simply not shown. Effect prefabs face +Z; their
    /// particle systems are restarted on every use (a <c>CombatEffect</c> component, if any, sets how long one lasts).
    /// </summary>
    [CreateAssetMenu(fileName = "CombatVisual", menuName = "Purge/Visual/Combat Visual")]
    public sealed class CombatVisualDefinition : ScriptableObject
    {
        public const string Address = "Combat/Visual";

        [Tooltip("Flying bullet (tracer), oriented along +Z, pivot at its head. With Tracer Length > 0 it is modelled " +
                 "1 unit long behind the pivot (-Z) and stretched to the visible length; otherwise it is not scaled.")]
        [SerializeField] private AssetReferenceGameObject _bullet;

        [Tooltip("Longest visible tracer, cells. 0 = the bullet prefab is shown as is.")]
        [SerializeField, Min(0f)] private float _tracerLength = 1.2f;

        [Tooltip("Flash at the gun's muzzle on a shot, facing along the barrel (+Z).")]
        [SerializeField] private AssetReferenceGameObject _muzzleFlash;

        [Tooltip("A spent shell thrown out of the gun on a shot: +Z = the gun's right side, +Y = up.")]
        [SerializeField] private AssetReferenceGameObject _shellCasing;

        [Tooltip("Where a bullet stops at a wall or closed door: +Z = out of the wall.")]
        [SerializeField] private AssetReferenceGameObject _impact;

        [Tooltip("A hit on a zombie (bullet or melee), +Z = towards the attacker.")]
        [SerializeField] private AssetReferenceGameObject _targetHit;

        [Tooltip("Melee swing over the attack sector, +Z = the attack direction. A SwingArc component sizes and " +
                 "animates it to the weapon's arc and reach.")]
        [SerializeField] private AssetReferenceGameObject _meleeSwing;

        [Tooltip("Seconds an effect without a CombatEffect component stays before returning to the pool.")]
        [SerializeField, Min(0.02f)] private float _effectDuration = 0.15f;

        [Header("Model flash when hit (Purge/Lit materials)")]
        [Tooltip("Zombie hit: colour, alpha = strength.")]
        [SerializeField] private Color _hitFlashColor = new Color(1f, 1f, 1f, 0.85f);

        [Tooltip("Player hit: colour, alpha = strength.")]
        [SerializeField] private Color _playerHitFlashColor = new Color(1f, 0.25f, 0.2f, 0.7f);

        [SerializeField, Min(0.02f)] private float _hitFlashTime = 0.15f;

        [Header("Throwing and zones")]
        [Tooltip("Dots of the throw arc (Purge/Particle, a soft round texture), drawn over everything.")]
        [SerializeField] private Material _throwArc;

        [Tooltip("Ring on the ground where the throwable will land, as wide as its effect (Purge/Ring).")]
        [SerializeField] private Material _throwTarget;

        [Tooltip("Cells of a zone (Purge/FireCells: its colours come from the zone definition).")]
        [SerializeField] private Material _zoneCells;

        [Header("Health bars and numbers")]
        [SerializeField] private CombatFeedback _feedback = new CombatFeedback();

        [Header("Feel (stage 4в)")]
        [Tooltip("Splashes of a monster's colour on a hit and a kill (particles, tinted per monster).")]
        [SerializeField] private Material _splash;
        [Tooltip("Ring under a monster hit (particle lying on the ground).")]
        [SerializeField] private Material _hitRing;

        [Tooltip("Bolts of chain lightning (a dynamic mesh of quads).")]
        [SerializeField] private Material _boltMaterial;
        [Tooltip("Particles of streams, explosions, sparks on the horde, burning and stunned monsters (additive).")]
        [SerializeField] private Material _effectParticles;

        [Header("Pack effects (stage Э): defaults of content without its own look")]
        [Tooltip("Muzzle flash by the weapon's hold; a hold not listed — Muzzle Flash above.")]
        [SerializeField] private List<HoldEffect> _muzzleFlashes = new List<HoldEffect>();
        [Tooltip("Explosion with a radius below Medium Explosion Radius (scaled to the radius).")]
        [SerializeField] private AssetReferenceGameObject _explosionSmall;
        [Tooltip("Stage Э3: explosion with a radius from Medium to Large Explosion Radius; empty = the small one. Each size " +
                 "has its own effect — one effect grown to a big radius looked like a small one magnified.")]
        [SerializeField] private AssetReferenceGameObject _explosionMedium;
        [SerializeField, Min(0.3f)] private float _mediumExplosionRadius = 1.75f;
        [Tooltip("Explosion with a radius from Large Explosion Radius (scaled to the radius); empty = the medium one.")]
        [SerializeField] private AssetReferenceGameObject _explosionLarge;
        [SerializeField, Min(0.3f)] private float _largeExplosionRadius = 2.25f;

        [Header("Pack effects (stage Э2): weapons")]
        [Tooltip("Stream (cone) of a weapon without its own: a looping effect at the muzzle while it fires, fitted to its " +
                 "range; empty = the draft flame.")]
        [SerializeField] private AssetReferenceGameObject _stream;
        [Tooltip("Flash on every monster a chain lightning hits.")]
        [SerializeField] private AssetReferenceGameObject _chainHit;
        [Tooltip("Bolts of chain lightning from a pack texture (a bolt drawn along V, Purge/Particle): one quad per jump, " +
                 "mirrored at random every frame; empty = the draft jagged bolt (Bolt Material).")]
        [SerializeField] private Material _chainBolt;
        [Tooltip("Width of a textured bolt, cells.")]
        [SerializeField, Min(0.05f)] private float _chainBoltWidth = 0.6f;
        [Tooltip("Smoke out of the muzzle when a burst of bullets ends.")]
        [SerializeField] private AssetReferenceGameObject _barrelSmoke;
        [Tooltip("Shots in a burst before it smokes (a shotgun's shot counts as many as its pellets).")]
        [SerializeField, Min(1)] private int _barrelSmokeShots = 3;
        [Tooltip("Sparks where the horde is hit: an effect the game emits from (many hits a second — no instances); " +
                 "empty = the draft sparks.")]
        [SerializeField] private AssetReferenceGameObject _hitSparks;
        [Tooltip("Particles per hit and per kill.")]
        [SerializeField, Min(0)] private int _hitSparksCount = 2;
        [SerializeField, Min(0)] private int _killSparksCount = 5;

        [Header("Monsters' abilities (stage 5c)")]
        [Tooltip("Warnings of abilities on the floor: lines, circles, sectors (Purge/Warning).")]
        [SerializeField] private Material _abilityWarning;
        [Tooltip("Ring under the player while a state holds them or the immunity to control lasts (Purge/Ring).")]
        [SerializeField] private Material _statusRing;

        [Header("Pack effects (stage Э3): monsters")]
        [Tooltip("Dust and earth around a monster climbing out of the ground: an effect the game emits from (a wave " +
                 "climbs out at once — no instances); empty = none.")]
        [SerializeField] private AssetReferenceGameObject _emerge;
        [Tooltip("Particles a second around one climbing monster.")]
        [SerializeField, Min(0f)] private float _emergeRate = 6f;
        [Tooltip("Puff where a corpse sinks into the ground at the end of its time (emitted from); empty = none.")]
        [SerializeField] private AssetReferenceGameObject _corpsePuff;
        [SerializeField, Min(0f)] private float _corpsePuffCount = 4f;
        [Tooltip("Seconds a corpse takes to sink at the end of its time (it lies still before).")]
        [SerializeField, Range(0.05f, 2f)] private float _corpseSinkTime = 0.4f;
        [Tooltip("How deep a corpse sinks, metres (× the monster's size).")]
        [SerializeField, Range(0.1f, 3f)] private float _corpseSinkDepth = 0.9f;
        [Tooltip("Flash on a monster when its rage begins (one-shot).")]
        [SerializeField] private AssetReferenceGameObject _rageFlash;
        [Tooltip("Glow of an enraged monster until it dies, sRGB; alpha = strength (over a champion's glow).")]
        [SerializeField] private Color _rageGlow = new Color(1f, 0.15f, 0.05f, 0.85f);
        [Tooltip("Height of the stars circling a stunned monster's head, × its size (Status.Particles of a stun).")]
        [SerializeField, Min(0.3f)] private float _stunHeight = 1.45f;

        public AssetReferenceGameObject Emerge { get => _emerge; internal set => _emerge = value; }
        public float EmergeRate { get => _emergeRate; internal set => _emergeRate = value; }
        public AssetReferenceGameObject CorpsePuff { get => _corpsePuff; internal set => _corpsePuff = value; }
        public float CorpsePuffCount { get => _corpsePuffCount; internal set => _corpsePuffCount = value; }
        public float CorpseSinkTime { get => _corpseSinkTime; internal set => _corpseSinkTime = value; }
        public float CorpseSinkDepth { get => _corpseSinkDepth; internal set => _corpseSinkDepth = value; }
        public AssetReferenceGameObject RageFlash { get => _rageFlash; internal set => _rageFlash = value; }
        public Color RageGlow { get => _rageGlow; internal set => _rageGlow = value; }
        public float StunHeight { get => _stunHeight; internal set => _stunHeight = value; }

        /// <summary>
        /// How far a corpse has sunk, 0..1, <paramref name="sinceDeath"/> s after death with <paramref name="corpseTime"/>
        /// to lie: still until the last <see cref="CorpseSinkTime"/>, then down.
        /// </summary>
        public float CorpseSink(float sinceDeath, float corpseTime)
        {
            var sink = Mathf.Min(_corpseSinkTime, corpseTime * 0.5f);
            return sink <= 0f ? 0f : Mathf.Clamp01((sinceDeath - (corpseTime - sink)) / sink);
        }

        public Material AbilityWarning { get => _abilityWarning; internal set => _abilityWarning = value; }
        public Material StatusRing { get => _statusRing; internal set => _statusRing = value; }
        public Material BoltMaterial { get => _boltMaterial; internal set => _boltMaterial = value; }
        public Material EffectParticles { get => _effectParticles; internal set => _effectParticles = value; }
        public Material Splash { get => _splash; internal set => _splash = value; }
        public Material HitRing { get => _hitRing; internal set => _hitRing = value; }

        public AssetReferenceGameObject Bullet { get => _bullet; internal set => _bullet = value; }
        public float TracerLength { get => _tracerLength; internal set => _tracerLength = value; }
        public AssetReferenceGameObject MuzzleFlash { get => _muzzleFlash; internal set => _muzzleFlash = value; }
        public AssetReferenceGameObject ShellCasing { get => _shellCasing; internal set => _shellCasing = value; }
        public AssetReferenceGameObject Impact { get => _impact; internal set => _impact = value; }
        public AssetReferenceGameObject TargetHit { get => _targetHit; internal set => _targetHit = value; }
        public AssetReferenceGameObject MeleeSwing { get => _meleeSwing; internal set => _meleeSwing = value; }
        public float EffectDuration => _effectDuration;
        public Color HitFlashColor => _hitFlashColor;
        public Color PlayerHitFlashColor => _playerHitFlashColor;
        public float HitFlashTime => _hitFlashTime;
        public Material ThrowArc { get => _throwArc; internal set => _throwArc = value; }
        public Material ThrowTarget { get => _throwTarget; internal set => _throwTarget = value; }
        public Material ZoneCells { get => _zoneCells; internal set => _zoneCells = value; }
        public CombatFeedback Feedback => _feedback ??= new CombatFeedback();

        public List<HoldEffect> MuzzleFlashes => _muzzleFlashes;
        public AssetReferenceGameObject ExplosionSmall { get => _explosionSmall; internal set => _explosionSmall = value; }
        public AssetReferenceGameObject ExplosionMedium { get => _explosionMedium; internal set => _explosionMedium = value; }
        public float MediumExplosionRadius { get => _mediumExplosionRadius; internal set => _mediumExplosionRadius = value; }
        public AssetReferenceGameObject ExplosionLarge { get => _explosionLarge; internal set => _explosionLarge = value; }
        public float LargeExplosionRadius { get => _largeExplosionRadius; internal set => _largeExplosionRadius = value; }

        public AssetReferenceGameObject Stream { get => _stream; internal set => _stream = value; }
        public AssetReferenceGameObject ChainHit { get => _chainHit; internal set => _chainHit = value; }
        public Material ChainBolt { get => _chainBolt; internal set => _chainBolt = value; }
        public float ChainBoltWidth { get => _chainBoltWidth; internal set => _chainBoltWidth = value; }
        public AssetReferenceGameObject BarrelSmoke { get => _barrelSmoke; internal set => _barrelSmoke = value; }
        public int BarrelSmokeShots { get => _barrelSmokeShots; internal set => _barrelSmokeShots = value; }
        public AssetReferenceGameObject HitSparks { get => _hitSparks; internal set => _hitSparks = value; }
        public int HitSparksCount { get => _hitSparksCount; internal set => _hitSparksCount = value; }
        public int KillSparksCount { get => _killSparksCount; internal set => _killSparksCount = value; }

        /// <summary>The stream look of a weapon: its own, else the default; null for other attacks.</summary>
        public AssetReferenceGameObject StreamOf(WeaponDefinition weapon) =>
            weapon == null || !(weapon.Attack is StreamAttack) ? null : Own(weapon.Effects?.Stream) ?? Own(_stream);

        /// <summary>The flash on a chain lightning's targets: the weapon's own, else the default; null for other attacks.</summary>
        public AssetReferenceGameObject ChainHitOf(WeaponDefinition weapon) =>
            weapon == null || !(weapon.Attack is ChainAttack) ? null : Own(weapon.Effects?.TargetHit) ?? Own(_chainHit);

        /// <summary>Smoke after a burst: the weapon's own, else the default; null for attacks without bullets.</summary>
        public AssetReferenceGameObject BarrelSmokeOf(WeaponDefinition weapon) =>
            weapon == null || !(weapon.Attack is BulletAttack) ? null : Own(weapon.Effects?.BarrelSmoke) ?? Own(_barrelSmoke);

        private static AssetReferenceGameObject Own(AssetReferenceGameObject reference) => IsSet(reference) ? reference : null;

        /// <summary>Default pack muzzle flash of a hold, or null (then <see cref="MuzzleFlash"/>, the own flash).</summary>
        public AssetReferenceGameObject MuzzleFlashOf(WeaponHold hold)
        {
            foreach (var entry in _muzzleFlashes)
                if (entry != null && entry.Hold == hold && IsSet(entry.Effect)) return entry.Effect;
            return null;
        }

        /// <summary>
        /// Default look of an explosion of <paramref name="radius"/> cells: its size's effect, else the nearest smaller
        /// one, else any; null without any.
        /// </summary>
        public AssetReferenceGameObject ExplosionOf(float radius)
        {
            if (radius >= _largeExplosionRadius && IsSet(_explosionLarge)) return _explosionLarge;
            if (radius >= _mediumExplosionRadius && IsSet(_explosionMedium)) return _explosionMedium;
            if (IsSet(_explosionSmall)) return _explosionSmall;
            return IsSet(_explosionMedium) ? _explosionMedium : IsSet(_explosionLarge) ? _explosionLarge : null;
        }

        public static bool IsSet(AssetReferenceGameObject reference) => reference != null && reference.RuntimeKeyIsValid();

        /// <summary>Every prefab reference (some may be empty).</summary>
        public IEnumerable<AssetReferenceGameObject> Prefabs
        {
            get
            {
                yield return _bullet;
                yield return _muzzleFlash;
                yield return _shellCasing;
                yield return _impact;
                yield return _targetHit;
                yield return _meleeSwing;
                foreach (var entry in _muzzleFlashes)
                    if (entry != null) yield return entry.Effect;
                yield return _explosionSmall;
                yield return _explosionMedium;
                yield return _explosionLarge;
                yield return _stream;
                yield return _chainHit;
                yield return _barrelSmoke;
                yield return _hitSparks;
            }
        }

        [System.Serializable]
        public sealed class HoldEffect
        {
            public WeaponHold Hold;
            public AssetReferenceGameObject Effect;
        }
    }
}
