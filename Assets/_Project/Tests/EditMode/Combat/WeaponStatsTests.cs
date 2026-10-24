using System.Collections.Generic;
using NUnit.Framework;
using Purge.Core.Definitions;
using UnityEngine;

namespace Purge.Tests.EditMode.Combat
{
    /// <summary>Stats compared at a weapon on the ground (stage 6e2).</summary>
    public class WeaponStatsTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created)
                Object.DestroyImmediate(created);
            _created.Clear();
        }

        private WeaponDefinition Weapon(string id, params WeaponModule[] modules)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.Configure(id, modules);
            _created.Add(weapon);
            return weapon;
        }

        [Test]
        public void DamagePerSecond_CountsPelletsAndReload()
        {
            // 10 shots of 2 pellets × 5 every 0.1 s, then a 1 s reload: 100 damage in 2 s.
            var shotgun = Weapon("shotgun",
                new BulletAttack { Damage = 5f, Pellets = 2, Interval = 0.1f, Range = 6f },
                new AmmoModule { Magazine = 10, ReloadTime = 1f, PerAttack = 1 });
            Assert.AreEqual(50f, WeaponStats.DamagePerSecond(shotgun), 1e-3f);
            Assert.AreEqual(10, WeaponStats.Magazine(shotgun));
            Assert.AreEqual(6f, WeaponStats.Range(shotgun), 1e-3f);

            var knife = Weapon("knife", new MeleeAttack { Damage = 20f, Interval = 0.5f, Range = 1.2f });
            Assert.AreEqual(40f, WeaponStats.DamagePerSecond(knife), 1e-3f);
            Assert.AreEqual(0, WeaponStats.Magazine(knife), "melee: no magazine");
            Assert.AreEqual(1f, WeaponStats.Mobility(knife), 1e-3f, "no mobility module: full speed");
        }

        [Test]
        public void Compare_MoreIsBetter_SmallDifferenceIsTheSame()
        {
            Assert.AreEqual(StatDelta.Better, WeaponStats.Compare(12f, 10f));
            Assert.AreEqual(StatDelta.Worse, WeaponStats.Compare(8f, 10f));
            Assert.AreEqual(StatDelta.Same, WeaponStats.Compare(10.1f, 10f));
            Assert.AreEqual(StatDelta.Same, WeaponStats.Compare(0f, 0f));
        }
    }
}
