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
    /// <summary>Stage 5e (5д1): front armor, raising the fallen, the area strike, rage, hostile effects on the player.</summary>
    public class EliteTests
    {
        private const float Dt = 1f / 60f;
        private static readonly Vector2 PlayerAt = new Vector2(5f, 10f);

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private LevelGrid _grid;
        private PlayerDefinition _playerDefinition;
        private DoorSystem _doors;
        private LevelPassability _passability;
        private PlayerSystem _player;
        private PlayerHealth _health;
        private CombatTargets _targets;
        private StressTestDefinition _stress;
        private MonsterDefinition _elite;
        private MonsterDefinition _mass;
        private MonsterDefinition _officer;
        private HordeSystem _horde;
        private StatusSystem _statuses;
        private ZoneSystem _zones;
        private EffectSystem _effects;
        private PlayerStatusSystem _playerStatuses;
        private MonsterAbilities _abilities;

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        /// <summary>40×21 open floor, the player at (5, 10).</summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(40, 21);
            for (var y = 1; y < 20; y++)
            for (var x = 1; x < 39; x++)
                geometry.SetCell(new GridPosition(x, y), CellType.Floor);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(5, 10)));

            _stress = ScriptableObject.CreateInstance<StressTestDefinition>();
            _created.Add(_stress);
            _level.StressTest = _stress;
            _elite = Monster("elite", MonsterRank.Elite, new MonsterStats { MaxHp = 200f, Speed = 2f, Radius = 0.4f, Damage = 0 });
            _mass = Monster("walker", MonsterRank.Mass, new MonsterStats { MaxHp = 30f, Speed = 2f, Damage = 0 });
            _officer = Monster("officer", MonsterRank.Officer, new MonsterStats { MaxHp = 60f, Speed = 2f, Damage = 0 });
            _stress.ConfigureSandbox(true, 0, 0);
            _stress.ConfigureMonsters(new[] { new HordeSpawn(_elite, 1f), new HordeSpawn(_mass, 1f), new HordeSpawn(_officer, 1f) });

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
            _horde?.Dispose();
            _statuses?.Dispose();
            _horde = null;
            _statuses = null;
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_playerDefinition);
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private MonsterDefinition Monster(string id, MonsterRank rank, MonsterStats stats)
        {
            var monster = ScriptableObject.CreateInstance<MonsterDefinition>();
            monster.Configure(id, rank, MonsterBiome.Common, stats);
            monster.SetPerception(MonsterPerception.Hunter);
            _created.Add(monster);
            return monster;
        }

        private void Build()
        {
            _horde = new HordeSystem(_level, _grid, _passability, _doors, _player, _health, new SoundEventBus());
            _horde.GodMode = false;
            _targets = new CombatTargets(new SpatialQueryService(_grid, _doors), _horde);
            _statuses = new StatusSystem(_horde, _targets);
            _zones = new ZoneSystem(_grid, _doors, _targets, _horde, _statuses, _player, _health);
            _playerStatuses = new PlayerStatusSystem(_player, _health, _zones, _horde);
            _effects = new EffectSystem(_targets, _zones, _statuses, new SoundEventBus(), _player, _health, _playerStatuses, _horde);
            _abilities = new MonsterAbilities(_horde, _targets, _player, _health, _playerStatuses, _effects);
            _horde.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _abilities.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _statuses.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _zones.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private void Tick(float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt)
            {
                _playerStatuses.Tick(Dt);
                _player.Tick(Dt);
                _horde.Tick(Dt);
                _abilities.Tick(Dt);
                _statuses.Tick(Dt);
                _zones.Tick(Dt);
            }
        }

        private int Spawn(MonsterDefinition kind, float x, float y)
        {
            var agent = _horde.SpawnAt(new Vector2(x, y), MonsterPerception.Hunter, _horde.KindIndexOf(kind));
            Assert.That(agent, Is.GreaterThanOrEqualTo(0));
            return agent;
        }

        private void Kill(int agent) => _horde.Damage(agent, 10000f);

        // ---- Armor ----

        [Test]
        public void ArmorFactor_HalfFromTheFront_FullFromBehindAndWithoutDirection()
        {
            var stats = new MonsterStats { FrontArmor = 0.5f, FrontArc = 120f };
            // Facing North (0): a bullet flying South hits its face.
            Assert.That(stats.ArmorFactor(0f, Vector2.down), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(stats.ArmorFactor(0f, Vector2.up), Is.EqualTo(1f), "from behind");
            Assert.That(stats.ArmorFactor(0f, Vector2.left), Is.EqualTo(1f), "from the side, outside 120°");
            Assert.That(stats.ArmorFactor(0f, new Vector2(0.4f, -1f).normalized), Is.EqualTo(0.5f).Within(1e-4f), "within 60° of the front");
            Assert.That(stats.ArmorFactor(0f, Vector2.zero), Is.EqualTo(1f), "zones and burning have no direction");
            Assert.That(new MonsterStats().ArmorFactor(0f, Vector2.down), Is.EqualTo(1f), "no armor by default");
        }

        [Test]
        public void Armadillo_TakesHalfFromTheFront_ThroughCombatTargets()
        {
            _elite.Stats.FrontArmor = 0.5f;
            Build();
            var armadillo = Spawn(_elite, 12f, 10f);
            Tick(0.5f); // turns to the player (West)
            var target = CombatTarget.OfAgent(armadillo);
            _targets.Damage(target, 40f, Vector2.right); // flies East — into its face
            Assert.That(_horde.Health[armadillo], Is.EqualTo(180f).Within(0.01f));
            Assert.That(_targets.ArmoredHits, Is.EqualTo(1));
            _targets.Damage(target, 40f, Vector2.left); // from behind
            Assert.That(_horde.Health[armadillo], Is.EqualTo(140f).Within(0.01f));
            _targets.Damage(target, 40f, Vector2.zero); // a zone
            Assert.That(_horde.Health[armadillo], Is.EqualTo(100f).Within(0.01f));
        }

        // ---- Raise ----

        private RaiseAbility Raise() => new RaiseAbility
        {
            FirstDelay = 0f, Cooldown = 30f, MinRange = 0f, MaxRange = 30f, NeedsSight = false, Warning = 0.5f, Recovery = 0.1f,
            Radius = 6f, Count = RaiseAbility.MaxCount, MaxAge = 20f, RiseTime = 1f,
        };

        [Test]
        public void Raise_NoCastWithoutTheFallen()
        {
            _elite.SetAbilities(Raise());
            Build();
            Spawn(_elite, 20f, 10f);
            Tick(1f);
            Assert.That(_abilities.Casts, Is.Zero);
        }

        [Test]
        public void Raise_LiftsAtMostFive_TheSameKinds_WhereTheyFell_NotTheElite()
        {
            _elite.SetAbilities(Raise());
            Build();
            _horde.Respawn = false;
            // Six rank-and-file, an officer and an elite die near where the Shaman will stand.
            var fallen = new List<Vector2>();
            for (var n = 0; n < 6; n++)
            {
                var at = new Vector2(24f + n * 0.8f, 12f);
                Kill(Spawn(_mass, at.x, at.y));
                fallen.Add(at);
            }

            Kill(Spawn(_officer, 25f, 8f));
            Kill(Spawn(_elite, 26f, 8f));
            Tick(5f); // the corpses are gone; the horde still remembers them

            var shaman = Spawn(_elite, 25f, 10f);
            Tick(0.1f);
            Assert.That(_abilities.Phases[shaman], Is.EqualTo(AbilityPhase.Warning));
            Assert.That(_abilities.RaiseCount(shaman), Is.EqualTo(RaiseAbility.MaxCount), "at most five a cast");

            Tick(0.6f);
            Assert.That(_horde.Raised, Is.EqualTo(RaiseAbility.MaxCount));
            var emerging = 0;
            var kinds = new HashSet<MonsterDefinition>();
            for (var i = 0; i < _horde.HighWater; i++)
            {
                if (_horde.States[i] != HordeAgentState.Emerging) continue;
                emerging++;
                kinds.Add(_horde.DefinitionOf(i));
                Assert.That(_horde.EmergeTimeOf(i), Is.EqualTo(1f));
            }

            Assert.That(emerging, Is.EqualTo(RaiseAbility.MaxCount));
            Assert.That(kinds.Contains(_elite), Is.False, "the elite is not raised");

            // Climbed out: they chase the player.
            Tick(1.2f);
            for (var i = 0; i < _horde.HighWater; i++)
                if (i != shaman && HordeSystem.IsAlive(_horde.States[i]))
                    Assert.That(_horde.Perception.AwarenessOf(i), Is.EqualTo(HordeAwareness.Chase));
        }

        [Test]
        public void Raise_ForgetsTheOld_AndDoesNotRaiseTwice()
        {
            Build();
            _horde.Respawn = false;
            Kill(Spawn(_mass, 20f, 10f));
            var found = new int[5];
            Assert.That(_horde.FindFallen(new Vector2(20f, 10f), 3f, 20f, found), Is.EqualTo(1));
            Assert.That(_horde.Raise(found[0], 1f), Is.GreaterThanOrEqualTo(0));
            Assert.That(_horde.Raise(found[0], 1f), Is.EqualTo(-1), "already raised");
            Assert.That(_horde.FindFallen(new Vector2(20f, 10f), 3f, 20f, found), Is.Zero);

            Kill(Spawn(_mass, 30f, 10f));
            Tick(2f);
            Assert.That(_horde.FindFallen(new Vector2(30f, 10f), 3f, 1f, found), Is.Zero, "older than the max age");
            Assert.That(_horde.FindFallen(new Vector2(30f, 10f), 3f, 20f, found), Is.EqualTo(1));
        }

        // ---- Area strike ----

        private AreaStrikeAbility Area() => new AreaStrikeAbility
        {
            FirstDelay = 0f, Cooldown = 30f, MinRange = 0f, MaxRange = 20f, NeedsSight = true, Warning = 1f, Recovery = 0.1f,
            Radius = 1.5f, Damage = 12f,
        };

        [Test]
        public void AreaStrike_HitsThePlayerStillInTheCircle()
        {
            _elite.SetAbilities(Area());
            Build();
            var shaman = Spawn(_elite, 12f, 10f);
            Tick(0.3f);
            Assert.That(_abilities.Phases[shaman], Is.EqualTo(AbilityPhase.Warning));
            Assert.That((_abilities.Aims[shaman] - PlayerAt).magnitude, Is.LessThan(0.1f), "aimed where the player stands");
            Assert.That(_health.Current, Is.EqualTo(_health.Max));
            Tick(0.9f);
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 12));
        }

        [Test]
        public void AreaStrike_HostileExplosion_HurtsThePlayer_GodSparesThem()
        {
            var area = Area();
            area.Damage = 0f;
            var blast = new ExplosionEffect { Damage = 20f, EdgeDamage = 1f };
            blast.SetRadius(1.5f);
            area.SetImpact(blast);
            _elite.SetAbilities(area);
            Build();
            Spawn(_elite, 12f, 10f);
            Tick(1.3f);
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 20), "the fireball's blast");

            _horde.GodMode = true;
            _effects.Apply(new EffectModule[] { blast }, PlayerAt, 0, hostile: true);
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 20), "God mode");
        }

        [Test]
        public void HostileStatusEffect_GoesOnThePlayer_NotOnTheMonsters()
        {
            Build();
            var stun = ScriptableObject.CreateInstance<StatusDefinition>();
            stun.Configure("freeze", new StunStatus());
            _created.Add(stun);
            var monster = Spawn(_mass, 6f, 10f);
            var freeze = new AreaStatusEffect { Status = stun, Duration = 1f };
            freeze.SetRadius(2f);
            _effects.Apply(new EffectModule[] { freeze }, PlayerAt, 0, hostile: true);
            Tick(Dt);
            Assert.That(_playerStatuses.IsControlled, Is.True);
            Assert.That(_horde.Stunned[monster], Is.False);
        }

        // ---- Rage ----

        [Test]
        public void Rage_AtThirtyPercent_RoarsOnce_ThenHitsHarder()
        {
            _elite.Stats.Damage = 10;
            _elite.Stats.AttackReach = 1f;
            _elite.Stats.AttackInterval = 0.5f;
            _elite.SetAbilities(new RageAbility { Warning = 0.5f, DamageFactor = 2f });
            Build();
            var brute = Spawn(_elite, 5.8f, 10f);
            Tick(0.1f);
            Assert.That(_abilities.Casts, Is.Zero, "full HP: no rage");

            _horde.Damage(brute, 150f); // 50 of 200 left = 25 %
            Tick(0.1f);
            Assert.That(_abilities.Phases[brute], Is.EqualTo(AbilityPhase.Warning), "roars");
            Tick(0.6f);
            Assert.That(_horde.IsEnraged(brute), Is.True);
            Assert.That(_horde.DamageFactorOf(brute), Is.EqualTo(2f));

            var before = _health.Current;
            Tick(0.55f);
            Assert.That(before - _health.Current, Is.EqualTo(20), "an attack doubled");
            Tick(3f);
            Assert.That(_abilities.Casts, Is.EqualTo(1), "once per life");
        }
    }
}
