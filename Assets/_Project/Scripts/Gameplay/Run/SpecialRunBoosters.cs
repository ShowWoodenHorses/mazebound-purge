using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Boosters;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Core.Monsters;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Waves;
using Purge.Gameplay.Weapons;
using UnityEngine;

namespace Purge.Gameplay.Run
{
    [Serializable]
    public sealed class SupplyAmmo
    {
        public string AmmoId;
        public int Rounds;
    }

    public sealed class SpecialRunBoosters : ILevelLoadStep, ILevelTickable, IDisposable
    {
        private readonly LevelLaunchOptions _options;
        private readonly WaveSystem _waves;
        private readonly HordeSystem _horde;
        private readonly WeaponSystem _weapons;
        private readonly PickupSystem _pickups;
        private readonly PlayerSystem _player;
        private readonly LevelPassability _passability;
        private readonly ContentCatalog _content;
        private readonly CombatTargets _targets;
        private readonly EffectSystem _effects;
        private readonly SoundEventBus _sounds;
        private readonly List<CombatTarget> _nearby = new List<CombatTarget>();
        private readonly List<Vector2> _mines = new List<Vector2>(4);
        private readonly Dictionary<Pickup, SupplyAmmo> _supply = new Dictionary<Pickup, SupplyAmmo>();
        private GridPosition _start;
        private int _bountiesPaid;
        private bool _supplyPlaced;
        private ExplosionEffect _mineLook;

        public SpecialRunBoosters(LevelLaunchOptions options, WaveSystem waves, HordeSystem horde, WeaponSystem weapons,
            PickupSystem pickups, PlayerSystem player, LevelPassability passability, ContentCatalog content,
            CombatTargets targets, EffectSystem effects, SoundEventBus sounds)
        {
            _options = options;
            _waves = waves;
            _horde = horde;
            _weapons = weapons;
            _pickups = pickups;
            _player = player;
            _passability = passability;
            _content = content;
            _targets = targets;
            _effects = effects;
            _sounds = sounds;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;
        public IReadOnlyList<Vector2> Mines => _mines;
        public int MineFloor => _start.Floor;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_player.IsSpawned) return UniTask.CompletedTask;
            _start = _player.Cell;
            if (_options.HasBoost(SpecialBoosterKind.Headhunter)) _horde.Killed += OnKilled;
            _waves.WaveStarted += OnWaveStarted;
            _waves.WaveCleared += OnWaveCleared;
            _pickups.Removed += OnPickupRemoved;
            if (_options.HasBoost(SpecialBoosterKind.Minefield))
            {
                var mine = _content?.Find<ThrowableDefinition>("mine");
                if (mine != null)
                    foreach (var effect in mine.Effects)
                        if (effect is ExplosionEffect explosion) { _mineLook = explosion; break; }
            }

            var saved = _options.Resume;
            if (saved != null)
            {
                _bountiesPaid = saved.BountiesPaid;
                _supplyPlaced = saved.SupplyPlaced;
                foreach (var item in saved.Supply) PlaceSupply(item);
            }
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            _horde.Killed -= OnKilled;
            _waves.WaveStarted -= OnWaveStarted;
            _waves.WaveCleared -= OnWaveCleared;
            _pickups.Removed -= OnPickupRemoved;
        }

        public void Capture(RunSnapshot snapshot)
        {
            snapshot.BountiesPaid = _bountiesPaid;
            snapshot.SupplyPlaced = _supplyPlaced;
            snapshot.Supply.AddRange(_supply.Values);
        }

        private void OnPickupRemoved(Pickup pickup) => _supply.Remove(pickup);

        private void OnKilled(int agent, Vector2 position)
        {
            if (_bountiesPaid >= 3) return;
            var rank = _horde.DefinitionOf(agent)?.Rank;
            if (rank != MonsterRank.Officer && rank != MonsterRank.Elite) return;
            var active = _weapons.ActiveSlot;
            var definition = _weapons.Get(active)?.Definition;
            if (definition?.UsesAmmo != true)
                definition = _weapons.Get(active == WeaponSlot.First ? WeaponSlot.Second : WeaponSlot.First)?.Definition;
            if (definition?.UsesAmmo != true) return;
            if (_pickups.DropAmmo(position, _player.Floor, definition.Ammo.Ammo, definition.MagazineSize, 30f) != null)
                _bountiesPaid++;
        }

        private void OnWaveStarted(int index)
        {
            if (index == 0 && _options.HasBoost(SpecialBoosterKind.Minefield)) ArmMines();
            if (index != 1 || _supplyPlaced || !_options.HasBoost(SpecialBoosterKind.DropLoad)) return;
            _supplyPlaced = true;
            for (var i = 0; i < 2; i++)
            {
                var definition = _weapons.Get((WeaponSlot)i)?.Definition;
                if (definition?.UsesAmmo == true)
                    PlaceSupply(new SupplyAmmo { AmmoId = definition.Ammo.Ammo.Id, Rounds = definition.MagazineSize * 2 });
            }
        }

        private void PlaceSupply(SupplyAmmo item)
        {
            var ammo = _content?.Find<AmmoDefinition>(item.AmmoId);
            if (ammo == null) return;
            var cell = _start;
            for (var i = 0; i < 4; i++)
            {
                var side = (_supply.Count + i) % 4;
                var offset = Cardinal[side];
                var next = new GridPosition(_start.X + offset.x, _start.Y + offset.y, _start.Floor);
                if (!_passability.IsWalkable(next)) continue;
                cell = next;
                break;
            }
            var drop = _pickups.DropAmmo(new Vector2(cell.X, cell.Y), cell.Floor, ammo, item.Rounds, 0f, keep: true);
            if (drop != null) _supply.Add(drop, item);
        }

        private static readonly Vector2Int[] Cardinal = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        private void ArmMines()
        {
            _mines.Clear();
            foreach (var direction in Cardinal)
            {
                var side = new Vector2Int(-direction.y, direction.x);
                var before = _mines.Count;
                for (var distance = 3; distance <= 4; distance++)
                {
                    for (var shift = -2; shift <= 2; shift++)
                    {
                        var offset = direction * distance + side * shift;
                        var cell = new GridPosition(_start.X + offset.x, _start.Y + offset.y, _start.Floor);
                        if (!_passability.IsWalkable(cell)) continue;
                        _mines.Add(new Vector2(cell.X, cell.Y));
                        break;
                    }
                    if (_mines.Count != before) break;
                }
            }
        }

        private void OnWaveCleared(int index) => _mines.Clear();

        private bool EnemyInRange(Vector2 position)
        {
            foreach (var target in _nearby)
                if (target.Agent >= 0 && (_targets.PositionOf(target) - position).sqrMagnitude <= 1f &&
                    _targets.IsClear(position, _targets.PositionOf(target), _start.Floor)) return true;
            return false;
        }

        public void Tick(float deltaTime)
        {
            for (var i = _mines.Count - 1; i >= 0; i--)
            {
                var position = _mines[i];
                _targets.Collect(position, 1f, _start.Floor, _nearby);
                if (!EnemyInRange(position)) continue;
                _mines.RemoveAt(i);
                _targets.Collect(position, 2f, _start.Floor, _nearby);
                foreach (var target in _nearby)
                {
                    var targetPosition = _targets.PositionOf(target);
                    if (target.Agent < 0 || (targetPosition - position).sqrMagnitude > 4f ||
                        !_targets.IsClear(position, targetPosition, _start.Floor)) continue;
                    _targets.Damage(target, 200f, (targetPosition - position).normalized);
                }
                _effects.ShowExplosion(position, _start.Floor, 2f, _mineLook);
                _sounds.EmitPlayer(SoundType.Explosion, position, _mineLook?.SoundRadius ?? 8f, _start.Floor);
            }
        }
    }
}
