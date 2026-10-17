using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Level;
using UnityEngine;

namespace Purge.Gameplay.Player
{
    /// <summary>Picks the player start (ТЗ §61): a forced index or a random one.</summary>
    public static class PlayerStartSelector
    {
        public static int Choose(int startCount, LevelLaunchOptions options, int fallbackSeed)
        {
            if (startCount <= 0)
                throw new InvalidOperationException("The level has no player start.");

            if (options.StartIndex.HasValue)
            {
                var index = options.StartIndex.Value;
                if (index < 0 || index >= startCount)
                    throw new ArgumentOutOfRangeException(nameof(options), index, $"The level has {startCount} player start(s).");
                return index;
            }

            return new DeterministicRandom(options.Seed ?? fallbackSeed).NextInt(startCount);
        }
    }

    /// <summary>
    /// The player in the level: spawns at a start (load stage SpawnPlayer) and moves smoothly from input every tick
    /// (ТЗ §63–65). Raises <see cref="CellChanged"/> when the logical cell changes — the trigger for visibility,
    /// exits and pickups. Holds no view: Presentation reads its state.
    /// </summary>
    public sealed class PlayerSystem : ILevelLoadStep, ILevelTickable, IPlayerBlockers
    {
        private const float DeadZone = 0.1f;

        /// <summary>1/s: how fast a push fades (14 — a recoil is over in about 0.2 s).</summary>
        private const float PushDamping = 14f;

        /// <summary>Strongest push, cells per second.</summary>
        private const float MaxPush = 8f;

        private Vector2 _push;

        private readonly LevelData _level;
        private readonly PlayerDefinition _definition;
        private readonly IPlayerInput _input;
        private readonly LevelPassability _passability;
        private readonly OccupancyMap _occupancy;
        private readonly LevelLaunchOptions _options;
        private readonly IAimSettings _aim;
        private bool _aiming;
        private bool _moved;
        private Vector2 _aimFacing;

        public PlayerSystem(LevelData level, PlayerDefinition definition, IPlayerInput input, LevelPassability passability,
            OccupancyMap occupancy, LevelLaunchOptions options, IAimSettings aim)
        {
            _aim = aim;
            _level = level;
            _definition = definition;
            _input = input;
            _passability = passability;
            _occupancy = occupancy;
            _options = options ?? LevelLaunchOptions.Default;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnPlayer;

        public bool IsSpawned { get; private set; }
        public int StartIndex { get; private set; } = -1;

        /// <summary>Continuous position in grid units (x = East, y = North).</summary>
        public Vector2 Position { get; private set; }

        public GridPosition Cell { get; private set; }

        /// <summary>Floor the player is on (the start's; stairs come with buildings).</summary>
        public int Floor => Cell.Floor;

        /// <summary>
        /// Unit look direction of the body: where the player attacks while attacking (<see cref="AimAt"/>), otherwise the
        /// last movement direction (ТЗ §65). When the player stops it snaps to the aim directions
        /// (<see cref="IAimSettings.AimMode"/>), so standing still shows where an attack would go.
        /// </summary>
        public Vector2 Facing { get; private set; } = Vector2.up;

        /// <summary>The body faced the aim this tick (<see cref="AimAt"/>), not the walk.</summary>
        public bool IsAiming { get; private set; }

        /// <summary>Unit direction the legs walk this tick; zero when standing.</summary>
        public Vector2 MoveDirection { get; private set; }

        /// <summary>
        /// Speed multiplier of the weapon in hands (ТЗ «Подвижность»), set by PlayerCombat every tick before this one;
        /// 0 = stands (a mortar while firing).
        /// </summary>
        public float WeaponSpeed { get; set; } = 1f;

        /// <summary>× speed of the states on the player (slow; <c>PlayerStatuses</c> sets it every tick before this one).</summary>
        public float StatusSpeed { get; set; } = 1f;

        /// <summary>A state holds the player in place (stun): no step, no attack (<c>PlayerStatuses</c>).</summary>
        public bool Stunned { get; set; }

        /// <summary>Actual speed this tick relative to <see cref="PlayerDefinition.MoveSpeed"/>, 0..1 (for animation).</summary>
        public float SpeedFactor { get; private set; }

        public PlayerDefinition Definition => _definition;

        public event Action Spawned;

        /// <summary>(from, to) — the player's logical cell changed (PlayerCellChanged, ТЗ §56).</summary>
        public event Action<GridPosition, GridPosition> CellChanged;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            Spawn(PlayerStartSelector.Choose(_level.PlayerStarts.Count, _options, Environment.TickCount));
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// The player attacks this tick: the body faces <paramref name="direction"/> while the legs keep walking where
        /// the stick says (shooting on the move, decision 2026-10-14).
        /// </summary>
        public void AimAt(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0f) return;
            _aiming = true;
            _aimFacing = direction.normalized;
        }

        public void Tick(float deltaTime)
        {
            if (!IsSpawned)
                return;

            var aiming = _aiming;
            _aiming = false;
            IsAiming = aiming;
            var move = Vector2.ClampMagnitude(_input.Move, 1f);
            if (move.sqrMagnitude < DeadZone * DeadZone || deltaTime <= 0f || WeaponSpeed <= 0f || Stunned)
            {
                if (aiming) Facing = _aimFacing;
                else if (_moved) Facing = Aim.Snap(Facing, _aim.AimMode);
                _moved = false;
                SpeedFactor = 0f;
                MoveDirection = Vector2.zero;
                ApplyPush(deltaTime);
                return;
            }

            MoveDirection = move.normalized;
            Facing = aiming ? _aimFacing : MoveDirection;
            _moved = true;
            var maxDistance = _definition.MoveSpeed * deltaTime;
            var slow = (_passability.IsSnowdrift(Cell) ? _definition.SnowdriftSpeed : 1f) * _passability.ObstacleSpeedFactor(Cell) *
                       WeaponSpeed * StatusSpeed;
            var target = PlayerMovement.Move(Position, move * (maxDistance * slow), _definition.BodyHalfSize, _definition.CornerAssist, this);
            // Of the full speed: in a snowdrift the walk animation slows down too.
            SpeedFactor = Mathf.Clamp01((target - Position).magnitude / maxDistance);
            Position = target;
            ApplyPush(deltaTime);
        }

        /// <summary>
        /// Pushes the player (recoil of a weapon, cells per second): added to the push in progress, which fades out fast
        /// (<see cref="PushDamping"/>) and is stopped by walls like a step.
        /// </summary>
        public void Push(Vector2 velocity)
        {
            _push += velocity;
            if (_push.sqrMagnitude > MaxPush * MaxPush) _push = _push.normalized * MaxPush;
        }

        /// <summary>The push in progress, cells per second (views, tests).</summary>
        public Vector2 PushVelocity => _push;

        private void ApplyPush(float deltaTime)
        {
            if (_push.sqrMagnitude < 1e-4f || deltaTime <= 0f)
            {
                _push = Vector2.zero;
                UpdateCell();
                return;
            }

            Position = PlayerMovement.Move(Position, _push * deltaTime, _definition.BodyHalfSize, _definition.CornerAssist, this);
            _push *= Mathf.Exp(-PushDamping * deltaTime);
            UpdateCell();
        }

        private void UpdateCell()
        {
            var cell = PlayerMovement.CellOf(Position, Floor);
            if (cell != Cell)
            {
                var previous = Cell;
                _occupancy.SetPlayer(cell);
                Cell = cell;
                CellChanged?.Invoke(previous, cell);
            }
        }

        bool IPlayerBlockers.IsWalkable(GridPosition cell) => _passability.IsWalkable(cell);

        bool IPlayerBlockers.CanEnter(GridPosition cell) => _occupancy.CanPlayerEnter(cell);

        int IPlayerBlockers.Floor => Floor;

        private void Spawn(int startIndex)
        {
            var start = _level.PlayerStarts[startIndex];
            _occupancy.SetPlayer(start.Position);
            StartIndex = startIndex;
            Cell = start.Position;
            Position = new Vector2(start.Position.X, start.Position.Y);
            Facing = InitialFacing(start.Position);
            _moved = false;
            _push = Vector2.zero;
            SpeedFactor = 0f;
            MoveDirection = Vector2.zero;
            IsSpawned = true;

            GameLog.Info(LogChannel.Gameplay, $"Player spawned at start #{startIndex} {start.Position}.");
            Spawned?.Invoke();
        }

        /// <summary>Face the first open direction (N, E, S, W) so the player does not start facing a wall.</summary>
        private Vector2 InitialFacing(GridPosition cell)
        {
            foreach (var direction in DirectionExtensions.All)
                if (_passability.IsWalkable(cell.Neighbour(direction)))
                {
                    var offset = direction.ToOffset();
                    return new Vector2(offset.X, offset.Y);
                }

            return Vector2.up;
        }
    }
}
