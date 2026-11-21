using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Core.Effects
{
    /// <summary>
    /// A state on a monster (ТЗ «Зоны на клетках и состояния»: горение, оглушение, замедление…) assembled from
    /// <see cref="StatusModule"/>s: damage over time, stun, slow. Applied with a duration by zones, effects and weapons
    /// (<see cref="Definitions.StatusOnHitModule"/>); applying it again keeps the longer of the two durations (no
    /// stacking). Burning continues after the monster leaves the fire: the zone only renews it.
    /// </summary>
    [CreateAssetMenu(fileName = "Status", menuName = "Purge/Definitions/Status")]
    public sealed class StatusDefinition : ContentDefinition
    {
        [Tooltip("Draft look until stage 4в (display only): tint of the monster and of its particles.")]
        [SerializeField] private Color _color = new Color(1f, 0.55f, 0.15f, 1f);

        [Tooltip("Look over a monster with the state (stage Э2): an effect of Art/Effects the game emits from (flames " +
                 "of burning); empty = the draft particles in the colour above.")]
        [SerializeField] private AssetReferenceGameObject _particles;
        [Tooltip("The look circles the head (stars of a stun) instead of rising around the body (stage Э3).")]
        [SerializeField] private bool _particlesAtHead;

        [SerializeReference, ModuleList] private List<StatusModule> _modules = new List<StatusModule>();

        [NonSerialized] private bool _cached;
        [NonSerialized] private float _damagePerSecond;
        [NonSerialized] private float _speedFactor;
        [NonSerialized] private bool _stuns;
        [NonSerialized] private float _incomingDamageFactor;
        [NonSerialized] private ElectricPulseStatus _electricPulse;
        [NonSerialized] private bool _fears;
        [NonSerialized] private bool _blinds;

        public Color Color => _color;
        public AssetReferenceGameObject Particles { get => _particles; internal set => _particles = value; }
        public bool ParticlesAtHead { get => _particlesAtHead; internal set => _particlesAtHead = value; }
        public IReadOnlyList<StatusModule> Modules => _modules;

        /// <summary>Sum of the damage-over-time modules.</summary>
        public float DamagePerSecond => Cache()._damagePerSecond;

        /// <summary>Product of the slow modules (1 = no slow).</summary>
        public float SpeedFactor => Cache()._speedFactor;

        /// <summary>The monster neither moves nor attacks.</summary>
        public bool Stuns => Cache()._stuns;
        public float IncomingDamageFactor => Cache()._incomingDamageFactor;
        public ElectricPulseStatus ElectricPulse => Cache()._electricPulse;
        public bool Fears => Cache()._fears;
        public bool Blinds => Cache()._blinds;

        public void Validate(List<string> problems)
        {
            var owner = $"Status '{name}'";
            Purge.Core.Modules.Modules.Validate(_modules, owner, problems);
            if (_modules.Count == 0) problems.Add($"{owner}: has no modules (does nothing).");
        }

        private void OnValidate() => _cached = false;

        private StatusDefinition Cache()
        {
            if (_cached) return this;
            _damagePerSecond = 0f;
            _speedFactor = 1f;
            _stuns = false;
            _incomingDamageFactor = 1f;
            _electricPulse = null;
            _fears = false;
            _blinds = false;
            foreach (var module in _modules)
                switch (module)
                {
                    case DamageOverTimeStatus damage: _damagePerSecond += damage.DamagePerSecond; break;
                    case SlowStatus slow: _speedFactor *= slow.SpeedFactor; break;
                    case StunStatus _: _stuns = true; break;
                    case VulnerabilityStatus vulnerability: _incomingDamageFactor *= vulnerability.DamageFactor; break;
                    case ElectricPulseStatus pulse: _electricPulse = pulse; break;
                    case FearStatus _: _fears = true; break;
                    case BlindStatus _: _blinds = true; break;
                }

            _cached = true;
            return this;
        }

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, params StatusModule[] modules)
        {
            SetId(id);
            _modules = new List<StatusModule>(modules);
            _cached = false;
        }
    }

    /// <summary>A module of a <see cref="StatusDefinition"/>.</summary>
    [Serializable]
    public abstract class StatusModule : ContentModule
    {
    }

    /// <summary>Damage every second the state lasts (burning, poison).</summary>
    [Serializable, ModuleInfo("Damage over time", Category = "Status")]
    public sealed class DamageOverTimeStatus : StatusModule
    {
        [SerializeField, Min(0f)] private float _damagePerSecond = 8f;

        public float DamagePerSecond { get => _damagePerSecond; internal set => _damagePerSecond = value; }
    }

    /// <summary>The monster stands still and does not attack while the state lasts.</summary>
    [Serializable, ModuleInfo("Stun", Category = "Status")]
    public sealed class StunStatus : StatusModule
    {
    }

    [Serializable, ModuleInfo("Incoming damage multiplier", Category = "Status")]
    public sealed class VulnerabilityStatus : StatusModule
    {
        [SerializeField, Min(1f)] private float _damageFactor = 1.25f;
        public float DamageFactor => _damageFactor;
    }

    [Serializable, ModuleInfo("Electric pulses", Category = "Status")]
    public sealed class ElectricPulseStatus : StatusModule
    {
        [SerializeField, Min(0.01f)] private float _interval = 0.5f;
        [SerializeField, Min(0f)] private float _radius = 1f;
        public float Interval => Mathf.Max(0.01f, _interval);
        public float Radius => _radius;
    }

    [Serializable, ModuleInfo("Fear", Category = "Status")]
    public sealed class FearStatus : StatusModule { }

    [Serializable, ModuleInfo("Blind", Category = "Status")]
    public sealed class BlindStatus : StatusModule { }

    /// <summary>Slower movement while the state lasts.</summary>
    [Serializable, ModuleInfo("Slow", Category = "Status", AllowMultiple = true)]
    public sealed class SlowStatus : StatusModule
    {
        [SerializeField, Range(0f, 1f)] private float _speedFactor = 0.5f;

        public float SpeedFactor { get => _speedFactor; internal set => _speedFactor = value; }
    }
}
