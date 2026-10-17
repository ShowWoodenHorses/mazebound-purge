using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Effects
{
    /// <summary>
    /// An effect lying on cells for a while (ТЗ «Зоны на клетках»: огонь, кислота, дым…) assembled from
    /// <see cref="ZoneModule"/>s: damage to those inside, a state put on them, spreading over flammable cells. Placed by
    /// effects (<see cref="PlaceZoneEffect"/>) with a duration and a strength; kept in flat arrays per cell, ticked
    /// several times a second. The same zone on a cell again: the longer duration and the stronger strength of the two,
    /// never their sum. The player's own zones never hurt the player.
    /// </summary>
    [CreateAssetMenu(fileName = "Zone", menuName = "Purge/Definitions/Zone")]
    public sealed class ZoneDefinition : ContentDefinition
    {
        [Tooltip("Draft look until stage 4в (display only): the core and the edge colour of the zone's cells.")]
        [SerializeField] private Color _core = new Color(1f, 0.86f, 0.35f, 1f);
        [SerializeField] private Color _edge = new Color(0.95f, 0.3f, 0.06f, 1f);

        [SerializeReference, ModuleList] private List<ZoneModule> _modules = new List<ZoneModule>();

        [NonSerialized] private bool _cached;
        [NonSerialized] private ZoneDamage _damage;
        [NonSerialized] private ZoneStatus _status;
        [NonSerialized] private ZoneSpread _spread;

        public Color Core => _core;
        public Color Edge => _edge;
        public IReadOnlyList<ZoneModule> Modules => _modules;

        public ZoneDamage Damage => Cache()._damage;
        public ZoneStatus Status => Cache()._status;
        public ZoneSpread Spread => Cache()._spread;

        public void Validate(List<string> problems)
        {
            var owner = $"Zone '{name}'";
            Purge.Core.Modules.Modules.Validate(_modules, owner, problems);
            if (_modules.Count == 0) problems.Add($"{owner}: has no modules (does nothing).");
        }

        private void OnValidate() => _cached = false;

        private ZoneDefinition Cache()
        {
            if (_cached) return this;
            _damage = null;
            _status = null;
            _spread = null;
            foreach (var module in _modules)
                switch (module)
                {
                    case ZoneDamage damage when _damage == null: _damage = damage; break;
                    case ZoneStatus status when _status == null: _status = status; break;
                    case ZoneSpread spread when _spread == null: _spread = spread; break;
                }

            _cached = true;
            return this;
        }

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, params ZoneModule[] modules)
        {
            SetId(id);
            _modules = new List<ZoneModule>(modules);
            _cached = false;
        }
    }

    /// <summary>A module of a <see cref="ZoneDefinition"/>.</summary>
    [Serializable]
    public abstract class ZoneModule : ContentModule
    {
    }

    /// <summary>Damage per second (× the zone's strength) to everyone standing in the zone.</summary>
    [Serializable, ModuleInfo("Damage", Category = "Zone")]
    public sealed class ZoneDamage : ZoneModule
    {
        [SerializeField, Min(0f)] private float _damagePerSecond = 20f;

        public float DamagePerSecond { get => _damagePerSecond; internal set => _damagePerSecond = value; }
    }

    /// <summary>Puts a state on everyone standing in the zone (renewed while they stay; burning goes on after).</summary>
    [Serializable, ModuleInfo("Status", Category = "Zone")]
    public sealed class ZoneStatus : ZoneModule
    {
        [SerializeField] private StatusDefinition _status;
        [Tooltip("Seconds the state lasts after the last tick in the zone.")]
        [SerializeField, Min(0.1f)] private float _duration = 3f;

        public StatusDefinition Status { get => _status; internal set => _status = value; }
        public float Duration { get => _duration; internal set => _duration = value; }

        public override void Validate(List<string> problems)
        {
            if (_status == null) problems.Add("no status.");
        }
    }

    /// <summary>
    /// Spreads to neighbouring flammable cells (bushes, grass: <c>CellFlags.Flammable</c>), at most
    /// <see cref="MaxSteps"/> cells away from where it was placed (ТЗ: распространение с ограничением площади).
    /// </summary>
    [Serializable, ModuleInfo("Spread over flammable", Category = "Zone")]
    public sealed class ZoneSpread : ZoneModule
    {
        [Tooltip("Chance per second that a burning cell sets one neighbouring flammable cell alight.")]
        [SerializeField, Range(0f, 10f)] private float _chancePerSecond = 1.5f;
        [Tooltip("Cells (steps between neighbours) the zone may spread from where it was placed.")]
        [SerializeField, Min(0)] private int _maxSteps = 6;
        [Tooltip("Duration of the zone on a flammable cell, × the duration it was placed with.")]
        [SerializeField, Min(0.1f)] private float _flammableDuration = 1.5f;

        public float ChancePerSecond { get => _chancePerSecond; internal set => _chancePerSecond = value; }
        public int MaxSteps { get => _maxSteps; internal set => _maxSteps = value; }
        public float FlammableDuration { get => _flammableDuration; internal set => _flammableDuration = value; }
    }
}
