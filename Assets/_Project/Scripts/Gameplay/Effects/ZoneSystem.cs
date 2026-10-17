using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Effects
{
    /// <summary>
    /// Zones on cells (ТЗ «Зоны на клетках»: огонь, кислота…) in flat arrays over every cell of every floor — kind,
    /// seconds left, strength, how far it spread — plus a compact list of the active cells; never an object per cell.
    /// Ticks <see cref="TicksPerSecond"/> times a second: monsters standing in a zone take its damage (× strength,
    /// through <see cref="CombatTargets"/>) and its state (<see cref="StatusSystem"/>); a spreading zone sets
    /// neighbouring flammable cells alight up to its step limit. The same zone again on a cell: the longer duration and
    /// the stronger strength of the two, never their sum; another zone replaces it (combinations — later). A zone lies only
    /// where a bullet could be (not in walls, trees, rocks or closed doors). A hostile zone (monsters' — stage 5) hurts the
    /// player too; the player's own never does. At most <see cref="MaxCells"/> cells at once (new ones are dropped).
    /// Allocation-free per tick.
    /// </summary>
    public sealed class ZoneSystem : ILevelLoadStep, ILevelTickable
    {
        public const int TicksPerSecond = 8;
        public const float TickInterval = 1f / TicksPerSecond;
        public const int MaxCells = 768;
        public const int MaxKinds = 16;

        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;
        private readonly CombatTargets _targets;
        private readonly HordeSystem _horde;
        private readonly StatusSystem _statuses;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _health;
        private readonly List<ZoneDefinition> _kinds = new List<ZoneDefinition>();
        private readonly DeterministicRandom _random = new DeterministicRandom(0x20e5);

        private byte[] _kind; // kind index + 1; 0 = no zone
        private float[] _left;
        private float[] _duration;
        private float[] _strength;
        private byte[] _steps;
        private bool[] _hostile;
        private int[] _cells;
        private float _tickTimer;
        private float _playerDamage;

        public ZoneSystem(LevelGrid grid, DoorSystem doors, CombatTargets targets, HordeSystem horde, StatusSystem statuses,
            PlayerSystem player, PlayerHealth health)
        {
            _grid = grid;
            _doors = doors;
            _targets = targets;
            _horde = horde;
            _statuses = statuses;
            _player = player;
            _health = health;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        public int Count { get; private set; }

        /// <summary>Indices (<see cref="LevelGrid.ToIndex"/>) of the cells with a zone, first <see cref="Count"/>.</summary>
        public ReadOnlySpan<int> Cells => new ReadOnlySpan<int>(_cells, 0, Count);

        /// <summary>Kinds seen so far; <see cref="KindAt"/> is an index into it.</summary>
        public IReadOnlyList<ZoneDefinition> Kinds => _kinds;

        /// <summary>Changes whenever a zone appears or disappears somewhere (views rebuild masks only then).</summary>
        public int Version { get; private set; }

        public LevelGrid Grid => _grid;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var size = _grid.CellCount;
            _kind = new byte[size];
            _left = new float[size];
            _duration = new float[size];
            _strength = new float[size];
            _steps = new byte[size];
            _hostile = new bool[size];
            _cells = new int[MaxCells];
            Count = 0;
            return UniTask.CompletedTask;
        }

        /// <summary>Kind index of the zone in the cell; -1 = none.</summary>
        public int KindAt(int index) => _kind != null && (uint)index < (uint)_kind.Length ? _kind[index] - 1 : -1;

        public ZoneDefinition ZoneAt(GridPosition cell)
        {
            if (_kind == null || !_grid.IsInside(cell)) return null;
            var kind = KindAt(_grid.ToIndex(cell));
            return kind >= 0 ? _kinds[kind] : null;
        }

        /// <summary>Share of the zone's time left, 0..1.</summary>
        public float Strength01(int index) => _duration[index] > 0f ? Mathf.Clamp01(_left[index] / _duration[index]) : 0f;

        public float StrengthAt(int index) => _kind[index] != 0 ? _strength[index] : 0f;

        /// <summary>The zone on one cell (see the class remarks for overlaps). False when the cell cannot hold it.</summary>
        public bool Place(GridPosition cell, ZoneDefinition zone, float duration, float strength = 1f, bool hostile = false,
            int steps = 0)
        {
            if (_kind == null || zone == null || duration <= 0f || !CanHold(cell)) return false;
            var kind = Register(zone);
            if (kind < 0) return false;

            var index = _grid.ToIndex(cell);
            var code = (byte)(kind + 1);
            if (_kind[index] == code)
            {
                if (_left[index] < duration)
                {
                    _left[index] = duration;
                    _duration[index] = duration;
                }

                _strength[index] = Mathf.Max(_strength[index], strength);
                _steps[index] = (byte)Mathf.Min(_steps[index], steps);
                _hostile[index] |= hostile;
                return true;
            }

            if (_kind[index] == 0)
            {
                if (Count >= MaxCells) return false;
                _cells[Count++] = index;
            }

            _kind[index] = code;
            _left[index] = duration;
            _duration[index] = duration;
            _strength[index] = strength;
            _steps[index] = (byte)Mathf.Clamp(steps, 0, 255);
            _hostile[index] = hostile;
            Version++;
            return true;
        }

        /// <summary>
        /// The zone on every cell within <paramref name="radius"/> of the cell at <paramref name="at"/> that a bullet from
        /// there could reach (walls cover). Returns the number of cells.
        /// </summary>
        public int PlaceArea(Vector2 at, int floor, float radius, ZoneDefinition zone, float duration, float strength = 1f,
            bool hostile = false)
        {
            var cx = Mathf.FloorToInt(at.x + 0.5f);
            var cy = Mathf.FloorToInt(at.y + 0.5f);
            var reach = Mathf.CeilToInt(radius);
            var origin = new Vector2(cx, cy);
            var placed = 0;
            for (var y = cy - reach; y <= cy + reach; y++)
            for (var x = cx - reach; x <= cx + reach; x++)
            {
                var offset = new Vector2(x - cx, y - cy);
                if (offset.sqrMagnitude > radius * radius + 0.01f) continue;
                var cell = new GridPosition(x, y, floor);
                if ((x != cx || y != cy) && !_targets.IsClear(origin, new Vector2(x, y), floor)) continue;
                if (Place(cell, zone, duration, strength, hostile)) placed++;
            }

            return placed;
        }

        /// <summary>Removes every zone (level stop, tests).</summary>
        public void Clear()
        {
            for (var i = 0; i < Count; i++)
                _kind[_cells[i]] = 0;
            Count = 0;
            Version++;
        }

        public void Tick(float deltaTime)
        {
            if (Count == 0 || deltaTime <= 0f)
            {
                _tickTimer = 0f;
                return;
            }

            _tickTimer += deltaTime;
            // At most two zone ticks per frame: a long frame does not burst.
            for (var n = 0; n < 2 && _tickTimer >= TickInterval; n++)
            {
                _tickTimer -= TickInterval;
                ZoneTick(TickInterval);
            }

            if (_tickTimer > TickInterval) _tickTimer = TickInterval;
        }

        private void ZoneTick(float interval)
        {
            var floorSize = _grid.FloorSize;
            var width = _grid.Width;
            var hordeFloor = _player.Floor;
            var playerIndex = _player.IsSpawned && _grid.IsInside(_player.Cell) ? _grid.ToIndex(_player.Cell) : -1;
            // New cells (spread) are appended: count them from the next tick.
            for (var i = Count - 1; i >= 0; i--)
            {
                var index = _cells[i];
                var zone = _kinds[_kind[index] - 1];
                var floor = index / floorSize;
                var x = index % floorSize % width;
                var y = index % floorSize / width;

                var damage = zone.Damage != null ? zone.Damage.DamagePerSecond * interval * _strength[index] : 0f;
                var status = zone.Status;
                if ((damage > 0f || status != null) && floor == hordeFloor)
                {
                    for (var agent = _horde.FirstInCell(x, y); agent >= 0; agent = _horde.NextInCell(agent))
                    {
                        var target = CombatTarget.OfAgent(agent);
                        if (!_targets.IsAlive(target)) continue;
                        if (status != null && status.Status != null) _statuses.Apply(target, status.Status, status.Duration);
                        if (damage > 0f) _targets.Damage(target, damage, Vector2.up);
                    }
                }

                if (_hostile[index] && index == playerIndex && damage > 0f)
                    HurtPlayer(damage);

                if (zone.Spread != null && _steps[index] < zone.Spread.MaxSteps &&
                    _random.NextDouble() < zone.Spread.ChancePerSecond * interval)
                    Spread(index, floor, x, y, zone);

                _left[index] -= interval;
                if (_left[index] > 0f) continue;

                _kind[index] = 0;
                _cells[i] = _cells[--Count];
                Version++;
            }
        }

        private void Spread(int index, int floor, int x, int y, ZoneDefinition zone)
        {
            // One random neighbour by side.
            var side = _random.NextInt(4);
            var cell = side switch
            {
                0 => new GridPosition(x, y + 1, floor),
                1 => new GridPosition(x + 1, y, floor),
                2 => new GridPosition(x, y - 1, floor),
                _ => new GridPosition(x - 1, y, floor),
            };
            if (!_grid.Has(cell, CellFlags.Flammable)) return;
            var neighbour = _grid.ToIndex(cell);
            if (_kind[neighbour] != 0 && _kinds[_kind[neighbour] - 1] == zone) return;
            Place(cell, zone, _duration[index] * zone.Spread.FlammableDuration, _strength[index], _hostile[index], _steps[index] + 1);
        }

        private void HurtPlayer(float damage)
        {
            _playerDamage += damage;
            var whole = Mathf.FloorToInt(_playerDamage);
            if (whole <= 0 || _health.IsDead) return;
            _playerDamage -= whole;
            _health.Damage(whole);
        }

        /// <summary>A bullet could be in the cell: no wall, tree, rock or closed door.</summary>
        private bool CanHold(GridPosition cell)
        {
            if (!_grid.IsInside(cell) || _grid.Has(cell, CellFlags.BlocksBullets)) return false;
            return !_doors.TryGetDoor(cell, out var door) || _doors.IsOpen(door.Id);
        }

        private int Register(ZoneDefinition zone)
        {
            for (var i = 0; i < _kinds.Count; i++)
                if (_kinds[i] == zone || !string.IsNullOrEmpty(zone.Id) && _kinds[i].Id == zone.Id)
                    return i;
            if (_kinds.Count >= MaxKinds)
            {
                GameLog.Warning(LogChannel.Gameplay, $"More than {MaxKinds} kinds of zones: '{zone.name}' is ignored.");
                return -1;
            }

            _kinds.Add(zone);
            return _kinds.Count - 1;
        }
    }
}
