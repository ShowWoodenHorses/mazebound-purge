using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Weapons;
using Purge.Gameplay.Waves;
using UnityEngine;

namespace Purge.Gameplay.Drops
{
    /// <summary>
    /// A killed horde monster (by anything — ТЗ: смерть от чего угодно даёт дроп) rolls its definition's table (the
    /// rank's), or the pool's (<see cref="DropRoller"/>) and the item lies where it died (<see cref="PickupSystem"/>: for the table's lifetime,
    /// at most its <see cref="Core.Drops.DropTable.MaxLying"/> at once). Inactive without a table or a horde.
    /// </summary>
    public sealed class DropSystem : ILevelLoadStep, IDisposable
    {
        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly DropPool _pool;
        private readonly PickupSystem _pickups;
        private readonly WeaponSystem _weapons;
        private readonly PlayerCombat _combat;
        private readonly PlayerHealth _health;
        private DeterministicRandom _random;
        private bool _subscribed;
        private readonly bool _lucky;
        private readonly WaveSystem _waves;
        private readonly bool _survival;
        private int _lastWave = int.MinValue;
        private int _waveKills;
        private int _survivalAmmoTenths;

        public DropSystem(LevelData level, HordeSystem horde, DropPool pool, PickupSystem pickups, WeaponSystem weapons,
            PlayerCombat combat, PlayerHealth health, LevelLaunchOptions options = null, WaveSystem waves = null)
        {
            _level = level;
            _horde = horde;
            _pool = pool;
            _pickups = pickups;
            _weapons = weapons;
            _combat = combat;
            _health = health;
            _lucky = options?.HasSkill(CharacterSkills.Luck) == true;
            _survival = options?.Survival == true;
            _waves = waves;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool IsActive => _subscribed;

        /// <summary>Items dropped so far (not counting empty drops).</summary>
        public int Dropped { get; private set; }

        /// <summary>(roll, where) after every item that dropped.</summary>
        public event Action<DropRoll, Vector2> ItemDropped;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_pool.Table == null || !_horde.IsActive || _subscribed)
                return UniTask.CompletedTask;

            _random = new DeterministicRandom((int)StableHash.Of(_level.name + "/drops"));
            _pickups.MaxDropped = _survival ? Math.Max(80, _pool.Table.MaxLying) : _pool.Table.MaxLying;
            _horde.Killed += OnKilled;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _horde.Killed -= OnKilled;
            _subscribed = false;
        }

        /// <summary>The player's state the smart drop looks at.</summary>
        public DropContext Context()
        {
            var first = _weapons.Get(WeaponSlot.First);
            var second = _weapons.Get(WeaponSlot.Second);
            var context = new DropContext
            {
                First = first != null && !first.IsBase ? first.Definition : null,
                Second = second != null && !second.IsBase ? second.Definition : null,
                ActiveSlot = (int)_weapons.ActiveSlot,
                FullHealth = _health.IsFull,
                RarityFactor = _horde.Difficulty != null ? _horde.Difficulty.DropRarity : 1f,
            };
            context.FirstReserve = context.First != null ? _combat.ReserveOf(context.First) : 0;
            context.SecondReserve = context.Second != null ? _combat.ReserveOf(context.Second) : 0;
            return context;
        }

        private void OnKilled(int agent, Vector2 position)
        {
            // ТЗ: ранг определяет дроп — the monster's table (its rank's), else the level's; a champion's — a rank higher.
            var definition = _horde.DefinitionOf(agent);
            var table = _horde.IsChampion(agent) ? _horde.Champions.DropOf(definition != null ? definition.Rank : MonsterRank.Mass) : null;
            if (table == null) table = definition != null && definition.Drop != null ? definition.Drop : _pool.Table;
            var context = Context();
            if (_survival)
            {
                var wave = _waves?.WaveIndex ?? -1;
                if (_lastWave != wave)
                {
                    _lastWave = wave;
                    _waveKills = 0;
                }
                _waveKills++;
                if (_waveKills % 3 == 0)
                    PlaceDrop(DropRoller.SurvivalAmmo(_pool.Table, _pool, context, _random), agent, position, float.PositiveInfinity);
                if (_waveKills % 12 == 3 &&
                    PlaceDrop(DropRoller.Weapon(_pool.Table, _pool, context, _random, allowEquipped: false),
                        agent, position, float.PositiveInfinity))
                    return;
            }

            var roll = DropRoller.Roll(table, _pool, context, _random, weaponChanceFactor: _lucky ? 1.15f : 1f);
            PlaceDrop(roll, agent, position, _horde.UsesWaves ? float.PositiveInfinity : table.Lifetime);
        }

        private bool PlaceDrop(DropRoll roll, int agent, Vector2 position, float lifetime)
        {
            if (_survival && roll.Kind == DropKind.Ammo)
                roll = DropRoller.ScaleSurvivalAmmo(roll, ref _survivalAmmoTenths);
            if (roll.Kind == DropKind.Nothing) return false;

            var floor = _horde.FloorOf(agent);
            var pickup = roll.Kind switch
            {
                DropKind.Ammo => _survival
                    ? _pickups.DropSurvivalAmmo(position, floor, (AmmoDefinition)roll.Item, roll.Amount, lifetime)
                    : _pickups.DropAmmo(position, floor, (AmmoDefinition)roll.Item, roll.Amount, lifetime),
                DropKind.Medkit => _pickups.DropMedkit(position, floor, (MedkitDefinition)roll.Item, lifetime),
                DropKind.Throwable => _pickups.DropThrowable(position, floor, (ThrowableDefinition)roll.Item, roll.Amount, lifetime),
                DropKind.Weapon => _pickups.DropWeaponItem(position, floor, (WeaponDefinition)roll.Item, lifetime),
                _ => null,
            };
            if (pickup == null) return false;
            Dropped++;
            ItemDropped?.Invoke(roll, position);
            return true;
        }
    }
}
