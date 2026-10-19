using System;
using System.Collections.Generic;
using Purge.Core.Cards;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using UnityEngine;

namespace Purge.Gameplay.Run
{
    /// <summary>
    /// Stats of the run the cards changed (stage 6c; they last until the run's end): sums by <see cref="RunStat"/> and
    /// the bonus damage against monsters with a state. Read by the systems that use them (<see cref="Combat.PlayerCombat"/>:
    /// fire rate, reload, magazine; <see cref="RunDamage"/>: damage, crits, explosions); <see cref="CardSystem"/> pushes the
    /// rest into the player (speed, HP, magnet). Zero everywhere = no cards.
    /// </summary>
    public sealed class RunStats
    {
        /// <summary>× damage of a critical hit.</summary>
        public const float CriticalFactor = 2f;

        /// <summary>The shortest reload, × the weapon's (−12 % × 5 levels is −60 %).</summary>
        public const float MinReloadFactor = 0.25f;

        private static readonly int StatCount = Enum.GetValues(typeof(RunStat)).Length;

        private readonly float[] _values = new float[StatCount];
        private readonly List<(StatusDefinition Status, float Bonus)> _statusDamage = new List<(StatusDefinition, float)>();

        /// <summary>After a stat changed.</summary>
        public event Action Changed;

        public float Get(RunStat stat) => _values[(int)stat];

        public void Add(RunStat stat, float value)
        {
            if (value == 0f) return;
            _values[(int)stat] += value;
            Changed?.Invoke();
        }

        /// <summary>+<paramref name="bonus"/> share of the damage to monsters with <paramref name="status"/>.</summary>
        public void AddStatusDamage(StatusDefinition status, float bonus)
        {
            if (status == null || bonus == 0f) return;
            for (var i = 0; i < _statusDamage.Count; i++)
            {
                if (_statusDamage[i].Status != status) continue;
                _statusDamage[i] = (status, _statusDamage[i].Bonus + bonus);
                Changed?.Invoke();
                return;
            }

            _statusDamage.Add((status, bonus));
            Changed?.Invoke();
        }

        /// <summary>(state, bonus share) of the damage against monsters with a state.</summary>
        public IReadOnlyList<(StatusDefinition Status, float Bonus)> StatusDamage => _statusDamage;

        public float WeaponDamageFactor => 1f + Get(RunStat.WeaponDamage);
        public float ExplosionDamageFactor => 1f + Get(RunStat.ExplosionDamage);
        public float ExplosionRadiusFactor => 1f + Get(RunStat.ExplosionRadius);
        public float CritChance => Mathf.Clamp01(Get(RunStat.CritChance));
        public float MoveSpeedFactor => 1f + Get(RunStat.MoveSpeed);

        /// <summary>× the time between attacks.</summary>
        public float CooldownFactor => 1f / Mathf.Max(0.1f, 1f + Get(RunStat.FireRate));

        public float ReloadFactor => Mathf.Max(MinReloadFactor, 1f - Get(RunStat.ReloadTime));

        public int MaxHealthBonus => Mathf.RoundToInt(Get(RunStat.MaxHealth));
        public float PickupRadius => Mathf.Max(0f, Get(RunStat.PickupRadius));
        public int HealOnKill => Mathf.RoundToInt(Get(RunStat.HealOnKill));

        /// <summary>Rounds of the weapon's magazine with the cards (0 without ammo).</summary>
        public int MagazineOf(WeaponDefinition weapon)
        {
            if (weapon == null || !weapon.UsesAmmo) return 0;
            var size = weapon.MagazineSize;
            var bonus = Get(RunStat.Magazine);
            return bonus > 0f ? Mathf.Max(size, Mathf.RoundToInt(size * (1f + bonus))) : size;
        }

        /// <summary>Clears the run (tests).</summary>
        public void Reset()
        {
            Array.Clear(_values, 0, _values.Length);
            _statusDamage.Clear();
            Changed?.Invoke();
        }
    }
}
