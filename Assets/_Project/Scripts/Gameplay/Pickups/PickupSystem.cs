using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Level;
using Maze.Gameplay.Map;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Weapons;

namespace Maze.Gameplay.Pickups
{
    public enum PickupKind
    {
        Key = 0,
        Medkit = 1,
        Weapon = 2,
        MapFragment = 3,
    }

    /// <summary>An item lying in a cell. Pickups never block movement (ТЗ §51).</summary>
    public sealed class Pickup
    {
        internal Pickup(PickupKind kind, GridPosition cell, KeyData key = null, MedkitData medkit = null, WeaponRuntime weapon = null,
            MapFragmentData fragment = null)
        {
            Fragment = fragment;
            Kind = kind;
            Cell = cell;
            Key = key;
            Medkit = medkit;
            Weapon = weapon;
        }

        public PickupKind Kind { get; }
        public GridPosition Cell { get; }
        public KeyData Key { get; }
        public MedkitData Medkit { get; }
        public WeaponRuntime Weapon { get; }
        public MapFragmentData Fragment { get; }

        /// <summary>Entity id; also the id of its view.</summary>
        public string Id => Key?.Id ?? Medkit?.Id ?? Fragment?.Id ?? Weapon.Id;

        /// <summary>Level object that defines the saved visual.</summary>
        public LevelEntityData Source => (LevelEntityData)Key ?? (LevelEntityData)Medkit ?? (LevelEntityData)Fragment ?? Weapon.Source;
    }

    /// <summary>
    /// Items on the ground (ТЗ §58, §67, §72). On entering a cell the player picks up keys and map fragments
    /// automatically and uses a
    /// medkit only when hurt (it heals to full and disappears; at full HP it stays). Weapons are picked up by the
    /// Interact action (<see cref="TryTakeWeapon"/>): the new one becomes active, a replaced one drops into the cell.
    /// </summary>
    public sealed class PickupSystem : ILevelLoadStep, IDisposable
    {
        private static readonly List<Pickup> Empty = new List<Pickup>();

        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _health;
        private readonly PlayerInventory _inventory;
        private readonly WeaponSystem _weapons;
        private readonly SoundEventBus _sounds;
        private readonly MapSystem _map;
        private readonly Dictionary<GridPosition, List<Pickup>> _byCell = new Dictionary<GridPosition, List<Pickup>>();
        private bool _subscribed;

        public PickupSystem(LevelData level, PlayerSystem player, PlayerHealth health, PlayerInventory inventory, WeaponSystem weapons,
            SoundEventBus sounds, MapSystem map)
        {
            _map = map;
            _sounds = sounds;
            _level = level;
            _player = player;
            _health = health;
            _inventory = inventory;
            _weapons = weapons;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        /// <summary>A pickup appeared (a dropped weapon).</summary>
        public event Action<Pickup> Added;

        /// <summary>A pickup was taken or used and no longer lies anywhere.</summary>
        public event Action<Pickup> Removed;

        public int Count
        {
            get
            {
                var count = 0;
                foreach (var list in _byCell.Values) count += list.Count;
                return count;
            }
        }

        public IReadOnlyList<Pickup> At(GridPosition cell) => _byCell.TryGetValue(cell, out var list) ? list : Empty;

        /// <summary>Every pickup lying in the level now, into <paramref name="result"/> (cleared first).</summary>
        public void GetAll(List<Pickup> result)
        {
            result.Clear();
            foreach (var list in _byCell.Values) result.AddRange(list);
        }

        /// <summary>A pickup with this id lies somewhere in the level now.</summary>
        public bool IsLying(string id)
        {
            foreach (var list in _byCell.Values)
                for (var i = 0; i < list.Count; i++)
                    if (list[i].Id == id) return true;
            return false;
        }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _byCell.Clear();
            foreach (var key in _level.Keys)
                Place(new Pickup(PickupKind.Key, key.Position, key: key));
            foreach (var medkit in _level.Medkits)
                Place(new Pickup(PickupKind.Medkit, medkit.Position, medkit: medkit));
            foreach (var weapon in _level.Weapons)
                Place(new Pickup(PickupKind.Weapon, weapon.Position, weapon: new WeaponRuntime(weapon)));
            foreach (var fragment in _level.MapFragments)
                Place(new Pickup(PickupKind.MapFragment, fragment.Position, fragment: fragment));

            if (!_subscribed)
            {
                _player.Spawned += OnSpawned;
                _player.CellChanged += OnCellChanged;
                _subscribed = true;
            }

            return UniTask.CompletedTask;
        }

        /// <summary>Picks up the first weapon lying in <paramref name="cell"/>. False when there is none.</summary>
        public bool TryTakeWeapon(GridPosition cell)
        {
            var list = At(cell);
            for (var i = 0; i < list.Count; i++)
            {
                var pickup = list[i];
                if (pickup.Kind != PickupKind.Weapon) continue;

                Take(pickup);
                var dropped = _weapons.Equip(pickup.Weapon);
                GameLog.Info(LogChannel.Gameplay, $"Picked up weapon '{pickup.Id}'.");
                if (dropped != null)
                {
                    var drop = new Pickup(PickupKind.Weapon, cell, weapon: dropped);
                    Place(drop);
                    Added?.Invoke(drop);
                }

                return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _player.Spawned -= OnSpawned;
            _player.CellChanged -= OnCellChanged;
            _subscribed = false;
        }

        private void OnSpawned() => Enter(_player.Cell);

        private void OnCellChanged(GridPosition from, GridPosition to) => Enter(to);

        private void Enter(GridPosition cell)
        {
            if (!_byCell.TryGetValue(cell, out var list))
                return;

            for (var i = list.Count - 1; i >= 0; i--)
            {
                var pickup = list[i];
                if (pickup.Kind == PickupKind.Key)
                {
                    Take(pickup);
                    _inventory.AddKey(pickup.Key);
                    GameLog.Info(LogChannel.Gameplay, $"Picked up key '{pickup.Id}'.");
                }
                else if (pickup.Kind == PickupKind.MapFragment)
                {
                    Take(pickup);
                    _map.Collect(pickup.Fragment);
                }
            }

            if (_health.IsFull)
                return;

            foreach (var pickup in At(cell))
                if (pickup.Kind == PickupKind.Medkit)
                {
                    Take(pickup);
                    _health.HealToFull();
                    GameLog.Info(LogChannel.Gameplay, $"Used medkit '{pickup.Id}'.");
                    break;
                }
        }

        private void Place(Pickup pickup)
        {
            if (!_byCell.TryGetValue(pickup.Cell, out var list))
                _byCell[pickup.Cell] = list = new List<Pickup>();
            list.Add(pickup);
        }

        private void Take(Pickup pickup)
        {
            var list = _byCell[pickup.Cell];
            list.Remove(pickup);
            if (list.Count == 0)
                _byCell.Remove(pickup.Cell);
            _sounds.Emit(SoundType.Pickup, new UnityEngine.Vector2(pickup.Cell.X, pickup.Cell.Y), _player.Definition.InteractionSoundRadius);
            Removed?.Invoke(pickup);
        }
    }
}
