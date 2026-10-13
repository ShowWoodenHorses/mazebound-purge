using System;
using Maze.Application.Levels;
using Maze.Core.Common;
using Maze.Gameplay.Level;

namespace Maze.Application.Save
{
    /// <summary>Player progress across levels: unlocked levels, best stars, total kills (ТЗ §85, §89).</summary>
    public interface IProgressService
    {
        int TotalKills { get; }

        /// <summary>
        /// The first catalog level is always unlocked; the others once the previous one is completed (also levels added
        /// to the catalog after that completion — an update with new levels opens them for players who finished all).
        /// </summary>
        bool IsUnlocked(string levelId);

        /// <summary>Best stars of the level, 0 when it was never completed.</summary>
        int GetStars(string levelId);

        /// <summary>Best time of a completed run of the level, seconds; 0 when there is none.</summary>
        float GetBestTime(string levelId);

        /// <summary>Stores a completed run (persistent event): best stars and time, kills, unlocks the next level, saves.</summary>
        void RecordCompletion(string levelId, LevelResult result);

        /// <summary>The map layer was bought for the level (a rewarded ad); it stays for every replay of it.</summary>
        bool IsMapLayerUnlocked(string levelId, MapUnlock layer);

        /// <summary>Remembers a bought map layer of the level and saves.</summary>
        void UnlockMapLayer(string levelId, MapUnlock layer);

        /// <summary>Forgets all progress (debug).</summary>
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

        public int TotalKills => Data.TotalKills;

        public bool IsUnlocked(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return false;
            if (Data.UnlockedLevels.Contains(levelId)) return true;
            var levels = _catalog.Levels;
            for (var i = 0; i < levels.Count; i++)
                if (string.Equals(levels[i].LevelId, levelId, StringComparison.Ordinal))
                    return i == 0 || GetStars(levels[i - 1].LevelId) > 0;

            return false;
        }

        public int GetStars(string levelId)
        {
            var record = FindStars(levelId);
            return record?.Stars ?? 0;
        }

        public float GetBestTime(string levelId) => FindStars(levelId)?.BestTime ?? 0f;

        public void RecordCompletion(string levelId, LevelResult result)
        {
            if (string.IsNullOrEmpty(levelId)) throw new ArgumentException("Level id is empty.", nameof(levelId));
            if (!result.Completed) throw new ArgumentException("Only completed runs are saved.", nameof(result));

            var record = FindStars(levelId);
            if (record == null)
                Data.LevelStars.Add(record = new LevelStarsRecord { LevelId = levelId });
            record.Stars = Math.Max(record.Stars, Math.Min(result.Stars, LevelResult.MaxStars));
            if (result.Time > 0f && (record.BestTime <= 0f || result.Time < record.BestTime))
                record.BestTime = result.Time;

            Data.TotalKills += result.Kills;
            Unlock(levelId);
            var next = NextLevelId(levelId);
            if (next != null) Unlock(next);

            _save.Save();
            GameLog.Info(LogChannel.Save, $"Progress saved: '{levelId}' {result}" +
                                          (next != null ? $", unlocked '{next}'." : "."));
        }

        public bool IsMapLayerUnlocked(string levelId, MapUnlock layer)
        {
            foreach (var record in Data.MapUnlocks)
                if (string.Equals(record.LevelId, levelId, StringComparison.Ordinal))
                    return (record.Layers & layer) == layer;
            return false;
        }

        public void UnlockMapLayer(string levelId, MapUnlock layer)
        {
            if (string.IsNullOrEmpty(levelId) || layer == MapUnlock.None) return;
            MapUnlockRecord found = null;
            foreach (var record in Data.MapUnlocks)
                if (string.Equals(record.LevelId, levelId, StringComparison.Ordinal))
                    found = record;
            if (found == null) Data.MapUnlocks.Add(found = new MapUnlockRecord { LevelId = levelId });
            if ((found.Layers & layer) == layer) return;
            found.Layers |= layer;
            _save.Save();
            GameLog.Info(LogChannel.Save, $"Map layer {layer} unlocked for '{levelId}'.");
        }

        public void ResetProgress()
        {
            Data.MapUnlocks.Clear();
            Data.UnlockedLevels.Clear();
            Data.LevelStars.Clear();
            Data.TotalKills = 0;
            _save.Save();
            GameLog.Info(LogChannel.Save, "Progress reset.");
        }

        private LevelStarsRecord FindStars(string levelId)
        {
            foreach (var record in Data.LevelStars)
                if (string.Equals(record.LevelId, levelId, StringComparison.Ordinal))
                    return record;

            return null;
        }

        private void Unlock(string levelId)
        {
            if (!Data.UnlockedLevels.Contains(levelId))
                Data.UnlockedLevels.Add(levelId);
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
