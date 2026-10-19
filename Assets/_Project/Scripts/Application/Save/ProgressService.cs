using System;
using Purge.Application.Levels;
using Purge.Core.Common;
using Purge.Gameplay.Level;

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

        /// <summary>Adds money (the result's rewarded ×2, a run left from the pause) and saves.</summary>
        void AddMoney(int amount);

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
