using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Spatial;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Monsters
{
    /// <summary>Stage 5d: doors by rank (open, break, locked), the charge (crowd impulse, knockback) and the push.</summary>
    public class BruteAndDoorsTests
    {
        private const float Dt = 1f / 60f;
        private static readonly GridPosition DoorCell = new GridPosition(10, 10);

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private LevelGrid _grid;
        private PlayerDefinition _playerDefinition;
        private DoorSystem _doors;
        private LevelPassability _passability;
        private PlayerSystem _player;
        private FakePlayerInput _input;
        private PlayerHealth _health;
        private StressTestDefinition _stress;
        private MonsterDefinition _monster;
        private MonsterDefinition _mass;
        private HordeSystem _horde;
        private MonsterAbilities _abilities;

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        /// <summary>40×21 floor, a wall x = 10 with a door at (10, 10); the player at (5, 10) west of it.</summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(40, 21);
            for (var y = 1; y < 20; y++)
            for (var x = 1; x < 39; x++)
                geometry.SetCell(new GridPosition(x, y), x != 10 ? CellType.Floor : y == 10 ? CellType.Door : CellType.Wall);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(5, 10)));

            _stress = ScriptableObject.CreateInstance<StressTestDefinition>();
            _created.Add(_stress);
            _level.StressTest = _stress;
            _monster = ScriptableObject.CreateInstance<MonsterDefinition>();
            _monster.Configure("brute", MonsterRank.Officer, MonsterBiome.Common,
                new MonsterStats { MaxHp = 300f, Speed = 3f, Radius = 0.4f, AttackReach = 0.9f, Damage = 0, AttackInterval = 0.5f });
            _monster.SetPerception(MonsterPerception.Hunter);
            _created.Add(_monster);
            _mass = ScriptableObject.CreateInstance<MonsterDefinition>();
            _mass.Configure("walker", MonsterRank.Mass, MonsterBiome.Common,
                new MonsterStats { MaxHp = 30f, Speed = 3f, Damage = 0 });
            _created.Add(_mass);
            _stress.ConfigureSandbox(true, 0, 0);
            _stress.ConfigureMonsters(new[] { new HordeSpawn(_monster, 1f), new HordeSpawn(_mass, 1f) });

            _playerDefinition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _playerDefinition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
        }

        [TearDown]
        public void TearDown()
        {
            _horde?.Dispose();
            _horde = null;
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_playerDefinition);
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private void Build(string keyId = null)
        {
            _level.MutableDoors.Add(new DoorData("door_1", DoorCell, keyId));
            _grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            _passability = new LevelPassability(_grid, _doors);
            _input = new FakePlayerInput();
            _player = new PlayerSystem(_level, _playerDefinition, _input, _passability, new OccupancyMap(_grid),
                new LevelLaunchOptions(startIndex: 0), new Aim());
            _health = new PlayerHealth(_playerDefinition);
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _horde = new HordeSystem(_level, _grid, _passability, _doors, _player, _health, new SoundEventBus());
            _horde.GodMode = false;
            var targets = new CombatTargets(new SpatialQueryService(_grid, _doors), _horde);
            _abilities = new MonsterAbilities(_horde, targets, _player, _health);
            _horde.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _abilities.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private void Tick(float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt)
            {
                _player.Tick(Dt);
                _horde.Tick(Dt);
                _abilities.Tick(Dt);
            }
        }

        private int Spawn(MonsterDefinition kind, float x, float y)
        {
            var agent = _horde.SpawnAt(new Vector2(x, y), MonsterPerception.Hunter, _horde.KindIndexOf(kind));
            Assert.That(agent, Is.GreaterThanOrEqualTo(0));
            return agent;
        }

        [Test]
        public void ByRank_MassStops_OfficerOpens_BossBreaks()
        {
            Assert.That(MonsterDefinition.DoorUseOf(MonsterRank.Mass, MonsterDoorUse.ByRank), Is.EqualTo(MonsterDoorUse.Never));
            Assert.That(MonsterDefinition.DoorUseOf(MonsterRank.Officer, MonsterDoorUse.ByRank), Is.EqualTo(MonsterDoorUse.Open));
            Assert.That(MonsterDefinition.DoorUseOf(MonsterRank.Elite, MonsterDoorUse.ByRank), Is.EqualTo(MonsterDoorUse.Open));
            Assert.That(MonsterDefinition.DoorUseOf(MonsterRank.Boss, MonsterDoorUse.ByRank), Is.EqualTo(MonsterDoorUse.Break));
            Assert.That(MonsterDefinition.DoorUseOf(MonsterRank.Mass, MonsterDoorUse.Break), Is.EqualTo(MonsterDoorUse.Break));
        }

        [Test]
        public void RankAndFile_DoNotOpenTheDoor()
        {
            Build();
            var walker = Spawn(_mass, 14f, 10f);
            Tick(4f);
            Assert.That(_doors.IsOpenAt(DoorCell), Is.False);
            Assert.That(_horde.Positions[walker].x, Is.GreaterThan(10.5f), "stays behind the closed door");
        }

        [Test]
        public void Officer_OpensTheDoorAtOnce_AndItStaysOpen()
        {
            Build();
            var officer = Spawn(_monster, 14f, 10f);
            Tick(3f);
            Assert.That(_doors.IsOpenAt(DoorCell), Is.True);
            Assert.That(_horde.DoorsOpened, Is.EqualTo(1));
            Assert.That(_horde.DoorBlows, Is.Zero);
            Assert.That(_horde.Positions[officer].x, Is.LessThan(9.5f), "came through to the player");
            Tick(2f);
            Assert.That(_doors.IsOpenAt(DoorCell), Is.True, "nobody closes it");
        }

        [Test]
        public void Brute_BreaksTheDoor_BlowByBlow_ItFallsAndCannotBeClosed()
        {
            _monster.Stats.Doors = MonsterDoorUse.Break;
            _monster.Stats.DoorDamage = 20f; // the default strength 60: three blows
            Build();
            var brute = Spawn(_monster, 13f, 10f);
            var broken = 0;
            var from = Vector2.zero;
            _doors.DoorBroken += (door, at) =>
            {
                broken++;
                from = at;
            };

            for (var t = 0f; t < 5f && _horde.DoorBlows < 2; t += Dt) Tick(Dt);
            Assert.That(_horde.DoorBlows, Is.EqualTo(2));
            Assert.That(_doors.IsOpenAt(DoorCell), Is.False, "still standing after two blows");
            Assert.That(_doors.StrengthLeft("door_1"), Is.EqualTo(20f).Within(1e-3f));
            Assert.That(_horde.Positions[brute].x, Is.InRange(10.8f, 11.3f), "beats it from right next to it");

            Tick(0.6f);
            Assert.That(broken, Is.EqualTo(1));
            Assert.That(_doors.IsBroken("door_1"), Is.True);
            Assert.That(_doors.IsOpenAt(DoorCell), Is.True);
            Assert.That(from.x, Is.GreaterThan(10f), "broken from the east: falls west");
            Assert.Throws<InvalidOperationException>(() => _doors.SetOpen("door_1", false));
            Tick(2f);
            Assert.That(_horde.Positions[brute].x, Is.LessThan(9.5f), "walks through");
        }

        [Test]
        public void LockedDoor_StopsEveryone()
        {
            _monster.Stats.Doors = MonsterDoorUse.Break;
            Build(keyId: "key_1");
            Spawn(_monster, 13f, 10f);
            Tick(4f);
            Assert.That(_doors.IsOpenAt(DoorCell), Is.False);
            Assert.That(_horde.DoorBlows, Is.Zero);
            Assert.That(_doors.HitDoor("door_1", 1000f, new Vector2(13f, 10f)), Is.False);
        }

        private static ChargeAbility Charge() => new ChargeAbility
        {
            FirstDelay = 0f, Warning = 0.4f, Recovery = 0.3f, Cooldown = 20f, MinRange = 0f, MaxRange = 10f,
            Distance = 9f, Speed = 12f, Damage = 20f, Knockback = 2f, CrowdPush = 8f,
        };

        [Test]
        public void Charge_WarnsAsFarAsItRushes_HitsAndKnocksThePlayer_WithoutStun()
        {
            _monster.SetAbilities(Charge());
            Build();
            var brute = Spawn(_monster, 5f, 3f); // south of the player, open floor between

            Tick(0.2f);
            Assert.That(_abilities.Phases[brute], Is.EqualTo(AbilityPhase.Warning));
            Assert.That(_abilities.ChargeLength(brute), Is.EqualTo(9f).Within(0.01f), "nothing in the way: the whole distance");

            Tick(1.2f);
            Assert.That(_abilities.ChargeHits, Is.EqualTo(1));
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 20));
            Assert.That(_player.Position.y, Is.GreaterThan(11.3f), "knocked along the charge");
            Assert.That(_player.Stunned, Is.False);
            Assert.That(_horde.IsDashing(brute), Is.False, "the rush ends on the player");
        }

        [Test]
        public void Charge_ShovesTheCrowdAside_Unhurt()
        {
            _mass.Stats.Speed = 0.1f;
            _monster.SetAbilities(Charge());
            Build();
            Spawn(_monster, 5f, 3f);
            var first = Spawn(_mass, 5.05f, 6f);
            var second = Spawn(_mass, 4.95f, 7.5f);

            Tick(1.0f);
            Assert.That(_abilities.Casts, Is.EqualTo(1));
            Assert.That(_horde.Health[first], Is.EqualTo(30f));
            Assert.That(_horde.Health[second], Is.EqualTo(30f));
            Assert.That(_horde.Positions[first].x, Is.GreaterThan(5.5f), "to the side it stood on (east)");
            Assert.That(_horde.Positions[second].x, Is.LessThan(4.5f), "to the side it stood on (west)");
        }

        [Test]
        public void Charge_StopsAtAWall()
        {
            _monster.SetAbilities(Charge());
            Build();
            var brute = Spawn(_monster, 13f, 4f); // the wall x = 10 between it and the player
            _horde.SetCasting(brute, false);
            var reach = _horde.DashReach(new Vector2(13f, 4f), Vector2.left, 9f, 0.4f);
            Assert.That(reach, Is.LessThan(2.6f), "the wall at x = 10 stops the body");
        }

        [Test]
        public void Charge_KnocksThePlayer_EvenInGodMode()
        {
            _monster.SetAbilities(Charge());
            Build();
            _horde.GodMode = true;
            Spawn(_monster, 5f, 3f);
            Tick(1.4f);
            Assert.That(_abilities.ChargeHits, Is.EqualTo(1));
            Assert.That(_health.Current, Is.EqualTo(_health.Max), "God: no damage");
            Assert.That(_player.Position.y, Is.GreaterThan(11.3f), "but the knock");
        }

        [Test]
        public void Mass_ByRank_AndExplicit()
        {
            Assert.That(MonsterDefinition.MassOf(MonsterRank.Mass, new MonsterStats()), Is.EqualTo(MonsterDefinition.RankAndFileMass));
            Assert.That(MonsterDefinition.MassOf(MonsterRank.Officer, new MonsterStats()), Is.EqualTo(MonsterDefinition.OfficerMass));
            Assert.That(MonsterDefinition.MassOf(MonsterRank.Elite, new MonsterStats()), Is.EqualTo(MonsterDefinition.EliteMass));
            Assert.That(float.IsPositiveInfinity(MonsterDefinition.MassOf(MonsterRank.Boss, new MonsterStats())), Is.True);
            Assert.That(MonsterDefinition.MassOf(MonsterRank.Boss, new MonsterStats { Mass = 20f }), Is.EqualTo(20f));
            Assert.That(float.IsPositiveInfinity(MonsterDefinition.MassOf(MonsterRank.Mass, new MonsterStats { Immovable = true })), Is.True);
        }

        /// <summary>The player walks east into a monster standing at (8, 10) for 2 s.</summary>
        private float WalkInto(MonsterDefinition kind, out int agent)
        {
            Build();
            agent = Spawn(kind, 8f, 10f);
            _input.Move = Vector2.right;
            Tick(2f);
            return _player.Position.x;
        }

        [Test]
        public void Player_WalksThroughTheRankAndFile_AsBefore()
        {
            var x = WalkInto(_mass, out var walker);
            // It gives way until the wall x = 10 pins it (≈ 9.2): pinned, it is not walked through.
            Assert.That(x, Is.GreaterThan(8.5f), "a light body gives way");
            Assert.That(_horde.Positions[walker].x, Is.GreaterThan(9.1f), "pushed up to the wall");
            Assert.That((_horde.Positions[walker] - _player.Position).magnitude, Is.GreaterThan(0.58f), "never inside each other");
        }

        [Test]
        public void Player_HardlyMovesAHeavyMonster()
        {
            _monster.Stats.Speed = 0.05f;
            _monster.Stats.Mass = 6f;
            var x = WalkInto(_monster, out var brute);
            Assert.That(x, Is.LessThan(8.2f), "leans into it, hardly gets on");
            Assert.That(_horde.Positions[brute].x, Is.GreaterThan(8f), "but it gives a little");
            Assert.That((_horde.Positions[brute] - _player.Position).magnitude, Is.GreaterThan(0.68f), "never inside each other");
        }

        [Test]
        public void Player_CannotMoveAnImmovableMonster()
        {
            _monster.Stats.Speed = 0.05f;
            _monster.Stats.Immovable = true;
            var x = WalkInto(_monster, out var boss);
            Assert.That(x, Is.LessThan(7.4f));
            Assert.That(_horde.Positions[boss].x, Is.GreaterThan(7.85f).And.LessThanOrEqualTo(8f));
        }

        [Test]
        public void Knockbacks_ScaleWithMass()
        {
            _monster.Stats.Speed = 0.01f;
            _monster.Stats.Mass = 6f;
            _mass.Stats.Speed = 0.01f;
            Build();
            var brute = Spawn(_monster, 30f, 5f);
            var walker = Spawn(_mass, 30f, 15f);
            _horde.Push(brute, new Vector2(6f, 0f));
            _horde.Push(walker, new Vector2(6f, 0f));
            Tick(1f);
            Assert.That(_horde.Positions[walker].x - 30f, Is.GreaterThan(0.4f), "the rank-and-file fly as before");
            Assert.That(_horde.Positions[brute].x - 30f, Is.LessThan(0.15f), "a Brute — a sixth");
        }

        [Test]
        public void Push_HurtsAndKnocksThePlayerAway()
        {
            _monster.SetAbilities(new PushAbility
            {
                FirstDelay = 0f, Warning = 0.3f, Cooldown = 20f, MinRange = 0f, MaxRange = 2.5f, Radius = 2.2f, Damage = 10f, Knockback = 2.5f,
            });
            Build();
            Spawn(_monster, 6.6f, 10f);
            Tick(0.5f);
            Assert.That(_abilities.Casts, Is.EqualTo(1));
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 10));
            Tick(0.3f);
            Assert.That(_player.Position.x, Is.LessThan(3.4f), "knocked west, away from the monster");
        }
    }
}
