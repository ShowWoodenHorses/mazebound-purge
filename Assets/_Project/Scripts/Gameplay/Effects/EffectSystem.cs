using System;
using System.Collections.Generic;
using Purge.Core.Effects;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using UnityEngine;

namespace Purge.Gameplay.Effects
{
    /// <summary>
    /// Makes <see cref="EffectModule"/>s happen at a point, once each, in order (code written once per module): an
    /// explosion damages the monsters around (<see cref="CombatTargets.Blast"/>; walls cover) and is heard far, a zone
    /// covers the cells around (<see cref="ZoneSystem.PlaceArea"/>), a state goes on the monsters around
    /// (<see cref="StatusSystem.ApplyInRadius"/>). Used by thrown items, monsters' projectiles, abilities and deaths.
    /// <para>A <b>hostile</b> effect (the monsters', stage 5e) reaches the player too: an explosion hurts them (less
    /// towards the edge, walls cover) as well as the monsters around, a zone is the monsters' (it hurts the player), a
    /// state goes on the player instead of the monsters. <see cref="HordeSystem.GodMode"/> spares the player.</para>
    /// </summary>
    public sealed class EffectSystem
    {
        private const float PlayerRadius = 0.35f;

        private readonly CombatTargets _targets;
        private readonly ZoneSystem _zones;
        private readonly StatusSystem _statuses;
        private readonly SoundEventBus _sounds;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _health;
        private readonly PlayerStatusSystem _playerStatuses;
        private readonly HordeSystem _horde;

        public EffectSystem(CombatTargets targets, ZoneSystem zones, StatusSystem statuses, SoundEventBus sounds,
            PlayerSystem player = null, PlayerHealth health = null, PlayerStatusSystem playerStatuses = null, HordeSystem horde = null)
        {
            _targets = targets;
            _zones = zones;
            _statuses = statuses;
            _sounds = sounds;
            _player = player;
            _health = health;
            _playerStatuses = playerStatuses;
            _horde = horde;
        }

        /// <summary>(where, radius, what exploded — its look) of an explosion.</summary>
        public event Action<Vector2, float, IExplosionLook> Exploded;

        /// <summary>(where, zone, radius) a zone was placed.</summary>
        public event Action<Vector2, ZoneDefinition, float> ZonePlaced;

        /// <param name="hostile">The monsters' effect: it reaches the player too (the player's own never do).</param>
        public void Apply(IReadOnlyList<EffectModule> effects, Vector2 at, int floor, bool hostile = false)
        {
            if (effects == null) return;
            for (var i = 0; i < effects.Count; i++)
            {
                switch (effects[i])
                {
                    case ExplosionEffect explosion:
                        _targets.Blast(at, explosion.Damage, explosion.Radius, explosion.EdgeDamage, floor);
                        if (hostile) BlastPlayer(at, explosion, floor);
                        _sounds.Emit(SoundType.Explosion, at, explosion.SoundRadius, floor);
                        Exploded?.Invoke(at, explosion.Radius, explosion);
                        break;
                    case PlaceZoneEffect zone when zone.Zone != null:
                        _zones.PlaceArea(at, floor, zone.Radius, zone.Zone, zone.Duration, zone.Strength, hostile);
                        ZonePlaced?.Invoke(at, zone.Zone, zone.Radius);
                        break;
                    case AreaStatusEffect status when status.Status != null:
                        if (hostile) StatusOnPlayer(at, status, floor);
                        else _statuses.ApplyInRadius(at, status.Radius, floor, status.Status, status.Duration);
                        break;
                }
            }
        }

        /// <summary>The player in reach of the point and not behind a wall (false without a player or in God mode).</summary>
        private bool PlayerInReach(Vector2 at, float radius, int floor, out float distance)
        {
            distance = 0f;
            if (_player == null || _health == null || !_player.IsSpawned || _player.Floor != floor || _health.IsDead) return false;
            if (_horde != null && _horde.IsActive && _horde.GodMode) return false;
            distance = (_player.Position - at).magnitude;
            return distance <= radius + PlayerRadius && _targets.IsClear(at, _player.Position, floor);
        }

        private void BlastPlayer(Vector2 at, ExplosionEffect explosion, int floor)
        {
            if (!PlayerInReach(at, explosion.Radius, floor, out var distance)) return;
            var share = Mathf.Clamp01((distance - PlayerRadius) / explosion.Radius);
            var amount = Mathf.RoundToInt(explosion.Damage * Mathf.Lerp(1f, explosion.EdgeDamage, share));
            if (amount > 0) _health.Damage(amount, at);
        }

        private void StatusOnPlayer(Vector2 at, AreaStatusEffect status, int floor)
        {
            if (_playerStatuses != null && PlayerInReach(at, status.Radius, floor, out _))
                _playerStatuses.Apply(status.Status, status.Duration);
        }
    }
}
