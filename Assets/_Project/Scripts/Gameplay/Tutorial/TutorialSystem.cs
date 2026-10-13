using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Level;
using Maze.Gameplay.Navigation;
using Maze.Gameplay.Pickups;
using Maze.Gameplay.Player;
using Maze.Gameplay.Zombies;
using UnityEngine;

namespace Maze.Gameplay.Tutorial
{
    /// <summary>
    /// Steps of the tutorial level (<see cref="LevelData.Tutorial"/>; inactive in other levels). A step is finished by the
    /// state of the game, checked on the events that can change it (cell change, pickup taken, door unlocked or moved,
    /// zombie died, map opened) — never per frame. A step already done when its turn comes (the player ran ahead) is
    /// skipped silently. The route line (<see cref="RouteHintSystem.Guide"/>) leads to the step's target; a hint the
    /// player asked for on the map is left alone until it ends.
    /// </summary>
    public sealed class TutorialSystem : ILevelLoadStep, IDisposable
    {
        private readonly TutorialScript _script;
        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly PickupSystem _pickups;
        private readonly DoorSystem _doors;
        private readonly ZombieSystem _zombies;
        private readonly RouteHintSystem _route;
        private bool _mapOpened;
        private bool _subscribed;

        public TutorialSystem(LevelData level, PlayerSystem player, PickupSystem pickups, DoorSystem doors,
            ZombieSystem zombies, RouteHintSystem route)
        {
            _level = level;
            _script = level.Tutorial;
            _player = player;
            _pickups = pickups;
            _doors = doors;
            _zombies = zombies;
            _route = route;
        }

        /// <summary>After the zombies: every target exists when the first step starts.</summary>
        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>The level is the tutorial.</summary>
        public bool IsActive => _script != null && _script.Steps.Count > 0;

        public int StepCount => _script != null ? _script.Steps.Count : 0;

        /// <summary>Index of the current step; <see cref="StepCount"/> once all are done; −1 before the start.</summary>
        public int StepIndex { get; private set; } = -1;

        /// <summary>The step in progress; null before the start and after the last one.</summary>
        public TutorialStep Current => StepIndex >= 0 && StepIndex < StepCount ? _script.Steps[StepIndex] : null;

        public bool IsFinished => IsActive && StepIndex >= StepCount;

        /// <summary>A step began (its index).</summary>
        public event Action<int> StepStarted;

        /// <summary>The last step is done.</summary>
        public event Action Finished;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!IsActive) return UniTask.CompletedTask;
            if (!_subscribed)
            {
                _player.CellChanged += OnCellChanged;
                _pickups.Removed += OnPickupRemoved;
                _doors.DoorUnlocked += OnDoorUnlocked;
                _doors.DoorChanged += OnDoorChanged;
                _zombies.Died += OnZombieDied;
                _subscribed = true;
            }

            StepIndex = -1;
            _mapOpened = false;
            Advance();
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _player.CellChanged -= OnCellChanged;
            _pickups.Removed -= OnPickupRemoved;
            _doors.DoorUnlocked -= OnDoorUnlocked;
            _doors.DoorChanged -= OnDoorChanged;
            _zombies.Died -= OnZombieDied;
            _subscribed = false;
        }

        /// <summary>The player opened the map (the map belongs to the application flow, the view reports it).</summary>
        public void ReportMapOpened()
        {
            _mapOpened = true;
            Check();
        }

        /// <summary>
        /// Where the current step points, in grid units (x = East, y = North): a zombie where it is now, other objects and
        /// cells — their centre. False when the step has nothing to point at (or its pickup / zombie is gone).
        /// </summary>
        public bool TryGetTarget(out Vector2 position)
        {
            position = default;
            var step = Current;
            if (step == null) return false;
            if (!step.HasTarget)
            {
                if (step.Goal != TutorialGoal.Reach) return false;
                position = new Vector2(step.Cell.X, step.Cell.Y);
                return true;
            }

            var zombie = FindZombie(step.Target);
            if (zombie != null)
            {
                position = zombie.Position;
                return zombie.IsAlive;
            }

            var entity = _level.FindEntity(step.Target);
            if (entity == null || IsPickup(entity) && !_pickups.IsLying(entity.Id)) return false;
            position = new Vector2(entity.Position.X, entity.Position.Y);
            return true;
        }

        private void Advance()
        {
            while (true)
            {
                StepIndex++;
                if (StepIndex >= StepCount)
                {
                    if (_route.Target == RouteTarget.Guide) _route.Clear();
                    GameLog.Info(LogChannel.Gameplay, "Tutorial finished.");
                    Finished?.Invoke();
                    return;
                }

                if (IsDone(Current)) continue; // Done ahead of its turn.

                Lead();
                StepStarted?.Invoke(StepIndex);
                return;
            }
        }

        private void Check()
        {
            var step = Current;
            if (step != null && IsDone(step)) Advance();
        }

        private bool IsDone(TutorialStep step)
        {
            switch (step.Goal)
            {
                case TutorialGoal.Reach:
                    // A pickup target (a medkit used on entering its cell) is gone by the time this is checked: taken = reached.
                    if (step.HasTarget && _level.FindEntity(step.Target) is LevelEntityData entity && IsPickup(entity) &&
                        !_pickups.IsLying(entity.Id))
                        return true;
                    return _player.IsSpawned && TryGetTargetCell(step, out var cell) && _player.Cell == cell;
                case TutorialGoal.Collect:
                    return !_pickups.IsLying(step.Target);
                case TutorialGoal.UnlockDoor:
                    return !_doors.IsLocked(step.Target);
                case TutorialGoal.OpenDoor:
                    return _doors.IsOpen(step.Target);
                case TutorialGoal.Kill:
                    var zombie = FindZombie(step.Target);
                    return zombie == null || !zombie.IsAlive;
                case TutorialGoal.OpenMap:
                    return _mapOpened;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The route line to the step's target, unless the player follows a hint of their own. Nothing when the player is
        /// already there.
        /// </summary>
        private void Lead()
        {
            var step = Current;
            if (step == null) return;
            if (_route.IsActive && _route.Target != RouteTarget.Guide) return;
            if (!TryGetTargetCell(step, out var cell))
            {
                if (_route.Target == RouteTarget.Guide) _route.Clear();
                return;
            }

            if (_route.IsActive && _route.TargetCell == cell) return;
            _route.Guide(cell, _level.FindEntity(step.Target) is DoorData);
        }

        private bool TryGetTargetCell(TutorialStep step, out GridPosition cell)
        {
            cell = step.Cell;
            if (!step.HasTarget) return step.Goal == TutorialGoal.Reach;

            var zombie = FindZombie(step.Target);
            if (zombie != null)
            {
                cell = zombie.Cell;
                return zombie.IsAlive;
            }

            var entity = _level.FindEntity(step.Target);
            if (entity == null || IsPickup(entity) && !_pickups.IsLying(entity.Id)) return false;
            cell = entity.Position;
            return true;
        }

        private ZombieRuntime FindZombie(string id)
        {
            var zombies = _zombies.Zombies;
            for (var i = 0; i < zombies.Count; i++)
                if (zombies[i].Id == id) return zombies[i];
            return null;
        }

        private static bool IsPickup(LevelEntityData entity) =>
            entity is KeyData || entity is MedkitData || entity is WeaponPickupData || entity is MapFragmentData;

        private void OnCellChanged(GridPosition from, GridPosition to)
        {
            Check();
            if (Current != null) Lead(); // A moving target (a zombie), or the line ended at the target.
        }

        private void OnPickupRemoved(Pickup pickup) => Check();

        private void OnDoorUnlocked(DoorData door) => Check();

        private void OnDoorChanged(DoorData door, bool open) => Check();

        private void OnZombieDied(ZombieRuntime zombie) => Check();
    }
}
