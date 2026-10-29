using System.Collections.Generic;
using Purge.Application.Levels;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Core.Boosters;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Level;

namespace Purge.Application.Meta
{
    /// <summary>
    /// Rules of the run's choice and preparation (ТЗ А.5, stage 11б): what can be taken to the start — the open weapons
    /// and throwables (bought in the arsenal or <c>StartPool</c>) within the rarity allowance of the character tree's trunk
    /// (<see cref="StartRarity"/>), and the boosters open now with their prices by location and difficulty. The basic
    /// pistol is always there (an empty slot 1). Reads the content catalog and the progress; changes nothing.
    /// </summary>
    public sealed class RunPreparation
    {
        private readonly IProgressService _progress;
        private readonly ILevelCatalog _catalog;
        private readonly SharedDefinitionsService _shared;

        public RunPreparation(IProgressService progress, ILevelCatalog catalog, SharedDefinitionsService shared)
        {
            _progress = progress;
            _catalog = catalog;
            _shared = shared;
        }

        /// <summary>The rarest the start may take: grey until the trunk of the character tree (stage 11д).</summary>
        public Rarity StartRarity => Rarity.Common;

        private ContentCatalog Content => _shared != null ? _shared.Content : null;

        /// <summary>The basic pistol (slot 1 when nothing is chosen); never in the lists.</summary>
        public WeaponDefinition BaseWeapon => _shared?.Player != null ? _shared.Player.BaseWeapon : null;

        /// <summary>Bought or in the start pool.</summary>
        public bool IsOpen(WeaponDefinition weapon) => weapon != null && (weapon.StartPool || _progress.Owns(weapon.Id));

        public bool IsOpen(ThrowableDefinition throwable) =>
            throwable != null && (throwable.StartPool || _progress.Owns(throwable.Id));

        public bool CanStartWith(WeaponDefinition weapon) =>
            IsOpen(weapon) && weapon.Rarity <= StartRarity && weapon != BaseWeapon;

        public bool CanStartWith(ThrowableDefinition throwable) => IsOpen(throwable) && throwable.Rarity <= StartRarity;

        /// <summary>Weapons for slot 1 (the basic pistol aside), by rarity, in the catalog's order within one.</summary>
        public void GetStartWeapons(List<WeaponDefinition> result)
        {
            result.Clear();
            Content?.GetAll(result);
            result.RemoveAll(weapon => !CanStartWith(weapon));
            SortByRarity(result, weapon => weapon.Rarity);
        }

        public void GetStartThrowables(List<ThrowableDefinition> result)
        {
            result.Clear();
            Content?.GetAll(result);
            result.RemoveAll(throwable => !CanStartWith(throwable));
            SortByRarity(result, throwable => throwable.Rarity);
        }

        /// <summary>Every booster, open or not (the screen shows the closed ones locked).</summary>
        public void GetBoosters(List<BoosterDefinition> result)
        {
            result.Clear();
            Content?.GetAll(result);
        }

        /// <summary>
        /// The booster can be bought: the basic ones once the first catalog level is completed (ТЗ: после уровня 1), the
        /// tree's once their node opened them.
        /// </summary>
        public bool IsOpen(BoosterDefinition booster)
        {
            if (booster == null) return false;
            if (booster.Unlock == BoosterUnlock.Tree) return _progress.IsBoosterOpened(booster.Id);
            var levels = _catalog.Levels;
            return levels.Count > 0 && _progress.IsCompleted(levels[0].LevelId);
        }

        /// <summary>The price for one run there; 0 = not offered there.</summary>
        public int PriceOf(BoosterDefinition booster, LevelLocation location, int difficulty) =>
            booster != null ? booster.PriceOn(location, difficulty) : 0;

        /// <summary>Weapon or throwable by its stable id (the saved preparation); null when unknown.</summary>
        public T Find<T>(string id) where T : ContentDefinition =>
            string.IsNullOrEmpty(id) || Content == null ? null : Content.Find<T>(id);

        private static void SortByRarity<T>(List<T> list, System.Func<T, Rarity> rarity)
        {
            // Stable: the catalog's order within a rarity.
            var sorted = new List<T>(list.Count);
            for (var r = Rarity.Common; r <= Rarity.Legendary; r++)
                foreach (var item in list)
                    if (rarity(item) == r) sorted.Add(item);
            foreach (var item in list)
                if (!sorted.Contains(item)) sorted.Add(item);
            list.Clear();
            list.AddRange(sorted);
        }
    }
}
