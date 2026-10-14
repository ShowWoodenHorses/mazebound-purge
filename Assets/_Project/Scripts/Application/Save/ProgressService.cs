using System;
using Purge.Application.Levels;
using Purge.Core.Common;
using Purge.Gameplay.Level;

namespace Purge.Application.Save
{
    /// <summary>Player progress across levels: opened levels, completed difficulties, best stars and time, total kills.</summary>
    public interface IProgressService
    {
        int TotalKills { get; }

        /// <summary>
        /// The first catalog level is always unlocked; the others once the previous one is completed on any difficulty
        /// (also levels added to the catalog after that completion — an update with new levels opens them for players who
        /// finished all).
        /// </summary>
        bool IsUnlocked(string levelId);

        /// <summary>Completed on any difficulty.</summary>
        bool IsCompleted(string levelId);

        /// <summary>Best stars of the level, 0 when it was never completed.</summary>
        int GetStars(string levelId);

        /// <summary>Best time of a completed run of the level, seconds; 0 when there is none.</summary>
        float GetBestTime(string levelId);

        /// <summary>
        /// Stores a completed run (persistent event): the difficulty, best stars and time, kills, unlocks the next level,
        /// saves.
        /// </summary>
        void RecordCompletion(string levelId, LevelResult result, int difficulty = 0);

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

        public int GetStars(string levelId) => Data.FindLevel(levelId)?.Stars ?? 0;

        public float GetBestTime(string levelId) => Data.FindLevel(levelId)?.BestTime ?? 0f;

        public void RecordCompletion(string levelId, LevelResult result, int difficulty = 0)
        {
            if (string.IsNullOrEmpty(levelId)) throw new ArgumentException("Level id is empty.", nameof(levelId));
            if (!result.Completed) throw new ArgumentException("Only completed runs are saved.", nameof(result));
            if (difficulty < 0 || difficulty > 30) throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, null);

            var record = Open(levelId);
            record.CompletedMask |= 1 << difficulty;
            record.Stars = Math.Max(record.Stars, Math.Min(result.Stars, LevelResult.MaxStars));
            if (result.Time > 0f && (record.BestTime <= 0f || result.Time < record.BestTime))
                record.BestTime = result.Time;

            Data.Stats.TotalKills += result.Kills;
            var next = NextLevelId(levelId);
            if (next != null) Open(next);

            _save.Save();
            GameLog.Info(LogChannel.Save, $"Progress saved: '{levelId}' (difficulty {difficulty}) {result}" +
                                          (next != null ? $", unlocked '{next}'." : "."));
        }

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
