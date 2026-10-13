using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Navigation;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Level;
using Maze.Gameplay.Map;
using Maze.Gameplay.Pickups;
using Maze.Gameplay.Player;

namespace Maze.Gameplay.Navigation
{
    /// <summary>What a route hint leads to.</summary>
    public enum RouteTarget
    {
        None = 0,
        MapFragment = 1,
        Exit = 2,
        Key = 3,
        Door = 4,

        /// <summary>A cell chosen by the game (the tutorial), not by <see cref="RouteHintSystem.Request"/>.</summary>
        Guide = 5,
    }

    /// <summary>
    /// Where the player can walk for a route hint: floor and doors (closed ones too — the player opens them); a locked
    /// door only with its key in the inventory. Zombies are ignored (they move). Struct: no boxing in the search.
    /// </summary>
    public readonly struct PlayerRoutePassability : IGridPassability
    {
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;
        private readonly PlayerInventory _inventory;

        public PlayerRoutePassability(LevelGrid grid, DoorSystem doors, PlayerInventory inventory)
        {
            _grid = grid;
            _doors = doors;
            _inventory = inventory;
        }

        public bool IsPassable(GridPosition position)
        {
            switch (_grid.GetCellOrWall(position))
            {
                case CellType.Floor:
                    return true;
                case CellType.Door:
                    return !_doors.TryGetDoor(position, out var door) || !_doors.IsLocked(door.Id) ||
                           _inventory.HasKey(door.KeyId);
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Route hint (a navigator to the next useful place). <see cref="Request"/> picks the target — the nearest by path
    /// length among those the player can reach now (<see cref="PlayerRoutePassability"/>), in this order: a map fragment
    /// not collected yet → an exit → a key lying in the level → a locked door whose key the player carries — and keeps
    /// the path from the player's cell to it. Walking along the path cuts the part behind; leaving it searches a new
    /// path to the same target (one search per cell change off the route, never per frame). The hint ends when the
    /// player reaches the target (enters its cell; a door — stands next to it or unlocks it) or a new hint replaces it.
    /// </summary>
    public sealed class RouteHintSystem : ILevelLoadStep, IDisposable
    {
        private readonly LevelData _level;
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;
        private readonly PlayerSystem _player;
        private readonly PlayerInventory _inventory;
        private readonly MapSystem _map;
        private readonly PickupSystem _pickups;
        private readonly List<GridPosition> _path = new List<GridPosition>();
        private readonly List<Pickup> _lying = new List<Pickup>();
        private GridBfs _search;
        private string _targetDoorId;
        private bool _adjacent;
        private bool _subscribed;

        public RouteHintSystem(LevelData level, LevelGrid grid, DoorSystem doors, PlayerSystem player,
            PlayerInventory inventory, MapSystem map, PickupSystem pickups)
        {
            _level = level;
            _grid = grid;
            _doors = doors;
            _player = player;
            _inventory = inventory;
            _map = map;
            _pickups = pickups;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        public bool IsActive => Target != RouteTarget.None;

        public RouteTarget Target { get; private set; }

        public GridPosition TargetCell { get; private set; }

        /// <summary>From the player's cell to the target, both included; empty when there is no hint.</summary>
        public IReadOnlyList<GridPosition> Path => _path;

        /// <summary>Grows with every change of the path (views rebuild only then).</summary>
        public int Version { get; private set; }

        /// <summary>The path changed: a new hint, progress along it, a detour, or the hint ended.</summary>
        public event Action Changed;

        /// <summary>A hint was asked for (<see cref="Request"/>) and what it found.</summary>
        public event Action<RouteTarget> Requested;

        /// <summary>The player got to the target of the hint (it has ended).</summary>
        public event Action<RouteTarget> Reached;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _search ??= new GridBfs(_grid.Width, _grid.Height);
            if (!_subscribed)
            {
                _player.CellChanged += OnCellChanged;
                _doors.DoorUnlocked += OnDoorUnlocked;
                _subscribed = true;
            }

            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _player.CellChanged -= OnCellChanged;
            _doors.DoorUnlocked -= OnDoorUnlocked;
            _subscribed = false;
        }

        /// <summary>Picks the target and lays the path to it; <see cref="RouteTarget.None"/> when nothing is reachable.</summary>
        public RouteTarget Request()
        {
            var target = Lay();
            Requested?.Invoke(target);
            return target;
        }

        private RouteTarget Lay()
        {
            if (!_player.IsSpawned || _search == null)
            {
                Clear();
                return RouteTarget.None;
            }

            var from = _player.Cell;
            _search.Run(from, Passability);

            var best = new Candidate();
            foreach (var fragment in _level.MapFragments)
                if (!_map.IsCollected(fragment))
                    Consider(ref best, fragment.Position, null);
            var kind = RouteTarget.MapFragment;

            if (!best.Found)
            {
                kind = RouteTarget.Exit;
                foreach (var exit in _level.Exits) Consider(ref best, exit.Position, null);
            }

            if (!best.Found)
            {
                kind = RouteTarget.Key;
                _pickups.GetAll(_lying);
                foreach (var pickup in _lying)
                    if (pickup.Kind == PickupKind.Key)
                        Consider(ref best, pickup.Cell, null);
                _lying.Clear();
            }

            if (!best.Found)
            {
                kind = RouteTarget.Door;
                foreach (var door in _level.Doors)
                    if (_doors.IsLocked(door.Id) && _inventory.HasKey(door.KeyId))
                        Consider(ref best, door.Position, door.Id);
            }

            if (!best.Found || !_search.TryGetPath(best.Cell, _path))
            {
                Clear();
                return RouteTarget.None;
            }

            Target = kind;
            TargetCell = best.Cell;
            _targetDoorId = best.DoorId;
            _adjacent = kind == RouteTarget.Door;
            Version++;
            Changed?.Invoke();
            return kind;
        }

        /// <summary>
        /// Leads to a given cell (<see cref="RouteTarget.Guide"/>; the tutorial): one search now, then like any hint.
        /// <paramref name="adjacent"/> — reached from a next cell (a door). False and no route when the player is already
        /// there or cannot get there.
        /// </summary>
        public bool Guide(GridPosition cell, bool adjacent)
        {
            if (!_player.IsSpawned || _search == null || IsNear(_player.Cell, cell, adjacent))
            {
                Clear();
                return false;
            }

            _search.Run(_player.Cell, Passability);
            if (!_search.TryGetPath(cell, _path))
            {
                Clear();
                return false;
            }

            Target = RouteTarget.Guide;
            TargetCell = cell;
            _targetDoorId = null;
            _adjacent = adjacent;
            Version++;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Ends the hint (no event <see cref="Reached"/>).</summary>
        public void Clear()
        {
            if (Target == RouteTarget.None && _path.Count == 0) return;
            Target = RouteTarget.None;
            _targetDoorId = null;
            _path.Clear();
            Version++;
            Changed?.Invoke();
        }

        private PlayerRoutePassability Passability => new PlayerRoutePassability(_grid, _doors, _inventory);

        /// <summary>The nearest reachable cell wins; the player's own cell does not count (nothing to show).</summary>
        private void Consider(ref Candidate best, GridPosition cell, string doorId)
        {
            var distance = _search.DistanceTo(cell);
            if (distance <= 0 || (best.Found && distance >= best.Distance)) return;
            best = new Candidate { Found = true, Cell = cell, Distance = distance, DoorId = doorId };
        }

        private void OnCellChanged(GridPosition from, GridPosition to)
        {
            if (!IsActive) return;
            if (IsAtTarget(to))
            {
                Finish();
                return;
            }

            var index = _path.IndexOf(to);
            if (index > 0)
            {
                _path.RemoveRange(0, index); // Walked forward: the part behind goes.
            }
            else if (index < 0)
            {
                // Off the route: a new path to the same target, like a navigator.
                _search.Run(to, Passability);
                if (!_search.TryGetPath(TargetCell, _path))
                {
                    Clear();
                    return;
                }
            }
            else
            {
                return;
            }

            Version++;
            Changed?.Invoke();
        }

        private bool IsAtTarget(GridPosition cell) => IsNear(cell, TargetCell, _adjacent);

        /// <summary>The cell itself, or also a next one (a door is used from the next cell).</summary>
        private static bool IsNear(GridPosition cell, GridPosition target, bool adjacent)
        {
            if (!adjacent) return cell == target;
            return Math.Abs(cell.X - target.X) + Math.Abs(cell.Y - target.Y) <= 1;
        }

        private void OnDoorUnlocked(DoorData door)
        {
            if (Target == RouteTarget.Door && door.Id == _targetDoorId) Finish();
        }

        private void Finish()
        {
            var target = Target;
            Clear();
            Reached?.Invoke(target);
        }

        private struct Candidate
        {
            public bool Found;
            public GridPosition Cell;
            public int Distance;
            public string DoorId;
        }
    }
}
