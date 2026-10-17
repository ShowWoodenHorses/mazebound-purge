using System;
using System.Collections.Generic;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Spatial;
using UnityEngine;

namespace Purge.Gameplay.Combat
{
    /// <summary>
    /// Something the player's attacks can hurt: a monster of the horde (an array slot, <see cref="Agent"/>) or a
    /// spatial object (<see cref="Object"/>, the zombies of Maze until stage 5).
    /// </summary>
    public readonly struct CombatTarget : IEquatable<CombatTarget>
    {
        private readonly int _agentPlusOne; // default(CombatTarget) is "none", not slot 0

        private CombatTarget(int agent, IDamageable damageable)
        {
            _agentPlusOne = agent + 1;
            Object = damageable;
        }

        public static CombatTarget None => default;
        public static CombatTarget OfAgent(int agent) => new CombatTarget(agent, null);
        public static CombatTarget Of(IDamageable damageable) => new CombatTarget(-1, damageable);

        /// <summary>Horde slot; -1 when the target is an object or none.</summary>
        public int Agent => _agentPlusOne - 1;

        public IDamageable Object { get; }

        public bool IsValid => Agent >= 0 || Object != null;

        public bool Equals(CombatTarget other) => Agent == other.Agent && ReferenceEquals(Object, other.Object);
        public override bool Equals(object obj) => obj is CombatTarget other && Equals(other);
        public override int GetHashCode() => Agent ^ (Object?.GetHashCode() ?? 0);
    }

    public enum CombatHitKind
    {
        None = 0,
        Target = 1,
        /// <summary>Wall, closed door, obstacle that stops bullets, the edge of the grid.</summary>
        Blocker = 2,
    }

    public readonly struct CombatHit
    {
        public CombatHit(CombatHitKind kind, float t, Vector2 point, CombatTarget target)
        {
            Kind = kind;
            T = t;
            Point = point;
            Target = target;
        }

        public CombatHitKind Kind { get; }

        /// <summary>Fraction 0..1 along the queried segment.</summary>
        public float T { get; }

        public Vector2 Point { get; }
        public CombatTarget Target { get; }
    }

    /// <summary>
    /// Every target of the player's attacks in one place: the horde (<see cref="HordeSystem"/>, flat arrays) and the
    /// damageable spatial objects (<see cref="ISpatialQueryService"/>). Queries take walls into account as bullets do
    /// (<see cref="CellFlags.BlocksBullets"/>); all damage goes through <see cref="Damage"/>, which raises
    /// <see cref="Damaged"/> for views. Allocation-free.
    /// </summary>
    public sealed class CombatTargets
    {
        private const int MaxAgents = 512;

        private readonly ISpatialQueryService _spatial;
        private readonly HordeSystem _horde;
        private readonly List<ISpatialObject> _objects = new List<ISpatialObject>();
        private readonly int[] _agents = new int[MaxAgents];
        private readonly List<CombatTarget> _blast = new List<CombatTarget>();

        public CombatTargets(ISpatialQueryService spatial, HordeSystem horde = null)
        {
            _spatial = spatial;
            _horde = horde;
        }

        public ISpatialQueryService Spatial => _spatial;

        /// <summary>(target, where, damage, direction, killed) after every damage dealt by the player.</summary>
        public event Action<CombatTarget, Vector2, float, Vector2, bool> Damaged;

        /// <summary>(target, weapon) after a weapon's attack damaged a target that is still alive.</summary>
        public event Action<CombatTarget, WeaponDefinition> WeaponHit;

        /// <summary>(target, where, direction, weapon, killed) after every damage of a weapon's attack (sounds, feel).</summary>
        public event Action<CombatTarget, Vector2, Vector2, WeaponDefinition, bool> WeaponDamaged;

        private bool HordeActive => _horde != null && _horde.IsActive;

        public bool IsAlive(in CombatTarget target) =>
            target.Agent >= 0
                ? HordeActive && target.Agent < _horde.HighWater && HordeSystem.IsAlive(_horde.States[target.Agent])
                : target.Object != null && target.Object.IsAlive;

        public Vector2 PositionOf(in CombatTarget target) =>
            target.Agent >= 0 ? _horde.Positions[target.Agent] : target.Object?.Position ?? default;

        public float RadiusOf(in CombatTarget target) =>
            target.Agent >= 0 ? _horde.RadiusOf(target.Agent) : target.Object?.Radius ?? 0f;

        /// <summary>
        /// The first target or blocker on the segment (a bullet's flight this tick); <paramref name="ignore"/> is passed
        /// through (a piercing bullet inside the monster it has just hit).
        /// </summary>
        public CombatHit Segment(Vector2 from, Vector2 to, int floor, in CombatTarget ignore = default)
        {
            var spatial = _spatial.QuerySegment(from, to, floor);
            var bestT = 1f;
            var best = new CombatHit(CombatHitKind.None, 1f, to, CombatTarget.None);
            if (spatial.Kind == SegmentHitKind.Blocker)
            {
                bestT = spatial.T;
                best = new CombatHit(CombatHitKind.Blocker, spatial.T, spatial.Point, CombatTarget.None);
            }
            else if (spatial.Kind == SegmentHitKind.Object && spatial.Object is IDamageable damageable && damageable.IsAlive &&
                     !ReferenceEquals(damageable, ignore.Object))
            {
                bestT = spatial.T;
                best = new CombatHit(CombatHitKind.Target, spatial.T, spatial.Point, CombatTarget.Of(damageable));
            }

            if (HordeActive)
            {
                var agent = _horde.QuerySegment(from, to, out var t, ignore.Agent);
                // A monster exactly at the wall: the bullet hits it first.
                if (agent >= 0 && t <= bestT)
                    best = new CombatHit(CombatHitKind.Target, t, from + (to - from) * t, CombatTarget.OfAgent(agent));
            }

            return best;
        }

        /// <summary>
        /// Living targets whose bodies reach into the circle (centre distance ≤ <paramref name="radius"/> + body radius),
        /// within <paramref name="cosHalfAngle"/> of <paramref name="direction"/> (−1 = all around). Walls are not checked.
        /// </summary>
        public void Collect(Vector2 origin, float radius, int floor, List<CombatTarget> results, Vector2 direction = default,
            float cosHalfAngle = -1f)
        {
            results.Clear();
            if (HordeActive)
            {
                var count = _horde.QueryCone(origin, direction.sqrMagnitude > 0f ? direction.normalized : Vector2.up, radius,
                    cosHalfAngle, _agents);
                for (var i = 0; i < count; i++)
                    results.Add(CombatTarget.OfAgent(_agents[i]));
            }

            _spatial.QueryRadius(origin, radius + 1f, _objects, floor);
            foreach (var candidate in _objects)
            {
                if (!(candidate is IDamageable target) || !target.IsAlive) continue;
                var offset = target.Position - origin;
                var distance = offset.magnitude;
                if (distance > radius + target.Radius) continue;
                if (cosHalfAngle > -1f && distance > target.Radius &&
                    Vector2.Dot(offset / distance, direction.normalized) < cosHalfAngle) continue;
                results.Add(CombatTarget.Of(target));
            }

            _objects.Clear();
        }

        /// <summary>Nothing that stops bullets between the points.</summary>
        public bool IsClear(Vector2 from, Vector2 to, int floor) => _spatial.IsClear(from, to, CellFlags.BlocksBullets, floor);

        /// <summary>Deals damage; true when it killed the target.</summary>
        public bool Damage(in CombatTarget target, float amount, Vector2 direction)
        {
            if (amount <= 0f || !IsAlive(target)) return false;
            var at = PositionOf(target);
            bool killed;
            if (target.Agent >= 0)
            {
                killed = _horde.Damage(target.Agent, amount);
            }
            else
            {
                target.Object.ApplyDamage(amount, direction);
                killed = !target.Object.IsAlive;
            }

            Damaged?.Invoke(target, at, amount, direction, killed);
            return killed;
        }

        /// <summary>
        /// Damage of a weapon's attack: as <see cref="Damage(in CombatTarget, float, Vector2)"/>, then
        /// <see cref="WeaponHit"/> for a target that survived (states on hit).
        /// </summary>
        public bool Damage(in CombatTarget target, float amount, Vector2 direction, WeaponDefinition weapon)
        {
            if (amount <= 0f || !IsAlive(target)) return false;
            var at = PositionOf(target);
            var killed = Damage(target, amount, direction);
            if (!killed && weapon != null) WeaponHit?.Invoke(target, weapon);
            if (weapon != null) WeaponDamaged?.Invoke(target, at, direction, weapon, killed);
            return killed;
        }

        /// <summary>
        /// Damage to every living target whose body reaches into the circle and is not behind something that stops
        /// bullets: <paramref name="damage"/> at the centre, × <paramref name="edgeShare"/> at the edge. With the
        /// <paramref name="weapon"/> that fired it, each damage is that weapon's (feel: pushed away from the centre).
        /// </summary>
        public void Blast(Vector2 at, float damage, float radius, float edgeShare, int floor, WeaponDefinition weapon = null)
        {
            Collect(at, radius, floor, _blast);
            foreach (var target in _blast)
            {
                var position = PositionOf(target);
                if (!IsClear(at, position, floor)) continue;
                var offset = position - at;
                var share = Mathf.Clamp01((offset.magnitude - RadiusOf(target)) / radius);
                Damage(target, damage * Mathf.Lerp(1f, edgeShare, share),
                    offset.sqrMagnitude > 1e-6f ? offset.normalized : Vector2.up, weapon);
            }

            _blast.Clear();
        }
    }
}
