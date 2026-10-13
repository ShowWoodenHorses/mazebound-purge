using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Level;
using UnityEngine;

namespace Maze.Gameplay.Player
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

        private readonly LevelData _level;
        private readonly PlayerDefinition _definition;
        private readonly IPlayerInput _input;
        private readonly LevelPassability _passability;
        private readonly OccupancyMap _occupancy;
        private readonly LevelLaunchOptions _options;
        private readonly IAimSettings _aim;
        private bool _holdStill;
        private bool _moved;
        private Vector2 _holdFacing;

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

        /// <summary>
        /// Unit look direction: the last movement direction (ТЗ §65). When the player stops it snaps to the aim
        /// directions (<see cref="IAimSettings.AimMode"/>), so standing still shows where an attack would go.
        /// An attack sets it exactly (it may aim at a target).
        /// </summary>
        public Vector2 Facing { get; private set; } = Vector2.up;

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
        /// The player attacks this tick (ТЗ §66): no movement during this tick, facing turns to
        /// <paramref name="facing"/>. Movement is allowed again on the next tick.
        /// </summary>
        public void HoldStill(Vector2 facing)
        {
            _holdStill = true;
            _holdFacing = facing;
        }

        public void Tick(float deltaTime)
        {
            if (!IsSpawned)
                return;

            if (_holdStill)
            {
                _holdStill = false;
                if (_holdFacing.sqrMagnitude > 0f)
                    Facing = _holdFacing.normalized;
                SpeedFactor = 0f;
                _moved = false;
                return;
            }

            var move = Vector2.ClampMagnitude(_input.Move, 1f);
            if (move.sqrMagnitude < DeadZone * DeadZone || deltaTime <= 0f)
            {
                if (_moved) Facing = Aim.Snap(Facing, _aim.AimMode);
                _moved = false;
                SpeedFactor = 0f;
                return;
            }

            Facing = move.normalized;
            _moved = true;
            var maxDistance = _definition.MoveSpeed * deltaTime;
            var slow = _passability.IsSnowdrift(Cell) ? _definition.SnowdriftSpeed : 1f;
            var target = PlayerMovement.Move(Position, move * (maxDistance * slow), _definition.BodyHalfSize, _definition.CornerAssist, this);
            // Of the full speed: in a snowdrift the walk animation slows down too.
            SpeedFactor = Mathf.Clamp01((target - Position).magnitude / maxDistance);
            Position = target;

            var cell = PlayerMovement.CellOf(Position);
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

        private void Spawn(int startIndex)
        {
            var start = _level.PlayerStarts[startIndex];
            _occupancy.SetPlayer(start.Position);
            StartIndex = startIndex;
            Cell = start.Position;
            Position = new Vector2(start.Position.X, start.Position.Y);
            Facing = InitialFacing(start.Position);
            _moved = false;
            SpeedFactor = 0f;
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
