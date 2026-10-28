using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Cards;
using Purge.Core.Common;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Level;
using Purge.Gameplay.Level;
using Purge.Gameplay.Objects;
using Purge.Gameplay.Vehicles;
using Purge.Gameplay.Visibility;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Waves;
using Purge.Gameplay.Weapons;

namespace Purge.Gameplay.Run
{
    /// <summary>
    /// The player's side of a run's start (stage 6e), step <see cref="LevelLoadStage.InitializeSystems"/> — before the
    /// views gather what to load (a restored weapon's look and effects): a new run gets the level's
    /// <see cref="LevelData.StartWeapon"/>, a continued one (<see cref="LevelLaunchOptions.Resume"/>) its cards, max HP,
    /// HP, revives, weapon slots and throwables.
    /// </summary>
    public sealed class RunLoadout : ILevelLoadStep
    {
        private readonly LevelData _level;
        private readonly LevelLaunchOptions _options;
        private readonly WeaponSystem _weapons;
        private readonly PickupSystem _pickups;
        private readonly PlayerHealth _health;
        private readonly ThrowableSlot _throwables;
        private readonly CardSystem _cards;
        private readonly PlayerRevival _revival;
        private readonly ContentCatalog _content;

        public RunLoadout(LevelData level, WeaponSystem weapons, PickupSystem pickups, PlayerHealth health,
            ThrowableSlot throwables, CardSystem cards, LevelLaunchOptions options = null, PlayerRevival revival = null,
            ContentCatalog content = null)
        {
            _level = level;
            _options = options;
            _weapons = weapons;
            _pickups = pickups;
            _health = health;
            _throwables = throwables;
            _cards = cards;
            _revival = revival;
            _content = content;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var resume = _options?.Resume;
            if (resume != null) Restore(resume);
            else if (_level.StartWeapon != null) _weapons.Equip(_pickups.CreateWeapon(_level.StartWeapon));
            return UniTask.CompletedTask;
        }

        private void Restore(RunSnapshot run)
        {
            var cards = new List<CardDefinition>();
            foreach (var id in run.Cards)
                if (Find<CardDefinition>(id) is { } card) cards.Add(card);
            _cards.Restore(cards);
            _health.Restore(run.Health);
            _revival?.Restore(run.AdReviveUsed, run.SkillReviveUsed);

            for (var i = 0; i < 2; i++)
            {
                var id = i < run.Weapons.Length ? run.Weapons[i] : null;
                var definition = string.IsNullOrEmpty(id) ? null : Find<WeaponDefinition>(id);
                var ammo = i < run.Ammo.Length ? run.Ammo[i] : 0;
                _weapons.Restore((WeaponSlot)i, definition != null ? _pickups.CreateWeapon(definition) : null, ammo);
            }

            _weapons.RestoreActive(run.ActiveSlot == 1 ? WeaponSlot.Second : WeaponSlot.First);
            _throwables.Replace(Find<ThrowableDefinition>(run.ThrowableId), run.ThrowableCount);
            GameLog.Info(LogChannel.Gameplay, $"Run continued: {cards.Count} card level(s), HP {_health.Current}/{_health.Max}.");
        }

        private T Find<T>(string id) where T : ContentDefinition
        {
            if (string.IsNullOrEmpty(id)) return null;
            var found = _content != null ? _content.Find<T>(id) : null;
            if (found == null) GameLog.Warning(LogChannel.Gameplay, $"Continued run: no {typeof(T).Name} '{id}', skipped.");
            return found;
        }
    }

    /// <summary>
    /// Checkpoints of a run (stage 6e; ТЗ «Продолжение забега»), step <see cref="LevelLoadStage.SpawnZombies"/> after the
    /// waves: <see cref="Reached"/> with the run's state when a wave starts (marked in progress, the money of the pause's
    /// skip left out — the continued pause can be skipped again) and when one is cleared (not the last: the run ends).
    /// A continued run gets its money, score, experience and play time back and starts in the pause before its wave.
    /// </summary>
    public sealed class RunCheckpoints : ILevelLoadStep, IDisposable
    {
        private readonly WaveSystem _waves;
        private readonly RunWallet _wallet;
        private readonly RunScore _score;
        private readonly RunExperience _experience;
        private readonly CardSystem _cards;
        private readonly PlayerHealth _health;
        private readonly PlayerRevival _revival;
        private readonly WeaponSystem _weapons;
        private readonly ThrowableSlot _throwables;
        private readonly LevelProgress _progress;
        private readonly LevelLaunchOptions _options;
        private readonly BarrelSystem _barrels;
        private readonly ChestSystem _chests;
        private readonly VehicleSystem _vehicles;
        private int _skipBonus;
        private bool _subscribed;

        private readonly MapExploration _exploration;

        public RunCheckpoints(WaveSystem waves, RunWallet wallet, RunScore score, RunExperience experience, CardSystem cards,
            PlayerHealth health, WeaponSystem weapons, ThrowableSlot throwables, LevelProgress progress,
            LevelLaunchOptions options = null, PlayerRevival revival = null, BarrelSystem barrels = null,
            ChestSystem chests = null, MapExploration exploration = null, VehicleSystem vehicles = null)
        {
            _vehicles = vehicles;
            _exploration = exploration;
            _barrels = barrels;
            _chests = chests;
            _waves = waves;
            _wallet = wallet;
            _score = score;
            _experience = experience;
            _cards = cards;
            _health = health;
            _revival = revival;
            _weapons = weapons;
            _throwables = throwables;
            _progress = progress;
            _options = options;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>The level is a run with waves: it has checkpoints.</summary>
        public bool IsActive => _waves.IsActive;

        /// <summary>A checkpoint: the state to save.</summary>
        public event Action<RunSnapshot> Reached;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed || !_waves.IsActive) return UniTask.CompletedTask;
            var resume = _options?.Resume;
            if (resume != null)
            {
                _wallet.Restore(resume.Money);
                _score.Restore(resume.Kills, resume.WavesCleared, resume.Score);
                _experience.Restore(resume.Level, resume.Experience);
                _progress.Restore(resume.Time);
                _waves.ResumeAt(resume.NextWave);
            }

            _waves.Skipped += OnSkipped;
            _waves.WaveStarted += OnWaveStarted;
            _waves.WaveCleared += OnWaveCleared;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _waves.Skipped -= OnSkipped;
            _waves.WaveStarted -= OnWaveStarted;
            _waves.WaveCleared -= OnWaveCleared;
            _subscribed = false;
        }

        /// <summary>The run's state now.</summary>
        public RunSnapshot Take(int wave, bool inProgress, int moneyLeftOut = 0)
        {
            var run = new RunSnapshot
            {
                Seed = _waves.Seed,
                Wave = wave,
                WaveInProgress = inProgress,
                Money = Math.Max(0, _wallet.Money - moneyLeftOut),
                Score = _score.Score,
                Kills = _score.Kills,
                WavesCleared = _score.WavesCleared,
                Experience = _experience.Experience,
                Level = _experience.Level,
                Health = _health.Current,
                AdReviveUsed = _revival != null && _revival.AdReviveUsed,
                SkillReviveUsed = _revival != null && _revival.SkillReviveUsed,
                Time = _progress.Elapsed,
                ActiveSlot = (int)_weapons.ActiveSlot,
                ThrowableId = _throwables.IsEmpty ? null : _throwables.Definition.Id,
                ThrowableCount = _throwables.IsEmpty ? 0 : _throwables.Count,
            };
            foreach (var pair in _cards.Levels)
                for (var i = 0; i < pair.Value; i++)
                    run.Cards.Add(pair.Key.Id);
            for (var i = 0; i < 2; i++)
            {
                var weapon = _weapons.Get((WeaponSlot)i);
                run.Weapons[i] = weapon == null || weapon.IsBase ? null : weapon.Definition.Id;
                run.Ammo[i] = weapon?.Ammo ?? 0;
            }

            // Level objects (stage 7в): what is gone or opened stays so; a challenge still on is not opened yet.
            if (_barrels != null) run.BrokenBarrels.AddRange(_barrels.BrokenIds);
            if (_chests != null)
            {
                run.OpenedChests.AddRange(_chests.OpenedIds);
                run.ChestKeyDropped = _chests.KeyDropped;
            }

            // Vehicles (stage 9в): one breaking down is as good as gone; the rest stand at their places, HP kept.
            if (_vehicles != null)
            {
                run.DestroyedVehicles.AddRange(_vehicles.GoneIds);
                foreach (var vehicle in _vehicles.Vehicles)
                    if (vehicle.Broken || vehicle.State == VehicleState.Destroyed) run.DestroyedVehicles.Add(vehicle.Id);
                    else if (vehicle.Health < vehicle.Definition.Hull.Health) run.VehicleHealth[vehicle.Id] = vehicle.Health;
            }

            if (_exploration != null) run.Explored = _exploration.Export();

            return run;
        }

        private void OnSkipped(int bonus) => _skipBonus = bonus;

        private void OnWaveStarted(int index)
        {
            var bonus = _skipBonus;
            _skipBonus = 0;
            Reached?.Invoke(Take(index, inProgress: true, moneyLeftOut: bonus));
        }

        private void OnWaveCleared(int index)
        {
            if (index + 1 >= _waves.WaveCount) return; // the last one: the run ends
            Reached?.Invoke(Take(index, inProgress: false));
        }
    }
}
