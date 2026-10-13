using System;
using System.Collections.Generic;
using Maze.Gameplay.Level;
using Maze.Gameplay.Spatial;
using UnityEngine;

namespace Maze.Gameplay.Combat
{
    public enum BulletEnd
    {
        /// <summary>Wall, closed door or the edge of the grid.</summary>
        Blocked = 0,
        Hit = 1,
        /// <summary>Flew its maximum lifetime (safety net; the maze is closed, so this should not happen).</summary>
        Expired = 2,
        /// <summary>Level stopped.</summary>
        Cleared = 3,
    }

    /// <summary>A flying bullet (runtime only). Reused from a pool: keep no references after it ended.</summary>
    public sealed class Bullet
    {
        public int Id { get; internal set; }

        /// <summary>Grid units.</summary>
        public Vector2 Position { get; internal set; }

        public Vector2 PreviousPosition { get; internal set; }
        public Vector2 Direction { get; internal set; }
        public float Speed { get; internal set; }
        public float Damage { get; internal set; }
        public float Age { get; internal set; }
    }

    /// <summary>
    /// Bullets of the level (ТЗ §70). Each tick a bullet moves from PreviousPosition to Position and the segment is
    /// queried: the first wall, closed door or target ends it (open doors let it through); a target takes the damage.
    /// No physics, no per-bullet allocations (pooled).
    /// </summary>
    public sealed class BulletSystem : ILevelTickable, IDisposable
    {
        public const float MaxLifetime = 5f;

        private readonly ISpatialQueryService _spatial;
        private readonly List<Bullet> _active = new List<Bullet>();
        private readonly Stack<Bullet> _pool = new Stack<Bullet>();
        private int _nextId;

        public BulletSystem(ISpatialQueryService spatial)
        {
            _spatial = spatial;
        }

        public IReadOnlyList<Bullet> Active => _active;

        public event Action<Bullet> Spawned;

        /// <summary>(bullet, where it ended, how). Raised before the bullet returns to the pool.</summary>
        public event Action<Bullet, Vector2, BulletEnd> Ended;

        /// <summary>(target, damage, direction) — raised after a bullet damaged a target.</summary>
        public event Action<IDamageable, float, Vector2> TargetHit;

        public Bullet Spawn(Vector2 origin, Vector2 direction, float speed, float damage)
        {
            var bullet = _pool.Count > 0 ? _pool.Pop() : new Bullet();
            bullet.Id = ++_nextId;
            bullet.Position = origin;
            bullet.PreviousPosition = origin;
            bullet.Direction = direction.normalized;
            bullet.Speed = speed;
            bullet.Damage = damage;
            bullet.Age = 0f;
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
                var next = bullet.Position + bullet.Direction * (bullet.Speed * deltaTime);

                var hit = _spatial.QuerySegment(bullet.PreviousPosition, next);
                switch (hit.Kind)
                {
                    case SegmentHitKind.Object:
                        bullet.Position = hit.Point;
                        if (hit.Object is IDamageable target)
                        {
                            target.ApplyDamage(bullet.Damage, bullet.Direction);
                            TargetHit?.Invoke(target, bullet.Damage, bullet.Direction);
                        }

                        End(i, BulletEnd.Hit);
                        continue;
                    case SegmentHitKind.Blocker:
                        bullet.Position = hit.Point;
                        End(i, BulletEnd.Blocked);
                        continue;
                }

                bullet.Position = next;
                if (bullet.Age >= MaxLifetime)
                    End(i, BulletEnd.Expired);
            }
        }

        /// <summary>Removes every bullet (level stop).</summary>
        public void Clear()
        {
            for (var i = _active.Count - 1; i >= 0; i--)
                End(i, BulletEnd.Cleared);
        }

        public void Dispose() => Clear();

        private void End(int index, BulletEnd reason)
        {
            var bullet = _active[index];
            _active.RemoveAt(index);
            Ended?.Invoke(bullet, bullet.Position, reason);
            _pool.Push(bullet);
        }
    }
}
