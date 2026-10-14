using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Modules;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Content
{
    /// <summary>Stage 3: stable content ids, the content catalog and the module framework.</summary>
    public class ContentTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _created)
                Object.DestroyImmediate(item);
            _created.Clear();
        }

        private T Create<T>(string id) where T : ContentDefinition
        {
            var item = ScriptableObject.CreateInstance<T>();
            item.name = id;
            item.SetId(id);
            _created.Add(item);
            return item;
        }

        // ------------------------------------------------------------ Ids

        [TestCase("ForestZombieWalker", "forest_zombie_walker")]
        [TestCase("AK-74", "ak_74")]
        [TestCase("MP5", "mp5")]
        [TestCase("HuntingRifle", "hunting_rifle")]
        [TestCase("  Fire  Axe!! ", "fire_axe")]
        [TestCase("Ёлка", "content")]
        public void IdFromName(string name, string id)
        {
            Assert.AreEqual(id, ContentIds.FromName(name));
            Assert.IsTrue(ContentIds.IsValid(ContentIds.FromName(name)));
        }

        [TestCase("ak_74", true)]
        [TestCase("a", true)]
        [TestCase("", false)]
        [TestCase("Ak_74", false)]
        [TestCase("_ak", false)]
        [TestCase("ak_", false)]
        [TestCase("ak__74", false)]
        [TestCase("ak-74", false)]
        public void IdRules(string id, bool valid) => Assert.AreEqual(valid, ContentIds.IsValid(id));

        [Test]
        public void TakenId_GetsASuffix()
        {
            var taken = new HashSet<string> { "bat", "bat_2" };
            Assert.AreEqual("bat_3", ContentIds.MakeUnique("bat", taken.Contains));
            Assert.AreEqual("axe", ContentIds.MakeUnique("axe", taken.Contains));
        }

        // ------------------------------------------------------------ Catalog

        [Test]
        public void Catalog_FindsByIdAndType_ReportsDuplicates()
        {
            var catalog = ScriptableObject.CreateInstance<ContentCatalog>();
            _created.Add(catalog);
            var weapon = Create<WeaponDefinition>("bat");
            var zombie = Create<ZombieDefinition>("zombie_walker");
            catalog.MutableItems.Add(weapon);
            catalog.MutableItems.Add(zombie);

            Assert.AreSame(weapon, catalog.Find("bat"));
            Assert.AreSame(weapon, catalog.Find<WeaponDefinition>("bat"));
            Assert.IsNull(catalog.Find<ZombieDefinition>("bat"), "Wrong type.");
            Assert.IsNull(catalog.Find("removed_long_ago"), "An id a save keeps after the content is gone.");
            var weapons = new List<WeaponDefinition>();
            catalog.GetAll(weapons);
            CollectionAssert.AreEqual(new[] { weapon }, weapons);

            var problems = new List<string>();
            catalog.Validate(problems);
            Assert.IsEmpty(problems);

            catalog.MutableItems.Add(Create<ObstacleDefinition>("bat"));
            catalog.MutableItems.Add(Create<ObstacleDefinition>("Bad Id"));
            catalog.Validate(problems);
            Assert.AreEqual(2, problems.Count);
        }

        // ------------------------------------------------------------ Modules

        [Test]
        public void ModuleTypes_ListConcreteClassesOfTheFamily()
        {
            var types = ModuleTypes.Concrete(typeof(TestModule));
            CollectionAssert.AreEquivalent(new[] { typeof(TestShape), typeof(TestEffect) }, types);
            Assert.AreEqual("Shape", ModuleTypes.DisplayName(typeof(TestShape)));
            Assert.AreEqual("Effects", ModuleTypes.CategoryOf(typeof(TestEffect)));
            Assert.IsTrue(ModuleTypes.AllowMultiple(typeof(TestEffect)));
            Assert.IsFalse(ModuleTypes.AllowMultiple(typeof(TestShape)));
        }

        [Test]
        public void ModuleQueries_FindByType()
        {
            var modules = new List<TestModule> { new TestEffect { Power = 1 }, new TestShape(), new TestEffect { Power = 2 } };

            Assert.IsTrue(Modules.TryGet(modules, out TestShape shape));
            Assert.IsNotNull(shape);
            Assert.IsFalse(Modules.Has<TestOther>(modules));
            var effects = new List<TestEffect>();
            Modules.GetAll(modules, effects);
            Assert.AreEqual(new[] { 1, 2 }, effects.ConvertAll(e => e.Power).ToArray());
        }

        [Test]
        public void ModuleValidation_LostRepeatedAndOwnProblems()
        {
            var modules = new List<TestModule> { new TestShape { Range = -1f }, null, new TestShape(), new TestEffect(), new TestEffect() };
            var problems = new List<string>();

            Modules.Validate(modules, "Pistol", problems);

            Assert.AreEqual(3, problems.Count, string.Join("\n", problems));
            Assert.IsTrue(problems.Exists(p => p.Contains("#2 is missing")));
            Assert.IsTrue(problems.Exists(p => p.Contains("added twice")));
            Assert.IsTrue(problems.Exists(p => p.Contains("Range")));
        }

        [Test]
        public void ModuleList_SurvivesSerialization()
        {
            var asset = ScriptableObject.CreateInstance<TestContent>();
            _created.Add(asset);
            asset.Modules.Add(new TestShape { Range = 4f });
            asset.Modules.Add(new TestEffect { Power = 7 });

            var copy = Object.Instantiate(asset); // a copy through Unity serialization
            _created.Add(copy);

            Assert.AreEqual(2, copy.Modules.Count);
            Assert.IsInstanceOf<TestShape>(copy.Modules[0]);
            Assert.AreEqual(4f, ((TestShape)copy.Modules[0]).Range);
            Assert.AreEqual(7, ((TestEffect)copy.Modules[1]).Power);
            Assert.AreNotSame(asset.Modules[0], copy.Modules[0]);
        }

        [Serializable]
        public abstract class TestModule : ContentModule
        {
        }

        [Serializable, ModuleInfo("Shape")]
        public sealed class TestShape : TestModule
        {
            public float Range = 1f;

            public override void Validate(List<string> problems)
            {
                if (Range <= 0f) problems.Add("Range must be positive.");
            }
        }

        [Serializable, ModuleInfo("Effect", Category = "Effects", AllowMultiple = true)]
        public sealed class TestEffect : TestModule
        {
            public int Power;
        }

        [Serializable]
        public sealed class TestOther : ContentModule
        {
        }
    }

    public sealed class TestContent : ContentDefinition
    {
        [SerializeReference, ModuleList] public List<ContentTests.TestModule> Modules = new List<ContentTests.TestModule>();
    }
}
