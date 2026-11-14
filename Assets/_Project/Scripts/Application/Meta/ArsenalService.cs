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
    public sealed class ArsenalService : IItemDropPolicy
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
