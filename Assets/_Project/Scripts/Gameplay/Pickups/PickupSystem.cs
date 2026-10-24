using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Weapons;
using UnityEngine;

namespace Purge.Gameplay.Pickups
{
    public enum PickupKind
    {
        Key = 0,
        Medkit = 1,
        Weapon = 2,
        Ammo = 3,
        Throwable = 4,
        /// <summary>The locked chest's key (stage 7в): picked up on entering, never expires.</summary>
        ChestKey = 5,
    }

    /// <summary>An item lying in a cell. Pickups never block movement (ТЗ §51).</summary>
    public sealed class Pickup
    {
        private readonly string _id;

        internal Pickup(PickupKind kind, GridPosition cell, KeyData key = null, MedkitData medkit = null, WeaponRuntime weapon = null)
        {
            Kind = kind;
            Cell = cell;
            Key = key;
            Medkit = medkit;
            Weapon = weapon;
            Position = new Vector2(cell.X, cell.Y);
            Home = Position;
            ExpiresAt = float.PositiveInfinity;
        }

        /// <summary>Dropped during play (not an object of the level): its own id, a point in the cell, maybe a lifetime.</summary>
        internal Pickup(string id, PickupKind kind, GridPosition cell, Vector2 position, float expiresAt,
            AmmoDefinition ammo = null, ThrowableDefinition throwable = null, MedkitDefinition medkitItem = null,
            WeaponRuntime weapon = null, int amount = 1, ContentDefinition chestKey = null)
        {
            _id = id;
            ChestKey = chestKey;
            Kind = kind;
            Cell = cell;
            Position = position;
            Home = position;
            ExpiresAt = expiresAt;
            Ammo = ammo;
            Throwable = throwable;
            MedkitItem = medkitItem;
            Weapon = weapon;
            Amount = amount;
        }

        public PickupKind Kind { get; }
        public GridPosition Cell { get; }

        /// <summary>
        /// Where it is (grid units): in its cell, the centre for level objects; on the way to the player while
        /// <see cref="Attracted"/>.
        /// </summary>
        public Vector2 Position { get; internal set; }

        /// <summary>Where it lies in its cell.</summary>
        internal Vector2 Home { get; }

        /// <summary>Flies to the player (the magnet of the run's cards); still lies in <see cref="Cell"/> until it arrives.</summary>
        public bool Attracted { get; internal set; }

        public KeyData Key { get; }

        /// <summary>A medkit placed in the level (heals to full); null for a dropped one (<see cref="MedkitItem"/>).</summary>
        public MedkitData Medkit { get; }

        public WeaponRuntime Weapon { get; }
        public AmmoDefinition Ammo { get; }
        public ThrowableDefinition Throwable { get; }

        /// <summary>A dropped medkit's kind.</summary>
        public MedkitDefinition MedkitItem { get; }

        /// <summary>The chest key's definition (its look) of a <see cref="PickupKind.ChestKey"/>.</summary>
        public ContentDefinition ChestKey { get; }

        /// <summary>Rounds of ammo, or throwables in the stack.</summary>
        public int Amount { get; internal set; }

        /// <summary>Game time (<see cref="PickupSystem.Time"/>) it disappears at; infinity — never.</summary>
        public float ExpiresAt { get; }

        /// <summary>Set before <see cref="PickupSystem.Removed"/>: taken by the player (false — it expired or made room).</summary>
        public bool WasTaken { get; internal set; }

        /// <summary>Not an object of the level: dropped by a monster, or a dropped weapon or stack that came from one.</summary>
        public bool IsDropped => _id != null || Weapon != null && Weapon.IsDropped;

        /// <summary>Entity id; also the id of its view.</summary>
        public string Id => _id ?? Key?.Id ?? Medkit?.Id ?? Weapon.Id;

        /// <summary>Level object that defines the saved visual; null for a dropped item.</summary>
        public LevelEntityData Source => IsDropped ? null : (LevelEntityData)Key ?? (LevelEntityData)Medkit ?? Weapon?.Source;

        /// <summary>Content kind of a dropped item (its look): ammo, throwable, medkit or weapon definition.</summary>
        public ContentDefinition Item =>
            (ContentDefinition)Ammo ?? (ContentDefinition)Throwable ?? (ContentDefinition)MedkitItem ?? ChestKey ?? Weapon?.Definition;
    }

    /// <summary>
    /// Items on the ground (ТЗ §67, §72, «Автоподбор»). On entering a cell the player picks up automatically keys, ammo
    /// (into <see cref="AmmoStock"/>), throwables the slot has room for (the same kind or an empty slot; the rest stays)
    /// and a medkit when hurt (a level's heals to full, a dropped one by its kind; at full health it stays). By Interact
    /// (<see cref="TryTakeWeapon"/>, <see cref="TryTakeThrowable"/>): a weapon — the new one becomes active, a replaced one
    /// drops into the cell (a slot with the base pistol is filled first); a throwable of another kind — it replaces the
    /// stack, which drops. Monsters' drops (<see cref="DropAmmo"/> etc.) lie for a lifetime and at most
    /// <see cref="MaxDropped"/> at once, of one weapon <see cref="MaxSameWeaponDropped"/> (the oldest goes); items the
    /// player drops stay. With a
    /// <see cref="MagnetRadius"/> (the run's cards) ammo, throwables the slot has room for and medkits (when hurt) that
    /// close fly to the player and are picked up on arrival; weapons and keys still only in their cell.
    /// </summary>
    public sealed class PickupSystem : ILevelLoadStep, ILevelTickable, IDisposable
    {
        private const float ExpiryCheckInterval = 0.25f;
        private const float MagnetCheckInterval = 0.1f;

        /// <summary>Cells per second of an item flying to the player; it arrives this close.</summary>
        public const float MagnetSpeed = 12f;
        private const float MagnetArrival = 0.3f;
        private static readonly List<Pickup> Empty = new List<Pickup>();

        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _health;
        private readonly PlayerInventory _inventory;
        private readonly WeaponSystem _weapons;
        private readonly SoundEventBus _sounds;
        private readonly AmmoStock _ammo;
        private readonly ThrowableSlot _throwables;
        private readonly Dictionary<GridPosition, List<Pickup>> _byCell = new Dictionary<GridPosition, List<Pickup>>();
        private readonly List<Pickup> _expiring = new List<Pickup>(); // monsters' drops, oldest first
        private readonly List<Pickup> _attracted = new List<Pickup>();
        private bool _subscribed;
        private int _nextDrop;
        private float _expiryTimer;
        private float _magnetTimer;

        public PickupSystem(LevelData level, PlayerSystem player, PlayerHealth health, PlayerInventory inventory, WeaponSystem weapons,
            SoundEventBus sounds, AmmoStock ammo = null, ThrowableSlot throwables = null)
        {
            _sounds = sounds;
            _level = level;
            _player = player;
            _health = health;
            _inventory = inventory;
            _weapons = weapons;
            _ammo = ammo ?? new AmmoStock();
            _throwables = throwables ?? new ThrowableSlot();
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        /// <summary>Game time of the pickups (seconds of ticking).</summary>
        public float Time { get; private set; }

        /// <summary>Most monsters' drops lying at once (the drop table's).</summary>
        public int MaxDropped { get; set; } = 40;

        /// <summary>Monsters' drops of one weapon lying at once (the oldest goes; its view pool holds as many).</summary>
        public const int MaxSameWeaponDropped = 8;

        public int DroppedCount => _expiring.Count;

        /// <summary>Cells around the player from which items fly to them (0 = none; the run's cards).</summary>
        public float MagnetRadius { get; set; }

        /// <summary>Items flying to the player now.</summary>
        public int AttractedCount => _attracted.Count;

        /// <summary>A pickup appeared (a dropped weapon, a monster's drop).</summary>
        public event Action<Pickup> Added;

        /// <summary>A pickup was taken, used or expired and no longer lies anywhere (<see cref="Pickup.WasTaken"/>).</summary>
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
            _expiring.Clear();
            _attracted.Clear();
            foreach (var key in _level.Keys)
                Place(new Pickup(PickupKind.Key, key.Position, key: key));
            foreach (var medkit in _level.Medkits)
                Place(new Pickup(PickupKind.Medkit, medkit.Position, medkit: medkit));
            foreach (var weapon in _level.Weapons)
                Place(new Pickup(PickupKind.Weapon, weapon.Position, weapon: new WeaponRuntime(weapon)));

            if (!_subscribed)
            {
                _player.Spawned += OnSpawned;
                _player.CellChanged += OnCellChanged;
                _weapons.DropRequested += OnDropRequested;
                _subscribed = true;
            }

            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            Time += deltaTime;
            TickMagnet(deltaTime);
            _expiryTimer -= deltaTime;
            if (_expiryTimer > 0f) return;
            _expiryTimer = ExpiryCheckInterval;
            for (var i = _expiring.Count - 1; i >= 0; i--)
                if (_expiring[i].ExpiresAt <= Time && !_expiring[i].Attracted)
                    Take(_expiring[i], taken: false);
        }

        // ------------------------------------------------------------------ Magnet

        private void TickMagnet(float deltaTime)
        {
            if (!_player.IsSpawned) return;
            if (MagnetRadius > 0f)
            {
                _magnetTimer -= deltaTime;
                if (_magnetTimer <= 0f)
                {
                    _magnetTimer = MagnetCheckInterval;
                    Attract();
                }
            }

            var target = _player.Position;
            var step = MagnetSpeed * deltaTime;
            for (var i = _attracted.Count - 1; i >= 0 && i < _attracted.Count; i--)
            {
                var pickup = _attracted[i];
                pickup.Position = Vector2.MoveTowards(pickup.Position, target, step);
                if ((pickup.Position - target).sqrMagnitude > MagnetArrival * MagnetArrival) continue;
                _attracted.RemoveAt(i);
                Arrive(pickup);
            }
        }

        /// <summary>Items within the radius on the player's floor start flying to them.</summary>
        private void Attract()
        {
            var radius = MagnetRadius;
            var reach = Mathf.CeilToInt(radius);
            var centre = _player.Position;
            var cell = _player.Cell;
            for (var y = cell.Y - reach; y <= cell.Y + reach; y++)
            for (var x = cell.X - reach; x <= cell.X + reach; x++)
            {
                if (!_byCell.TryGetValue(new GridPosition(x, y, cell.Floor), out var list)) continue;
                for (var i = 0; i < list.Count; i++)
                {
                    var pickup = list[i];
                    if (pickup.Attracted || !Attractable(pickup) || (pickup.Position - centre).sqrMagnitude > radius * radius)
                        continue;
                    pickup.Attracted = true;
                    _attracted.Add(pickup);
                }
            }
        }

        /// <summary>Monsters' drops only (level objects have their own views that do not fly).</summary>
        private bool Attractable(Pickup pickup) => pickup.IsDropped && pickup.Kind switch
        {
            PickupKind.Ammo => true,
            PickupKind.Throwable => _throwables.Room(pickup.Throwable) > 0,
            PickupKind.Medkit => !_health.IsFull,
            _ => false,
        };

        /// <summary>Picked up as in its cell; what cannot be taken now goes back where it lay.</summary>
        private void Arrive(Pickup pickup)
        {
            pickup.Attracted = false;
            switch (pickup.Kind)
            {
                case PickupKind.Ammo:
                    Take(pickup, taken: true);
                    _ammo.Add(pickup.Ammo, pickup.Amount);
                    return;
                case PickupKind.Throwable:
                    pickup.Amount -= _throwables.Add(pickup.Throwable, pickup.Amount);
                    if (pickup.Amount <= 0)
                    {
                        Take(pickup, taken: true);
                        return;
                    }

                    break;
                case PickupKind.Medkit when !_health.IsFull:
                    Take(pickup, taken: true);
                    UseMedkit(pickup);
                    return;
            }

            pickup.Position = pickup.Home;
        }

        /// <summary>Drops the weapon of the slot into the player's cell (the base pistol stays). False when nothing was dropped.</summary>
        public bool DropWeapon(WeaponSlot slot)
        {
            var weapon = _weapons.Drop(slot);
            if (weapon == null) return false;
            AddWeapon(_player.Cell, weapon);
            GameLog.Info(LogChannel.Gameplay, $"Dropped weapon '{weapon.Id}'.");
            return true;
        }

        /// <summary>Picks up the first weapon lying in <paramref name="cell"/>. False when there is none.</summary>
        public bool TryTakeWeapon(GridPosition cell)
        {
            var list = At(cell);
            for (var i = 0; i < list.Count; i++)
            {
                var pickup = list[i];
                if (pickup.Kind != PickupKind.Weapon) continue;

                Take(pickup, taken: true);
                var dropped = _weapons.Equip(pickup.Weapon);
                GameLog.Info(LogChannel.Gameplay, $"Picked up weapon '{pickup.Id}'.");
                if (dropped != null) AddWeapon(cell, dropped);
                return true;
            }

            return false;
        }

        /// <summary>A throwable in the cell the slot cannot simply add (another kind) — what Interact would take.</summary>
        public Pickup ThrowableToSwap(GridPosition cell)
        {
            var list = At(cell);
            for (var i = 0; i < list.Count; i++)
                if (list[i].Kind == PickupKind.Throwable && !_throwables.IsSameKind(list[i].Throwable) && !_throwables.IsEmpty)
                    return list[i];
            return null;
        }

        /// <summary>Takes a throwable of another kind lying in the cell; the slot's stack drops there. False when none.</summary>
        public bool TryTakeThrowable(GridPosition cell)
        {
            var pickup = ThrowableToSwap(cell);
            if (pickup == null) return false;
            Take(pickup, taken: true);
            var (old, count) = _throwables.Replace(pickup.Throwable, pickup.Amount);
            GameLog.Info(LogChannel.Gameplay, $"Picked up {pickup.Amount} × '{pickup.Throwable.Id}'.");
            if (old != null && count > 0)
                AddDrop(new Pickup(NextId(), PickupKind.Throwable, cell, CentreOf(cell), float.PositiveInfinity, throwable: old,
                    amount: count), expires: false);
            return true;
        }

        // ------------------------------------------------------------------ Monsters' drops

        // keep: a chest's reward (stage 7в) — lies until taken, not counted with the monsters' drops, not cleared by a wave.

        public Pickup DropAmmo(Vector2 at, int floor, AmmoDefinition ammo, int rounds, float lifetime, bool keep = false) =>
            ammo == null || rounds <= 0 ? null
                : AddDrop(Dropped(PickupKind.Ammo, at, floor, keep ? float.PositiveInfinity : lifetime, ammo: ammo, amount: rounds),
                    expires: !keep);

        public Pickup DropThrowable(Vector2 at, int floor, ThrowableDefinition throwable, int count, float lifetime, bool keep = false) =>
            throwable == null || count <= 0 ? null
                : AddDrop(Dropped(PickupKind.Throwable, at, floor, keep ? float.PositiveInfinity : lifetime, throwable: throwable,
                    amount: count), expires: !keep);

        public Pickup DropMedkit(Vector2 at, int floor, MedkitDefinition medkit, float lifetime, bool keep = false) =>
            medkit == null ? null
                : AddDrop(Dropped(PickupKind.Medkit, at, floor, keep ? float.PositiveInfinity : lifetime, medkit: medkit),
                    expires: !keep);

        public Pickup DropWeaponItem(Vector2 at, int floor, WeaponDefinition weapon, float lifetime, bool keep = false)
        {
            if (weapon == null) return null;
            var cell = CellOf(at, floor);
            var runtime = CreateWeapon(weapon, cell);
            return AddDrop(new Pickup(runtime.Id, PickupKind.Weapon, cell, Inside(at, cell),
                keep ? float.PositiveInfinity : Time + lifetime, weapon: runtime), expires: !keep);
        }

        /// <summary>The locked chest's key (stage 7в): lies until taken, never cleared by a wave.</summary>
        public Pickup DropChestKey(Vector2 at, int floor, ContentDefinition key)
        {
            var cell = CellOf(at, floor);
            return AddDrop(new Pickup(NextId(), PickupKind.ChestKey, cell, Inside(at, cell), float.PositiveInfinity,
                chestKey: key), expires: false);
        }

        /// <summary>A chest key lies somewhere in the level.</summary>
        public bool ChestKeyLying
        {
            get
            {
                foreach (var list in _byCell.Values)
                    for (var i = 0; i < list.Count; i++)
                        if (list[i].Kind == PickupKind.ChestKey) return true;
                return false;
            }
        }

        /// <summary>
        /// A weapon that is not an object of the level (a drop, the run's start weapon, a continued run's slot, stage 6e):
        /// a fresh drop id, full magazine.
        /// </summary>
        public WeaponRuntime CreateWeapon(WeaponDefinition weapon, GridPosition cell = default) =>
            new WeaponRuntime(new WeaponPickupData(NextId(), cell, weapon), dropped: true);

        /// <summary>
        /// Every monsters' drop still lying disappears (not taken; the views show it vanish) — the next wave starts
        /// (stage 6b). Items the player dropped stay.
        /// </summary>
        public int ClearDropped()
        {
            var count = _expiring.Count;
            for (var i = _expiring.Count - 1; i >= 0; i--)
                Take(_expiring[i], taken: false);
            if (count > 0) GameLog.Info(LogChannel.Gameplay, $"Wave drop gone: {count} item(s).");
            return count;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _player.Spawned -= OnSpawned;
            _player.CellChanged -= OnCellChanged;
            _weapons.DropRequested -= OnDropRequested;
            _subscribed = false;
        }

        private Pickup Dropped(PickupKind kind, Vector2 at, int floor, float lifetime, AmmoDefinition ammo = null,
            ThrowableDefinition throwable = null, MedkitDefinition medkit = null, int amount = 1)
        {
            var cell = CellOf(at, floor);
            return new Pickup(NextId(), kind, cell, Inside(at, cell), Time + lifetime, ammo, throwable, medkit, amount: amount);
        }

        /// <summary>A weapon the player dropped (or swapped): it stays until taken.</summary>
        private void AddWeapon(GridPosition cell, WeaponRuntime weapon)
        {
            var drop = weapon.IsDropped
                ? new Pickup(weapon.Id, PickupKind.Weapon, cell, CentreOf(cell), float.PositiveInfinity, weapon: weapon)
                : new Pickup(PickupKind.Weapon, cell, weapon: weapon);
            Place(drop);
            Added?.Invoke(drop);
        }

        private Pickup AddDrop(Pickup drop, bool expires)
        {
            if (expires)
            {
                while (_expiring.Count >= Math.Max(1, MaxDropped))
                    Take(_expiring[0], taken: false);
                if (drop.Kind == PickupKind.Weapon)
                    TrimSameWeapon(drop.Weapon.Definition);
                _expiring.Add(drop);
            }

            Place(drop);
            Added?.Invoke(drop);
            // Dropped where the player stands: picked up at once if it would be on entering.
            if (_player.IsSpawned && drop.Cell == _player.Cell)
                Enter(drop.Cell);
            return drop;
        }

        /// <summary>Makes room for one more monsters' drop of <paramref name="weapon"/>: the oldest ones of it go.</summary>
        private void TrimSameWeapon(WeaponDefinition weapon)
        {
            var count = 0;
            var oldest = -1;
            for (var i = 0; i < _expiring.Count; i++)
            {
                if (_expiring[i].Kind != PickupKind.Weapon || _expiring[i].Weapon.Definition != weapon) continue;
                if (count++ == 0) oldest = i;
            }

            if (count >= MaxSameWeaponDropped && oldest >= 0)
                Take(_expiring[oldest], taken: false);
        }

        private void OnDropRequested(WeaponSlot slot) => DropWeapon(slot);

        private void OnSpawned() => Enter(_player.Cell);

        private void OnCellChanged(GridPosition from, GridPosition to) => Enter(to);

        private void Enter(GridPosition cell)
        {
            if (!_byCell.TryGetValue(cell, out var list))
                return;

            for (var i = list.Count - 1; i >= 0 && i < list.Count; i--)
            {
                var pickup = list[i];
                switch (pickup.Kind)
                {
                    case PickupKind.Key:
                        Take(pickup, taken: true);
                        _inventory.AddKey(pickup.Key);
                        GameLog.Info(LogChannel.Gameplay, $"Picked up key '{pickup.Id}'.");
                        break;
                    case PickupKind.ChestKey:
                        Take(pickup, taken: true);
                        _inventory.AddChestKey();
                        GameLog.Info(LogChannel.Gameplay, "Picked up the chest key.");
                        break;
                    case PickupKind.Ammo:
                        Take(pickup, taken: true);
                        _ammo.Add(pickup.Ammo, pickup.Amount);
                        break;
                    case PickupKind.Throwable:
                        var added = _throwables.Add(pickup.Throwable, pickup.Amount);
                        if (added <= 0) break;
                        pickup.Amount -= added;
                        if (pickup.Amount <= 0) Take(pickup, taken: true);
                        break;
                }
            }

            if (_health.IsFull || !_byCell.TryGetValue(cell, out list))
                return;

            foreach (var pickup in list)
                if (pickup.Kind == PickupKind.Medkit)
                {
                    Take(pickup, taken: true);
                    UseMedkit(pickup);
                    break;
                }
        }

        private void UseMedkit(Pickup pickup)
        {
            if (pickup.MedkitItem != null) _health.Heal(pickup.MedkitItem.Heal);
            else _health.HealToFull();
            GameLog.Info(LogChannel.Gameplay, $"Used medkit '{pickup.Id}'.");
        }

        private void Place(Pickup pickup)
        {
            if (!_byCell.TryGetValue(pickup.Cell, out var list))
                _byCell[pickup.Cell] = list = new List<Pickup>();
            list.Add(pickup);
        }

        private void Take(Pickup pickup, bool taken)
        {
            if (!_byCell.TryGetValue(pickup.Cell, out var list) || !list.Remove(pickup)) return;
            if (list.Count == 0)
                _byCell.Remove(pickup.Cell);
            _expiring.Remove(pickup);
            if (pickup.Attracted)
            {
                pickup.Attracted = false;
                _attracted.Remove(pickup);
            }

            pickup.WasTaken = taken;
            if (taken)
                _sounds.Emit(SoundType.Pickup, pickup.Position, _player.Definition.InteractionSoundRadius, pickup.Cell.Floor);
            Removed?.Invoke(pickup);
        }

        private string NextId() => "drop_" + ++_nextDrop;

        private static GridPosition CellOf(Vector2 at, int floor) =>
            new GridPosition(Mathf.FloorToInt(at.x + 0.5f), Mathf.FloorToInt(at.y + 0.5f), floor);

        private static Vector2 CentreOf(GridPosition cell) => new Vector2(cell.X, cell.Y);

        /// <summary>The point, kept a little inside its cell (the item is seen in the cell it is picked up in).</summary>
        private static Vector2 Inside(Vector2 at, GridPosition cell) =>
            new Vector2(Mathf.Clamp(at.x, cell.X - 0.3f, cell.X + 0.3f), Mathf.Clamp(at.y, cell.Y - 0.3f, cell.Y + 0.3f));
    }
}
