using System;
using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Level;

namespace Maze.Gameplay.Doors
{
    /// <summary>
    /// Runtime door state (ТЗ §37, §45: the prefab never decides): open/closed and locked/unlocked.
    /// A door with a key starts locked unless it is initially open. A key only unlocks its door (1 key = 1 door);
    /// the door stays closed until it is opened separately, and an unlocked door can be opened and closed any
    /// number of times. Who may open or close (reach, occupancy) is decided by the interaction, not here.
    /// A frozen door (<see cref="DoorData.IceHits"/>) starts closed and cannot be opened until its ice is broken by that
    /// many hits (<see cref="HitIce"/>); then it is an ordinary door (still locked if it has a key).
    /// </summary>
    public sealed class DoorSystem
    {
        private readonly Dictionary<GridPosition, DoorData> _byCell = new Dictionary<GridPosition, DoorData>();
        private readonly Dictionary<string, DoorData> _byId = new Dictionary<string, DoorData>();
        private readonly Dictionary<string, bool> _open = new Dictionary<string, bool>();
        private readonly HashSet<string> _locked = new HashSet<string>();
        private readonly Dictionary<string, int> _ice = new Dictionary<string, int>();

        public DoorSystem(LevelData level)
        {
            foreach (var door in level.Doors)
            {
                _byCell[door.Position] = door;
                _byId[door.Id] = door;
                _open[door.Id] = door.IsInitiallyOpen && !door.IsFrozen;
                if (door.RequiresKey && !door.IsInitiallyOpen)
                    _locked.Add(door.Id);
                if (door.IsFrozen)
                    _ice[door.Id] = door.IceHits;
            }
        }

        /// <summary>Raised after a door opened (true) or closed (false).</summary>
        public event Action<DoorData, bool> DoorChanged;

        /// <summary>Raised after a locked door was unlocked with its key.</summary>
        public event Action<DoorData> DoorUnlocked;

        /// <summary>(door, hits left) after a hit that did not break the ice yet.</summary>
        public event Action<DoorData, int> IceHit;

        /// <summary>Raised after the last hit broke the door's ice (the door stays closed).</summary>
        public event Action<DoorData> IceBroken;

        public IEnumerable<DoorData> Doors => _byId.Values;

        public bool TryGetDoor(GridPosition cell, out DoorData door) => _byCell.TryGetValue(cell, out door);

        public bool IsOpen(string doorId) => _open.TryGetValue(doorId, out var open) && open;

        /// <summary>False for cells without a door object (a door cell without DoorData counts as closed).</summary>
        public bool IsOpenAt(GridPosition cell) => _byCell.TryGetValue(cell, out var door) && IsOpen(door.Id);

        public bool IsLocked(string doorId) => _locked.Contains(doorId);

        public bool IsFrozen(string doorId) => _ice.ContainsKey(doorId);

        /// <summary>Hits still needed to break the ice; 0 when not frozen.</summary>
        public int IceLeft(string doorId) => _ice.TryGetValue(doorId, out var left) ? left : 0;

        /// <summary>One hit on the ice. False when the door is not frozen.</summary>
        public bool HitIce(string doorId)
        {
            var door = Get(doorId);
            if (!_ice.TryGetValue(doorId, out var left))
                return false;

            left--;
            if (left > 0)
            {
                _ice[doorId] = left;
                IceHit?.Invoke(door, left);
                return true;
            }

            _ice.Remove(doorId);
            IceBroken?.Invoke(door);
            return true;
        }

        /// <summary>Unlocks the door; the door stays closed. False when it was not locked.</summary>
        public bool Unlock(string doorId)
        {
            var door = Get(doorId);
            if (!_locked.Remove(doorId))
                return false;

            DoorUnlocked?.Invoke(door);
            return true;
        }

        public void SetOpen(string doorId, bool open)
        {
            var door = Get(doorId);
            if (_open[doorId] == open) return;
            if (open && _locked.Contains(doorId))
                throw new InvalidOperationException($"Door '{doorId}' is locked: unlock it with its key first.");
            if (open && _ice.ContainsKey(doorId))
                throw new InvalidOperationException($"Door '{doorId}' is frozen: break the ice first.");

            _open[doorId] = open;
            DoorChanged?.Invoke(door, open);
        }

        private DoorData Get(string doorId) =>
            _byId.TryGetValue(doorId, out var door) ? door : throw new ArgumentException($"Unknown door '{doorId}'.", nameof(doorId));
    }
}
