using System;
using System.Collections.Generic;
using Purge.Core.Effects;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Sound;
using UnityEngine;

namespace Purge.Gameplay.Effects
{
    /// <summary>
    /// Makes <see cref="EffectModule"/>s happen at a point, once each, in order (code written once per module): an
    /// explosion damages the monsters around (<see cref="CombatTargets.Blast"/>; walls cover) and is heard far, a zone
    /// covers the cells around (<see cref="ZoneSystem.PlaceArea"/>), a state goes on the monsters around
    /// (<see cref="StatusSystem.ApplyInRadius"/>). Used by thrown items now; by impacts and abilities later.
    /// </summary>
    public sealed class EffectSystem
    {
        private readonly CombatTargets _targets;
        private readonly ZoneSystem _zones;
        private readonly StatusSystem _statuses;
        private readonly SoundEventBus _sounds;

        public EffectSystem(CombatTargets targets, ZoneSystem zones, StatusSystem statuses, SoundEventBus sounds)
        {
            _targets = targets;
            _zones = zones;
            _statuses = statuses;
            _sounds = sounds;
        }

        /// <summary>(where, radius) of an explosion.</summary>
        public event Action<Vector2, float> Exploded;

        /// <summary>(where, zone, radius) a zone was placed.</summary>
        public event Action<Vector2, ZoneDefinition, float> ZonePlaced;

        /// <param name="hostile">The monsters' effect: its zones hurt the player too (the player's own never do).</param>
        public void Apply(IReadOnlyList<EffectModule> effects, Vector2 at, int floor, bool hostile = false)
        {
            if (effects == null) return;
            for (var i = 0; i < effects.Count; i++)
            {
                switch (effects[i])
                {
                    case ExplosionEffect explosion:
                        _targets.Blast(at, explosion.Damage, explosion.Radius, explosion.EdgeDamage, floor);
                        _sounds.Emit(SoundType.Explosion, at, explosion.SoundRadius, floor);
                        Exploded?.Invoke(at, explosion.Radius);
                        break;
                    case PlaceZoneEffect zone when zone.Zone != null:
                        _zones.PlaceArea(at, floor, zone.Radius, zone.Zone, zone.Duration, zone.Strength, hostile);
                        ZonePlaced?.Invoke(at, zone.Zone, zone.Radius);
                        break;
                    case AreaStatusEffect status when status.Status != null:
                        _statuses.ApplyInRadius(at, status.Radius, floor, status.Status, status.Duration);
                        break;
                }
            }
        }
    }
}
