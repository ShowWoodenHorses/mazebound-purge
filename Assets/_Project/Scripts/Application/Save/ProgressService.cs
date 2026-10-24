using System;
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

        /// <summary>
        /// Stores an ended run (persistent event; ТЗ: при смерти собранное остаётся): its money and kills; a won one also
        /// the completed difficulty, the win, and unlocks the next level. Saves.
        /// </summary>
        void RecordRun(string levelId, LevelResult result, int difficulty = 0);

        /// <summary>Adds money (the result's rewarded ×2) and saves.</summary>
        void AddMoney(int amount);

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

        public int Money => Data.Money;

        public void RecordRun(string levelId, LevelResult result, int difficulty = 0)
        {
            if (string.IsNullOrEmpty(levelId)) throw new ArgumentException("Level id is empty.", nameof(levelId));
            if (difficulty < 0 || difficulty > 30) throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, null);

            Data.Money = SafeAdd(Data.Money, result.Money);
            Data.Stats.TotalKills = SafeAdd(Data.Stats.TotalKills, result.Kills);
            string next = null;
            if (result.Completed)
            {
                Open(levelId).CompletedMask |= 1 << difficulty;
                Data.Stats.RunsWon++;
                next = NextLevelId(levelId);
                if (next != null) Open(next);
            }

            _save.Save();
            GameLog.Info(LogChannel.Save, $"Run saved: '{levelId}' (difficulty {difficulty}) {result}" +
                                          (next != null ? $", unlocked '{next}'." : "."));
        }

        public void AddMoney(int amount)
        {
            if (amount <= 0) return;
            Data.Money = SafeAdd(Data.Money, amount);
            _save.Save();
            GameLog.Info(LogChannel.Save, $"Money +{amount} → {Data.Money}.");
        }

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
