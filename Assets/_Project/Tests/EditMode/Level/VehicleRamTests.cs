using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Vehicles;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Vehicles;
using Purge.Gameplay.Weapons;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Level
{
    /// <summary>Stage 9b: ramming the horde, monsters hitting the body, the wreck throwing the player out.</summary>
    public class VehicleRamTests
    {
        private const float Dt = 1f / 60f;

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private PlayerDefinition _playerDefinition;
        private VehicleDefinition _car;
        private MonsterDefinition _mass;
        private MonsterDefinition _biter;
        private MonsterDefinition _elite;
        private FakePlayerInput _input;
        private PlayerSystem _player;
        private PlayerHealth _health;
        private HordeSystem _horde;
        private VehicleSystem _vehicles;

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        /// <summary>60×21 open floor walled around, the player at (5, 10), the car at (8, 10) facing east.</summary>
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

            var stress = Create<StressTestDefinition>();
            stress.ConfigureSandbox(true, 0, 0);
            _level.StressTest = stress;
            var horde = Create<HordeDefinition>();
            _level.Horde = horde;
            _mass = Monster("walker", MonsterRank.Mass, new MonsterStats { MaxHp = 30f, Speed = 0.5f, Damage = 0 });
            _biter = Monster("biter", MonsterRank.Mass,
                new MonsterStats { MaxHp = 30f, Speed = 2f, AttackReach = 0.9f, Damage = 10, AttackInterval = 0.5f });
            _elite = Monster("elite", MonsterRank.Elite, new MonsterStats { MaxHp = 500f, Speed = 0.1f, Radius = 0.45f, Damage = 0 });
            horde.ConfigureMonsters(new[] { new HordeSpawn(_mass, 1f), new HordeSpawn(_biter, 1f), new HordeSpawn(_elite, 1f) });

            _playerDefinition = Create<PlayerDefinition>();
            _playerDefinition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _car = Create<VehicleDefinition>();
            _car.Configure("car",
                new EngineModule { MaxSpeed = 10f, Acceleration = 6f, Braking = 14f, Rolling = 3f, ReverseSpeed = 3.5f, Power = 1f },
                new SteeringModule { TurnRadius = 3.5f },
                new HullModule { Health = 300f, Length = 3.5f, Width = 1.4f, WallSafeSpeed = 3f, WallDamage = 4f },
                new RamModule { Mass = 8f },
                new WreckModule());
        }

        [TearDown]
        public void TearDown()
        {
            _vehicles?.Dispose();
            _horde?.Dispose();
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

        private MonsterDefinition Monster(string id, MonsterRank rank, MonsterStats stats)
        {
            var monster = Create<MonsterDefinition>();
            monster.Configure(id, rank, MonsterBiome.Common, stats);
            monster.SetPerception(MonsterPerception.Hunter);
            return monster;
        }

        private Vehicle Drive()
        {
            _level.MutableVehicles.Add(new VehicleData("vehicle_1", new GridPosition(8, 10), _car, 90f));
            var grid = new LevelGrid(_level.Geometry);
            var doors = new DoorSystem(_level);
            var blockers = new CellBlockers(grid);
            var passability = new LevelPassability(grid, doors, blockers);
            var spatial = new SpatialQueryService(grid, doors);
            _input = new FakePlayerInput();
            _player = new PlayerSystem(_level, _playerDefinition, _input, passability, new OccupancyMap(grid),
                new LevelLaunchOptions(startIndex: 0, seed: 5), new Aim());
            _health = new PlayerHealth(_playerDefinition);
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _horde = new HordeSystem(_level, grid, passability, doors, _player, _health, new SoundEventBus());
            _horde.GodMode = false;
            _horde.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            var targets = new CombatTargets(spatial, _horde);
            _vehicles = new VehicleSystem(_level, _player, _input, passability, blockers, spatial,
                new WeaponSystem(_input, _playerDefinition), _horde, targets, health: _health);
            _vehicles.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            var vehicle = _vehicles.Vehicles[0];
            Assert.IsTrue(_vehicles.Enter(vehicle));
            return vehicle;
        }

        private int Spawn(MonsterDefinition kind, float x, float y)
        {
            var agent = _horde.SpawnAt(new Vector2(x, y), MonsterPerception.Hunter, _horde.KindIndexOf(kind));
            Assert.That(agent, Is.GreaterThanOrEqualTo(0));
            return agent;
        }

        private void Tick(float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt)
            {
                _player.Tick(Dt);
                _vehicles.Tick(Dt);
                _horde.Tick(Dt);
                _input.EndFrame();
            }
        }

        [Test]
        public void Body_PushOut_PutsACircleJustOutside()
        {
            // Facing north: half length 2 along y, half width 1 along x.
            Assert.IsTrue(VehicleBody.PushOut(new Vector2(1.2f, 0f), 0.5f, Vector2.zero, 0f, 2f, 1f, out var step));
            Assert.That(step.x, Is.EqualTo(0.3f).Within(1e-4f), "beside the right side: out to the right");
            Assert.IsTrue(VehicleBody.PushOut(new Vector2(0.5f, -1.8f), 0.3f, Vector2.zero, 0f, 2f, 1f, out step));
            Assert.That(step.y, Is.EqualTo(-0.5f).Within(1e-4f), "inside near the back: out through the back");
            Assert.IsFalse(VehicleBody.PushOut(new Vector2(2f, 0f), 0.5f, Vector2.zero, 0f, 2f, 1f, out _));
        }

        [Test]
        public void AtSpeed_RankAndFileDie_EachWearsTheHull_AndBrakes()
        {
            var car = Drive();
            for (var i = 0; i < 5; i++) Spawn(_mass, 30f, 8.5f + i * 0.7f); // a wall of five across its way
            var hits = 0;
            _vehicles.Rammed += (vehicle, count, kills) => hits += count;
            _input.Hold(PlayerAction.Gas, true);
            for (var t = 0f; t < 6f && _vehicles.RamKills < 5; t += Dt) Tick(Dt);

            Assert.That(_vehicles.RamKills, Is.EqualTo(5));
            Assert.That(hits, Is.EqualTo(5));
            Assert.That(_horde.AliveCount, Is.Zero);
            Assert.That(car.Health, Is.EqualTo(300f - 5 * 4f).Within(1e-3f), "wear 4 per monster");
            Assert.That(_health.Current, Is.EqualTo(_health.Max), "the player is not hurt");
        }

        [Test]
        public void Slow_NoRam_TheHordeIsPutOutOfTheBody()
        {
            var car = Drive();
            var walker = Spawn(_mass, 10.4f, 10f); // in the front of the body (front edge x = 9.75)
            Tick(0.5f);
            Assert.That(_vehicles.RamHits, Is.Zero);
            Assert.That(_horde.Health[walker], Is.EqualTo(30f));
            var distance = VehicleBody.Distance(_horde.Positions[walker], car.Position, car.Heading, car.HalfLength, car.HalfWidth);
            Assert.That(distance, Is.GreaterThan(_horde.RadiusOf(walker) * 0.9f), "stands off the body");
        }

        [Test]
        public void Elite_ThrowsItBack_HurtALittle_TheHullTakesTheBounce()
        {
            var car = Drive();
            var elite = Spawn(_elite, 30f, 10f);
            var bounced = 0;
            _vehicles.Bounced += (vehicle, at) => bounced++;
            _input.Hold(PlayerAction.Gas, true);
            for (var t = 0f; t < 6f && bounced == 0; t += Dt) Tick(Dt);

            Assert.That(bounced, Is.EqualTo(1));
            Assert.That(car.Speed, Is.LessThan(0f), "thrown back");
            Assert.That(_horde.Health[elite], Is.InRange(400f, 499f), "a little hurt");
            Assert.That(car.Health, Is.EqualTo(300f - 25f).Within(1e-3f));
            Assert.That(car.Position.x, Is.LessThan(_horde.Positions[elite].x - car.HalfLength));
        }

        [Test]
        public void MonstersHitTheBody_TheVehicleTakesIt_NotThePlayer()
        {
            var car = Drive();
            Spawn(_biter, 14f, 10f);
            Tick(4f);
            Assert.That(_horde.PlayerHits, Is.GreaterThan(0), "reached the body (the player is 1.75 inside)");
            Assert.That(car.Health, Is.LessThan(300f));
            Assert.That(_health.Current, Is.EqualTo(_health.Max));
        }

        [Test]
        public void Wreck_SmokesThenThrowsThePlayerOut_UnhurtForASecond_AndIsGone()
        {
            var car = Drive();
            var near = Spawn(_mass, 9f, 12.5f);
            var exploded = 0;
            _vehicles.Exploded += vehicle => exploded++;
            _vehicles.Hurt(car, 1000f);
            Assert.IsTrue(car.Broken);

            _input.Hold(PlayerAction.Gas, true);
            Tick(1f);
            Assert.That(car.Speed, Is.EqualTo(0f), "the engine is dead");
            Assert.IsTrue(_player.InVehicle, "still smoking");

            Tick(0.6f);
            Assert.That(exploded, Is.EqualTo(1));
            Assert.That(car.State, Is.EqualTo(VehicleState.Destroyed));
            Assert.IsFalse(_player.InVehicle);
            Assert.That(_horde.Shell, Is.Null);
            Assert.That(Vector2.Distance(_player.Position, car.Position), Is.GreaterThan(car.HalfWidth), "out beside it");
            Assert.That(_horde.PushVelocities[near].sqrMagnitude, Is.GreaterThan(0f), "pushed off");

            _health.Damage(10, Vector2.zero);
            Assert.That(_health.Current, Is.EqualTo(_health.Max), "unhurt for a while");
            Tick(1.1f);
            _health.Damage(10, Vector2.zero);
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 10));
            Assert.IsNull(_vehicles.FindEnterable(), "nothing to get into");
        }
    }
}
