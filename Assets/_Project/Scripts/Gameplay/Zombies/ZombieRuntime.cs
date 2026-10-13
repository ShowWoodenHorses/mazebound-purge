using System;
using System.Collections.Generic;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Spatial;
using UnityEngine;

namespace Maze.Gameplay.Zombies
{
    /// <summary>ТЗ §74 plus <see cref="Alert"/>.</summary>
    public enum ZombieState
    {
        Idle = 0,
        Patrol = 1,
        Chase = 2,
        Attack = 3,
        Return = 4,
        Dead = 5,
        /// <summary>
        /// Beyond ТЗ §74: noticed the player and roars for <see cref="ZombieController.AlertDuration"/> before every
        /// chase that starts from Idle / Patrol / Return.
        /// </summary>
        Alert = 6,
    }

    /// <summary>
    /// Runtime state of one zombie (ТЗ §79: data → runtime → view). The AI that drives it is
    /// <see cref="ZombieController"/>; the view only reads this. Hidden zombies keep running (ТЗ §57).
    /// </summary>
    public sealed class ZombieRuntime : IDamageable
    {
        public const float BodyRadius = 0.35f;

        public ZombieRuntime(ZombieSpawnData spawn, IReadOnlyList<GridPosition> patrol)
        {
            Spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
            if (spawn.Definition == null)
                throw new ArgumentException($"Zombie '{spawn.Id}' has no definition.", nameof(spawn));

            Patrol = patrol != null && patrol.Count > 0 ? patrol : null;
            Cell = spawn.Position;
            Position = new Vector2(spawn.Position.X, spawn.Position.Y);
            var offset = spawn.Facing.ToOffset();
            Facing = new Vector2(offset.X, offset.Y);
            Health = Definition.MaxHp;
            State = Patrol != null ? ZombieState.Patrol : ZombieState.Idle;
        }

        public string Id => Spawn.Id;
        public ZombieSpawnData Spawn { get; }
        public ZombieDefinition Definition => Spawn.Definition;

        /// <summary>Patrol loop A → B → … → A, or null (stands at the spawn).</summary>
        public IReadOnlyList<GridPosition> Patrol { get; }

        public Vector2 Position { get; internal set; }
        public GridPosition Cell { get; internal set; }
        public Vector2 Facing { get; internal set; }
        public float Radius => BodyRadius;
        public float Health { get; private set; }

        /// <summary>Damage of the last hit as dealt (not cut to the HP left) — also of the killing one.</summary>
        public float LastDamage { get; private set; }
        public ZombieState State { get; internal set; }
        public bool IsAlive => State != ZombieState.Dead;

        /// <summary>Actual speed this tick relative to MoveSpeed, 0..1 (animation).</summary>
        public float SpeedFactor { get; internal set; }

        /// <summary>(zombie, damage) — after a hit that did not kill.</summary>
        public event Action<ZombieRuntime, float> Damaged;

        public event Action<ZombieRuntime> Died;

        /// <summary>
        /// Takes damage. Never makes the zombie react by itself (ТЗ §76): only detection starts a chase.
        /// </summary>
        public void ApplyDamage(float amount, Vector2 direction)
        {
            if (!IsAlive || amount <= 0f)
                return;

            LastDamage = amount;
            Health = Mathf.Max(0f, Health - amount);
            if (Health > 0f)
            {
                Damaged?.Invoke(this, amount);
                return;
            }

            State = ZombieState.Dead;
            SpeedFactor = 0f;
            Died?.Invoke(this);
        }
    }
}
