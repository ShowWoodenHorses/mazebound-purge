using System.Collections.Generic;
using System.Threading;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Navigation;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Level;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Map;
using Maze.Gameplay.Navigation;
using Maze.Gameplay.Pickups;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Weapons;
using Maze.Presentation.Visual;
using Maze.Tests.EditMode.Player;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Navigation
{
    /// <summary>Route hint: target priority, cutting the walked part, detours, reaching (and the line's geometry).</summary>
    public class RouteHintTests
    {
        private const float Dt = 1f / 60f;

        private LevelData _level;
        private PlayerDefinition _definition;
        private FakePlayerInput _input;
        private DoorSystem _doors;
        private PlayerInventory _inventory;
        private PlayerSystem _player;
        private PickupSystem _pickups;
        private MapSystem _map;
        private RouteHintSystem _route;
        private readonly List<RouteTarget> _reached = new List<RouteTarget>();

        /// <summary>
        /// Corridor x = 1..10 at y = 1; start (1,1); key_1 at (4,1) opens the locked door (8,1); the map fragment at
        /// (10,1) is behind it; optionally an exit at (6,1).
        /// </summary>
        private void Build(bool withExit)
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(12, 3);
            for (var x = 1; x <= 10; x++) geometry.SetCell(new GridPosition(x, 1), CellType.Floor);
            geometry.SetCell(new GridPosition(8, 1), CellType.Door);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(1, 1)));
            _level.MutableDoors.Add(new DoorData("door_1", new GridPosition(8, 1), keyId: "key_1"));
            _level.MutableKeys.Add(new KeyData("key_1", new GridPosition(4, 1)));
            _level.MutableMapFragments.Add(new MapFragmentData("fragment_1", new GridPosition(10, 1), new GridRect(0, 0, 12, 3)));
            if (withExit) _level.MutableExits.Add(new ExitData("exit_1", new GridPosition(6, 1)));

            _definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _definition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _input = new FakePlayerInput();
            var grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            var occupancy = new OccupancyMap(grid);
            _inventory = new PlayerInventory();
            var health = new PlayerHealth(_definition);
            var weapons = new WeaponSystem(_input);
            _player = new PlayerSystem(_level, _definition, _input, new LevelPassability(grid, _doors), occupancy,
                new LevelLaunchOptions(startIndex: 0), Aim.FreeNoAssist);
            _map = new MapSystem(_level);
            _pickups = new PickupSystem(_level, _player, health, _inventory, weapons, new SoundEventBus(), _map);
            _route = new RouteHintSystem(_level, grid, _doors, _player, _inventory, _map, _pickups);
            _route.Reached += _reached.Add;
            _reached.Clear();

            _pickups.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _route.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _route?.Dispose();
            _pickups?.Dispose();
            _map?.Dispose();
            if (_level != null) Object.DestroyImmediate(_level);
            if (_definition != null) Object.DestroyImmediate(_definition);
        }

        private void WalkTo(int x)
        {
            for (var i = 0; i < 600 && _player.Cell.X != x; i++)
            {
                _input.Move = new Vector2(Mathf.Sign(x - _player.Cell.X), 0f);
                _player.Tick(Dt);
            }

            _input.Move = Vector2.zero;
            Assert.AreEqual(x, _player.Cell.X, "The player walked there.");
        }

        [Test]
        public void ReachableExit_ComesBeforeKeys_FragmentsBehindALockedDoorDoNotCount()
        {
            Build(withExit: true);
            Assert.AreEqual(RouteTarget.Exit, _route.Request());
            Assert.AreEqual(new GridPosition(6, 1), _route.TargetCell);
            Assert.AreEqual(6, _route.Path.Count, "(1,1) … (6,1).");

            var version = _route.Version;
            WalkTo(2);
            Assert.AreEqual(new GridPosition(2, 1), _route.Path[0], "The walked part is cut.");
            Assert.Greater(_route.Version, version);

            WalkTo(6);
            Assert.IsFalse(_route.IsActive, "Ends at the target.");
            CollectionAssert.AreEqual(new[] { RouteTarget.Exit }, _reached);
        }

        [Test]
        public void Key_ThenFragmentThroughItsDoor_DetourKeepsTheTarget_ThenTheDoor()
        {
            Build(withExit: false);
            Assert.AreEqual(RouteTarget.Key, _route.Request(), "No exit, the fragment is behind the locked door: the key.");
            WalkTo(4);
            Assert.IsTrue(_inventory.HasKey("key_1"));
            CollectionAssert.AreEqual(new[] { RouteTarget.Key }, _reached);

            Assert.AreEqual(RouteTarget.MapFragment, _route.Request(), "With its key the door opens the way.");
            Assert.AreEqual(new GridPosition(10, 1), _route.Path[_route.Path.Count - 1]);
            Assert.Contains(new GridPosition(8, 1), (System.Collections.ICollection)_route.Path);

            WalkTo(3); // Backwards: off the route.
            Assert.IsTrue(_route.IsActive);
            Assert.AreEqual(new GridPosition(3, 1), _route.Path[0], "A new path from where the player is.");
            Assert.AreEqual(new GridPosition(10, 1), _route.TargetCell, "Same target, like a navigator.");

            _map.Collect(_level.MapFragments[0]);
            Assert.AreEqual(RouteTarget.Door, _route.Request(), "Nothing else: the door the carried key opens.");
            WalkTo(7);
            Assert.IsFalse(_route.IsActive, "A door is reached next to it.");
            Assert.AreEqual(RouteTarget.Door, _reached[_reached.Count - 1]);
        }

        [Test]
        public void GridBfs_DistancesAndPath_AroundWalls()
        {
            // 5x3 open grid with a wall at (2,1) and (2,0): from (0,0) to (4,0) the path goes over the top row.
            var walls = new HashSet<GridPosition> { new GridPosition(2, 0), new GridPosition(2, 1) };
            var search = new GridBfs(5, 3);
            search.Run(new GridPosition(0, 0), new Open(5, 3, walls));
            Assert.AreEqual(8, search.DistanceTo(new GridPosition(4, 0)));
            Assert.AreEqual(-1, search.DistanceTo(new GridPosition(2, 0)), "A wall is never reached.");
            var path = new List<GridPosition>();
            Assert.IsTrue(search.TryGetPath(new GridPosition(4, 0), path));
            Assert.AreEqual(9, path.Count);
            Assert.AreEqual(new GridPosition(0, 0), path[0]);
            Assert.AreEqual(new GridPosition(2, 2), path[4], "Over the wall.");
        }

        [Test]
        public void LineGeometry_StartsAtThePlayer_RoundsTurns()
        {
            var path = new List<GridPosition> { new GridPosition(1, 1), new GridPosition(2, 1), new GridPosition(2, 2) };
            var points = new List<Vector2>();
            RouteLineMesh.BuildPoints(new Vector2(1.2f, 1f), path, 0.3f, points, new List<Vector2>());
            Assert.AreEqual(new Vector2(1.2f, 1f), points[0], "From the player, nothing behind.");
            Assert.AreEqual(new Vector2(2f, 2f), points[points.Count - 1]);
            Assert.AreEqual(2 + RouteLineMesh.CornerSegments + 1, points.Count, "One rounded turn.");
            Assert.IsFalse(points.Contains(new Vector2(2f, 1f)), "The corner itself is cut by the arc.");

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            RouteLineMesh.BuildRibbon(points, 0.1f, 0.05f, vertices, new List<Vector2>(), new List<Vector2>(), triangles);
            Assert.AreEqual(points.Count * 2, vertices.Count);
            Assert.AreEqual((points.Count - 1) * 6, triangles.Count);
        }

        private readonly struct Open : IGridPassability
        {
            private readonly int _width, _height;
            private readonly HashSet<GridPosition> _walls;

            public Open(int width, int height, HashSet<GridPosition> walls)
            {
                _width = width;
                _height = height;
                _walls = walls;
            }

            public bool IsPassable(GridPosition p) =>
                p.X >= 0 && p.Y >= 0 && p.X < _width && p.Y < _height && !_walls.Contains(p);
        }
    }
}
