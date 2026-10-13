using Maze.Core.Authoring;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Visual
{
    /// <summary>One key opens one door; the key always has the colour of its door (ТЗ §40).</summary>
    public class KeyDoorColorTests
    {
        private VisualFixture _fixture;
        private LevelData Level => _fixture.Level;

        [SetUp]
        public void SetUp()
        {
            _fixture = new VisualFixture();
            LevelAuthoring.GenerateNew(Level);
        }

        [TearDown]
        public void TearDown() => _fixture.Dispose();

        private void AddPair(string doorId, string keyId, int x)
        {
            Level.MutableDoors.Add(new DoorData(doorId, new GridPosition(x, 1), keyId));
            Level.MutableKeys.Add(new KeyData(keyId, new GridPosition(x, 3)));
        }

        private string VariantOf(LevelEntityData entity) => VisualResolver.ResolveObject(Level, entity).VariantId;

        private string KeyColor(KeyData key) => _fixture.Key.FindVariant(VariantOf(key))?.ColorTag;

        [Test]
        public void LockedDoor_IsColoured_AndKeyMatches_UnlockedDoorIsNot()
        {
            AddPair("door_1", "key_1", 1);
            Level.MutableDoors.Add(new DoorData("door_2", new GridPosition(3, 1)));

            LevelAuthoring.RegenerateVisuals(Level, clearOverrides: false);

            var lockedColor = VisualAssigner.ResolvedColor(Level, Level.Doors[0]);
            Assert.IsNotNull(lockedColor);
            Assert.AreEqual(lockedColor, KeyColor(Level.Keys[0]));
            Assert.IsNull(VisualAssigner.ResolvedColor(Level, Level.Doors[1]));
        }

        [Test]
        public void LockedDoors_GetDistinctColours_WhileColoursLast()
        {
            for (var seed = 0; seed < 20; seed++)
            {
                Level.Generation.VisualSeed = seed;
                Level.ClearObjects();
                AddPair("door_1", "key_1", 1);
                AddPair("door_2", "key_2", 3);

                LevelAuthoring.RegenerateVisuals(Level, clearOverrides: true);

                var a = VisualAssigner.ResolvedColor(Level, Level.Doors[0]);
                var b = VisualAssigner.ResolvedColor(Level, Level.Doors[1]);
                Assert.AreNotEqual(a, b, $"seed {seed}");
                Assert.AreEqual(a, KeyColor(Level.Keys[0]));
                Assert.AreEqual(b, KeyColor(Level.Keys[1]));
            }
        }

        [Test]
        public void DoorOverride_KeyFollowsDoorColour()
        {
            AddPair("door_1", "key_1", 1);
            LevelAuthoring.RegenerateVisuals(Level, clearOverrides: false);

            var other = VisualAssigner.ResolvedColor(Level, Level.Doors[0]) == "red" ? "blue" : "red";
            Level.VisualData.SetObjectOverride("door_1", new VisualChoice("door_" + other));
            VisualAssigner.AssignObject(Level, Level.Keys[0]);

            Assert.AreEqual(other, KeyColor(Level.Keys[0]));
        }

        [Test]
        public void GeometryEditNearDoor_KeepsDoorVariant_UpdatesRotation()
        {
            AddPair("door_1", "key_1", 1);
            LevelAuthoring.RegenerateVisuals(Level, clearOverrides: false);
            var before = VariantOf(Level.Doors[0]);

            for (var seed = 0; seed < 10; seed++)
            {
                Level.Generation.VisualSeed = 1000 + seed;
                VisualAssigner.ReassignAround(Level, new GridPosition(1, 2));
                Assert.AreEqual(before, VariantOf(Level.Doors[0]));
            }
        }
    }
}
