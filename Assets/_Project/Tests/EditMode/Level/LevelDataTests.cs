using System.Linq;
using Purge.Core.Grid;
using Purge.Core.Level;
using NUnit.Framework;
using UnityEngine;

namespace Purge.Tests.EditMode.Level
{
    public class LevelDataTests
    {
        private LevelData _level;

        [SetUp]
        public void SetUp() => _level = ScriptableObject.CreateInstance<LevelData>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_level);

        [Test]
        public void NewLevel_HasDefaultGeometryAndNoObjects()
        {
            Assert.IsTrue(_level.Geometry.IsConsistent);
            Assert.AreEqual(_level.Generation.Width, _level.Geometry.Width);
            Assert.IsEmpty(_level.AllIds());
        }

        [Test]
        public void CreateUniqueId_StartsAtOne_AndContinuesAfterMax()
        {
            Assert.AreEqual("door_1", _level.CreateUniqueId(DoorData.IdPrefix));

            _level.MutableDoors.Add(new DoorData("door_1", new GridPosition(1, 1)));
            _level.MutableDoors.Add(new DoorData("door_7", new GridPosition(2, 1)));
            _level.MutableDoors.Add(new DoorData("door_custom", new GridPosition(3, 1)));

            Assert.AreEqual("door_8", _level.CreateUniqueId(DoorData.IdPrefix));
            Assert.AreEqual("key_1", _level.CreateUniqueId(KeyData.IdPrefix));
        }

        [Test]
        public void AllIds_IncludesEveryCollection()
        {
            var p = new GridPosition(1, 1);
            _level.MutableDoors.Add(new DoorData("door_1", p, "key_1"));
            _level.MutableKeys.Add(new KeyData("key_1", p));
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", p));
            _level.MutableZombieSpawns.Add(new ZombieSpawnData("zombie_1", p, null));
            _level.MutableWeapons.Add(new WeaponPickupData("weapon_1", p, null));
            _level.MutableMedkits.Add(new MedkitData("medkit_1", p));
            _level.MutableLights.Add(new LightSourceData("light_1", p, Vector2.zero, Color.white, 3f, 1f, 0f, false));

            CollectionAssert.AreEquivalent(
                new[] { "door_1", "key_1", "start_1", "zombie_1", "weapon_1", "medkit_1", "light_1" },
                _level.AllIds().ToArray());

            Assert.IsTrue(_level.Doors[0].RequiresKey);
        }

        [Test]
        public void ClearObjects_KeepsGeometry()
        {
            _level.Geometry.SetCell(new GridPosition(1, 1), CellType.Floor);
            _level.MutableMedkits.Add(new MedkitData("medkit_1", new GridPosition(1, 1)));

            _level.ClearObjects();

            Assert.IsEmpty(_level.AllIds());
            Assert.AreEqual(CellType.Floor, _level.Geometry.GetCell(new GridPosition(1, 1)));
        }
    }
}
