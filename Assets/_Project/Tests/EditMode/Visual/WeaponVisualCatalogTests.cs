using Purge.Core.Definitions;
using Purge.Core.Visual;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Tests.EditMode.Visual
{
    public class WeaponVisualCatalogTests
    {
        private WeaponVisualCatalog _catalog;
        private WeaponDefinition _bat;
        private WeaponDefinition _batCopy;
        private WeaponDefinition _rifle;

        [SetUp]
        public void SetUp()
        {
            _bat = Definition("bat", true);
            _batCopy = Definition("bat", true);
            _rifle = Definition("rifle", false);
            _catalog = ScriptableObject.CreateInstance<WeaponVisualCatalog>();
            _catalog.MutableWeapons.Add(new WeaponVisualDefinition(_bat, new AssetReferenceGameObject("bat_held")));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_bat);
            Object.DestroyImmediate(_batCopy);
            Object.DestroyImmediate(_rifle);
        }

        [Test]
        public void Find_ByReference() => Assert.AreSame(_bat, _catalog.Find(_bat).Definition);

        [Test]
        public void Find_AnotherCopyOfTheSameDefinition_ById()
        {
            // In a player build a non-Addressable definition is copied into every bundle that references it.
            Assert.AreSame(_bat, _catalog.Find(_batCopy)?.Definition);
        }

        [Test]
        public void Find_Unknown_IsNull()
        {
            Assert.IsNull(_catalog.Find(_rifle));
            Assert.IsNull(_catalog.Find(null));
        }

        private static WeaponDefinition Definition(string id, bool melee)
        {
            var definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            definition.Configure(id, melee ? (WeaponModule)new MeleeAttack() : new BulletAttack());
            return definition;
        }
    }
}
