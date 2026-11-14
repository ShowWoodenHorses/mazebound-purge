using System;
using System.Reflection;
using NUnit.Framework;
using Purge.Application.Meta;
using Purge.Application.Save;
using Purge.Core.Definitions;
using Purge.Gameplay.Drops;
using Purge.Core.Content;
using UnityEngine;
namespace Purge.Tests.EditMode.Progress
{
    public class ArsenalTests
    {
        private sealed class Storage : ISaveStorage { public string Json; public int Writes; public string Read()=>Json; public void Write(string json){Json=json;Writes++;} }
        private Storage _storage; private SaveService _save; private ArsenalService _arsenal; private WeaponDefinition _weapon;
        private static void Field(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        [SetUp]public void Setup(){_storage=new Storage();_save=new SaveService(_storage);_save.Load();_arsenal=new ArsenalService(_save);_weapon=ScriptableObject.CreateInstance<WeaponDefinition>();_weapon.SetId("test_weapon");Field(_weapon,"_purchasePrice",100);}
        [TearDown]public void TearDown(){_save.Dispose();UnityEngine.Object.DestroyImmediate(_weapon);}
        [Test]public void PurchaseIsAtomicAndCannotRepeat()
        {
            _save.Data.Money=150;int writes=_storage.Writes;
            Assert.That(_arsenal.Buy(_weapon));Assert.That(_arsenal.Money,Is.EqualTo(50));Assert.That(_storage.Writes,Is.EqualTo(writes+1));
            Assert.That(_arsenal.Buy(_weapon),Is.False);Assert.That(_save.Data.Arsenal.Count,Is.EqualTo(1));
            var loaded=new SaveService(_storage);loaded.Load();Assert.That(new ArsenalService(loaded).IsOpen(_weapon));Assert.That(loaded.Data.Money,Is.EqualTo(50));loaded.Dispose();
        }
        [Test]public void InsufficientFundsAndUniqueRewardCannotBeBought()
        {
            _save.Data.Money=99;Assert.That(_arsenal.Buy(_weapon),Is.False);Assert.That(_save.Data.Money,Is.EqualTo(99));
            _save.Data.Money=1000;Field(_weapon,"_uniqueReward",true);Assert.That(_arsenal.Buy(_weapon),Is.False);Assert.That(_arsenal.WeightOf(_weapon),Is.Zero);
        }
        [Test]public void StartPoolIsFreeAndMarksSurviveReload()
        {
            _weapon.StartPool=true;Assert.That(_arsenal.IsOpen(_weapon));Assert.That(_arsenal.CanBuy(_weapon),Is.False);
            Assert.That(_arsenal.WeightOf(_weapon),Is.EqualTo(1));Assert.That(_arsenal.SetMark(_weapon,PoolMark.Favorite));Assert.That(_arsenal.WeightOf(_weapon),Is.EqualTo(3));
            var loaded=new SaveService(_storage);loaded.Load();var service=new ArsenalService(loaded);Assert.That(service.Mark(_weapon),Is.EqualTo(PoolMark.Favorite));
            service.SetMark(_weapon,PoolMark.Excluded);Assert.That(service.WeightOf(_weapon),Is.Zero);Assert.That(service.IsOpen(_weapon));loaded.Dispose();
        }
        [Test]public void DropRollerHonorsOpenPoolFavoriteAndExclusion()
        {
            _weapon.StartPool=true;_arsenal.SetMark(_weapon,PoolMark.Favorite);
            var other=ScriptableObject.CreateInstance<WeaponDefinition>();other.SetId("other");other.StartPool=true;
            var locked=ScriptableObject.CreateInstance<WeaponDefinition>();locked.SetId("locked");
            var table=ScriptableObject.CreateInstance<Purge.Core.Drops.DropTable>();table.Configure("weapons",0,0,0,0,1);
            try
            {
                var pool=new DropPool(null,null,_arsenal);pool.Configure(table,new[]{_weapon,other,locked},null);
                Assert.That(pool.Weapons.Count,Is.EqualTo(2));
                var random=new Purge.Core.Common.DeterministicRandom(42);int favorites=0;
                for(int i=0;i<1000;i++)if(DropRoller.Roll(table,pool,new DropContext(),random).Item==_weapon)favorites++;
                Assert.That(favorites,Is.InRange(680,820));
                _arsenal.SetMark(_weapon,PoolMark.Excluded);pool.Configure(table,new[]{_weapon,other,locked},null);
                for(int i=0;i<20;i++)Assert.That(DropRoller.Weapon(table,pool,new DropContext(),random).Item,Is.EqualTo(other));
                _arsenal.SetMark(other,PoolMark.Excluded);pool.Configure(table,new[]{_weapon,other,locked},null);
                Assert.That(DropRoller.Weapon(table,pool,new DropContext(),random).Kind,Is.EqualTo(DropKind.Nothing));
            }
            finally{UnityEngine.Object.DestroyImmediate(other);UnityEngine.Object.DestroyImmediate(locked);UnityEngine.Object.DestroyImmediate(table);}
        }
        [Test]public void LockedItemCannotBeUnlockedByMark()
        { Assert.That(_arsenal.SetMark(_weapon,PoolMark.Favorite),Is.False);Assert.That(_save.Data.Arsenal,Is.Empty); }
        [Test]public void DropPolicyUsesStableIdAndUpgradeWeight()
        {
            _save.Data.Arsenal.Add(new OwnedItem{Id=_weapon.Id,Level=4,Mark=PoolMark.Favorite});
            var copy=ScriptableObject.CreateInstance<WeaponDefinition>();copy.SetId(_weapon.Id);
            Assert.That(_arsenal.WeightOf(copy),Is.EqualTo(3.9f).Within(0.001));UnityEngine.Object.DestroyImmediate(copy);
        }
    }
}
