using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Effects;
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
    /// <summary>Stage 5a: monster definitions in the horde — kinds by weight, own stats, climbing out, resistances, death effects.</summary>
    public class MonsterHordeTests
    {
        private const float Dt = 1f / 60f;

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private LevelGrid _grid;
        private PlayerDefinition _playerDefinition;
        private DoorSystem _doors;
        private LevelPassability _passability;
        private PlayerSystem _player;
        private PlayerHealth _health;
        private StressTestDefinition _stress;

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        /// <summary>30×30 open floor, the player at (15, 15).</summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(30, 30);
            for (var y = 1; y < 29; y++)
            for (var x = 1; x < 29; x++)
                geometry.SetCell(new GridPosition(x, y), CellType.Floor);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(15, 15)));

            _stress = ScriptableObject.CreateInstance<StressTestDefinition>();
            _created.Add(_stress);
            _level.StressTest = _stress;

            _playerDefinition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _playerDefinition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            _passability = new LevelPassability(_grid, _doors);
            _player = new PlayerSystem(_level, _playerDefinition, new FakePlayerInput(), _passability, new OccupancyMap(_grid),
                new LevelLaunchOptions(startIndex: 0), new Aim());
            _health = new PlayerHealth(_playerDefinition);
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_playerDefinition);
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private MonsterDefinition Monster(string id, float hp, float speed, float emerge = 0f)
        {
            var monster = ScriptableObject.CreateInstance<MonsterDefinition>();
            monster.Configure(id, MonsterRank.Mass, MonsterBiome.Common,
                new MonsterStats { MaxHp = hp, Speed = speed, EmergeTime = emerge });
            _created.Add(monster);
            return monster;
        }

        private HordeSystem Horde(int count, params HordeSpawn[] monsters)
        {
            _stress.ConfigureSandbox(true, 0, count);
            _stress.ConfigureMonsters(monsters);
            var horde = new HordeSystem(_level, _grid, _passability, _doors, _player, _health);
            horde.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            return horde;
        }

        private static void Tick(HordeSystem horde, float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt) horde.Tick(Dt);
        }

        [Test]
        public void Kinds_SpawnByWeight_WithOwnStats()
        {
            var walker = Monster("walker", hp: 45f, speed: 1.6f);
            var runner = Monster("runner", hp: 18f, speed: 3.4f);
            var horde = Horde(200, new HordeSpawn(walker, 3f), new HordeSpawn(runner, 1f));

            Assert.That(horde.KindList.Count, Is.EqualTo(2));
            int walkers = 0, runners = 0;
            for (var i = 0; i < horde.HighWater; i++)
            {
                if (!HordeSystem.IsAlive(horde.States[i])) continue;
                var definition = horde.DefinitionOf(i);
                Assert.That(horde.Health[i], Is.EqualTo(definition.Stats.MaxHp));
                Assert.That(horde.Speeds[i], Is.InRange(definition.Stats.Speed * 0.89f, definition.Stats.Speed * 1.11f));
                if (definition == walker) walkers++;
                else runners++;
            }

            Assert.That(walkers + runners, Is.EqualTo(200));
            Assert.That(runners, Is.GreaterThan(20), "the lighter weight still spawns");
            Assert.That(walkers, Is.GreaterThan(runners * 2), "weights 3 : 1");
        }

        [Test]
        public void NoMonsters_TheBuiltInOne()
        {
            _stress.Monster.MaxHp = 77f;
            var horde = Horde(5);

            Assert.That(horde.KindList.Count, Is.EqualTo(1));
            Assert.That(horde.DefinitionOf(0), Is.Null);
            Assert.That(horde.Health[0], Is.EqualTo(77f));
        }

        [Test]
        public void Replacement_ClimbsOutOfTheGround_ThenWalks()
        {
            var climber = Monster("climber", hp: 10f, speed: 2f, emerge: 1f);
            var horde = Horde(1, new HordeSpawn(climber, 1f));
            Assert.That(horde.States[0], Is.EqualTo(HordeAgentState.Moving), "the first wave stands");

            horde.Damage(0, 100f);
            horde.Tick(Dt);
            var slot = -1;
            for (var i = 0; i < horde.HighWater; i++)
                if (horde.States[i] == HordeAgentState.Emerging) slot = i;
            Assert.That(slot, Is.GreaterThanOrEqualTo(0), "the replacement climbs out");

            var at = horde.Positions[slot];
            Tick(horde, 0.8f);
            Assert.That(horde.States[slot], Is.EqualTo(HordeAgentState.Emerging));
            Assert.That(horde.Positions[slot], Is.EqualTo(at), "does not walk while climbing out");
            Assert.That(HordeSystem.IsAlive(horde.States[slot]), Is.True, "can be hit meanwhile");

            Tick(horde, 0.4f);
            Assert.That(horde.States[slot], Is.EqualTo(HordeAgentState.Moving));
            Tick(horde, 0.5f);
            Assert.That(horde.Positions[slot], Is.Not.EqualTo(at));
        }

        [Test]
        public void Resistance_ShortensOrBlocksStates()
        {
            var stun = ScriptableObject.CreateInstance<StatusDefinition>();
            stun.Configure("stun", new StunStatus());
            _created.Add(stun);
            var immune = Monster("immune", 50f, 2f);
            immune.ConfigureEffects(new[] { new StatusResistance(stun, 0f) });
            var tough = Monster("tough", 50f, 2f);
            tough.ConfigureEffects(new[] { new StatusResistance(stun, 0.5f) });
            var horde = Horde(40, new HordeSpawn(immune, 1f), new HordeSpawn(tough, 1f));
            var targets = new CombatTargets(new SpatialQueryService(_grid, _doors), horde);
            var statuses = new StatusSystem(horde, targets);
            statuses.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

            int checkedImmune = 0, checkedTough = 0;
            for (var i = 0; i < horde.HighWater; i++)
            {
                statuses.Apply(CombatTarget.OfAgent(i), stun, 2f);
                if (horde.DefinitionOf(i) == immune)
                {
                    Assert.That(statuses.Left(i, stun), Is.EqualTo(0f));
                    checkedImmune++;
                }
                else
                {
                    Assert.That(statuses.Left(i, stun), Is.EqualTo(1f).Within(1e-4f));
                    checkedTough++;
                }
            }

            Assert.That(checkedImmune, Is.GreaterThan(0));
            Assert.That(checkedTough, Is.GreaterThan(0));
        }

        [Test]
        public void DeathEffect_HappensWhereItDied_NextTick()
        {
            var bomber = Monster("bomber", hp: 10f, speed: 0.1f);
            var explosion = new ExplosionEffect { Damage = 100f, EdgeDamage = 1f };
            explosion.SetRadius(30f);
            bomber.ConfigureEffects(null, explosion);
            var horde = Horde(10, new HordeSpawn(bomber, 1f));
            horde.Respawn = false;
            var targets = new CombatTargets(new SpatialQueryService(_grid, _doors), horde);
            var statuses = new StatusSystem(horde, targets);
            var zones = new ZoneSystem(_grid, _doors, targets, horde, statuses, _player, _health);
            var effects = new EffectSystem(targets, zones, statuses, new SoundEventBus());
            var deaths = new MonsterDeathEffects(horde, effects, _player);
            deaths.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            horde.Tick(Dt); // buckets for the blast's query

            horde.Damage(0, 100f);
            Assert.That(deaths.Pending, Is.EqualTo(1), "waits for the tick");
            Assert.That(horde.AliveCount, Is.EqualTo(9));

            deaths.Tick(Dt);
            Assert.That(horde.AliveCount, Is.EqualTo(0), "the blast killed the rest");
            Assert.That(deaths.Pending, Is.EqualTo(9), "their own blasts wait for the next tick");
            deaths.Tick(Dt);
            Assert.That(deaths.Pending, Is.EqualTo(0));
            deaths.Dispose();
        }
    }
}
