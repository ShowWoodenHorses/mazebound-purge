using System;
using System.Collections.Generic;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Navigation;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Spatial;
using UnityEngine;

namespace Maze.Gameplay.Zombies
{
    /// <summary>
    /// Detection (ТЗ §73): only reports where the player was noticed; it never drives the AI (ТЗ §74).
    /// <list type="bullet">
    /// <item>Vision only: the player within VisionRange, inside the VisionAngle cone around the facing, with a clear
    /// line (walls and closed doors block).</item>
    /// <item>Hearing only: a sound whose position is within both the sound's radius and the HearingRadius.</item>
    /// <item>Vision + hearing: sees all around — the player within DetectionRadius with a clear line (walls and closed
    /// doors block, as for vision only; the zone shown on the floor is exactly this), and hears sounds as hearing only.</item>
    /// </list>
    /// </summary>
    public static class ZombieDetection
    {
        public static bool Sees(ZombieRuntime zombie, Vector2 player, ISpatialQueryService spatial)
        {
            var definition = zombie.Definition;
            var offset = player - zombie.Position;
            var distance = offset.magnitude;
            if (distance > definition.VisionRange)
                return false;
            if (distance > 1e-3f && Vector2.Angle(zombie.Facing, offset) > definition.VisionAngle * 0.5f)
                return false;
            return spatial.IsClear(zombie.Position, player);
        }

        /// <summary>Vision + hearing: all around within DetectionRadius, walls and closed doors block.</summary>
        public static bool SeesAround(ZombieRuntime zombie, Vector2 player, ISpatialQueryService spatial)
        {
            var radius = zombie.Definition.DetectionRadius;
            return (player - zombie.Position).sqrMagnitude <= radius * radius && spatial.IsClear(zombie.Position, player);
        }

        /// <summary>Whether this type hears sounds at all.</summary>
        public static bool CanHear(ZombieDefinition definition) =>
            definition.DetectionType == ZombieDetectionType.HearingOnly ||
            definition.DetectionType == ZombieDetectionType.VisionAndHearing;

        public static bool Hears(ZombieRuntime zombie, SoundEvent sound)
        {
            var reach = Mathf.Min(sound.Radius, zombie.Definition.HearingRadius);
            return (sound.Position - zombie.Position).sqrMagnitude <= reach * reach;
        }
    }

    /// <summary>
    /// The AI of one zombie (ТЗ §74–77): Idle / Patrol / Chase / Attack / Return.
    /// <list type="bullet">
    /// <item>Idle — stands at the spawn; Patrol — walks the loop A → B → … → A.</item>
    /// <item>Alert — on detection from Idle / Patrol / Return: stands and roars for <see cref="AlertDuration"/>, then
    /// Chase; the player stepping next to it → Attack at once.</item>
    /// <item>Chase — runs (ChaseSpeed) to the last known player cell (updated while detected). Reaching it without
    /// noticing the player again → Return.</item>
    /// <item>Attack — the player is in a side-adjacent cell (ТЗ §77): hits every AttackInterval (first hit after half
    /// of it). The player leaving → Chase.</item>
    /// <item>Return — to the nearest patrol point (then Patrol continues from it) or to the spawn (then Idle).</item>
    /// </list>
    /// Moves cell to cell along A* paths (searched only when the goal or doors change), never into the player's cell;
    /// zombies pass through each other. Detection is sampled every <see cref="DetectionInterval"/>, not every frame.
    /// </summary>
    public sealed class ZombieController
    {
        public const float DetectionInterval = 0.1f;

        /// <summary>Seconds a zombie roars (Alert) after noticing the player, before it starts the chase.</summary>
        public const float AlertDuration = 0.5f;
        private const float ArriveDistance = 0.02f;

        private readonly NavigationSystem _navigation;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _playerHealth;
        private readonly OccupancyMap _occupancy;
        private readonly ISpatialQueryService _spatial;
        private readonly List<GridPosition> _path = new List<GridPosition>();

        private bool _hasPath;
        private GridPosition _pathGoal;
        private int _pathIndex;
        private int _pathVersion;
        private float _detectTimer;
        private bool _hasTarget;
        private GridPosition _target;
        private float _attackTimer;
        private float _alertTimer;
        private int _patrolIndex;
        private GridPosition _returnGoal;
        private int _returnPatrolIndex = -1;

        public ZombieController(ZombieRuntime zombie, NavigationSystem navigation, PlayerSystem player, PlayerHealth playerHealth,
            OccupancyMap occupancy, ISpatialQueryService spatial, float detectionPhase)
        {
            Zombie = zombie;
            _navigation = navigation;
            _player = player;
            _playerHealth = playerHealth;
            _occupancy = occupancy;
            _spatial = spatial;
            _detectTimer = detectionPhase;
        }

        public ZombieRuntime Zombie { get; }

        /// <summary>(zombie) after each hit on the player.</summary>
        public event Action<ZombieRuntime> Attacked;

        /// <summary>Called for every gameplay sound (only hearing zombies use it).</summary>
        public void Hear(SoundEvent sound)
        {
            if (!Zombie.IsAlive || !ZombieDetection.CanHear(Zombie.Definition))
                return;
            if (ZombieDetection.Hears(Zombie, sound))
                Notice(PlayerMovement.CellOf(sound.Position));
        }

        public void Tick(float deltaTime)
        {
            if (!Zombie.IsAlive)
                return;

            Zombie.SpeedFactor = 0f;
            Detect(deltaTime);

            switch (Zombie.State)
            {
                case ZombieState.Idle:
                case ZombieState.Patrol:
                case ZombieState.Return:
                    if (_hasTarget) Enter(ZombieState.Alert);
                    break;
            }

            switch (Zombie.State)
            {
                case ZombieState.Idle: break;
                case ZombieState.Patrol: TickPatrol(deltaTime); break;
                case ZombieState.Alert: TickAlert(deltaTime); break;
                case ZombieState.Chase: TickChase(deltaTime); break;
                case ZombieState.Attack: TickAttack(deltaTime); break;
                case ZombieState.Return: TickReturn(deltaTime); break;
            }
        }

        // ------------------------------------------------------------------ Detection

        private void Detect(float deltaTime)
        {
            _detectTimer -= deltaTime;
            if (_detectTimer > 0f || !_player.IsSpawned)
                return;

            _detectTimer += DetectionInterval;
            if (_detectTimer < 0f) _detectTimer = DetectionInterval;

            var player = _player.Position;
            switch (Zombie.Definition.DetectionType)
            {
                case ZombieDetectionType.VisionOnly:
                    if (ZombieDetection.Sees(Zombie, player, _spatial)) Notice(_player.Cell);
                    break;
                case ZombieDetectionType.VisionAndHearing:
                    if (ZombieDetection.SeesAround(Zombie, player, _spatial)) Notice(_player.Cell);
                    break;
            }
        }

        /// <summary>Chase runs (ChaseSpeed), everything else walks (MoveSpeed).</summary>
        private float CurrentSpeed => Zombie.State == ZombieState.Chase ? Zombie.Definition.ChaseSpeed : Zombie.Definition.MoveSpeed;

        private void Notice(GridPosition cell)
        {
            _hasTarget = true;
            _target = cell;
        }

        // ------------------------------------------------------------------ States

        private void Enter(ZombieState state)
        {
            Zombie.State = state;
            switch (state)
            {
                case ZombieState.Attack:
                    _attackTimer = Zombie.Definition.AttackInterval * 0.5f;
                    break;
                case ZombieState.Alert:
                    _alertTimer = AlertDuration;
                    break;
                case ZombieState.Return:
                    ChooseReturnGoal();
                    break;
            }
        }

        private void TickPatrol(float deltaTime)
        {
            var points = Zombie.Patrol;
            if (points == null)
            {
                Enter(ZombieState.Idle);
                return;
            }

            var result = MoveTo(points[_patrolIndex], deltaTime);
            if (result != MoveResult.Moving)
                _patrolIndex = (_patrolIndex + 1) % points.Count; // arrived, or unreachable: try the next point
        }

        /// <summary>Stands roaring toward the target, then chases; the player stepping next to it cuts the roar short.</summary>
        private void TickAlert(float deltaTime)
        {
            if (IsPlayerAdjacent())
            {
                Enter(ZombieState.Attack);
                return;
            }

            var toTarget = new Vector2(_target.X, _target.Y) - Zombie.Position;
            if (toTarget.sqrMagnitude > 1e-6f) Zombie.Facing = toTarget.normalized;

            _alertTimer -= deltaTime;
            if (_alertTimer <= 0f)
                Enter(ZombieState.Chase);
        }

        private void TickChase(float deltaTime)
        {
            if (IsPlayerAdjacent())
            {
                Enter(ZombieState.Attack);
                return;
            }

            if (!_hasTarget)
            {
                LoseTarget();
                return;
            }

            // The target follows the player while detected; reaching it means the player was lost there.
            switch (MoveTo(_target, deltaTime))
            {
                case MoveResult.Arrived:
                case MoveResult.Unreachable:
                    LoseTarget();
                    break;
            }
        }

        private void TickAttack(float deltaTime)
        {
            if (!IsPlayerAdjacent())
            {
                Enter(_hasTarget ? ZombieState.Chase : ZombieState.Return);
                return;
            }

            // Settle in the middle of its own cell (it may have stopped at the edge) — never into another cell.
            var center = new Vector2(Zombie.Cell.X, Zombie.Cell.Y);
            Zombie.Position = Vector2.MoveTowards(Zombie.Position, center, Zombie.Definition.MoveSpeed * deltaTime);

            var toPlayer = _player.Position - Zombie.Position;
            if (toPlayer.sqrMagnitude > 1e-6f) Zombie.Facing = toPlayer.normalized;
            Notice(_player.Cell);

            _attackTimer -= deltaTime;
            if (_attackTimer > 0f)
                return;

            _attackTimer += Zombie.Definition.AttackInterval;
            _playerHealth.Damage(Mathf.Max(1, Mathf.RoundToInt(Zombie.Definition.Damage)));
            Attacked?.Invoke(Zombie);
        }

        private void TickReturn(float deltaTime)
        {
            var result = MoveTo(_returnGoal, deltaTime);
            if (result == MoveResult.Moving || result == MoveResult.Blocked)
                return;

            if (_returnPatrolIndex >= 0)
            {
                _patrolIndex = (_returnPatrolIndex + 1) % Zombie.Patrol.Count;
                Enter(ZombieState.Patrol);
            }
            else
            {
                var offset = Zombie.Spawn.Facing.ToOffset();
                Zombie.Facing = new Vector2(offset.X, offset.Y);
                Enter(ZombieState.Idle);
            }
        }

        private void LoseTarget()
        {
            _hasTarget = false;
            Enter(ZombieState.Return);
        }

        /// <summary>The nearest patrol point (straight-line), or the spawn when there is no patrol (ТЗ §75).</summary>
        private void ChooseReturnGoal()
        {
            _returnPatrolIndex = -1;
            _returnGoal = Zombie.Spawn.Position;
            var points = Zombie.Patrol;
            if (points == null)
                return;

            var best = float.PositiveInfinity;
            for (var i = 0; i < points.Count; i++)
            {
                var distance = (new Vector2(points[i].X, points[i].Y) - Zombie.Position).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    _returnPatrolIndex = i;
                }
            }

            _returnGoal = points[_returnPatrolIndex];
        }

        private bool IsPlayerAdjacent()
        {
            if (!_player.IsSpawned || _playerHealth.IsDead)
                return false;
            var cell = _player.Cell;
            return Math.Abs(cell.X - Zombie.Cell.X) + Math.Abs(cell.Y - Zombie.Cell.Y) == 1 &&
                   _navigation.IsPassable(cell);
        }

        // ------------------------------------------------------------------ Movement

        private enum MoveResult
        {
            Moving = 0,
            Arrived = 1,
            /// <summary>No path to the goal (walls, closed doors).</summary>
            Unreachable = 2,
            /// <summary>The next cell is temporarily taken (the player stands there) or just became closed.</summary>
            Blocked = 3,
        }

        private MoveResult MoveTo(GridPosition goal, float deltaTime)
        {
            var goalCenter = new Vector2(goal.X, goal.Y);
            if (Zombie.Cell == goal && (Zombie.Position - goalCenter).sqrMagnitude <= ArriveDistance * ArriveDistance)
            {
                Zombie.Position = goalCenter;
                return MoveResult.Arrived;
            }

            if (!_hasPath || _pathGoal != goal || _pathVersion != _navigation.Version)
            {
                _hasPath = _navigation.TryFindPath(Zombie.Cell, goal, _path);
                _pathGoal = goal;
                _pathVersion = _navigation.Version;
                _pathIndex = _path.Count > 1 ? 1 : 0;
                if (!_hasPath)
                    return MoveResult.Unreachable;
            }

            if (_pathIndex >= _path.Count)
                _pathIndex = _path.Count - 1;
            var next = _path[_pathIndex];
            if (!_navigation.IsPassable(next))
            {
                _hasPath = false; // a door closed in front: search again next tick
                return MoveResult.Blocked;
            }

            var target = new Vector2(next.X, next.Y);
            var offset = target - Zombie.Position;
            var distance = offset.magnitude;
            var fullStep = CurrentSpeed * deltaTime;
            var step = _navigation.IsSnowdrift(Zombie.Cell) ? fullStep * Zombie.Definition.SnowdriftSpeed : fullStep;
            var newPosition = distance <= step ? target : Zombie.Position + offset / distance * step;

            var newCell = PlayerMovement.CellOf(newPosition);
            if (newCell != Zombie.Cell)
            {
                if (!_occupancy.CanZombieEnter(newCell))
                    return MoveResult.Blocked;
                _occupancy.MoveZombie(Zombie.Cell, newCell);
                Zombie.Cell = newCell;
            }

            if (distance > 1e-5f) Zombie.Facing = offset / distance;
            Zombie.SpeedFactor = deltaTime > 0f ? Mathf.Clamp01((newPosition - Zombie.Position).magnitude / fullStep) : 0f;
            Zombie.Position = newPosition;
            if (distance <= step && _pathIndex < _path.Count - 1)
                _pathIndex++;
            return MoveResult.Moving;
        }
    }
}
