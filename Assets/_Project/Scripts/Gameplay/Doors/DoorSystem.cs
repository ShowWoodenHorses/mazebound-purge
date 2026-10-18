using System;
using System.Collections.Generic;
using Purge.Core.Grid;
using Purge.Core.Level;
using UnityEngine;

namespace Purge.Gameplay.Doors
{
    /// <summary>
    /// Runtime door state (ТЗ §37, §45: the prefab never decides): open/closed and locked/unlocked.
    /// A door with a key starts locked unless it is initially open. A key only unlocks its door (1 key = 1 door);
    /// the door stays closed until it is opened separately, and an unlocked door can be opened and closed any
    /// number of times. Who may open or close (reach, occupancy) is decided by the interaction, not here.
    /// A frozen door (<see cref="DoorData.IceHits"/>) starts closed and cannot be opened until its ice is broken by that
    /// many hits (<see cref="HitIce"/>); then it is an ordinary door (still locked if it has a key).
    /// A door monsters break (<see cref="HitDoor"/>, stage 5d: the Brute) falls after <see cref="DoorData.Strength"/>
    /// damage: <see cref="IsBroken"/> — open for good (passage, bullets and sight), it can never be closed again; the
    /// fallen leaf lies on the floor as decor.
    /// </summary>
    public sealed class DoorSystem
    {
        private readonly Dictionary<GridPosition, DoorData> _byCell = new Dictionary<GridPosition, DoorData>();
        private readonly Dictionary<string, DoorData> _byId = new Dictionary<string, DoorData>();
        private readonly Dictionary<string, bool> _open = new Dictionary<string, bool>();
        private readonly HashSet<string> _locked = new HashSet<string>();
        private readonly Dictionary<string, int> _ice = new Dictionary<string, int>();
        private readonly Dictionary<string, float> _damage = new Dictionary<string, float>();
        private readonly HashSet<string> _broken = new HashSet<string>();

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

        /// <summary>(door, where the blow came from) a monster's blow that did not break the door yet.</summary>
        public event Action<DoorData, Vector2> DoorHit;

        /// <summary>(door, where the last blow came from) the door fell: raised before its <see cref="DoorChanged"/> (open).</summary>
        public event Action<DoorData, Vector2> DoorBroken;

        public IEnumerable<DoorData> Doors => _byId.Values;

        public bool TryGetDoor(GridPosition cell, out DoorData door) => _byCell.TryGetValue(cell, out door);

        public bool IsOpen(string doorId) => _open.TryGetValue(doorId, out var open) && open;

        /// <summary>False for cells without a door object (a door cell without DoorData counts as closed).</summary>
        public bool IsOpenAt(GridPosition cell) => _byCell.TryGetValue(cell, out var door) && IsOpen(door.Id);

        public bool IsLocked(string doorId) => _locked.Contains(doorId);

        public bool IsFrozen(string doorId) => _ice.ContainsKey(doorId);

        /// <summary>Hits still needed to break the ice; 0 when not frozen.</summary>
        public int IceLeft(string doorId) => _ice.TryGetValue(doorId, out var left) ? left : 0;

        /// <summary>Broken down by a monster: open for good.</summary>
        public bool IsBroken(string doorId) => _broken.Contains(doorId);

        /// <summary>Damage the door can still take before it falls.</summary>
        public float StrengthLeft(string doorId)
        {
            var door = Get(doorId);
            return _broken.Contains(doorId) ? 0f : door.Strength - (_damage.TryGetValue(doorId, out var taken) ? taken : 0f);
        }

        /// <summary>
        /// A closed door a monster may get through (stage 5d): not locked, not frozen. The rest stop every monster.
        /// </summary>
        public bool IsPassableForMonsters(string doorId) => !_locked.Contains(doorId) && !_ice.ContainsKey(doorId);

        /// <summary>
        /// A monster's blow on a closed door (<paramref name="from"/> — where it stands): after
        /// <see cref="DoorData.Strength"/> damage in all the door falls (<see cref="IsBroken"/>). True when this blow broke
        /// it; false also for an open, locked or frozen door (nothing happens).
        /// </summary>
        public bool HitDoor(string doorId, float damage, Vector2 from)
        {
            var door = Get(doorId);
            if (_open[doorId] || !IsPassableForMonsters(doorId) || damage <= 0f) return false;
            var taken = (_damage.TryGetValue(doorId, out var before) ? before : 0f) + damage;
            if (taken < door.Strength)
            {
                _damage[doorId] = taken;
                DoorHit?.Invoke(door, from);
                return false;
            }

            _damage.Remove(doorId);
            _broken.Add(doorId);
            _open[doorId] = true;
            DoorBroken?.Invoke(door, from);
            DoorChanged?.Invoke(door, true);
            return true;
        }

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
            if (!open && _broken.Contains(doorId))
                throw new InvalidOperationException($"Door '{doorId}' is broken: it cannot be closed.");

            _open[doorId] = open;
            DoorChanged?.Invoke(door, open);
        }

        private DoorData Get(string doorId) =>
            _byId.TryGetValue(doorId, out var door) ? door : throw new ArgumentException($"Unknown door '{doorId}'.", nameof(doorId));
    }
}
