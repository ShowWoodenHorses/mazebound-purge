using System;
using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Navigation;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Grid;

namespace Maze.Gameplay.Navigation
{
    /// <summary>Zombie walkability (ТЗ §52): Floor and open doors; zombies never open doors. Struct: no boxing in A*.</summary>
    public readonly struct LevelWalkability : IGridPassability
    {
        private readonly LevelPassability _passability;

        public LevelWalkability(LevelPassability passability)
        {
            _passability = passability;
        }

        public bool IsPassable(GridPosition position) => _passability.IsWalkable(position);
    }

    /// <summary>Step costs of zombie routes: a snowdrift costs <see cref="NavigationSystem.SnowdriftCost"/> steps.</summary>
    public readonly struct LevelStepCost : IGridCost
    {
        private readonly LevelPassability _passability;

        public LevelStepCost(LevelPassability passability)
        {
            _passability = passability;
        }

        public int StepCost(GridPosition to) => _passability.IsSnowdrift(to) ? NavigationSystem.SnowdriftCost : 1;
    }

    /// <summary>
    /// Grid navigation for AI (ТЗ §52): 4-directional A* over walls and closed doors. Players, zombies and pickups are
    /// not blockers. Callers search only when their target, AI task or doors change — <see cref="Version"/> grows on
    /// every door change, so a stored path is known to be stale. One pathfinder, reused, no allocations per search.
    /// Routes are cheapest, not shortest: a snowdrift cell costs <see cref="SnowdriftCost"/> steps, so zombies walk
    /// around drifts when the detour is short and wade through them otherwise.
    /// </summary>
    public sealed class NavigationSystem : IDisposable
    {
        /// <summary>Route cost of stepping into a snowdrift (an ordinary cell costs 1).</summary>
        public const int SnowdriftCost = 3;

        private readonly LevelPassability _passability;
        private readonly DoorSystem _doors;
        private readonly GridPathfinder _pathfinder;

        public NavigationSystem(LevelGrid grid, LevelPassability passability, DoorSystem doors)
        {
            _passability = passability;
            _doors = doors;
            _pathfinder = new GridPathfinder(grid.Width, grid.Height);
            _doors.DoorChanged += OnDoorChanged;
        }

        /// <summary>Grows whenever paths may have become invalid (a door opened or closed).</summary>
        public int Version { get; private set; }

        /// <summary>Searches made so far (diagnostics and tests: A* must not run every frame).</summary>
        public int SearchCount { get; private set; }

        public bool IsPassable(GridPosition cell) => _passability.IsWalkable(cell);

        public bool IsSnowdrift(GridPosition cell) => _passability.IsSnowdrift(cell);

        /// <summary>Cells from <paramref name="start"/> to <paramref name="goal"/> inclusive; false when unreachable.</summary>
        public bool TryFindPath(GridPosition start, GridPosition goal, List<GridPosition> path)
        {
            SearchCount++;
            return _pathfinder.TryFindPath(start, goal, new LevelWalkability(_passability), new LevelStepCost(_passability), path);
        }

        public void Dispose() => _doors.DoorChanged -= OnDoorChanged;

        private void OnDoorChanged(DoorData door, bool open) => Version++;
    }
}
