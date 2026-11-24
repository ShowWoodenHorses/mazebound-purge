using System;
using System.Collections.Generic;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using UnityEngine;

namespace Purge.Gameplay.Throwing
{
    [Serializable]
    public struct DeployedItem
    {
        public string ThrowableId;
        public Vector2 Position;
        public int Floor;
        public float Remaining;
        public float ShotDelay;
        public Vector2 Facing;
    }

    /// <summary>One active item of each kind. A new deployment replaces the previous item of the same kind.</summary>
    public sealed class Deployables
    {
        private DeployedItem _decoy;
        private DeployedItem _turret;
        private ThrowableDefinition _decoyDefinition;
        private ThrowableDefinition _turretDefinition;
        private readonly List<CombatTarget> _targets = new List<CombatTarget>();
        private readonly SmokeScreen _smoke;
        private readonly LevelPassability _passability;

        public Deployables(SmokeScreen smoke = null, LevelPassability passability = null)
        {
            _smoke = smoke;
            _passability = passability;
        }

        public DeployedItem Decoy => _decoy;
        public DeployedItem Turret => _turret;
        public float DecoyRadius { get; private set; }
        public event Action<DeployedItem, Bullet> TurretFired;

        public bool CanPlace(Vector2 position, int floor) =>
            _passability == null || _passability.IsWalkable(PlayerMovement.CellOf(position, floor));

        public void Place(ThrowableDefinition definition, Vector2 position, int floor, Vector2 facing)
        {
            Restore(definition, new DeployedItem
            {
                ThrowableId = definition.Id, Position = position, Floor = floor,
                Remaining = definition.Deployable.Duration, Facing = facing
            });
        }

        public void Restore(ThrowableDefinition definition, DeployedItem item)
        {
            var effect = definition != null ? definition.Deployable : null;
            if (effect == null || item.Remaining <= 0f) return;
            if (effect.Kind == DeployableKind.Decoy)
            {
                _decoy = item;
                _decoyDefinition = definition;
                DecoyRadius = effect.Radius;
            }
            else
            {
                _turret = item;
                _turretDefinition = definition;
            }
        }

        public void Tick(float deltaTime, CombatTargets targets, BulletSystem bullets, ThrowableSlot slot)
        {
            _decoy.Remaining = Mathf.Max(0f, _decoy.Remaining - deltaTime);
            if (_decoy.Remaining > 0f)
                DecoyRadius = _decoyDefinition.Deployable.Radius * slot.RadiusFactor(_decoyDefinition);
            if (_turret.Remaining <= 0f) return;
            _turret.Remaining = Mathf.Max(0f, _turret.Remaining - deltaTime);
            _turret.ShotDelay -= deltaTime;
            if (_turret.Remaining <= 0f || _turret.ShotDelay > 0f) return;

            var effect = _turretDefinition.Deployable;
            var radius = effect.Radius * slot.RadiusFactor(_turretDefinition);
            var closest = radius * radius;
            var direction = Vector2.zero;
            var found = false;
            targets.Collect(_turret.Position, radius, _turret.Floor, _targets);
            foreach (var target in _targets)
            {
                if (target.Agent < 0 || !targets.IsAlive(target)) continue;
                var position = targets.PositionOf(target);
                var offset = position - _turret.Position;
                var squareDistance = offset.sqrMagnitude;
                if (squareDistance > closest || (found && squareDistance == closest) ||
                    !targets.Spatial.IsClear(_turret.Position, position, CellFlags.BlocksSight | CellFlags.BlocksBullets,
                        _turret.Floor) ||
                    _smoke?.Obscures(_turret.Position, position, _turret.Floor) == true) continue;
                closest = squareDistance;
                direction = offset;
                found = true;
            }
            _targets.Clear();
            _turret.ShotDelay = found ? effect.Interval : 0.15f;
            if (!found) return;

            if (direction.sqrMagnitude > 1e-8f) _turret.Facing = direction.normalized;
            var bullet = bullets.Spawn(_turret.Position, _turret.Facing, 36f, effect.Damage * slot.DamageFactor(_turretDefinition),
                _turret.Floor, Mathf.Max(0.05f, Mathf.Sqrt(closest)));
            TurretFired?.Invoke(_turret, bullet);
        }
    }
}
