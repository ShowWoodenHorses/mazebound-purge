using System;
using System.Collections.Generic;
using UnityEngine;

namespace Purge.Core.Level
{
    /// <summary>One playable level: stable id, Addressables address of its <see cref="LevelData"/> and menu name.</summary>
    [Serializable]
    public sealed class LevelCatalogEntry
    {
        [SerializeField] private string _levelId;
        [SerializeField] private string _address;
        [SerializeField] private string _displayName;
        [SerializeField] private LevelLocation _location;

        internal LevelCatalogEntry(string levelId, string address, string displayName, LevelLocation location = default)
        {
            _levelId = levelId;
            _address = address;
            _displayName = displayName;
            _location = location;
        }

        public string LevelId => _levelId;
        public string Address { get => _address; internal set => _address = value; }
        public string DisplayName { get => _displayName; internal set => _displayName = value; }

        /// <summary>The level's location (<see cref="LevelSettings.Location"/>): its tile in the level choice (stage 11б).</summary>
        public LevelLocation Location { get => _location; internal set => _location = value; }
    }

    /// <summary>
    /// Ordered list of levels shown in the main menu. Addressable (<see cref="Address"/>), filled by Build / Sync
    /// in the Level Designer; the order can be changed in the inspector. The menu reads names from here,
    /// so it never has to load every level. A second asset of this type (<see cref="DevAddress"/>) lists the
    /// development levels: shown only in the editor and development builds, left out of release builds.
    /// The tutorial (a level with <see cref="LevelData.Tutorial"/>) is kept apart from the numbered levels: it is
    /// started from its own main menu button and is not part of the progression.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelCatalog", menuName = "Purge/Level Catalog")]
    public sealed class LevelCatalog : ScriptableObject
    {
        public const string Address = "LevelCatalog";

        /// <summary>Address of the development levels catalog (its group is not built into release builds).</summary>
        public const string DevAddress = "LevelCatalog/Dev";

        [SerializeField] private List<LevelCatalogEntry> _levels = new List<LevelCatalogEntry>();
        [SerializeField] private LevelCatalogEntry _tutorial;

        [Tooltip("The run's difficulties in order — easy, normal, hard, unreal (stage 11б: the level choice); the save " +
                 "keeps the number.")]
        [SerializeField] private List<Monsters.DifficultyDefinition> _difficulties = new List<Monsters.DifficultyDefinition>();

        public IReadOnlyList<LevelCatalogEntry> Levels => _levels;

        /// <summary>The run's difficulties by number (0 = easy); empty in catalogs without them (dev, tests).</summary>
        public IReadOnlyList<Monsters.DifficultyDefinition> Difficulties => _difficulties;

        /// <summary>The tutorial level; null when there is none.</summary>
        public LevelCatalogEntry Tutorial => _tutorial != null && !string.IsNullOrEmpty(_tutorial.LevelId) ? _tutorial : null;

        public LevelCatalogEntry Find(string levelId)
        {
            foreach (var entry in _levels)
                if (string.Equals(entry.LevelId, levelId, StringComparison.Ordinal))
                    return entry;

            var tutorial = Tutorial;
            return tutorial != null && string.Equals(tutorial.LevelId, levelId, StringComparison.Ordinal) ? tutorial : null;
        }

        /// <summary>Makes the level the tutorial (replacing another one). Returns true when something changed.</summary>
        internal bool SetTutorial(string levelId, string address, string displayName)
        {
            var current = Tutorial;
            if (current != null && current.LevelId == levelId && current.Address == address && current.DisplayName == displayName)
                return false;

            _tutorial = new LevelCatalogEntry(levelId, address, displayName);
            return true;
        }

        /// <summary>Adds the level to the end or updates its address and name. Returns true when something changed.</summary>
        internal bool AddOrUpdate(string levelId, string address, string displayName, LevelLocation location = default)
        {
            var entry = _levels.Find(e => string.Equals(e.LevelId, levelId, StringComparison.Ordinal));
            if (entry == null)
            {
                _levels.Add(new LevelCatalogEntry(levelId, address, displayName, location));
                return true;
            }

            if (entry.Address == address && entry.DisplayName == displayName && entry.Location == location)
                return false;

            entry.Address = address;
            entry.DisplayName = displayName;
            entry.Location = location;
            return true;
        }

        /// <summary>For tests and tools.</summary>
        internal void SetDifficulties(IEnumerable<Monsters.DifficultyDefinition> difficulties)
        {
            _difficulties = new List<Monsters.DifficultyDefinition>(difficulties);
        }

        /// <summary>Takes the level out of the numbered list and, if it is the tutorial, out of that place too.</summary>
        internal bool Remove(string levelId)
        {
            var removed = false;
            for (var i = _levels.Count - 1; i >= 0; i--)
                if (string.Equals(_levels[i].LevelId, levelId, StringComparison.Ordinal))
                {
                    _levels.RemoveAt(i);
                    removed = true;
                }

            var tutorial = Tutorial;
            if (tutorial != null && string.Equals(tutorial.LevelId, levelId, StringComparison.Ordinal))
            {
                _tutorial = null;
                removed = true;
            }

            return removed;
        }
    }
}
