using System;
using System.Collections.Generic;
using Purge.Application.Levels;
using Purge.Core.Common;
using Purge.Gameplay.Level;
using Purge.Gameplay.Run;

namespace Purge.Application.Save
{
    /// <summary>Player progress across levels: opened levels, completed difficulties, money, total kills and wins.</summary>
    public interface IProgressService
    {
        int TotalKills { get; }

        /// <summary>The one currency, kept between runs.</summary>
        int Money { get; }

        /// <summary>
        /// The first catalog level is always unlocked; the others once the previous one is completed on any difficulty
        /// (also levels added to the catalog after that completion — an update with new levels opens them for players who
        /// finished all).
        /// </summary>
        bool IsUnlocked(string levelId);

        /// <summary>Completed on any difficulty.</summary>
        bool IsCompleted(string levelId);

        /// <summary>Completed on this difficulty (0 = easy).</summary>
        bool IsCompletedOn(string levelId, int difficulty);

        /// <summary>
        /// The difficulty can be chosen: the level is unlocked and the difficulty is easy or the previous one is completed
        /// (ТЗ А.2).
        /// </summary>
        bool IsDifficultyOpen(string levelId, int difficulty);

        /// <summary>The last preparation (stage 11): the menu podium shows its weapon.</summary>
        LoadoutSave Loadout { get; }

        /// <summary>Stores the preparation a run starts with and saves.</summary>
        void SaveLoadout(string weaponId, string throwableId, IReadOnlyList<string> boosters);

        /// <summary>
        /// Stores an ended run (persistent event; ТЗ: при смерти собранное остаётся): its money and kills; a won one also
        /// the completed difficulty (with the closed arena — <see cref="LevelRecord.ClosedArenaMask"/>, stage 11б), the win,
        /// and unlocks the next level. Saves.
        /// </summary>
        void RecordRun(string levelId, LevelResult result, int difficulty = 0, bool arenaClosed = false);

        /// <summary>Adds money (the result's rewarded ×2) and saves.</summary>
        void AddMoney(int amount);

        /// <summary>Takes money (boosters at a run's start, stage 11б) and saves; false — not enough, nothing taken.</summary>
        bool SpendMoney(int amount);

        /// <summary>
        /// A booster opened by the tree (stage 11д: <see cref="SaveData.Boosters"/>); the basic ones open with the first
        /// level (<see cref="IsCompleted"/> of the catalog's first).
        /// </summary>
        bool IsBoosterOpened(string boosterId);

        /// <summary>The item (weapon, throwable) is bought in the arsenal (stage 11в).</summary>
        bool Owns(string itemId);

        /// <summary>The saved run's difficulty (0 = easy) and arena closing; 0 / false without one.</summary>
        int RunDifficulty { get; }

        bool RunArenaClosed { get; }

        /// <summary>An unfinished run is saved (stage 6e): the menu offers «Продолжить».</summary>
        bool HasRun { get; }

        /// <summary>Level of the saved run; null without one.</summary>
        string RunLevelId { get; }

        /// <summary>The saved run as the level continues it; null without one.</summary>
        RunSnapshot LoadRun();

        /// <summary>A run's checkpoint (a wave started or cleared): saved at once, to the cloud with the save's delay.</summary>
        void SaveRun(string levelId, int difficulty, RunSnapshot run);

        /// <summary>The run ended (death, the last wave): its save goes. Nothing is saved when there is none.</summary>
        void ClearRun();

        /// <summary>
        /// The run is given up for a new one (stage 6e: деньги брошенного забега — в сейв): <paramref name="money"/> is
        /// added, the run's save goes.
        /// </summary>
        void AbandonRun(int money);

        /// <summary>Forgets all progress (debug); settings stay.</summary>
        void ResetProgress();
    }

    public sealed class ProgressService : IProgressService
    {
        private readonly SaveService _save;
        private readonly ILevelCatalog _catalog;

        public ProgressService(SaveService save, ILevelCatalog catalog)
        {
            _save = save;
            _catalog = catalog;
        }

        private SaveData Data => _save.Data;

        public int TotalKills => Data.Stats.TotalKills;

        public bool IsUnlocked(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return false;
            if (Data.FindLevel(levelId) != null) return true;
            var levels = _catalog.Levels;
            for (var i = 0; i < levels.Count; i++)
                if (string.Equals(levels[i].LevelId, levelId, StringComparison.Ordinal))
                    return i == 0 || IsCompleted(levels[i - 1].LevelId);

            return false;
        }

        public bool IsCompleted(string levelId) => Data.FindLevel(levelId)?.IsCompleted ?? false;

        public bool IsCompletedOn(string levelId, int difficulty) => Data.FindLevel(levelId)?.IsCompletedOn(difficulty) ?? false;

        public bool IsDifficultyOpen(string levelId, int difficulty)
        {
            if (difficulty < 0 || difficulty > 30 || !IsUnlocked(levelId)) return false;
            return difficulty == 0 || (Data.FindLevel(levelId)?.IsDifficultyOpen(difficulty) ?? false);
        }

        public LoadoutSave Loadout => Data.Loadout;

        public void SaveLoadout(string weaponId, string throwableId, IReadOnlyList<string> boosters)
        {
            var loadout = Data.Loadout ??= new LoadoutSave();
            loadout.WeaponId = weaponId ?? string.Empty;
            loadout.ThrowableId = throwableId ?? string.Empty;
            loadout.Boosters.Clear();
            if (boosters != null) loadout.Boosters.AddRange(boosters);
            loadout.Normalize();
            _save.Save();
        }

        public int Money => Data.Money;

        public void RecordRun(string levelId, LevelResult result, int difficulty = 0, bool arenaClosed = false)
        {
            if (string.IsNullOrEmpty(levelId)) throw new ArgumentException("Level id is empty.", nameof(levelId));
            if (difficulty < 0 || difficulty > 30) throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, null);

            Data.Money = SafeAdd(Data.Money, result.Money);
            Data.Stats.TotalKills = SafeAdd(Data.Stats.TotalKills, result.Kills);
            string next = null;
            if (result.Completed)
            {
                var record = Open(levelId);
                record.CompletedMask |= 1 << difficulty;
                if (arenaClosed) record.ClosedArenaMask |= 1 << difficulty;
                Data.Stats.RunsWon++;
                next = NextLevelId(levelId);
                if (next != null) Open(next);
            }

            _save.Save();
            GameLog.Info(LogChannel.Save, $"Run saved: '{levelId}' (difficulty {difficulty}{(arenaClosed ? ", closed arena" : "")}) {result}" +
                                          (next != null ? $", unlocked '{next}'." : "."));
        }

        public void AddMoney(int amount)
        {
            if (amount <= 0) return;
            Data.Money = SafeAdd(Data.Money, amount);
            _save.Save();
            GameLog.Info(LogChannel.Save, $"Money +{amount} → {Data.Money}.");
        }

        public bool SpendMoney(int amount)
        {
            if (amount <= 0) return true;
            if (Data.Money < amount) return false;
            Data.Money -= amount;
            _save.Save();
            GameLog.Info(LogChannel.Save, $"Money -{amount} → {Data.Money}.");
            return true;
        }

        public bool Owns(string itemId) => !string.IsNullOrEmpty(itemId) && Data.FindItem(itemId) != null;

        public bool IsBoosterOpened(string boosterId) => !string.IsNullOrEmpty(boosterId) && Data.Boosters.Contains(boosterId);

        public int RunDifficulty => Data.HasRun ? Data.Run.Difficulty : 0;

        public bool RunArenaClosed => Data.HasRun && Data.Run.ArenaClosed;

        public bool HasRun => Data.HasRun;

        public string RunLevelId => Data.HasRun ? Data.Run.LevelId : null;

        public RunSnapshot LoadRun()
        {
            if (!Data.HasRun) return null;
            var save = Data.Run;
            var run = new RunSnapshot
            {
                Seed = save.Seed,
                Wave = save.Wave - 1,
                WaveInProgress = save.WaveInProgress,
                Money = save.Money,
                Score = save.Score,
                Kills = save.Kills,
                Experience = save.Experience,
                Level = save.PlayerLevel,
                Health = save.Health,
                AdReviveUsed = save.AdReviveUsed,
                SkillReviveUsed = save.SkillReviveUsed,
                Time = save.Time,
                ActiveSlot = save.ActiveSlot,
                ThrowableId = save.ThrowableId,
                ThrowableCount = save.ThrowableCount,
            };
            run.WavesCleared = run.NextWave;
            run.Cards.AddRange(save.Cards);
            if (save.BrokenBarrels != null) run.BrokenBarrels.AddRange(save.BrokenBarrels);
            if (save.OpenedChests != null) run.OpenedChests.AddRange(save.OpenedChests);
            run.ChestKeyDropped = save.ChestKeyDropped;
            if (save.DestroyedVehicles != null) run.DestroyedVehicles.AddRange(save.DestroyedVehicles);
            if (save.Vehicles != null)
                foreach (var vehicle in save.Vehicles)
                    run.VehicleHealth[vehicle.Id] = vehicle.Health;
            run.Explored = save.Explored;
            run.Difficulty = save.Difficulty;
            run.ArenaClosed = save.ArenaClosed;
            run.Boosters.AddRange(save.Boosters);
            run.FreeRerolls = save.FreeRerolls;
            run.StartMedkitTaken = save.StartMedkitTaken;
            for (var i = 0; i < save.Weapons.Count && i < run.Weapons.Length; i++)
            {
                run.Weapons[i] = save.Weapons[i].Id;
                run.Ammo[i] = save.Weapons[i].Ammo;
            }

            return run;
        }

        public void SaveRun(string levelId, int difficulty, RunSnapshot run)
        {
            if (string.IsNullOrEmpty(levelId)) throw new ArgumentException("Level id is empty.", nameof(levelId));
            if (run == null) throw new ArgumentNullException(nameof(run));
            var save = Data.Run ??= new RunSave();
            save.Clear();
            save.Active = true;
            save.Mode = RunMode.Level;
            save.LevelId = levelId;
            save.Difficulty = difficulty;
            save.Seed = run.Seed;
            save.Wave = run.Wave + 1;
            save.WaveInProgress = run.WaveInProgress;
            save.Money = run.Money;
            save.Score = run.Score;
            save.Kills = run.Kills;
            save.Experience = run.Experience;
            save.PlayerLevel = run.Level;
            save.Health = run.Health;
            save.AdReviveUsed = run.AdReviveUsed;
            save.SkillReviveUsed = run.SkillReviveUsed;
            save.Time = run.Time;
            save.ActiveSlot = run.ActiveSlot;
            save.ThrowableId = run.ThrowableId;
            save.ThrowableCount = run.ThrowableCount;
            save.Cards.AddRange(run.Cards);
            save.BrokenBarrels.AddRange(run.BrokenBarrels);
            save.OpenedChests.AddRange(run.OpenedChests);
            save.ChestKeyDropped = run.ChestKeyDropped;
            save.DestroyedVehicles.AddRange(run.DestroyedVehicles);
            foreach (var pair in run.VehicleHealth)
                save.Vehicles.Add(new RunVehicle { Id = pair.Key, Health = pair.Value });
            save.Explored = run.Explored;
            save.ArenaClosed = run.ArenaClosed;
            save.Boosters.AddRange(run.Boosters);
            save.FreeRerolls = run.FreeRerolls;
            save.StartMedkitTaken = run.StartMedkitTaken;
            for (var i = 0; i < run.Weapons.Length; i++)
                save.Weapons.Add(new RunWeapon { Id = run.Weapons[i] ?? "", Ammo = i < run.Ammo.Length ? run.Ammo[i] : 0 });
            _save.Save();
            GameLog.Info(LogChannel.Save, $"Run saved: '{levelId}' wave {save.Wave}{(save.WaveInProgress ? " (in progress)" : " cleared")}, " +
                                          $"money {save.Money}.");
        }

        public void ClearRun()
        {
            if (!Data.HasRun) return;
            Data.Run.Clear();
            _save.Save();
            GameLog.Info(LogChannel.Save, "Run save cleared.");
        }

        public void AbandonRun(int money)
        {
            if (money > 0) Data.Money = SafeAdd(Data.Money, money);
            if (Data.HasRun) Data.Run.Clear();
            _save.Save();
            GameLog.Info(LogChannel.Save, $"Run abandoned: money +{Math.Max(0, money)} → {Data.Money}.");
        }

        private static int SafeAdd(int value, int amount) => amount <= 0 ? value : (int)Math.Min(int.MaxValue, (long)value + amount);

        public void ResetProgress()
        {
            var settings = Data.Settings;
            Data.Money = 0;
            Data.Levels.Clear();
            Data.Survival.Clear();
            Data.Arsenal.Clear();
            Data.CharacterNodes.Clear();
            Data.Boosters.Clear();
            Data.Loadout = new LoadoutSave();
            Data.Stats = new SaveStats();
            Data.Run.Clear();
            Data.Settings = settings;
            _save.Save();
            GameLog.Info(LogChannel.Save, "Progress reset.");
        }

        /// <summary>The level's record, created (= the level opened) when missing.</summary>
        private LevelRecord Open(string levelId)
        {
            var record = Data.FindLevel(levelId);
            if (record == null)
                Data.Levels.Add(record = new LevelRecord { LevelId = levelId });
            return record;
        }

        private string NextLevelId(string levelId)
        {
            var levels = _catalog.Levels;
            for (var i = 0; i < levels.Count - 1; i++)
                if (string.Equals(levels[i].LevelId, levelId, StringComparison.Ordinal))
                    return levels[i + 1].LevelId;

            return null;
        }
    }
}
