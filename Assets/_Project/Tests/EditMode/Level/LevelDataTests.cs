using System.Linq;
using Maze.Core.Grid;
using Maze.Core.Level;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Level
{
    public class LevelDataTests
    {
        private LevelData _level;

        [SetUp]
        public void SetUp() => _level = ScriptableObject.CreateInstance<LevelData>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_level);

        [Test]
        public void NewLevel_HasDefaultOddGeometryAndNoObjects()
        {
            Assert.IsTrue(_level.Geometry.IsConsistent);
            Assert.IsTrue(_level.Generation.HasOddSize);
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
        public void CreateUniqueId_ConsidersPatrols()
        {
            _level.MutablePatrols.Add(new PatrolData("patrol_3"));
            Assert.AreEqual("patrol_4", _level.CreateUniqueId(PatrolData.IdPrefix));
        }

        [Test]
        public void AllIds_IncludesEveryCollection()
        {
            var p = new GridPosition(1, 1);
            _level.MutableDoors.Add(new DoorData("door_1", p, "key_1"));
            _level.MutableKeys.Add(new KeyData("key_1", p));
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", p));
            _level.MutableExits.Add(new ExitData("exit_1", p));
            _level.MutableZombieSpawns.Add(new ZombieSpawnData("zombie_1", p, null, patrolId: "patrol_1"));
            _level.MutablePatrols.Add(new PatrolData("patrol_1", new[] { p, new GridPosition(3, 1) }));
            _level.MutableWeapons.Add(new WeaponPickupData("weapon_1", p, null));
            _level.MutableMedkits.Add(new MedkitData("medkit_1", p));
            _level.MutableMapFragments.Add(new MapFragmentData("fragment_1", p, new GridRect(0, 0, 5, 5)));

            CollectionAssert.AreEquivalent(
                new[] { "door_1", "key_1", "start_1", "exit_1", "zombie_1", "patrol_1", "weapon_1", "medkit_1", "fragment_1" },
                _level.AllIds().ToArray());

            Assert.IsTrue(_level.Doors[0].RequiresKey);
            Assert.IsTrue(_level.ZombieSpawns[0].HasPatrol);
        }

        [Test]
        public void ClearObjects_KeepsGeometry()
        {
            _level.Geometry.SetCell(new GridPosition(1, 1), CellType.Floor);
            _level.MutableExits.Add(new ExitData("exit_1", new GridPosition(1, 1)));

            _level.ClearObjects();

            Assert.IsEmpty(_level.AllIds());
            Assert.AreEqual(CellType.Floor, _level.Geometry.GetCell(new GridPosition(1, 1)));
        }

        [Test]
        public void HasOddSize_DetectsEvenDimensions()
        {
            _level.Generation.Width = 20;
            Assert.IsFalse(_level.Generation.HasOddSize);
        }
    }
}
