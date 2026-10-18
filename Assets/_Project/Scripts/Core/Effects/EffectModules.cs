using System;
using System.Collections.Generic;
using Purge.Core.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Core.Effects
{
    /// <summary>
    /// What happens at a point (ТЗ: «Зоны и состояния — те же модули, из которых собираются оружие, метательное и
    /// способности»): a throwable's landing now; weapon impacts and monster abilities later. Applied once, in list order.
    /// Walls cover: nothing reaches a cell or a monster behind something that stops bullets.
    /// </summary>
    [Serializable]
    public abstract class EffectModule : ContentModule
    {
        /// <summary>How far from the point the effect reaches, cells (the throw aim shows it).</summary>
        public abstract float Radius { get; }
    }

    /// <summary>
    /// Something that explodes and has its own look (display only): an explosion effect, an explosive projectile.
    /// Gameplay passes it with its explosion events; the view reads the look.
    /// </summary>
    public interface IExplosionLook
    {
        /// <summary>An effect of Art/Effects scaled to the radius; empty = the default by size.</summary>
        AssetReferenceGameObject Look { get; }
    }

    /// <summary>Damage to every monster in the radius, less towards the edge.</summary>
    [Serializable, ModuleInfo("Explosion", Category = "Effect", AllowMultiple = true)]
    public sealed class ExplosionEffect : EffectModule, IExplosionLook
    {
        [Tooltip("Damage at the centre.")]
        [SerializeField, Min(0f)] private float _damage = 80f;
        [Tooltip("Cells.")]
        [SerializeField, Min(0.3f)] private float _radius = 2.5f;
        [Tooltip("Share of the damage at the edge of the radius.")]
        [SerializeField, Range(0f, 1f)] private float _edgeDamage = 0.35f;
        [Tooltip("How far the blast is heard by monsters, cells.")]
        [SerializeField, Min(0f)] private float _soundRadius = 12f;
        [Tooltip("Look of the blast (an effect of Art/Effects, scaled to the radius); empty = by size (Combat Visual).")]
        [SerializeField] private AssetReferenceGameObject _look;

        public AssetReferenceGameObject Look { get => _look; internal set => _look = value; }
        public float Damage { get => _damage; internal set => _damage = value; }
        public override float Radius => _radius;
        public float EdgeDamage { get => _edgeDamage; internal set => _edgeDamage = value; }
        public float SoundRadius { get => _soundRadius; internal set => _soundRadius = value; }
        internal void SetRadius(float radius) => _radius = radius;
    }

    /// <summary>A zone on every cell in the radius that a bullet could reach from the point.</summary>
    [Serializable, ModuleInfo("Place zone", Category = "Effect", AllowMultiple = true)]
    public sealed class PlaceZoneEffect : EffectModule
    {
        [SerializeField] private ZoneDefinition _zone;
        [Tooltip("Cells from the centre cell (0 = only that cell).")]
        [SerializeField, Min(0f)] private float _radius = 1.5f;
        [Tooltip("Seconds the zone lasts.")]
        [SerializeField, Min(0.1f)] private float _duration = 5f;
        [Tooltip("Multiplier of the zone's damage.")]
        [SerializeField, Min(0f)] private float _strength = 1f;

        public ZoneDefinition Zone { get => _zone; internal set => _zone = value; }
        public override float Radius => _radius;
        public float Duration { get => _duration; internal set => _duration = value; }
        public float Strength { get => _strength; internal set => _strength = value; }
        internal void SetRadius(float radius) => _radius = radius;

        public override void Validate(List<string> problems)
        {
            if (_zone == null) problems.Add("no zone.");
        }
    }

    /// <summary>A state on every monster in the radius (a grenade stuns those near the blast).</summary>
    [Serializable, ModuleInfo("Status in radius", Category = "Effect", AllowMultiple = true)]
    public sealed class AreaStatusEffect : EffectModule
    {
        [SerializeField] private StatusDefinition _status;
        [SerializeField, Min(0.3f)] private float _radius = 2f;
        [Tooltip("Seconds.")]
        [SerializeField, Min(0.1f)] private float _duration = 1f;

        public StatusDefinition Status { get => _status; internal set => _status = value; }
        public override float Radius => _radius;
        public float Duration { get => _duration; internal set => _duration = value; }
        internal void SetRadius(float radius) => _radius = radius;

        public override void Validate(List<string> problems)
        {
            if (_status == null) problems.Add("no status.");
        }
    }
}
