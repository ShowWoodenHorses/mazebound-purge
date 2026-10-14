using System.Collections.Generic;
using System.Linq;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Navigation;
using Purge.Core.Validation;
using Purge.Core.Visibility;
using NUnit.Framework;
using UnityEngine;

namespace Purge.Tests.EditMode.Grid
{
    /// <summary>Stage 3: floors in the grid and cell flags (structure + obstacle + surface).</summary>
    public class FloorsAndFlagsTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _created)
                Object.DestroyImmediate(item);
            _created.Clear();
        }

        private ObstacleDefinition Obstacle(string id, bool movement, bool bullets, bool sight, float speed = 1f, bool flammable = false)
        {
            var obstacle = ScriptableObject.CreateInstance<ObstacleDefinition>();
            obstacle.name = id;
            obstacle.Configure(id, movement, bullets, sight, flammable, speed);
            _created.Add(obstacle);
            return obstacle;
        }

        private LevelData Level(int width, int height)
        {
            var level = ScriptableObject.CreateInstance<LevelData>();
            level.ReplaceGeometry(new LevelGeometry(width, height, CellType.Floor));
            _created.Add(level);
            return level;
        }

        // ------------------------------------------------------------ Positions and indexing

        [Test]
        public void GridPosition_CarriesItsFloor_NeighboursStayOnIt()
        {
            var cell = new GridPosition(3, 4, 2);
            Assert.AreEqual(2, cell.Neighbour(Direction.East).Floor);
            Assert.AreNotEqual(new GridPosition(3, 4), cell);
            Assert.AreEqual(new GridPosition(3, 4), cell.OnFloor(0));
            Assert.AreEqual(1, cell.ManhattanDistance(new GridPosition(3, 5)), "Distance is on the floor.");
            Assert.AreEqual(2 * GridPosition.FloorHeight, cell.ToWorld().y, 1e-5f);
            Assert.AreEqual(cell, GridPosition.FromWorld(cell.ToWorld()));
        }

        [Test]
        public void Geometry_FloorZeroKeepsItsIndices_UpperFloorsFollow()
        {
            var geometry = new LevelGeometry(4, 3, CellType.Floor, floors: 2);
            Assert.AreEqual(12, geometry.FloorSize);
            Assert.AreEqual(24, geometry.CellCount);
            Assert.AreEqual(2 * 4 + 1, geometry.ToIndex(new GridPosition(1, 2)));
            Assert.AreEqual(12 + 2 * 4 + 1, geometry.ToIndex(new GridPosition(1, 2, 1)));
            Assert.AreEqual(new GridPosition(1, 2, 1), geometry.ToPosition(21));
            Assert.IsFalse(geometry.IsInside(new GridPosition(0, 0, 2)));
            Assert.IsFalse(geometry.IsInside(new GridPosition(0, 0, -1)));
        }

        [Test]
        public void AddAndRemoveTopFloor_KeepTheFloorsBelow()
        {
            var geometry = new LevelGeometry(3, 3, CellType.Floor);
            geometry.SetCell(new GridPosition(1, 1), CellType.Wall);
            geometry.SetSurface(new GridPosition(2, 2), CellSurface.Snowdrift);

            Assert.AreEqual(1, geometry.AddFloor());
            Assert.AreEqual(2, geometry.FloorCount);
            Assert.IsTrue(geometry.IsConsistent);
            Assert.AreEqual(CellType.Void, geometry.GetCell(new GridPosition(0, 0, 1)));
            Assert.AreEqual(CellType.Wall, geometry.GetCell(new GridPosition(1, 1)));
            Assert.AreEqual(CellSurface.Snowdrift, geometry.GetSurface(new GridPosition(2, 2)));

            geometry.RemoveTopFloor();
            Assert.AreEqual(1, geometry.FloorCount);
            Assert.IsTrue(geometry.IsConsistent);
            Assert.IsTrue(geometry.SurfacesConsistent);
            Assert.AreEqual(CellSurface.Snowdrift, geometry.GetSurface(new GridPosition(2, 2)));
            Assert.Throws<System.InvalidOperationException>(() => geometry.RemoveTopFloor());
        }

        // ------------------------------------------------------------ Flags

        [Test]
        public void Flags_ComeFromStructureObstacleAndSurface()
        {
            var tree = Obstacle("tree", movement: true, bullets: true, sight: false);
            var bush = Obstacle("bush", movement: false, bullets: false, sight: false, speed: 0.5f, flammable: true);
            var geometry = new LevelGeometry(5, 1, CellType.Floor, floors: 2);
            geometry.SetCell(new GridPosition(0, 0), CellType.Wall);
            geometry.SetObstacle(new GridPosition(1, 0), tree);
            geometry.SetObstacle(new GridPosition(2, 0), bush);
            geometry.SetSurface(new GridPosition(3, 0), CellSurface.Snowdrift);
            geometry.SetCell(new GridPosition(4, 0, 1), CellType.Void);

            var grid = new LevelGrid(geometry);

            Assert.AreEqual(CellFlags.Solid, grid.GetFlags(new GridPosition(0, 0)));
            Assert.AreEqual(CellFlags.BlocksMovement | CellFlags.BlocksBullets, grid.GetFlags(new GridPosition(1, 0)));
            Assert.AreEqual(CellFlags.Slows | CellFlags.Flammable, grid.GetFlags(new GridPosition(2, 0)));
            Assert.AreEqual(0.5f, grid.ObstacleSpeedFactor(new GridPosition(2, 0)), 1e-5f);
            Assert.AreEqual(CellFlags.Slows, grid.GetFlags(new GridPosition(3, 0)));
            Assert.AreEqual(CellFlags.None, grid.GetFlags(new GridPosition(4, 0)));
            Assert.AreEqual(CellFlags.BlocksMovement, grid.GetFlags(new GridPosition(4, 0, 1)), "Void: not walkable, see-through.");
            Assert.AreEqual(CellFlags.Solid, grid.GetFlags(new GridPosition(-1, 0)), "Outside is a wall.");
            Assert.AreEqual(CellFlags.Solid, grid.GetFlags(new GridPosition(0, 0, 5)));
            Assert.AreEqual(geometry.GetFlags(new GridPosition(1, 0)), grid.GetFlags(new GridPosition(1, 0)), "Editor and runtime agree.");
        }

        [Test]
        public void ObstacleKinds_AreKeptOnlyWhileUsed()
        {
            var tree = Obstacle("tree", true, true, false);
            var rock = Obstacle("rock", true, true, true);
            var geometry = new LevelGeometry(3, 1, CellType.Floor);
            geometry.SetObstacle(new GridPosition(0, 0), tree);
            geometry.SetObstacle(new GridPosition(1, 0), rock);
            geometry.SetObstacle(new GridPosition(2, 0), tree);
            Assert.AreEqual(2, geometry.ObstacleTypes.Count);

            geometry.SetObstacle(new GridPosition(0, 0), null);
            geometry.SetObstacle(new GridPosition(2, 0), null);

            Assert.AreEqual(1, geometry.ObstacleTypes.Count);
            Assert.AreSame(rock, geometry.GetObstacle(new GridPosition(1, 0)), "Cells are renumbered with the list.");
            Assert.IsTrue(geometry.ObstaclesConsistent);
        }

        // ------------------------------------------------------------ Algorithms stay on a floor

        [Test]
        public void Pathfinding_FlowField_Bfs_StayOnTheFloorOfTheStart()
        {
            var geometry = new LevelGeometry(4, 4, CellType.Floor, floors: 2);
            var passable = Enumerable.Repeat(true, geometry.CellCount).ToArray();
            passable[geometry.ToIndex(new GridPosition(1, 1, 1))] = false;
            var mask = new CellMaskPassability(4, 4, passable);
            Assert.IsFalse(mask.IsPassable(new GridPosition(1, 1, 1)));
            Assert.IsTrue(mask.IsPassable(new GridPosition(1, 1)));
            Assert.IsFalse(mask.IsPassable(new GridPosition(1, 1, 2)), "Beyond the mask's floors.");

            var path = new List<GridPosition>();
            var pathfinder = new GridPathfinder(4, 4);
            Assert.IsTrue(pathfinder.TryFindPath(new GridPosition(0, 0, 1), new GridPosition(3, 3, 1), mask, path));
            Assert.IsTrue(path.All(cell => cell.Floor == 1));
            Assert.IsFalse(path.Contains(new GridPosition(1, 1, 1)));
            Assert.IsFalse(pathfinder.TryFindPath(new GridPosition(0, 0), new GridPosition(3, 3, 1), mask, path),
                "No stairs: floors do not connect.");

            var bfs = new GridBfs(4, 4);
            bfs.Run(new GridPosition(0, 0, 1), mask);
            Assert.IsFalse(bfs.IsReachable(new GridPosition(1, 1, 1)));
            Assert.IsTrue(bfs.IsReachable(new GridPosition(3, 3, 1)));
            Assert.IsFalse(bfs.IsReachable(new GridPosition(3, 3)), "Another floor.");

            var field = new FlowField(4, 4);
            field.Build(new GridPosition(3, 3, 1), mask);
            Assert.AreEqual(1, field.Floor);
            Assert.AreEqual(FlowField.Unreachable, field.DistanceAt(1 * 4 + 1), "The blocked cell of floor 1.");
        }

        [Test]
        public void FieldOfView_SeesItsFloorOnly()
        {
            var opaque = new bool[5 * 5 * 2];
            opaque[(1 * 5 + 2) * 5 + 3] = true; // a wall on floor 1 at (3, 2)
            var opacity = new CellMaskOpacity(5, 5, opaque);
            var fov = new FieldOfView(5, 5);

            fov.Compute(new GridPosition(0, 2, 1), 4, opacity);

            Assert.AreEqual(1, fov.Floor);
            Assert.IsTrue(fov.IsVisible(new GridPosition(3, 2, 1)), "The wall is seen.");
            Assert.IsFalse(fov.IsVisible(new GridPosition(4, 2, 1)), "Behind the wall.");
            Assert.IsFalse(fov.IsVisible(new GridPosition(1, 2)), "Floor 0 is never visible from floor 1.");
            Assert.IsTrue(fov.VisibleCells.All(cell => cell.Floor == 1));
        }

        [Test]
        public void LineOfSight_UsesTheFloorItIsGiven()
        {
            var opaque = new bool[3 * 1 * 2];
            opaque[3 + 1] = true; // floor 1, (1, 0)
            var opacity = new CellMaskOpacity(3, 1, opaque);
            Assert.IsTrue(GridLineOfSight.IsClear(0f, 0f, 2f, 0f, opacity));
            Assert.IsFalse(GridLineOfSight.IsClear(0f, 0f, 2f, 0f, opacity, floor: 1));
            Assert.IsTrue(GridRaycast.Cast(Vector2.zero, new Vector2(2f, 0f), opacity, out _, out var hit, floor: 1));
            Assert.AreEqual(new GridPosition(1, 0, 1), hit);
        }

        // ------------------------------------------------------------ Editing and validation

        [Test]
        public void SetObstacle_OnlyOnFloor_ErasedWithTheCell()
        {
            var level = Level(4, 4);
            var tree = Obstacle("tree", true, true, false);
            Assert.IsTrue(LevelEditing.SetObstacle(level, new GridPosition(1, 1), tree));
            Assert.IsFalse(LevelEditing.SetObstacle(level, new GridPosition(1, 1), tree), "Nothing changed.");
            Assert.AreSame(tree, level.Geometry.GetObstacle(new GridPosition(1, 1)));

            LevelEditing.SetCellType(level, new GridPosition(1, 1), CellType.Wall);
            Assert.IsNull(level.Geometry.GetObstacle(new GridPosition(1, 1)), "A wall has no obstacle.");
            Assert.IsFalse(LevelEditing.SetObstacle(level, new GridPosition(1, 1), tree), "Not on a wall.");

            LevelEditing.SetObstacle(level, new GridPosition(2, 2), tree);
            Assert.IsTrue(LevelEditing.ClearCell(level, new GridPosition(2, 2)));
            Assert.IsNull(level.Geometry.GetObstacle(new GridPosition(2, 2)));
        }

        [Test]
        public void Floors_AddAndRemoveTop_TakeEverythingOnIt()
        {
            var level = Level(4, 4);
            LevelEditing.AddPlayerStart(level, new GridPosition(0, 0));
            Assert.AreEqual(1, LevelEditing.AddFloor(level));
            var upper = new GridPosition(2, 2, 1);
            LevelEditing.SetCellType(level, upper, CellType.Floor);
            LevelEditing.AddMedkit(level, upper);
            LevelEditing.AddLight(level, upper);

            Assert.IsTrue(LevelEditing.RemoveTopFloor(level));

            Assert.AreEqual(1, level.Geometry.FloorCount);
            Assert.IsEmpty(level.Medkits);
            Assert.IsEmpty(level.Lights.Where(light => light.Cell.Floor == 1));
            Assert.AreEqual(1, level.PlayerStarts.Count, "The ground floor keeps its objects.");
            Assert.IsFalse(LevelEditing.RemoveTopFloor(level), "The only floor stays.");
        }

        [Test]
        public void Validator_ObjectInBlockingObstacle_AndOverhangingFloor()
        {
            var level = Level(4, 4);
            var tree = Obstacle("tree", true, true, false);
            var start = LevelEditing.AddPlayerStart(level, new GridPosition(0, 0));
            level.Geometry.SetObstacle(new GridPosition(0, 0), tree); // behind the editor's back
            LevelEditing.AddFloor(level);
            level.Geometry.SetCell(new GridPosition(3, 3, 1), CellType.Floor);
            level.Geometry.SetCell(new GridPosition(3, 3), CellType.Void);

            var report = LevelValidator.Validate(level);

            Assert.IsTrue(report.Issues.Any(i => i.Code == ValidationCodes.ObjectInBlockedCell && i.EntityId == start.Id));
            Assert.IsTrue(report.Issues.Any(i => i.Code == ValidationCodes.FloorOverhang));
        }

        [Test]
        public void KeyDoorSolver_ObstaclesBlockTheWay()
        {
            var level = Level(3, 1);
            var rock = Obstacle("rock", true, true, true);
            LevelEditing.SetObstacle(level, new GridPosition(1, 0), rock);

            var result = KeyDoorSolver.Solve(level.Geometry, level.Doors, level.Keys, new GridPosition(0, 0));

            Assert.IsFalse(result.IsReachable(new GridPosition(2, 0)));
        }
    }
}
