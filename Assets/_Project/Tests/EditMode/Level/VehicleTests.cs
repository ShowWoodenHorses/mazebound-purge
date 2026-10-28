using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Vehicles;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Vehicles;
using Purge.Gameplay.Weapons;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Level
{
    /// <summary>Stage 9a: the vehicle's body on the grid, getting in and out, driving, walls.</summary>
    public class VehicleTests
    {
        private const float Dt = 1f / 60f;

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private PlayerDefinition _playerDefinition;
        private VehicleDefinition _car;
        private FakePlayerInput _input;
        private LevelPassability _passability;
        private SpatialQueryService _spatial;
        private PlayerSystem _player;
        private WeaponSystem _weapons;
        private VehicleSystem _vehicles;

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        /// <summary>60×21 open floor walled around (interior 1..58 × 1..19), the player at (5, 10).</summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(60, 21);
            for (var y = 1; y < 20; y++)
            for (var x = 1; x < 59; x++)
                geometry.SetCell(new GridPosition(x, y), CellType.Floor);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(5, 10)));

            _playerDefinition = Create<PlayerDefinition>();
            _playerDefinition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _car = Create<VehicleDefinition>();
            _car.Configure("car",
                new EngineModule { MaxSpeed = 10f, Acceleration = 6f, Braking = 14f, Rolling = 3f, ReverseSpeed = 3.5f },
                new SteeringModule { TurnRadius = 3.5f },
                new HullModule { Health = 300f, Length = 4.6f, Width = 2f, WallSafeSpeed = 3f, WallDamage = 4f });
        }

        [TearDown]
        public void TearDown()
        {
            _vehicles?.Dispose();
            Object.DestroyImmediate(_level);
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private T Create<T>() where T : ScriptableObject
        {
            var created = ScriptableObject.CreateInstance<T>();
            _created.Add(created);
            return created;
        }

        private void Place(int x, int y, float heading) =>
            _level.MutableVehicles.Add(new VehicleData("vehicle_" + (_level.Vehicles.Count + 1), new GridPosition(x, y), _car, heading));

        private void Load()
        {
            var options = new LevelLaunchOptions(startIndex: 0, seed: 5);
            var grid = new LevelGrid(_level.Geometry);
            var doors = new DoorSystem(_level);
            var blockers = new CellBlockers(grid);
            _passability = new LevelPassability(grid, doors, blockers);
            _spatial = new SpatialQueryService(grid, doors);
            _input = new FakePlayerInput();
            _player = new PlayerSystem(_level, _playerDefinition, _input, _passability, new OccupancyMap(grid), options, new Aim());
            _weapons = new WeaponSystem(_input, _playerDefinition);
            _vehicles = new VehicleSystem(_level, _player, _input, _passability, blockers, _spatial, _weapons);
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _vehicles.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private void Tick(float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt)
            {
                _player.Tick(Dt);
                _vehicles.Tick(Dt);
                _input.EndFrame();
            }
        }

        private Vehicle DriveFrom(int x, int y, float heading)
        {
            Place(x, y, heading);
            Load();
            var vehicle = _vehicles.Vehicles[0];
            Assert.IsTrue(_vehicles.Enter(vehicle));
            return vehicle;
        }

        // ------------------------------------------------------------------ Body

        [Test]
        public void Body_OverlapsCellsUnderItTurned()
        {
            var forward = VehicleBody.Forward(45f * Mathf.Deg2Rad);
            Assert.IsTrue(VehicleBody.OverlapsCell(Vector2.zero, forward, 2f, 0.5f, 1, 1), "Along the diagonal.");
            Assert.IsFalse(VehicleBody.OverlapsCell(Vector2.zero, forward, 2f, 0.5f, 2, -2), "Across the diagonal, away.");
            Assert.IsTrue(VehicleBody.Segment(new Vector2(-5f, 0f), new Vector2(5f, 0f), Vector2.zero, 0f, 2f, 1f, out var t));
            Assert.AreEqual(0.4f, t, 1e-4f, "Enters at x = -1 (half width), 4 of 10 along.");
            Assert.IsFalse(VehicleBody.Segment(new Vector2(-5f, 3f), new Vector2(5f, 3f), Vector2.zero, 0f, 2f, 1f, out _));
        }

        [Test]
        public void Parked_BlocksItsCellsAndShots()
        {
            Place(7, 10, 0f);
            Load();
            Assert.IsFalse(_passability.IsWalkable(new GridPosition(7, 10)), "Under the body.");
            Assert.IsFalse(_passability.IsWalkable(new GridPosition(7, 12)), "Under its front.");
            Assert.IsTrue(_passability.IsWalkable(new GridPosition(5, 10)), "The player beside it.");
            Assert.IsFalse(_spatial.IsClear(new Vector2(3f, 10f), new Vector2(11f, 10f), CellFlags.BlocksBullets),
                "A shot through it stops.");
            Assert.IsTrue(_spatial.IsClear(new Vector2(3f, 10f), new Vector2(11f, 10f)), "Sight passes.");
            var hit = _spatial.QuerySegment(new Vector2(3f, 10f), new Vector2(11f, 10f));
            Assert.AreEqual(SegmentHitKind.Blocker, hit.Kind);
            Assert.AreEqual(6f, hit.Point.x, 0.01f, "At its side (x = 7 - 1).");
        }

        // ------------------------------------------------------------------ In and out

        [Test]
        public void Enter_WithinReach_CarriesThePlayerAndLocksWeapons()
        {
            Place(7, 10, 0f);
            Load();
            var vehicle = _vehicles.FindEnterable();
            Assert.IsNotNull(vehicle, "The player at (5, 10) is a cell from its side.");
            Assert.IsTrue(_vehicles.Enter(vehicle));
            Assert.IsTrue(_player.InVehicle);
            Assert.IsTrue(_weapons.InputLocked);
            Assert.AreEqual(vehicle.Position, _player.Position);
            Assert.IsTrue(_passability.IsWalkable(new GridPosition(7, 10)), "Driven: its cells are free.");
            Assert.IsNull(_vehicles.FindEnterable(), "Nothing to get into while driving.");

            _input.Move = Vector2.right; // the stick does not walk the player
            Tick(0.5f);
            Assert.AreEqual(vehicle.Position, _player.Position);
        }

        [Test]
        public void Enter_TooFar_NothingToGetInto()
        {
            Place(12, 10, 0f);
            Load();
            Assert.IsNull(_vehicles.FindEnterable());
        }

        [Test]
        public void Exit_PutsThePlayerBesideAndParks()
        {
            var vehicle = DriveFrom(10, 10, 90f);
            _input.Hold(PlayerAction.Gas, true);
            Tick(1f);
            _input.Hold(PlayerAction.Gas, false);
            Assert.IsTrue(_vehicles.TryExit());
            Assert.IsFalse(_player.InVehicle);
            Assert.IsFalse(_weapons.InputLocked);
            Assert.AreEqual(VehicleState.Parked, vehicle.State);
            Assert.AreEqual(0f, vehicle.Speed);
            var distance = VehicleBody.Distance(_player.Position, vehicle.Position, vehicle.Heading, vehicle.HalfLength,
                vehicle.HalfWidth);
            Assert.That(distance, Is.GreaterThanOrEqualTo(_playerDefinition.BodyHalfSize), "Outside the body.");
            Assert.That(_player.Position.y, Is.GreaterThan(vehicle.Position.y), "Facing East, the left side is North.");
            Assert.IsFalse(_passability.IsWalkable(PlayerMovement.CellOf(vehicle.Position)), "Parked again: a wall.");
        }

        [Test]
        public void Exit_NoRoom_StaysInside()
        {
            // A corridor 3 wide: the body (2 wide, x 7.7..12.3) fills it, walls right behind and in front of it.
            Place(10, 10, 90f);
            for (var x = 1; x < 59; x++)
            {
                _level.Geometry.SetCell(new GridPosition(x, 8), CellType.Wall);
                _level.Geometry.SetCell(new GridPosition(x, 12), CellType.Wall);
            }

            for (var y = 9; y <= 11; y++)
            {
                _level.Geometry.SetCell(new GridPosition(7, y), CellType.Wall);
                _level.Geometry.SetCell(new GridPosition(13, y), CellType.Wall);
            }
            _level.MutablePlayerStarts.Clear();
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(30, 10)));
            Load();
            var vehicle = _vehicles.Vehicles[0];
            Assert.IsTrue(_vehicles.Enter(vehicle));
            Assert.IsFalse(_vehicles.TryExit());
            Assert.IsTrue(_player.InVehicle);
            Assert.AreEqual(VehicleState.Driven, vehicle.State);
            Assert.IsTrue(_passability.IsWalkable(new GridPosition(10, 10)), "Still driven: its cells stay free.");
        }

        // ------------------------------------------------------------------ Driving

        [Test]
        public void Gas_SpeedsUpToTopSpeedAlongTheHeading()
        {
            var vehicle = DriveFrom(10, 10, 90f);
            _input.Hold(PlayerAction.Gas, true);
            Tick(1f);
            Assert.AreEqual(6f, vehicle.Speed, 0.2f, "Acceleration 6 per second.");
            Tick(1.5f);
            Assert.AreEqual(10f, vehicle.Speed, 1e-3f, "Top speed.");
            Assert.That(vehicle.Position.x, Is.GreaterThan(20f));
            Assert.AreEqual(10f, vehicle.Position.y, 1e-3f, "Straight East.");
            Assert.AreEqual(vehicle.Position, _player.Position);
        }

        [Test]
        public void Brake_StopsThenReverses()
        {
            var vehicle = DriveFrom(20, 10, 90f);
            _input.Hold(PlayerAction.Gas, true);
            Tick(1f);
            _input.Hold(PlayerAction.Gas, false);
            _input.Hold(PlayerAction.Brake, true);
            Tick(0.5f);
            Assert.That(vehicle.Speed, Is.LessThanOrEqualTo(0f), "Braking 14 per second stops 6 within half a second.");
            Tick(2f);
            Assert.AreEqual(-3.5f, vehicle.Speed, 1e-3f, "Then backwards at the reverse speed.");
        }

        [Test]
        public void Steer_TurnsOnlyWhileMoving()
        {
            var vehicle = DriveFrom(20, 10, 90f);
            _input.Move = Vector2.right;
            Tick(0.5f);
            Assert.AreEqual(90f * Mathf.Deg2Rad, vehicle.Heading, 1e-4f, "Standing: the wheel does not turn the body.");
            _input.Hold(PlayerAction.Gas, true);
            Tick(1f);
            Assert.That(vehicle.Heading, Is.GreaterThan(100f * Mathf.Deg2Rad), "Right: clockwise, towards South.");
            Assert.That(vehicle.Position.y, Is.LessThan(10f));
        }

        [Test]
        public void HeadOn_WallStopsAndDamages()
        {
            var vehicle = DriveFrom(30, 10, 90f);
            _input.Hold(PlayerAction.Gas, true);
            var hits = 0;
            var impact = 0f;
            _vehicles.WallHit += (_, speed) =>
            {
                hits++;
                impact = speed;
            };
            Tick(5f);
            Assert.AreEqual(1, hits, "One hit per contact, though the throttle is still held.");
            Assert.That(impact, Is.GreaterThan(8f));
            Assert.That(vehicle.Position.x + vehicle.HalfLength, Is.LessThanOrEqualTo(58.5f + VehicleBody.Skin + 1e-3f),
                "Before the wall.");
            Assert.That(vehicle.Position.x + vehicle.HalfLength, Is.GreaterThan(58f), "Right at it.");
            Assert.That(vehicle.Speed, Is.LessThan(0.5f));
            Assert.That(vehicle.Health, Is.LessThan(300f - 4f * (8f - 3f) + 1e-3f), "(impact − safe) × damage.");
        }

        [Test]
        public void Glancing_WallSlidesAlongWithoutDamage()
        {
            // Nearly East, 10° towards North: it reaches the north wall (y 19.5) and slides along it.
            var vehicle = DriveFrom(10, 16, 80f);
            _input.Hold(PlayerAction.Gas, true);
            Tick(3f);
            Assert.IsTrue(vehicle.InContact, "Against the wall.");
            Assert.IsTrue(vehicle.IsScraping);
            Assert.That(vehicle.Speed, Is.GreaterThan(5f), "It keeps going.");
            Assert.That(vehicle.Position.x, Is.GreaterThan(25f));
            Assert.AreEqual(300f, vehicle.Health, "10° into the wall at top speed is below the safe speed.");
        }

        [Test]
        public void Riding_CellUpdatesAtMostFourTimesASecond()
        {
            var vehicle = DriveFrom(10, 10, 90f);
            var changes = 0;
            _player.CellChanged += (_, __) => changes++;
            _input.Hold(PlayerAction.Gas, true);
            Tick(4f);
            Assert.That(vehicle.Position.x - 10f, Is.GreaterThan(25f), "Crossed more than 25 cells.");
            Assert.That(changes, Is.LessThanOrEqualTo(Mathf.CeilToInt(4f / PlayerSystem.RideCellInterval) + 1));
            Assert.AreEqual(PlayerMovement.CellOf(_player.Position).X, _player.Cell.X, 3, "The cell lags by at most a quarter second.");
        }
    }
}
