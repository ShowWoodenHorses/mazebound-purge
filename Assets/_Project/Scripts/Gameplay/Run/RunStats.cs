using System;
using System.Collections.Generic;
using Purge.Core.Cards;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using UnityEngine;

namespace Purge.Gameplay.Run
{
    [Serializable]
    public struct RunPerkState
    {
        public float CalmTime, EscapeTime, EscapeCooldown, RageTime, GrindTime;
        public int RageStacks;
        public bool ConverterPrimed;
    }

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
        private readonly Level.LevelLaunchOptions _options;

        public RunStats(Level.LevelLaunchOptions options = null) => _options = options;

        public bool TacticianActive { get; set; }
        private bool Has(CharacterSkills skill) => _options?.HasSkill(skill) == true;

        /// <summary>The shortest reload, × the weapon's (−12 % × 5 levels is −60 %).</summary>
        public const float MinReloadFactor = 0.25f;

        private static readonly int StatCount = Enum.GetValues(typeof(RunStat)).Length;

        private readonly float[] _values = new float[StatCount];
        private readonly HashSet<RunPerk> _perks = new HashSet<RunPerk>();
        public bool Has(RunPerk perk) => _perks.Contains(perk);
        internal void Unlock(RunPerk perk) => _perks.Add(perk);
        internal float CalmTime { get; set; }
        internal float EscapeTime { get; set; }
        internal float EscapeCooldown { get; set; }
        internal float RageTime { get; set; }
        internal int RageStacks { get; set; }
        internal float GrindTime { get; set; }
        public bool ConverterPrimed { get; set; }

        internal RunPerkState CapturePerks() => new RunPerkState
        {
            CalmTime = CalmTime, EscapeTime = EscapeTime, EscapeCooldown = EscapeCooldown,
            RageTime = RageTime, RageStacks = RageStacks, GrindTime = GrindTime, ConverterPrimed = ConverterPrimed
        };

        internal void RestorePerks(RunPerkState state)
        {
            CalmTime = state.CalmTime;
            EscapeTime = state.EscapeTime;
            EscapeCooldown = state.EscapeCooldown;
            RageTime = state.RageTime;
            RageStacks = state.RageStacks;
            GrindTime = state.GrindTime;
            ConverterPrimed = state.ConverterPrimed;
        }
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
        public float ExplosionRadiusFactor => (1f + Get(RunStat.ExplosionRadius)) * (Has(CharacterSkills.Sapper) ? 1.15f : 1f);
        public float CritChance => Mathf.Clamp01(Get(RunStat.CritChance) + (Has(CharacterSkills.SharpEye) ? 0.05f : 0f)
            + (Has(RunPerk.CoolHead) && CalmTime >= 3f ? 0.1f : 0f));
        public float MoveSpeedFactor => 1f + Get(RunStat.MoveSpeed) + (EscapeTime > 0f ? 0.25f : 0f);

        /// <summary>× the time between attacks.</summary>
        public float CooldownFactor => 1f / Mathf.Max(0.1f, 1f + Get(RunStat.FireRate) + RageStacks * 0.04f);

        internal float ConsumeMeleeSpeed()
        {
            if (GrindTime <= 0f) return 1f;
            GrindTime = 0f;
            return 1.4f;
        }

        public float ReloadFactor => Mathf.Max(MinReloadFactor, (1f - Get(RunStat.ReloadTime)) * (Has(CharacterSkills.QuickHands) ? 0.85f : 1f));

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
            _perks.Clear();
            RestorePerks(default);
            Changed?.Invoke();
        }
    }
}
