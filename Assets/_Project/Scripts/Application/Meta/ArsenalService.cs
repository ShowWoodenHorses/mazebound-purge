using System;
using System.Collections.Generic;
using Purge.Application.Save;
using Purge.Application.Platform;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Gameplay.Drops;
namespace Purge.Application.Meta
{
    /// <summary>Persistent purchases and drop preferences. Start-pool items are open without a purchase.</summary>
    public sealed class ArsenalService : IItemDropPolicy, IItemUpgrades
    {
        private readonly SaveService _save;
        private readonly AnalyticsService _analytics;
        public ArsenalService(SaveService save, AnalyticsService analytics = null) { _save = save; _analytics = analytics; }
        public int Money => _save.Data.Money;
        public static bool IsItem(ContentDefinition item) => item is WeaponDefinition || item is ThrowableDefinition;
        public static bool IsBase(ContentDefinition item) => item is WeaponDefinition w && w.UsesAmmo && w.Ammo.InfiniteReserve;
        public static bool IsStart(ContentDefinition item) => item is WeaponDefinition w ? w.StartPool : item is ThrowableDefinition t && t.StartPool;
        public static int Price(ContentDefinition item) => item is WeaponDefinition w ? w.PurchasePrice : item is ThrowableDefinition t ? t.PurchasePrice : 0;
        public bool IsOpen(ContentDefinition item) => item != null && IsItem(item) && (IsStart(item) || _save.Data.FindItem(item.Id) != null);
        public int Level(ContentDefinition item) => item == null ? 1 : _save.Data.FindItem(item.Id)?.Level ?? 1;
        public PoolMark Mark(ContentDefinition item) => item == null ? PoolMark.Normal : _save.Data.FindItem(item.Id)?.Mark ?? PoolMark.Normal;
        public const string TutorialPurchaseId = "desert";

        public bool CanBuy(ContentDefinition item) => item != null && IsItem(item) && !IsBase(item) && !IsOpen(item)
            && !(item is WeaponDefinition w && w.UniqueReward) && Price(item) > 0 && Money >= Price(item);
        public bool Buy(ContentDefinition item)
        {
            if (!CanBuy(item)) return false;
            var price = Price(item);
            _save.Data.Money -= price;
            _save.Data.Arsenal.Add(new OwnedItem { Id = item.Id });
            _save.Save();
            Send("item_purchase", item, price);
            return true;
        }
        public bool SetMark(ContentDefinition item, PoolMark mark)
        {
            if (!IsOpen(item) || IsBase(item) || !Enum.IsDefined(typeof(PoolMark), mark)) return false;
            var owned = _save.Data.FindItem(item.Id);
            if (owned == null) { owned = new OwnedItem { Id = item.Id }; _save.Data.Arsenal.Add(owned); }
            if (owned.Mark == mark) return true;
            owned.Mark = mark; _save.Save(); Send("item_pool_mark", item, (int)mark); return true;
        }
        public int ForkChoice(WeaponDefinition item,int level) => item==null || Level(item)<level ? -1 : ((_save.Data.FindItem(item.Id)?.Forks??0)&(level==4?1:2))==0?0:1;
        public bool Has(WeaponDefinition item,WeaponUpgrade upgrade) => Count(item,upgrade)>0;
        public int Count(WeaponDefinition item, WeaponUpgrade upgrade)
        {
            if(item==null || !item.Upgradable || upgrade==WeaponUpgrade.None || upgrade==WeaponUpgrade.Electrify)return 0;
            int count=0;
            for(int i=0;i<2;i++){int level=i==0?4:8,choice=ForkChoice(item,level);var fork=item.Fork(level);if(choice>=0 && fork!=null && (choice==0?fork.First:fork.Second)==upgrade)count++;}
            return count;
        }
        public float DamageFactor(ContentDefinition item) => item is WeaponDefinition w ? w.Upgradable ? w.UpgradeDamage(Level(w)) : 1f : item is ThrowableDefinition t ? t.UpgradeDamage(Level(t)) : 1f;
        public float RadiusFactor(ThrowableDefinition item) => item != null ? item.UpgradeRadius(Level(item)) : 1f;
        public int StackBonus(ThrowableDefinition item) => item != null ? item.UpgradeStack(Level(item)) : 0;
        public int UpgradePrice(ContentDefinition item)
        {
            if (!IsOpen(item) || IsBase(item)) return 0;
            int next = Level(item) + 1;
            if (item is WeaponDefinition w) return w.Upgradable && next <= 10 ? w.UpgradeCost(next) : 0;
            return item is ThrowableDefinition t && t.Upgradable && next <= 5 ? t.UpgradeCost(next) : 0;
        }
        // At a fork the caller must explicitly select one of the two options.
        public bool Upgrade(ContentDefinition item, int choice = -1)
        {
            int price = UpgradePrice(item);
            if (price <= 0 || Money < price) return false;
            int next = Level(item) + 1;
            bool fork = item is WeaponDefinition && (next == 4 || next == 8);
            if (fork ? choice < 0 || choice > 1 : choice != -1) return false;
            if (fork && !((WeaponDefinition)item).Fork(next).Available(choice)) return false;
            var owned = _save.Data.FindItem(item.Id);
            if (owned == null) { owned = new OwnedItem { Id = item.Id }; _save.Data.Arsenal.Add(owned); }
            if (fork)
            {
                int bit = next == 4 ? 1 : 2;
                owned.Forks = (owned.Forks & ~bit) | (choice == 1 ? bit : 0);
            }
            owned.Level = next; _save.Data.Money -= price; _save.Save();
            Send("item_upgrade", item, price); return true;
        }
        public bool ChangeFork(WeaponDefinition item, int forkLevel, int choice)
        {
            if (!IsOpen(item) || IsBase(item) || !item.Upgradable || (forkLevel != 4 && forkLevel != 8)
                || Level(item) < forkLevel || choice < 0 || choice > 1) return false;
            if (!item.Fork(forkLevel).Available(choice)) return false;
            var owned = _save.Data.FindItem(item.Id);
            if (owned == null) return false;
            int bit = forkLevel == 4 ? 1 : 2;
            if (((owned.Forks & bit) != 0 ? 1 : 0) == choice) return false;
            int currentPrice = item.UpgradeCost(owned.Level);
            if (currentPrice <= 0) return false;
            int price = Math.Max(1, (int)Math.Ceiling(currentPrice * 0.1));
            if (Money < price) return false;
            owned.Forks = (owned.Forks & ~bit) | (choice == 1 ? bit : 0);
            _save.Data.Money -= price; _save.Save(); Send("item_fork_change", item, price); return true;
        }
        public float WeightOf(ContentDefinition item)
        {
            if (!IsOpen(item) || IsBase(item)) return 0f;
            var mark = Mark(item);
            return (mark == PoolMark.Excluded ? 0f : mark == PoolMark.Favorite ? 3f : 1f) * (1f + 0.1f * Math.Max(0, Level(item) - 1));
        }
        private void Send(string name, ContentDefinition item, int value)
        {
            if (_analytics == null) return;
            var data = _analytics.Begin(); data["item"] = item.Id; data["value"] = value; data["money"] = Money; _analytics.Send(name);
        }
    }
}
