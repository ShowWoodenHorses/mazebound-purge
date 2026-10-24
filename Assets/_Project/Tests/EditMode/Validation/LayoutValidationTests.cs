using System.Collections.Generic;
using System.Linq;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Validation;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Validation
{
    /// <summary>Layout checks of a location and the Level Designer brushes behind them (stage 7б).</summary>
    public class LayoutValidationTests
    {
        private static readonly GridRect ArenaRect = new GridRect(24, 24, 16, 16);
        private const int Ring = 2;

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private ObstacleDefinition _tree;

        [SetUp]
        public void SetUp()
        {
            _tree = ScriptableObject.CreateInstance<ObstacleDefinition>();
            _tree.Configure("tree", true, true, false, false);
            _created.Add(_tree);
            _level = ScriptableObject.CreateInstance<LevelData>();
            _created.Add(_level);
            BuildField();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        /// <summary>
        /// 48×48 floor in a wall border, start at (5, 5); an arena 16×16 at (24, 24) in a wall ring 2 deep with entries
        /// 3 wide on the north, south and west; one spawn point far from the start.
        /// </summary>
        private void BuildField()
        {
            var geometry = new LevelGeometry(48, 48, CellType.Floor, 1);
            for (var i = 0; i < 48; i++)
            {
                geometry.SetCell(new GridPosition(i, 0), CellType.Wall);
                geometry.SetCell(new GridPosition(i, 47), CellType.Wall);
                geometry.SetCell(new GridPosition(0, i), CellType.Wall);
                geometry.SetCell(new GridPosition(47, i), CellType.Wall);
            }

            _level.ReplaceGeometry(geometry);
            _level.ClearObjects();
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(5, 5)));
            _level.MutableMonsterSpawns.Add(new MonsterSpawnData("spawn_1", new GridPosition(40, 8)));

            var arena = new ArenaData(ArenaRect, Ring);
            var outer = arena.Outer;
            for (var y = outer.Y; y < outer.YMax; y++)
            for (var x = outer.X; x < outer.XMax; x++)
                if (!ArenaRect.Contains(new GridPosition(x, y)))
                    geometry.SetCell(new GridPosition(x, y), CellType.Wall);
            Open(new GridRect(30, 40, 3, 2)); // north
            Open(new GridRect(30, 22, 3, 2)); // south
            Open(new GridRect(22, 30, 2, 3)); // west
            _level.SetArena(arena);
        }

        private void Open(GridRect rect)
        {
            for (var y = rect.Y; y < rect.YMax; y++)
            for (var x = rect.X; x < rect.XMax; x++)
                _level.Geometry.SetCell(new GridPosition(x, y), CellType.Floor);
        }

        private void Wall(GridRect rect)
        {
            for (var y = rect.Y; y < rect.YMax; y++)
            for (var x = rect.X; x < rect.XMax; x++)
                _level.Geometry.SetCell(new GridPosition(x, y), CellType.Wall);
        }

        private ValidationReport Validate() => LevelValidator.Validate(_level);

        private static readonly string[] LayoutCodes =
        {
            ValidationCodes.LevelDisconnected, ValidationCodes.ArenaTooSmall, ValidationCodes.ArenaFewEntries,
            ValidationCodes.SpawnInStartView, ValidationCodes.SpawnUnreachable, ValidationCodes.SpawnNoBodyPath,
            ValidationCodes.ObstacleBudget,
        };

        private static string LayoutIssues(ValidationReport report) =>
            string.Join("\n", report.Issues.Where(i => LayoutCodes.Contains(i.Code)));

        [Test]
        public void GoodLayout_HasNoLayoutIssues()
        {
            Assert.AreEqual(string.Empty, LayoutIssues(Validate()));
            var entries = ArenaLayout.FindEntries(_level.Geometry, _level.Arena);
            Assert.AreEqual(3, entries.Count);
            Assert.IsTrue(entries.All(e => e.Width == 3));
            Assert.AreEqual(16, ArenaLayout.LargestFreeSquare(_level.Geometry, _level.Arena));
        }

        [Test]
        public void WalledOffPocket_IsDisconnected()
        {
            Wall(new GridRect(1, 40, 6, 1));
            Wall(new GridRect(6, 41, 1, 6));
            var report = Validate();
            Assert.AreEqual(1, report.Count(ValidationCodes.LevelDisconnected));
            Assert.AreEqual(ValidationSeverity.Error,
                report.Issues.First(i => i.Code == ValidationCodes.LevelDisconnected).Severity);

            _level.Geometry.SetCell(new GridPosition(3, 40), CellType.Door); // a door is a passage
            _level.MutableDoors.Add(new DoorData("door_1", new GridPosition(3, 40)));
            Assert.IsFalse(Validate().Has(ValidationCodes.LevelDisconnected));
        }

        [Test]
        public void Arena_ClosedOrNarrowEntry_TooFewEntries()
        {
            Wall(new GridRect(30, 40, 3, 2)); // north closed
            Assert.IsTrue(Validate().Has(ValidationCodes.ArenaFewEntries));

            Open(new GridRect(30, 40, 2, 2)); // 2 wide is not enough
            Assert.IsTrue(Validate().Has(ValidationCodes.ArenaFewEntries));

            Open(new GridRect(32, 40, 1, 2));
            Assert.IsFalse(Validate().Has(ValidationCodes.ArenaFewEntries));
        }

        [Test]
        public void Arena_ObstaclesInside_TooSmall()
        {
            for (var y = ArenaRect.Y; y < ArenaRect.YMax; y++)
                _level.Geometry.SetObstacle(new GridPosition(ArenaRect.X + 8, y), _tree); // splits it into 8-wide halves
            var report = Validate();
            Assert.IsTrue(report.Has(ValidationCodes.ArenaTooSmall));
            Assert.AreEqual(8, ArenaLayout.LargestFreeSquare(_level.Geometry, _level.Arena));
        }

        [Test]
        public void Spawn_InStartView_Error()
        {
            _level.MutableMonsterSpawns.Add(new MonsterSpawnData("spawn_2", new GridPosition(15, 10)));
            var report = Validate();
            Assert.AreEqual(1, report.Count(ValidationCodes.SpawnInStartView));
            Assert.AreEqual("spawn_2", report.Issues.First(i => i.Code == ValidationCodes.SpawnInStartView).EntityId);
        }

        [Test]
        public void Spawn_BehindNarrowPassage_NoBodyPath_ButReachable()
        {
            // A room in the south-east corner whose only way out is one cell wide.
            Wall(new GridRect(34, 1, 1, 14));
            Wall(new GridRect(35, 14, 12, 1));
            Open(new GridRect(34, 6, 1, 1));
            var report = Validate();
            Assert.AreEqual(1, report.Count(ValidationCodes.SpawnNoBodyPath), LayoutIssues(report));
            Assert.IsFalse(report.Has(ValidationCodes.SpawnUnreachable));
            Assert.IsFalse(report.Has(ValidationCodes.LevelDisconnected));

            Wall(new GridRect(34, 6, 1, 1)); // closed: unreachable as well
            report = Validate();
            Assert.IsTrue(report.Has(ValidationCodes.SpawnUnreachable));
            Assert.IsTrue(report.Has(ValidationCodes.LevelDisconnected));
        }

        [Test]
        public void ObstacleBudget_FullChunk_Warning()
        {
            for (var y = 8; y < 16; y++)
            for (var x = 8; x < 12; x++)
                _level.Geometry.SetObstacle(new GridPosition(x, y), _tree); // 32 of 64 > 22
            var report = Validate();
            Assert.AreEqual(1, report.Count(ValidationCodes.ObstacleBudget));
            Assert.AreEqual(ValidationSeverity.Warning,
                report.Issues.First(i => i.Code == ValidationCodes.ObstacleBudget).Severity);
        }

        [Test]
        public void ArenaBrush_MarksCells_ReadsEntries_EditsOfTheRingRefreshThem()
        {
            _level.SetArena(null);
            var arena = LevelEditing.SetArena(_level, ArenaRect, Ring);
            Assert.AreEqual(3, arena.Entries.Count);
            Assert.IsTrue(_level.Marks.IsManual(_level.Geometry, new GridPosition(ArenaRect.X - Ring, ArenaRect.Y - Ring)));
            Assert.IsTrue(_level.Marks.IsManual(_level.Geometry, ArenaRect.Min));

            LevelEditing.SetCellType(_level, new GridPosition(31, 40), CellType.Wall); // north entry split
            Assert.AreEqual(4, _level.Arena.Entries.Count);
            Assert.AreEqual(2, _level.Arena.Entries.Count(e => e.Width == 1));

            Assert.IsTrue(LevelEditing.RemoveArena(_level));
            Assert.IsTrue(_level.Arena.IsEmpty);
        }

        [Test]
        public void BarrelsAndChests_AreObjects_EraseAndGenerateNewRemoveThem()
        {
            var barrel = LevelEditing.AddBarrel(_level, new GridPosition(10, 10), BarrelKind.Fire);
            var chest = LevelEditing.AddChest(_level, new GridPosition(11, 10), ChestKind.Locked);
            Assert.AreEqual("barrel_1", barrel.Id);
            Assert.AreEqual("chest_1", chest.Id);
            Assert.IsTrue(_level.AllEntities().Contains(barrel));
            Assert.IsTrue(_level.Marks.IsManual(_level.Geometry, barrel.Position));

            LevelEditing.SetChestKind(chest, ChestKind.Challenge);
            Assert.AreEqual(ChestKind.Challenge, chest.Kind);

            LevelEditing.AddBarrel(_level, new GridPosition(10, 10), BarrelKind.Gas); // same cell
            Assert.IsTrue(Validate().Has(ValidationCodes.Intersection));

            LevelEditing.ClearCell(_level, new GridPosition(10, 10));
            Assert.AreEqual(0, _level.Barrels.Count);

            LevelEditing.Remove(_level, chest);
            Assert.AreEqual(0, _level.Chests.Count);

            LevelEditing.AddChest(_level, new GridPosition(12, 10), ChestKind.Common);
            _level.ClearObjects();
            Assert.AreEqual(0, _level.Chests.Count);
        }

        [Test]
        public void LockedArea_FoundByCell_RemovedByBrush()
        {
            LevelEditing.AddLockedArea(_level, new GridRect(2, 2, 4, 4));
            var area = _level.Marks.FindLockedArea(new GridPosition(3, 3));
            Assert.IsNotNull(area);
            Assert.IsNull(_level.Marks.FindLockedArea(new GridPosition(6, 6)));
            Assert.IsTrue(LevelEditing.RemoveLockedArea(_level, area));
            Assert.IsFalse(_level.Marks.IsLocked(new GridPosition(3, 3)));
        }

        [Test]
        public void DoorStrength_ZeroIsTheDefault()
        {
            var door = new DoorData("door_1", new GridPosition(3, 3));
            LevelEditing.SetDoorStrength(door, 150);
            Assert.AreEqual(150, door.Strength);
            LevelEditing.SetDoorStrength(door, 0);
            Assert.AreEqual(DoorData.DefaultStrength, door.Strength);
        }
    }
}
