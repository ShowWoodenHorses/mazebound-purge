using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visibility;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;

namespace Maze.Gameplay.Visibility
{
    /// <summary>Sight blockers of the running level (ТЗ §55): Wall and closed Door; open Door and Floor are see-through.</summary>
    public readonly struct LevelOpacity : IGridOpacity
    {
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;

        public LevelOpacity(LevelGrid grid, DoorSystem doors)
        {
            _grid = grid;
            _doors = doors;
        }

        public bool IsOpaque(GridPosition position)
        {
            switch (_grid.GetCellOrWall(position))
            {
                case CellType.Floor: return false;
                case CellType.Door: return !_doors.IsOpenAt(position);
                default: return true;
            }
        }
    }

    /// <summary>
    /// VisibleCells of the player (ТЗ §53–56, §106): 11×11 around the player's cell plus line of sight.
    /// Recalculated only on events — the player spawned, PlayerCellChanged, a door in the window opened or closed —
    /// never per frame. Knows nothing about views: <c>VisibilityController</c> applies the result.
    /// Visibility only hides views; AI and simulation ignore it (ТЗ §57).
    /// </summary>
    public sealed class VisibilitySystem : ILevelLoadStep, IDisposable
    {
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;
        private readonly PlayerSystem _player;
        private readonly FieldOfView _fieldOfView;
        private bool _subscribed;

        public VisibilitySystem(LevelGrid grid, DoorSystem doors, PlayerSystem player)
        {
            _grid = grid;
            _doors = doors;
            _player = player;
            _fieldOfView = new FieldOfView(grid.Width, grid.Height);
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeVisibility;

        public int Radius => FieldOfView.DefaultRadius;

        /// <summary>False until the player spawned: then nothing is visible.</summary>
        public bool HasResult => _fieldOfView.HasResult;

        public IReadOnlyList<GridPosition> VisibleCells => _fieldOfView.VisibleCells;

        /// <summary>How many times visibility was calculated (diagnostics and tests).</summary>
        public int RecalculationCount { get; private set; }

        /// <summary>Raised after every recalculation; <see cref="VisibleCells"/> holds the new result.</summary>
        public event Action Changed;

        /// <summary>Strict visibility (11×11 + LOS): objects — zombies, pickups, doors — follow it (ТЗ §54).</summary>
        public bool IsVisible(GridPosition cell) => _fieldOfView.IsVisible(cell);

        /// <summary>Cells whose static geometry is shown: visible cells plus one-cell gaps between them, so the fog
        /// has no single-cell holes. Never used for objects.</summary>
        public IReadOnlyList<GridPosition> RevealedCells => _fieldOfView.RevealedCells;

        public bool IsRevealed(GridPosition cell) => _fieldOfView.IsRevealed(cell);

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_subscribed)
            {
                _player.Spawned += Recalculate;
                _player.CellChanged += OnPlayerCellChanged;
                _doors.DoorChanged += OnDoorChanged;
                _subscribed = true;
            }

            if (_player.IsSpawned)
                Recalculate();
            return UniTask.CompletedTask;
        }

        public void Recalculate()
        {
            if (!_player.IsSpawned)
                return;

            _fieldOfView.Compute(_player.Cell, Radius, new LevelOpacity(_grid, _doors));
            RecalculationCount++;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _player.Spawned -= Recalculate;
            _player.CellChanged -= OnPlayerCellChanged;
            _doors.DoorChanged -= OnDoorChanged;
            _subscribed = false;
        }

        private void OnPlayerCellChanged(GridPosition from, GridPosition to) => Recalculate();

        private void OnDoorChanged(DoorData door, bool open)
        {
            // A door outside the window cannot change what the player sees.
            if (HasResult && FieldOfView.Window(_fieldOfView.Origin, Radius, _grid.Width, _grid.Height).Contains(door.Position))
                Recalculate();
        }
    }
}
