using System;
using System.Collections.Generic;
using Purge.Core.Definitions;

namespace Purge.Gameplay.Weapons
{
    /// <summary>
    /// The player's reserve of every ammo type (ТЗ: запас не ограничен), by the type's stable id — definitions from
    /// different Addressable roots may be different objects in a build.
    /// </summary>
    public sealed class AmmoStock
    {
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        /// <summary>(ammo id) after the reserve of a type changed.</summary>
        public event Action<string> Changed;

        public int Get(AmmoDefinition ammo) => ammo != null ? Get(ammo.Id) : 0;

        public int Get(string ammoId) =>
            !string.IsNullOrEmpty(ammoId) && _counts.TryGetValue(ammoId, out var count) ? count : 0;

        public void Add(AmmoDefinition ammo, int amount)
        {
            if (ammo == null || amount <= 0) return;
            _counts[ammo.Id] = Get(ammo.Id) + amount;
            Changed?.Invoke(ammo.Id);
        }

        /// <summary>Takes up to <paramref name="amount"/>; returns how many were taken.</summary>
        public int Take(AmmoDefinition ammo, int amount)
        {
            var have = Get(ammo);
            var taken = Math.Min(have, Math.Max(0, amount));
            if (taken <= 0) return 0;
            _counts[ammo.Id] = have - taken;
            Changed?.Invoke(ammo.Id);
            return taken;
        }
    }
}
