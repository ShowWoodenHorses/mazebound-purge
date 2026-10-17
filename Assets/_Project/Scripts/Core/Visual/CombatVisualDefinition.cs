using System.Collections.Generic;
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
            }
        }
    }
}
