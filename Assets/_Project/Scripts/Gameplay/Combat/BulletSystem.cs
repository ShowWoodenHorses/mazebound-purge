using System;
using System.Collections.Generic;
using Purge.Core.Definitions;
using Purge.Gameplay.Level;
using UnityEngine;

namespace Purge.Gameplay.Combat
{
    public enum BulletEnd
    {
        /// <summary>Wall, closed door or the edge of the grid.</summary>
        Blocked = 0,
        Hit = 1,
        /// <summary>Flew its range.</summary>
        Expired = 2,
        /// <summary>Level stopped.</summary>
        Cleared = 3,
    }

    /// <summary>A flying bullet or projectile (runtime only). Reused from a pool: keep no references after it ended.</summary>
    public sealed class Bullet
    {
        public int Id { get; internal set; }

        /// <summary>Grid units.</summary>
        public Vector2 Position { get; internal set; }

        public Vector2 PreviousPosition { get; internal set; }

        /// <summary>Floor the bullet flies on (it never leaves it).</summary>
        public int Floor { get; internal set; }
        public Vector2 Direction { get; internal set; }
        public float Speed { get; internal set; }
        public float Damage { get; internal set; }
        public float Age { get; internal set; }

        /// <summary>Cells left to fly.</summary>
        public float RangeLeft { get; internal set; }

        /// <summary>Monsters it still passes through.</summary>
        public int Pierce { get; internal set; }

        /// <summary>Explodes where it ends (grenade launcher); null for a plain bullet.</summary>
        public ExplosiveAttack Explosive { get; internal set; }

        /// <summary>Weapon that fired it (views pick the look); null in tests and for throwables.</summary>
        public WeaponDefinition Weapon { get; internal set; }

        /// <summary>
        /// A thrown item flying along an arc to its cell: over monsters (it never hits them), stopped only by what stops
        /// bullets; its effects happen where it ends (<see cref="BulletSystem.Ended"/>). Null for bullets.
        /// </summary>
        public ThrowableDefinition Throwable { get; internal set; }

        /// <summary>Cells it was to fly in all.</summary>
        public float Travel { get; internal set; }

        /// <summary>0 at the start, 1 at the end of its flight (the arc of a throwable).</summary>
        public float Progress => Travel > 0f ? Mathf.Clamp01(1f - RangeLeft / Travel) : 1f;

        internal CombatTarget Ignore { get; set; }
    }

    /// <summary>
    /// Bullets and projectiles of the level. Each tick a bullet moves from PreviousPosition to Position and the segment
    /// is queried (<see cref="CombatTargets.Segment"/>): the first wall, closed door or target ends it (open doors let it
    /// through); a target takes the damage, a piercing bullet flies on; an explosive one explodes where it ends
    /// (also at the end of its range) — every target in the radius not behind a wall takes damage, less towards the edge.
    /// A thrown item (<see cref="Bullet.Throwable"/>) flies over every target to the end of its range unless a wall,
    /// closed door or obstacle that stops bullets is in the way (it falls just short of it).
    /// No physics, no per-bullet allocations (pooled).
    /// </summary>
    public sealed class BulletSystem : ILevelTickable, IDisposable
    {
        public const float DefaultRange = 30f;

        private readonly CombatTargets _targets;
        private readonly List<Bullet> _active = new List<Bullet>();
        private readonly Stack<Bullet> _pool = new Stack<Bullet>();
        private int _nextId;

        public BulletSystem(CombatTargets targets)
        {
            _targets = targets;
        }

        public IReadOnlyList<Bullet> Active => _active;

        public event Action<Bullet> Spawned;

        /// <summary>(bullet, where it ended, how). Raised before the bullet returns to the pool.</summary>
        public event Action<Bullet, Vector2, BulletEnd> Ended;

        /// <summary>(where, radius) of an explosion.</summary>
        public event Action<Vector2, float> Exploded;

        public Bullet Spawn(Vector2 origin, Vector2 direction, float speed, float damage, int floor = 0, float range = DefaultRange,
            int pierce = 0, ExplosiveAttack explosive = null, WeaponDefinition weapon = null, ThrowableDefinition throwable = null)
        {
            var bullet = _pool.Count > 0 ? _pool.Pop() : new Bullet();
            bullet.Id = ++_nextId;
            bullet.Position = origin;
            bullet.PreviousPosition = origin;
            bullet.Floor = floor;
            bullet.Direction = direction.normalized;
            bullet.Speed = speed;
            bullet.Damage = damage;
            bullet.Age = 0f;
            bullet.RangeLeft = range;
            bullet.Travel = range;
            bullet.Pierce = pierce;
            bullet.Explosive = explosive;
            bullet.Weapon = weapon;
            bullet.Throwable = throwable;
            bullet.Ignore = CombatTarget.None;
            _active.Add(bullet);
            Spawned?.Invoke(bullet);
            return bullet;
        }

        public void Tick(float deltaTime)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var bullet = _active[i];
                bullet.PreviousPosition = bullet.Position;
                bullet.Age += deltaTime;
                var distance = Mathf.Min(bullet.Speed * deltaTime, bullet.RangeLeft);
                var next = bullet.Position + bullet.Direction * distance;
                if (bullet.Throwable != null)
                {
                    TickThrown(i, bullet, next, distance);
                    continue;
                }

                var hit = _targets.Segment(bullet.PreviousPosition, next, bullet.Floor, bullet.Ignore);
                switch (hit.Kind)
                {
                    case CombatHitKind.Target:
                        bullet.Position = hit.Point;
                        _targets.Damage(hit.Target, bullet.Damage, bullet.Direction, bullet.Weapon);
                        if (bullet.Explosive != null)
                        {
                            Explode(bullet, hit.Point);
                        }
                        else if (bullet.Pierce > 0)
                        {
                            bullet.Pierce--;
                            bullet.Ignore = hit.Target;
                            bullet.RangeLeft -= distance * hit.T;
                            continue;
                        }

                        End(i, BulletEnd.Hit);
                        continue;
                    case CombatHitKind.Blocker:
                        // Just short of the wall: the blast is on this side.
                        bullet.Position = hit.Point - bullet.Direction * 0.05f;
                        if (bullet.Explosive != null) Explode(bullet, bullet.Position);
                        bullet.Position = hit.Point;
                        End(i, BulletEnd.Blocked);
                        continue;
                }

                bullet.Position = next;
                bullet.RangeLeft -= distance;
                if (bullet.RangeLeft <= 1e-4f)
                {
                    if (bullet.Explosive != null) Explode(bullet, next);
                    End(i, BulletEnd.Expired);
                }
            }
        }

        /// <summary>Removes every bullet (level stop).</summary>
        public void Clear()
        {
            for (var i = _active.Count - 1; i >= 0; i--)
                End(i, BulletEnd.Cleared);
        }

        public void Dispose() => Clear();

        /// <summary>Damage of an explosion at <paramref name="at"/> to every target in the radius not behind a wall.</summary>
        public void Explode(Vector2 at, ExplosiveAttack explosive, int floor)
        {
            _targets.Blast(at, explosive.ExplosionDamage, explosive.Radius, explosive.EdgeDamage, floor);
            Exploded?.Invoke(at, explosive.Radius);
        }

        private void TickThrown(int index, Bullet bullet, Vector2 next, float distance)
        {
            if (!_targets.IsClear(bullet.PreviousPosition, next, bullet.Floor))
            {
                // Falls where it is, just short of the wall.
                End(index, BulletEnd.Blocked);
                return;
            }

            bullet.Position = next;
            bullet.RangeLeft -= distance;
            if (bullet.RangeLeft <= 1e-4f)
                End(index, BulletEnd.Expired);
        }

        private void Explode(Bullet bullet, Vector2 at)
        {
            // The direct hit's extra damage is the bullet's own (applied before).
            Explode(at, bullet.Explosive, bullet.Floor);
        }

        private void End(int index, BulletEnd reason)
        {
            var bullet = _active[index];
            _active.RemoveAt(index);
            Ended?.Invoke(bullet, bullet.Position, reason);
            bullet.Explosive = null;
            bullet.Weapon = null;
            bullet.Throwable = null;
            bullet.Ignore = CombatTarget.None;
            _pool.Push(bullet);
        }
    }
}
