using System.Linq;
using Maze.Core.Authoring;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Validation;
using Maze.Core.Visual;
using Maze.Tests.EditMode.Visual;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Authoring
{
    public class LevelEditingTests
    {
        private VisualFixture _fixture;
        private LevelData Level => _fixture.Level;

        // Generated 21x21 maze: (1,1) is a room (floor), (2,2) a pillar (always wall).
        private static readonly GridPosition Room = new GridPosition(1, 1);
        private static readonly GridPosition Pillar = new GridPosition(2, 2);

        [SetUp]
        public void SetUp()
        {
            _fixture = new VisualFixture();
            LevelAuthoring.GenerateNew(Level);
        }

        [TearDown]
        public void TearDown() => _fixture.Dispose();

        private VisualChoice[] AllCellVisuals() =>
            Enumerable.Range(0, Level.Geometry.CellCount)
                .SelectMany(i => CellLayers.All.Select(layer => VisualResolver.ResolveCell(Level, Level.Geometry.ToPosition(i), layer)))
                .ToArray();

        [Test]
        public void Snowdrift_OnlyOnFloor_RemovedWhenCellStopsBeingFloor_AndByErase()
        {
            Assert.IsFalse(LevelEditing.SetSurface(Level, Pillar, CellSurface.Snowdrift), "Not on a wall.");
            Assert.AreEqual(CellSurface.None, Level.Geometry.GetSurface(Pillar));

            Assert.IsTrue(LevelEditing.SetSurface(Level, Room, CellSurface.Snowdrift));
            Assert.IsFalse(LevelEditing.SetSurface(Level, Room, CellSurface.Snowdrift), "No-op returns false.");
            Assert.AreEqual(CellSurface.Snowdrift, Level.Geometry.GetSurface(Room));
            Assert.IsTrue(new LevelGrid(Level.Geometry).IsSnowdrift(Room));

            LevelEditing.SetCellType(Level, Room, CellType.Wall);
            Assert.AreEqual(CellSurface.None, Level.Geometry.GetSurface(Room), "A wall has no surface.");

            LevelEditing.SetCellType(Level, Room, CellType.Floor);
            LevelEditing.SetSurface(Level, Room, CellSurface.Snowdrift);
            Assert.IsTrue(LevelEditing.ClearCell(Level, Room));
            Assert.AreEqual(CellSurface.None, Level.Geometry.GetSurface(Room), "Erase removes the drift.");
        }

        [Test]
        public void Snowdrift_GetsDriftFloorVariant_AndNoAutoDecor_BackToNormalWhenRemoved()
        {
            _fixture.Floor.MutableVariants.Add(new VisualVariant("floor_drift", 1, VisualCategory.Snowdrift));
            LevelAuthoring.RegenerateVisuals(Level, clearOverrides: false);
            var before = VisualResolver.ResolveCell(Level, Room, CellLayer.Floor);
            Assert.AreNotEqual("floor_drift", before.VariantId, "Drift floors are only for drift cells.");
            Assert.IsTrue(Enumerable.Range(0, Level.Geometry.CellCount)
                .All(i => VisualResolver.ResolveCell(Level, Level.Geometry.ToPosition(i), CellLayer.Floor).VariantId != "floor_drift"));

            Level.Generation.DecorDensity = 1f;
            LevelEditing.SetSurface(Level, Room, CellSurface.Snowdrift);
            Assert.AreEqual("floor_drift", VisualResolver.ResolveCell(Level, Room, CellLayer.Floor).VariantId);
            Assert.IsTrue(VisualAssigner.ChooseDecor(Level, Room).IsEmpty, "No auto decor on a drift.");

            LevelEditing.SetSurface(Level, Room, CellSurface.None);
            Assert.AreEqual(before, VisualResolver.ResolveCell(Level, Room, CellLayer.Floor), "Back to the same floor.");
        }

        [Test]
        public void Snowdrift_ThemeWithoutDriftFloors_KeepsOrdinaryFloor()
        {
            var before = VisualResolver.ResolveCell(Level, Room, CellLayer.Floor);
            LevelEditing.SetSurface(Level, Room, CellSurface.Snowdrift);
            Assert.AreEqual(before, VisualResolver.ResolveCell(Level, Room, CellLayer.Floor));
        }

        [Test]
        public void Geometry_WithoutSurfaces_IsConsistent_AndHasNoDrifts()
        {
            var geometry = new LevelGeometry(5, 5, CellType.Floor);
            Assert.IsTrue(geometry.SurfacesConsistent);
            Assert.IsFalse(geometry.HasSurfaces);
            Assert.AreEqual(CellSurface.None, geometry.GetSurface(new GridPosition(2, 2)));
            Assert.AreEqual(CellSurface.None, new LevelGrid(geometry).GetSurface(new GridPosition(2, 2)));
        }

        [Test]
        public void Validator_WarnsAboutSurfaceOnWall()
        {
            Level.Geometry.SetSurface(Pillar, CellSurface.Snowdrift); // bypassing LevelEditing
            var report = LevelValidator.Validate(Level);
            Assert.IsTrue(report.Issues.Any(i => i.Code == ValidationCodes.SurfaceNotOnFloor && i.Position == Pillar));
            Assert.IsFalse(new LevelGrid(Level.Geometry).IsSnowdrift(Pillar), "Ignored in the game.");
        }

        [Test]
        public void FrozenDoor_StartsClosed_OpenThaws_AndValidatorRejectsBoth()
        {
            LevelEditing.SetCellType(Level, Pillar, CellType.Door);
            var door = Level.Doors.Single();
            LevelEditing.SetDoorInitiallyOpen(door, true);

            LevelEditing.SetDoorIce(door, 3);
            Assert.IsTrue(door.IsFrozen);
            Assert.AreEqual(3, door.IceHits);
            Assert.IsFalse(door.IsInitiallyOpen, "A frozen door starts closed.");

            LevelEditing.SetDoorInitiallyOpen(door, true);
            Assert.IsFalse(door.IsFrozen, "Opening it initially thaws it.");

            door.IceHits = 2; // both, bypassing LevelEditing
            Assert.IsTrue(LevelValidator.Validate(Level).Issues.Any(i => i.Code == ValidationCodes.FrozenDoorInitiallyOpen));
            Assert.IsTrue(LevelValidator.Validate(Level).Issues.Any(i => i.Code == ValidationCodes.FrozenDoorWithoutIceVisual),
                "The fixture theme has no ice overlay.");
        }

        [Test]
        public void PaintDoor_CreatesDoorData_PaintFloor_RemovesIt()
        {
            Assert.IsTrue(LevelEditing.SetCellType(Level, Pillar, CellType.Door));
            var door = Level.Doors.Single();
            Assert.AreEqual(Pillar, door.Position);
            Assert.IsFalse(VisualResolver.ResolveObject(Level, door).IsEmpty);

            Assert.IsTrue(LevelEditing.SetCellType(Level, Pillar, CellType.Floor));
            Assert.IsEmpty(Level.Doors);
            Assert.IsTrue(Level.VisualData.GetObjectAssignment(door.Id).IsEmpty);
            Assert.IsFalse(LevelEditing.SetCellType(Level, Pillar, CellType.Floor), "No-op returns false.");
        }

        [Test]
        public void Paint_UpdatesVisualsLocally_LikeFullRegenerate()
        {
            var floorUnderPillar = VisualResolver.ResolveCell(Level, Pillar, CellLayer.Floor);
            LevelEditing.SetCellType(Level, Pillar, CellType.Floor);
            var local = AllCellVisuals();

            LevelAuthoring.RegenerateVisuals(Level, clearOverrides: false);
            CollectionAssert.AreEqual(AllCellVisuals(), local);

            Assert.IsTrue(VisualResolver.ResolveCell(Level, Pillar, CellLayer.Wall).IsEmpty);
            Assert.AreEqual(floorUnderPillar, VisualResolver.ResolveCell(Level, Pillar, CellLayer.Floor),
                "The floor that was under the wall stays the same when the wall is removed.");
        }

        [Test]
        public void WallOverride_DroppedWhenCellStopsBeingWall_FloorOverrideAlwaysKept()
        {
            Level.VisualData.SetCellOverride(Pillar, CellLayer.Wall, new VisualChoice("wall_cross"));
            Level.VisualData.SetCellOverride(Pillar, CellLayer.Floor, new VisualChoice("floor_special_blood"));

            LevelEditing.SetCellType(Level, Pillar, CellType.Floor);
            Assert.IsFalse(Level.VisualData.TryGetCellOverride(Pillar, CellLayer.Wall, out _));
            Assert.IsTrue(Level.VisualData.TryGetCellOverride(Pillar, CellLayer.Floor, out _));

            LevelEditing.SetCellType(Level, Pillar, CellType.Door);
            LevelEditing.SetCellType(Level, Pillar, CellType.Wall);
            Assert.AreEqual("floor_special_blood", VisualResolver.ResolveCell(Level, Pillar, CellLayer.Floor).VariantId,
                "Floor under any cell type keeps the designer's choice.");
        }

        [Test]
        public void KeyForDoor_IsLinkedAndColoured_RemovingKeyUnlocksDoor()
        {
            LevelEditing.SetCellType(Level, Pillar, CellType.Door);
            var door = Level.Doors.Single();
            Assert.IsNull(VisualAssigner.ResolvedColor(Level, door));

            var key = LevelEditing.AddKey(Level, Room, door);
            Assert.AreEqual(key.Id, door.KeyId);
            var color = VisualAssigner.ResolvedColor(Level, door);
            Assert.IsNotNull(color);
            Assert.AreEqual(color, _fixture.Key.FindVariant(VisualResolver.ResolveObject(Level, key).VariantId).ColorTag);

            LevelEditing.Remove(Level, key);
            Assert.IsFalse(door.RequiresKey);
            Assert.IsNull(VisualAssigner.ResolvedColor(Level, door), "Unlocked door loses its colour.");
        }

        [Test]
        public void LinkingKeyToSecondDoor_UnlinksFirst_OneKeyOneDoor()
        {
            LevelEditing.SetCellType(Level, new GridPosition(2, 4), CellType.Door);
            LevelEditing.SetCellType(Level, new GridPosition(4, 2), CellType.Door);
            var first = Level.Doors[0];
            var second = Level.Doors[1];
            var key = LevelEditing.AddKey(Level, Room, first);

            LevelEditing.LinkKey(Level, second, key);

            Assert.IsFalse(first.RequiresKey);
            Assert.AreEqual(key.Id, second.KeyId);
        }

        [Test]
        public void DoorOverride_RecoloursItsKey()
        {
            LevelEditing.SetCellType(Level, Pillar, CellType.Door);
            var door = Level.Doors.Single();
            var key = LevelEditing.AddKey(Level, Room, door);
            var other = VisualAssigner.ResolvedColor(Level, door) == "red" ? "blue" : "red";

            LevelEditing.SetObjectOverride(Level, door, new VisualChoice("door_" + other));

            Assert.AreEqual("key_" + other, VisualResolver.ResolveObject(Level, key).VariantId);
        }

        [Test]
        public void RemovingDoor_OpensPassage()
        {
            LevelEditing.SetCellType(Level, Pillar, CellType.Door);
            LevelEditing.Remove(Level, Level.Doors.Single());
            Assert.AreEqual(CellType.Floor, Level.Geometry.GetCell(Pillar));
            Assert.IsEmpty(Level.Doors);
        }

        [Test]
        public void MovingDoor_MovesItsCell()
        {
            LevelEditing.SetCellType(Level, Pillar, CellType.Door);
            var door = Level.Doors.Single();
            var target = new GridPosition(2, 4);

            Assert.IsTrue(LevelEditing.Move(Level, door, target));

            Assert.AreEqual(CellType.Floor, Level.Geometry.GetCell(Pillar));
            Assert.AreEqual(CellType.Door, Level.Geometry.GetCell(target));
            Assert.AreEqual(target, door.Position);
        }

        [Test]
        public void Rename_UpdatesReferencesAndKeepsVisuals_RejectsTakenIds()
        {
            LevelEditing.SetCellType(Level, Pillar, CellType.Door);
            var door = Level.Doors.Single();
            var key = LevelEditing.AddKey(Level, Room, door);
            var keyVisual = VisualResolver.ResolveObject(Level, key);

            Assert.IsTrue(LevelEditing.Rename(Level, key, "key_gold"));
            Assert.AreEqual("key_gold", door.KeyId);
            Assert.AreEqual(keyVisual, VisualResolver.ResolveObject(Level, key));

            Assert.IsFalse(LevelEditing.Rename(Level, key, door.Id));
            Assert.IsFalse(LevelEditing.Rename(Level, key, "  "));
        }

        [Test]
        public void Patrol_CreatedOnFirstPoint_RemovedWithZombie()
        {
            var zombie = LevelEditing.AddZombie(Level, Room, _fixture.Walker, Direction.East);
            var patrol = LevelEditing.AddPatrolPoint(Level, zombie, new GridPosition(1, 3));
            LevelEditing.AddPatrolPoint(Level, zombie, new GridPosition(3, 1));

            Assert.AreEqual(patrol.Id, zombie.PatrolId);
            Assert.AreEqual(2, patrol.Points.Count);

            LevelEditing.RemoveLastPatrolPoint(Level, zombie);
            Assert.AreEqual(1, patrol.Points.Count);

            LevelEditing.Remove(Level, zombie);
            Assert.IsEmpty(Level.Patrols);
        }

        [Test]
        public void ClearCell_RemovesObjectsLightsDecor_WallAndDoorBecomeFloor()
        {
            LevelEditing.AddMedkit(Level, Room);
            LevelEditing.AddLight(Level, Room);
            LevelEditing.SetCellOverride(Level, Room, CellLayer.Decor, new VisualChoice("decor_01"));

            Assert.IsTrue(LevelEditing.ClearCell(Level, Room));
            Assert.IsFalse(Level.AllEntities().Any(e => e.Position == Room));
            Assert.IsFalse(Level.Lights.Any(l => l.Cell == Room));
            Assert.IsTrue(VisualResolver.ResolveDecor(Level, Room).IsEmpty);
            Assert.IsFalse(LevelEditing.ClearCell(Level, Room), "Nothing left to erase.");

            Assert.IsTrue(LevelEditing.ClearCell(Level, Pillar));
            Assert.AreEqual(CellType.Floor, Level.Geometry.GetCell(Pillar));

            LevelEditing.SetCellType(Level, Pillar, CellType.Door);
            Assert.IsTrue(LevelEditing.ClearCell(Level, Pillar));
            Assert.AreEqual(CellType.Floor, Level.Geometry.GetCell(Pillar));
            Assert.IsEmpty(Level.Doors);
        }

        private static void AssertPose(LevelData level, LevelEntityData entity, float x, float z, float height, float yaw)
        {
            Assert.IsTrue(level.VisualData.TryGetObjectPlacement(entity.Id, out var p), entity.Id);
            Assert.AreEqual(x, p.Offset.x, 1e-4f, "x");
            Assert.AreEqual(z, p.Offset.y, 1e-4f, "z");
            Assert.AreEqual(height, p.Height, 1e-4f, "height");
            Assert.AreEqual(yaw, p.Yaw, 1e-3f, "yaw");
        }

        private static void AssertDecor(LevelData level, GridPosition cell, float x, float z, float yaw)
        {
            Assert.IsTrue(level.VisualData.TryGetDecorPlacement(cell, out var p));
            Assert.AreEqual(x, p.Offset.x, 1e-4f, "decor x");
            Assert.AreEqual(z, p.Offset.y, 1e-4f, "decor z");
            Assert.AreEqual(yaw, p.Yaw, 1e-3f, "decor yaw");
        }

        [Test]
        public void PickupOnDecor_MovesAndTurnsWithIt_BothWays_StaysInCell()
        {
            var medkit = LevelEditing.AddMedkit(Level, Room);
            LevelEditing.SetCellOverride(Level, Room, CellLayer.Decor, new VisualChoice("decor_01"));
            Assert.IsTrue(LevelEditing.PutOnDecor(Level, medkit, new UnityEngine.Vector2(0.1f, 0f), 0.8f, 0f));
            Assert.IsTrue(LevelEditing.IsOnDecor(Level, medkit));

            // Decor shifted and turned 90° clockwise: the pickup (east of its centre) ends up south of it.
            LevelEditing.SetDecorPlacement(Level, Room, new UnityEngine.Vector2(0.2f, 0f), 0.1f, 90f);
            AssertPose(Level, medkit, 0.2f, -0.1f, 0.9f, 90f);

            // Pickup shifted: the decor follows; lifting the pickup does not lift the decor.
            LevelEditing.SetObjectPlacement(Level, medkit, new UnityEngine.Vector2(0.45f, -0.1f), 1f, 90f);
            AssertDecor(Level, Room, 0.45f, 0f, 90f);
            Assert.AreEqual(0.1f, Level.VisualData.DecorPlacements.Single(p => p.Cell == Room).Height, 1e-4f);

            // Past the cell edge both stop together instead of being pulled apart.
            LevelEditing.SetObjectPlacement(Level, medkit, new UnityEngine.Vector2(0.7f, -0.1f), 1f, 90f);
            AssertPose(Level, medkit, 0.5f, -0.1f, 1f, 90f);
            AssertDecor(Level, Room, 0.5f, 0f, 90f);

            // Turning the pickup turns the decor around the pickup.
            LevelEditing.SetObjectPlacement(Level, medkit, new UnityEngine.Vector2(0.2f, 0f), 1f, 180f);
            AssertDecor(Level, Room, 0.3f, 0f, 180f);

            // Reset of the decor brings the pickup along; detached, it stays.
            LevelEditing.ResetDecorPlacement(Level, Room);
            AssertPose(Level, medkit, 0.1f, 0f, 0.9f, 0f);
            LevelEditing.DetachFromDecor(Level, medkit);
            Assert.IsFalse(LevelEditing.IsOnDecor(Level, medkit));
            LevelEditing.SetDecorPlacement(Level, Room, new UnityEngine.Vector2(-0.3f, 0f), 0f, 0f);
            AssertPose(Level, medkit, 0.1f, 0f, 0.9f, 0f);
        }

        [Test]
        public void DecorQuarterTurn_TurnsPickupOnIt()
        {
            var medkit = LevelEditing.AddMedkit(Level, Room);
            LevelEditing.SetCellOverride(Level, Room, CellLayer.Decor, new VisualChoice("decor_01"));
            LevelEditing.PutOnDecor(Level, medkit, new UnityEngine.Vector2(0f, 0.2f), 0.5f, 0f);

            LevelEditing.SetDecorRotation(Level, Room, 1);
            Assert.AreEqual(1, VisualResolver.ResolveDecor(Level, Room).Rotation);
            AssertPose(Level, medkit, 0.2f, 0f, 0.5f, 90f);
        }

        [Test]
        public void EditedLevel_StaysValid()
        {
            var room2 = new GridPosition(3, 1);
            var wallBetween = new GridPosition(2, 1);
            LevelEditing.SetCellType(Level, wallBetween, CellType.Floor);
            LevelEditing.SetCellType(Level, wallBetween, CellType.Door);
            var door = Level.Doors.Single();

            // Place the key next to the start so it is reachable from it.
            var start = Level.PlayerStarts[0].Position;
            var keyCell = DirectionExtensions.All.Select(start.Neighbour)
                .First(p => Level.Geometry.GetCell(p) == CellType.Floor);
            LevelEditing.AddKey(Level, keyCell, door);
            LevelEditing.AddWeapon(Level, room2 == start ? Room : room2, _fixture.Pistol);

            var report = LevelValidator.Validate(Level);
            Assert.IsFalse(report.Has(ValidationCodes.StaleWallVisual), string.Join("\n", report.Issues));
            Assert.IsFalse(report.Has(ValidationCodes.KeyDoorColorMismatch));
            Assert.IsFalse(report.Has(ValidationCodes.MissingVisual));
            Assert.IsFalse(report.Has(ValidationCodes.DoorCellWithoutDoor));
        }
    }
}
