using System.Collections.Generic;
using System.Globalization;
using Maze.Core.Generation;
using Maze.Core.Grid;
using Maze.Core.Visual;
using UnityEngine;

namespace Maze.Core.Level
{
    /// <summary>
    /// Source of truth for a finished level design. Each level is an Addressable asset.
    /// Read-only for runtime code; mutation API is internal (editor tool and tests only).
    /// </summary>
    [CreateAssetMenu(fileName = "LevelData", menuName = "Maze/Level Data")]
    public sealed class LevelData : ScriptableObject
    {
        [SerializeField] private LevelSettings _settings = new LevelSettings();
        [SerializeField] private LevelGenerationSettings _generation = new LevelGenerationSettings();
        [SerializeField] private LevelGeometry _geometry = new LevelGeometry(21, 21);
        [SerializeField] private List<DoorData> _doors = new List<DoorData>();
        [SerializeField] private List<PlayerStartData> _playerStarts = new List<PlayerStartData>();
        [SerializeField] private List<ExitData> _exits = new List<ExitData>();
        [SerializeField] private List<ZombieSpawnData> _zombieSpawns = new List<ZombieSpawnData>();
        [SerializeField] private List<PatrolData> _patrols = new List<PatrolData>();
        [SerializeField] private List<WeaponPickupData> _weapons = new List<WeaponPickupData>();
        [SerializeField] private List<KeyData> _keys = new List<KeyData>();
        [SerializeField] private List<MedkitData> _medkits = new List<MedkitData>();
        [SerializeField] private List<MapFragmentData> _mapFragments = new List<MapFragmentData>();
        [SerializeField] private List<LightSourceData> _lights = new List<LightSourceData>();
        [SerializeField] private VisualTheme _visualTheme;
        [SerializeField] private VisualData _visualData = new VisualData();

        [Tooltip("Steps of the tutorial: set only on the tutorial level (Build / Sync lists it apart from the numbered levels).")]
        [SerializeField] private TutorialScript _tutorial;

        public LevelSettings Settings => _settings;
        public VisualTheme VisualTheme { get => _visualTheme; internal set => _visualTheme = value; }
        public VisualData VisualData => _visualData;

        /// <summary>Tutorial steps; null for every level but the tutorial.</summary>
        public TutorialScript Tutorial { get => _tutorial; internal set => _tutorial = value; }

        /// <summary>A placed object by id (doors, starts, exits, zombies, pickups); null when there is none.</summary>
        public LevelEntityData FindEntity(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var entity in AllEntities())
                if (entity.Id == id) return entity;
            return null;
        }
        public LevelGenerationSettings Generation => _generation;
        public LevelGeometry Geometry => _geometry;
        public IReadOnlyList<DoorData> Doors => _doors;
        public IReadOnlyList<PlayerStartData> PlayerStarts => _playerStarts;
        public IReadOnlyList<ExitData> Exits => _exits;
        public IReadOnlyList<ZombieSpawnData> ZombieSpawns => _zombieSpawns;
        public IReadOnlyList<PatrolData> Patrols => _patrols;
        public IReadOnlyList<WeaponPickupData> Weapons => _weapons;
        public IReadOnlyList<KeyData> Keys => _keys;
        public IReadOnlyList<MedkitData> Medkits => _medkits;
        public IReadOnlyList<MapFragmentData> MapFragments => _mapFragments;

        /// <summary>Light sources (visual only, not in <see cref="AllEntities"/>).</summary>
        public IReadOnlyList<LightSourceData> Lights => _lights;

        internal List<DoorData> MutableDoors => _doors;
        internal List<PlayerStartData> MutablePlayerStarts => _playerStarts;
        internal List<ExitData> MutableExits => _exits;
        internal List<ZombieSpawnData> MutableZombieSpawns => _zombieSpawns;
        internal List<PatrolData> MutablePatrols => _patrols;
        internal List<WeaponPickupData> MutableWeapons => _weapons;
        internal List<KeyData> MutableKeys => _keys;
        internal List<MedkitData> MutableMedkits => _medkits;
        internal List<MapFragmentData> MutableMapFragments => _mapFragments;
        internal List<LightSourceData> MutableLights => _lights;

        /// <summary>All placed objects, in a stable order.</summary>
        public IEnumerable<LevelEntityData> AllEntities()
        {
            foreach (var e in _doors) yield return e;
            foreach (var e in _playerStarts) yield return e;
            foreach (var e in _exits) yield return e;
            foreach (var e in _zombieSpawns) yield return e;
            foreach (var e in _weapons) yield return e;
            foreach (var e in _keys) yield return e;
            foreach (var e in _medkits) yield return e;
            foreach (var e in _mapFragments) yield return e;
        }

        /// <summary>Ids of all placed objects and patrols. Used for uniqueness checks.</summary>
        public IEnumerable<string> AllIds()
        {
            foreach (var entity in AllEntities()) yield return entity.Id;
            foreach (var patrol in _patrols) yield return patrol.Id;
            foreach (var light in _lights) yield return light.Id;
        }

        internal void ReplaceGeometry(LevelGeometry geometry) => _geometry = geometry;

        /// <summary>
        /// Destructive "Generate New": replaces geometry, removes every placed object and the whole
        /// visual design (assignments and overrides), then adds the generated player starts and exits.
        /// </summary>
        internal void ApplyGeneratedMaze(MazeGenerationResult result)
        {
            _geometry = result.Geometry;
            ClearObjects();
            _visualData.Clear();
            _lights.Clear();

            foreach (var position in result.PlayerStarts)
                _playerStarts.Add(new PlayerStartData(CreateUniqueId(PlayerStartData.IdPrefix), position));

            foreach (var position in result.Exits)
                _exits.Add(new ExitData(CreateUniqueId(ExitData.IdPrefix), position));
        }

        /// <summary>Removes every placed object and patrol. Geometry is left untouched.</summary>
        internal void ClearObjects()
        {
            _doors.Clear();
            _playerStarts.Clear();
            _exits.Clear();
            _zombieSpawns.Clear();
            _patrols.Clear();
            _weapons.Clear();
            _keys.Clear();
            _medkits.Clear();
            _mapFragments.Clear();
        }

        /// <summary>Returns "{prefix}_{N}" where N is one greater than the largest existing N for that prefix.</summary>
        internal string CreateUniqueId(string prefix)
        {
            var head = prefix + "_";
            var max = 0;
            foreach (var id in AllIds())
            {
                if (id == null || !id.StartsWith(head, System.StringComparison.Ordinal))
                    continue;

                if (int.TryParse(id.Substring(head.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max)
                    max = n;
            }

            return head + (max + 1).ToString(CultureInfo.InvariantCulture);
        }
    }
}
