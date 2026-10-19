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
        private readonly PlayerSystem _player;
        private DeterministicRandom _random;
        private bool _subscribed;

        public DropSystem(LevelData level, HordeSystem horde, DropPool pool, PickupSystem pickups, WeaponSystem weapons,
            PlayerCombat combat, PlayerHealth health, PlayerSystem player)
        {
            _level = level;
            _horde = horde;
            _pool = pool;
            _pickups = pickups;
            _weapons = weapons;
            _combat = combat;
            _health = health;
            _player = player;
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
            _pickups.MaxDropped = _pool.Table.MaxLying;
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
            var roll = DropRoller.Roll(table, _pool, Context(), _random);
            if (roll.Kind == DropKind.Nothing) return;

            var floor = _player.Floor; // the horde lives on the player's floor
            // On a wave level the drop lies until the next wave starts (stage 6b: WaveSystem clears it), not for a lifetime.
            var lifetime = _horde.UsesWaves ? float.PositiveInfinity : table.Lifetime;
            var pickup = roll.Kind switch
            {
                DropKind.Ammo => _pickups.DropAmmo(position, floor, (AmmoDefinition)roll.Item, roll.Amount, lifetime),
                DropKind.Medkit => _pickups.DropMedkit(position, floor, (MedkitDefinition)roll.Item, lifetime),
                DropKind.Throwable => _pickups.DropThrowable(position, floor, (ThrowableDefinition)roll.Item, roll.Amount, lifetime),
                DropKind.Weapon => _pickups.DropWeaponItem(position, floor, (WeaponDefinition)roll.Item, lifetime),
                _ => null,
            };
            if (pickup == null) return;
            Dropped++;
            ItemDropped?.Invoke(roll, position);
        }
    }
}
