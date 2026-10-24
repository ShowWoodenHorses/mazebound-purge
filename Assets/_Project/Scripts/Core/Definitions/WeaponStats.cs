using UnityEngine;

namespace Purge.Core.Definitions
{
    /// <summary>Which way a stat of one weapon differs from another's (the comparison at a weapon on the ground).</summary>
    public enum StatDelta
    {
        Same = 0,
        Better = 1,
        Worse = 2,
    }

    /// <summary>
    /// Stats of a weapon definition shown when comparing (stage 6e2): damage per second, magazine, range, mobility.
    /// From the definition only — cards of the run and upgrades are not counted (the same for both weapons).
    /// </summary>
    public static class WeaponStats
    {
        /// <summary>Relative difference below which two values are the same.</summary>
        public const float Tolerance = 0.02f;

        /// <summary>
        /// Damage per second of steady fire against one target, reloads included: one magazine's damage over the time to
        /// shoot it and reload. Shotguns count all pellets, explosives direct and blast damage, chains the first target.
        /// </summary>
        public static float DamagePerSecond(WeaponDefinition weapon)
        {
            var attack = weapon != null ? weapon.Attack : null;
            if (attack == null) return 0f;
            var interval = Mathf.Max(0.01f, attack.Interval);
            float perAttack;
            switch (attack)
            {
                case BulletAttack bullet: perAttack = bullet.Damage * Mathf.Max(1, bullet.Pellets); break;
                case StreamAttack stream: perAttack = stream.DamagePerSecond * interval; break;
                case ChainAttack chain: perAttack = chain.Damage; break;
                case ExplosiveAttack explosive: perAttack = explosive.DirectDamage + explosive.ExplosionDamage; break;
                case MeleeAttack melee: perAttack = melee.Damage; break;
                default: return 0f;
            }

            if (!weapon.UsesAmmo || weapon.MagazineSize <= 0) return perAttack / interval;
            var attacks = Mathf.Max(1, weapon.MagazineSize / Mathf.Max(1, weapon.Ammo.PerAttack));
            return perAttack * attacks / (attacks * interval + Mathf.Max(0f, weapon.ReloadTime));
        }

        /// <summary>Rounds in the magazine; 0 — melee or no magazine.</summary>
        public static int Magazine(WeaponDefinition weapon) => weapon != null ? weapon.MagazineSize : 0;

        /// <summary>How far the attack reaches, cells.</summary>
        public static float Range(WeaponDefinition weapon)
        {
            switch (weapon != null ? weapon.Attack : null)
            {
                case BulletAttack bullet: return bullet.Range;
                case StreamAttack stream: return stream.Range;
                case ChainAttack chain: return chain.Range;
                case ExplosiveAttack explosive: return explosive.Range;
                case MeleeAttack melee: return melee.Range;
                default: return 0f;
            }
        }

        /// <summary>Running speed with it in hands, share of the full speed.</summary>
        public static float Mobility(WeaponDefinition weapon) => weapon != null ? weapon.SpeedInHands : 1f;

        /// <summary>How <paramref name="value"/> compares with <paramref name="other"/> (more is better).</summary>
        public static StatDelta Compare(float value, float other)
        {
            var scale = Mathf.Max(Mathf.Abs(value), Mathf.Abs(other));
            if (scale <= 0f || Mathf.Abs(value - other) <= scale * Tolerance) return StatDelta.Same;
            return value > other ? StatDelta.Better : StatDelta.Worse;
        }
    }
}
