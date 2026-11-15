using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Core.Cards;
using Purge.Core.Definitions;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Run;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Waves;
using Purge.Gameplay.Weapons;
using UnityEngine;

namespace Purge.Gameplay.Tutorial
{
    /// <summary>
    /// Steps of the tutorial level (<see cref="LevelData.Tutorial"/>; inactive in other levels). A step is finished by the
    /// state of the game, checked when movement, equipment, combat or wave events change it. A step already done when its turn comes (the player ran ahead) is
    /// skipped silently.
    /// </summary>
    public sealed class TutorialSystem : ILevelLoadStep, IDisposable
    {
        private readonly TutorialScript _script;
        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly PickupSystem _pickups;
        private readonly DoorSystem _doors;
        private readonly PlayerCombat _combat;
        private readonly WeaponSystem _weapons;
        private readonly PlayerThrowing _throwing;
        private readonly CardSystem _cards;
        private readonly RunExperience _experience;
        private readonly WaveSystem _waves;
        private bool _fired, _switched, _dropped, _thrown, _choseCard, _firstCleared, _officerWave, _officerCleared;
        private bool _mapOpened;
        private bool _subscribed;

        public TutorialSystem(LevelData level, PlayerSystem player, PickupSystem pickups, DoorSystem doors,
            PlayerCombat combat = null, WeaponSystem weapons = null,
            PlayerThrowing throwing = null, CardSystem cards = null, RunExperience experience = null, WaveSystem waves = null)
        {
            _level = level;
            _script = level.Tutorial;
            _player = player;
            _pickups = pickups;
            _doors = doors;
            _combat = combat;
            _weapons = weapons;
            _throwing = throwing;
            _cards = cards;
            _experience = experience;
            _waves = waves;
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
                if (_combat != null) _combat.Attacked += OnAttacked;
                if (_weapons != null)
                {
                    _weapons.Switched += OnSwitched;
                    _weapons.Dropped += OnDropped;
                }
                if (_throwing != null) _throwing.Thrown += OnThrown;
                if (_cards != null) _cards.Chosen += OnCardChosen;
                if (_waves != null)
                {
                    _waves.WaveCleared += OnWaveCleared;
                    _waves.WaveStarted += OnWaveStarted;
                    _waves.TutorialHold = _waves.IsActive;
                }
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
            if (_combat != null) _combat.Attacked -= OnAttacked;
            if (_weapons != null)
            {
                _weapons.Switched -= OnSwitched;
                _weapons.Dropped -= OnDropped;
            }
            if (_throwing != null) _throwing.Thrown -= OnThrown;
            if (_cards != null) _cards.Chosen -= OnCardChosen;
            if (_waves != null)
            {
                _waves.WaveCleared -= OnWaveCleared;
                _waves.WaveStarted -= OnWaveStarted;
                _waves.TutorialHold = false;
            }
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
                    GameLog.Info(LogChannel.Gameplay, "Tutorial finished.");
                    Finished?.Invoke();
                    return;
                }

                if (IsDone(Current)) continue; // Done ahead of its turn.

                if (_waves != null && Current.Goal == TutorialGoal.PickCard && _waves.Phase == WavePhase.Delay)
                    _waves.TutorialHold = false;
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
                case TutorialGoal.Fire: return _fired;
                case TutorialGoal.SwitchWeapon: return _switched;
                case TutorialGoal.DropWeapon: return _dropped;
                case TutorialGoal.ThrowGrenade: return _thrown;
                case TutorialGoal.PickCard: return _firstCleared && _choseCard;
                case TutorialGoal.Intermission: return _firstCleared && _officerWave;
                case TutorialGoal.OfficerWave: return _officerCleared;
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
                case TutorialGoal.OpenMap:
                    return _mapOpened;
                default:
                    return false;
            }
        }

        private bool TryGetTargetCell(TutorialStep step, out GridPosition cell)
        {
            cell = step.Cell;
            if (!step.HasTarget) return step.Goal == TutorialGoal.Reach;

            var entity = _level.FindEntity(step.Target);
            if (entity == null || IsPickup(entity) && !_pickups.IsLying(entity.Id)) return false;
            cell = entity.Position;
            return true;
        }

        private static bool IsPickup(LevelEntityData entity) =>
            entity is KeyData || entity is MedkitData || entity is WeaponPickupData;

        private void OnAttacked(WeaponRuntime weapon, Vector2 direction) { _fired = true; Check(); }
        private void OnSwitched(WeaponSlot slot) { _switched = true; Check(); }
        private void OnDropped(WeaponSlot slot) { _dropped = true; Check(); }

        private void OnThrown(ThrowableDefinition item, Vector2 position)
        {
            _thrown = true;
            Check();
        }

        private void OnCardChosen(CardDefinition card, int level)
        {
            _choseCard = true;
            Check();
            if (_waves != null && _firstCleared) _waves.TutorialHold = false;
        }

        private void OnWaveCleared(int wave)
        {
            if (wave == 1)
            {
                _officerCleared = true;
                Check();
                return;
            }

            if (wave != 0) return;
            _firstCleared = true;
            if (_choseCard)
            {
                Check();
                return;
            }
            _waves.TutorialHold = true;
            if (_experience is { Pending: 0 }) _experience.Add(_experience.Next - _experience.Experience);
        }

        private void OnWaveStarted(int wave)
        {
            if (wave != 1) return;
            _officerWave = true;
            Check();
        }

        private void OnCellChanged(GridPosition from, GridPosition to) => Check();

        private void OnPickupRemoved(Pickup pickup) => Check();

        private void OnDoorUnlocked(DoorData door) => Check();

        private void OnDoorChanged(DoorData door, bool open) => Check();

    }
}
