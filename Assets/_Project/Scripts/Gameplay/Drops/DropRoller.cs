using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Drops;
using UnityEngine;

namespace Purge.Gameplay.Drops
{
    public enum DropKind
    {
        Nothing = 0,
        Ammo = 1,
        Medkit = 2,
        Throwable = 3,
        Weapon = 4,
    }

    /// <summary>One roll of a drop table: what lies on the ground, how many.</summary>
    public readonly struct DropRoll
    {
        public DropRoll(DropKind kind, ContentDefinition item, int amount)
        {
            Kind = kind;
            Item = item;
            Amount = amount;
        }

        public static DropRoll Nothing => default;

        public DropKind Kind { get; }
        public ContentDefinition Item { get; }

        /// <summary>Rounds of ammo; throwables in the stack; 1 otherwise.</summary>
        public int Amount { get; }
    }

    /// <summary>The player's state the smart drop looks at.</summary>
    public struct DropContext
    {
        /// <summary>Weapons in the two slots (null — empty or the base pistol), and which one is in hands.</summary>
        public WeaponDefinition First;
        public WeaponDefinition Second;
        public int ActiveSlot;

        /// <summary>Rounds in the reserve for each slot's weapon.</summary>
        public int FirstReserve;
        public int SecondReserve;

        public bool FullHealth;

        /// <summary>× the weights of weapons and throwables above Common (the difficulty's <c>DropRarity</c>); 0 = 1.</summary>
        public float RarityFactor;
    }

    /// <summary>
    /// Rolls a <see cref="DropTable"/> (ТЗ «Дроп»): the kind by weight (a kind with nothing to give weighs 0; a medkit at
    /// full health weighs less), then the item: ammo — mostly for the weapons in the slots (more for the one in hands and
    /// for one whose reserve is under two magazines), otherwise any type of the pool, the amount by the magazine;
    /// weapons and throwables of the pool by rarity (capped by the table); weapons in the slots are not dropped again.
    /// Deterministic for a given random; no allocations.
    /// </summary>
    public static class DropRoller
    {
        public static DropRoll Roll(DropTable table, DropPool pool, in DropContext context, DeterministicRandom random)
        {
            if (table == null || pool == null) return DropRoll.Nothing;

            var ammoWeight = HasAmmo(pool, context) ? table.AmmoWeight : 0f;
            var medkitWeight = MedkitTotal(table) > 0f ? table.MedkitWeight * (context.FullHealth ? table.MedkitAtFullHealth : 1f) : 0f;
            var throwableWeight = ThrowableTotal(table, pool, context) > 0f ? table.ThrowableWeight : 0f;
            var weaponWeight = WeaponTotal(table, pool, context) > 0f ? table.WeaponWeight : 0f;
            var total = table.NothingWeight + ammoWeight + medkitWeight + throwableWeight + weaponWeight;
            if (total <= 0f) return DropRoll.Nothing;

            var pick = (float)random.NextDouble() * total;
            if ((pick -= table.NothingWeight) < 0f) return DropRoll.Nothing;
            if ((pick -= ammoWeight) < 0f) return RollAmmo(table, pool, context, random);
            if ((pick -= medkitWeight) < 0f) return RollMedkit(table, random);
            if ((pick -= throwableWeight) < 0f) return RollThrowable(table, pool, context, random);
            return RollWeapon(table, pool, context, random);
        }

        private static bool HasAmmo(DropPool pool, in DropContext context) =>
            pool.AmmoTypes.Count > 0 || NeedsAmmo(context.First) || NeedsAmmo(context.Second);

        private static bool NeedsAmmo(WeaponDefinition weapon) =>
            weapon != null && weapon.UsesAmmo && !weapon.Ammo.InfiniteReserve && weapon.Ammo.Ammo != null;

        private static DropRoll RollAmmo(DropTable table, DropPool pool, in DropContext context, DeterministicRandom random)
        {
            var first = NeedsAmmo(context.First) ? HeldWeight(context.First, context.FirstReserve, context.ActiveSlot == 0) : 0f;
            var second = NeedsAmmo(context.Second) ? HeldWeight(context.Second, context.SecondReserve, context.ActiveSlot == 1) : 0f;
            var held = first + second > 0f && (pool.AmmoTypes.Count == 0 || random.NextDouble() < table.HeldShare);
            if (held)
            {
                var weapon = random.NextDouble() * (first + second) < first ? context.First : context.Second;
                return new DropRoll(DropKind.Ammo, weapon.Ammo.Ammo, Rounds(table, weapon.MagazineSize, random));
            }

            var ammo = pool.AmmoTypes[random.NextInt(pool.AmmoTypes.Count)];
            var magazine = MagazineFor(ammo, context.First);
            if (magazine == 0) magazine = MagazineFor(ammo, context.Second);
            if (magazine == 0)
                foreach (var weapon in pool.Weapons)
                    if ((magazine = MagazineFor(ammo, weapon)) > 0) break;
            return new DropRoll(DropKind.Ammo, ammo, magazine > 0 ? Rounds(table, magazine, random) : table.FallbackRounds);
        }

        /// <summary>The weapon in hands counts double; one with under two magazines in reserve — more.</summary>
        private static float HeldWeight(WeaponDefinition weapon, int reserve, bool active)
        {
            var weight = active ? 2f : 1f;
            if (reserve < weapon.MagazineSize * 2) weight += 1.5f;
            return weight;
        }

        private static int MagazineFor(AmmoDefinition ammo, WeaponDefinition weapon) =>
            NeedsAmmo(weapon) && (weapon.Ammo.Ammo == ammo || weapon.Ammo.Ammo.Id == ammo.Id) ? weapon.MagazineSize : 0;

        private static int Rounds(DropTable table, int magazine, DeterministicRandom random)
        {
            var range = table.Magazines;
            var share = Mathf.Lerp(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y), (float)random.NextDouble());
            return Math.Max(1, Mathf.RoundToInt(magazine * share));
        }

        private static float MedkitTotal(DropTable table)
        {
            var total = 0f;
            foreach (var medkit in table.Medkits)
                if (medkit?.Medkit != null) total += Mathf.Max(0f, medkit.Weight);
            return total;
        }

        private static DropRoll RollMedkit(DropTable table, DeterministicRandom random)
        {
            var pick = (float)random.NextDouble() * MedkitTotal(table);
            MedkitDefinition last = null;
            foreach (var medkit in table.Medkits)
            {
                if (medkit?.Medkit == null || medkit.Weight <= 0f) continue;
                last = medkit.Medkit;
                if ((pick -= medkit.Weight) < 0f) break;
            }

            return last != null ? new DropRoll(DropKind.Medkit, last, 1) : DropRoll.Nothing;
        }

        /// <summary>The table's weight of <paramref name="rarity"/>, the rarer ones × the difficulty's factor.</summary>
        private static float Weight(DropTable table, Rarity rarity, in DropContext context)
        {
            var weight = table.RarityWeight(rarity);
            return rarity > Rarity.Common && context.RarityFactor > 0f ? weight * context.RarityFactor : weight;
        }

        private static float ThrowableTotal(DropTable table, DropPool pool, in DropContext context)
        {
            var total = 0f;
            foreach (var throwable in pool.Throwables) total += Weight(table, throwable.Rarity, context);
            return total;
        }

        private static DropRoll RollThrowable(DropTable table, DropPool pool, in DropContext context, DeterministicRandom random)
        {
            var pick = (float)random.NextDouble() * ThrowableTotal(table, pool, context);
            ThrowableDefinition last = null;
            foreach (var throwable in pool.Throwables)
            {
                var weight = Weight(table, throwable.Rarity, context);
                if (weight <= 0f) continue;
                last = throwable;
                if ((pick -= weight) < 0f) break;
            }

            return last != null ? new DropRoll(DropKind.Throwable, last, 1) : DropRoll.Nothing;
        }

        private static bool InSlots(WeaponDefinition weapon, in DropContext context) =>
            Same(weapon, context.First) || Same(weapon, context.Second);

        private static bool Same(WeaponDefinition a, WeaponDefinition b) =>
            a != null && b != null && (a == b || !string.IsNullOrEmpty(a.Id) && a.Id == b.Id);

        private static float WeaponTotal(DropTable table, DropPool pool, in DropContext context)
        {
            var total = 0f;
            foreach (var weapon in pool.Weapons)
                if (!InSlots(weapon, context)) total += Weight(table, weapon.Rarity, context);
            return total;
        }

        private static DropRoll RollWeapon(DropTable table, DropPool pool, in DropContext context, DeterministicRandom random)
        {
            var pick = (float)random.NextDouble() * WeaponTotal(table, pool, context);
            WeaponDefinition last = null;
            foreach (var weapon in pool.Weapons)
            {
                if (InSlots(weapon, context)) continue;
                var weight = Weight(table, weapon.Rarity, context);
                if (weight <= 0f) continue;
                last = weapon;
                if ((pick -= weight) < 0f) break;
            }

            return last != null ? new DropRoll(DropKind.Weapon, last, 1) : DropRoll.Nothing;
        }
    }
}
