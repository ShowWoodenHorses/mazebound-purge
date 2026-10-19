using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Definitions;
using Purge.Core.Drops;
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
    /// <summary>Stage 5e2 (5д2): champions — the common bonus, the five affixes, the rank-up drop, the waves' chance.</summary>
    public class ChampionTests
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
        private CombatTargets _targets;
        private StressTestDefinition _stress;
        private HordeDefinition _hordeData;
        private MonsterDefinition _mass;
        private MonsterDefinition _officer;
        private MonsterDefinition _elite;
        private HordeSystem _horde;
        private StatusSystem _statuses;
        private ZoneSystem _zones;
        private EffectSystem _effects;
        private PlayerStatusSystem _playerStatuses;
        private MonsterAbilities _abilities;
        private ChampionSystem _champions;

        private ChampionAffixDefinition _fast, _fiery, _armored, _explosive, _vampire;
        private StatusDefinition _burning;
        private ZoneDefinition _fire;
        private DropTable _massDrop, _officerDrop;

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
            _hordeData = ScriptableObject.CreateInstance<HordeDefinition>();
            _created.Add(_hordeData);
            _level.Horde = _hordeData;
            _mass = Monster("walker", MonsterRank.Mass, new MonsterStats { MaxHp = 30f, Speed = 2f, Damage = 0, AttackInterval = 1.2f });
            _officer = Monster("officer", MonsterRank.Officer, new MonsterStats { MaxHp = 60f, Speed = 2f, Damage = 0 });
            _elite = Monster("elite", MonsterRank.Elite, new MonsterStats { MaxHp = 200f, Speed = 2f, Radius = 0.4f, Damage = 0 });
            _stress.ConfigureSandbox(true, 0, 0);
            _hordeData.ConfigureMonsters(new[] { new HordeSpawn(_mass, 1f), new HordeSpawn(_officer, 1f), new HordeSpawn(_elite, 1f) });

            _burning = Create<StatusDefinition>();
            _burning.Configure("burning", new DamageOverTimeStatus { DamagePerSecond = 8f });
            _fire = Create<ZoneDefinition>();
            _fire.Configure("fire", new ZoneDamage { DamagePerSecond = 20f }, new ZoneStatus { Status = _burning, Duration = 2f });
            _fast = Affix("fast", new SpeedAffix { SpeedFactor = 1.5f, AttackRateFactor = 1.3f });
            var resist = new ResistAffix();
            resist.SetResistances(new StatusResistance(_burning, 0f));
            _fiery = Affix("fiery", new HitStatusAffix { Status = _burning, Duration = 3f },
                new TrailAffix { Zone = _fire, Duration = 3f, Strength = 1f }, resist);
            _armored = Affix("armored", new ArmorAffix { DamageTaken = 0.5f });
            var blast = new DeathBlastAffix { Delay = 0.6f };
            var explosion = new ExplosionEffect { Damage = 30f, EdgeDamage = 1f, SoundRadius = 5f };
            explosion.SetRadius(2f);
            blast.SetEffects(explosion);
            _explosive = Affix("explosive", blast);
            _vampire = Affix("vampire", new LifestealAffix { Share = 1f });
            _massDrop = Create<DropTable>();
            _officerDrop = Create<DropTable>();
            _hordeData.Champions.Configure(0f, new[] { _fast, _fiery, _armored, _explosive, _vampire }, 2f, 1.15f, _massDrop, _officerDrop);

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
            _champions?.Dispose();
            _horde?.Dispose();
            _statuses?.Dispose();
            _champions = null;
            _horde = null;
            _statuses = null;
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_playerDefinition);
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private T Create<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            _created.Add(asset);
            return asset;
        }

        private MonsterDefinition Monster(string id, MonsterRank rank, MonsterStats stats)
        {
            var monster = Create<MonsterDefinition>();
            monster.Configure(id, rank, MonsterBiome.Common, stats);
            monster.SetPerception(MonsterPerception.Hunter);
            return monster;
        }

        private ChampionAffixDefinition Affix(string id, params AffixModule[] modules)
        {
            var affix = Create<ChampionAffixDefinition>();
            affix.Configure(id, Color.white, modules);
            return affix;
        }

        private int IndexOf(ChampionAffixDefinition affix) => System.Array.IndexOf(_hordeData.Champions.Affixes, affix);

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
            _champions = new ChampionSystem(_horde, _effects, _zones, _player, _playerStatuses);
            _horde.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _abilities.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _statuses.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _zones.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _champions.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
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
                _champions.Tick(Dt);
            }
        }

        private int Spawn(MonsterDefinition kind, float x, float y)
        {
            var agent = _horde.SpawnAt(new Vector2(x, y), MonsterPerception.Hunter, _horde.KindIndexOf(kind));
            Assert.That(agent, Is.GreaterThanOrEqualTo(0));
            return agent;
        }

        private int Champion(MonsterDefinition kind, ChampionAffixDefinition affix, float x, float y)
        {
            var agent = Spawn(kind, x, y);
            Assert.That(_horde.MakeChampion(agent, IndexOf(affix)), Is.True);
            return agent;
        }

        [Test]
        public void MakeChampion_DoublesHp_OnlyRankAndFileAndOfficers_Once()
        {
            Build();
            var walker = Spawn(_mass, 20f, 10f);
            var speed = _horde.Speeds[walker];
            Assert.That(_horde.MakeChampion(walker, IndexOf(_fast)), Is.True);
            Assert.That(_horde.IsChampion(walker), Is.True);
            Assert.That(_horde.AffixOf(walker), Is.SameAs(_fast));
            Assert.That(_horde.MaxHealthOf(walker), Is.EqualTo(60f).Within(1e-3f));
            Assert.That(_horde.Health[walker], Is.EqualTo(60f).Within(1e-3f));
            Assert.That(_horde.Speeds[walker], Is.EqualTo(speed * 1.5f).Within(1e-3f), "the fast walks ×1.5");
            Assert.That(_horde.AttackIntervalOf(walker), Is.EqualTo(1.2f / 1.3f).Within(1e-3f), "and attacks ×1.3 as often");
            Assert.That(_horde.MakeChampion(walker, IndexOf(_vampire)), Is.False, "one affix");

            var officer = Spawn(_officer, 20f, 14f);
            Assert.That(_horde.MakeChampion(officer, IndexOf(_armored)), Is.True);
            var elite = Spawn(_elite, 20f, 6f);
            Assert.That(_horde.MakeChampion(elite, IndexOf(_armored)), Is.False, "the elite never");
            Assert.That(_horde.ChampionsMade, Is.EqualTo(2));
        }

        [Test]
        public void NewMonsterInTheSlot_IsNoChampion()
        {
            Build();
            var walker = Champion(_mass, _armored, 20f, 10f);
            _horde.Damage(walker, 10000f);
            Tick(4f); // the corpse is gone
            var next = Spawn(_mass, 20f, 10f);
            Assert.That(next, Is.EqualTo(walker));
            Assert.That(_horde.IsChampion(next), Is.False);
            Assert.That(_horde.MaxHealthOf(next), Is.EqualTo(30f).Within(1e-3f));
        }

        [Test]
        public void Armored_HalvesHitsWithADirection_ZonesInFull()
        {
            Build();
            var walker = Champion(_mass, _armored, 20f, 10f);
            var target = CombatTarget.OfAgent(walker);
            _targets.Damage(target, 20f, Vector2.right);
            Assert.That(_horde.Health[walker], Is.EqualTo(50f).Within(1e-3f));
            _targets.Damage(target, 20f, Vector2.left);
            Assert.That(_horde.Health[walker], Is.EqualTo(40f).Within(1e-3f), "from any side");
            _targets.Damage(target, 20f, Vector2.zero);
            Assert.That(_horde.Health[walker], Is.EqualTo(20f).Within(1e-3f), "zones and states go through");
        }

        [Test]
        public void Vampire_HealsWhatItDealsToThePlayer()
        {
            _mass.Stats.Damage = 10;
            Build();
            var walker = Champion(_mass, _vampire, 6f, 10f);
            _horde.Damage(walker, 25f);
            Assert.That(_horde.Health[walker], Is.EqualTo(35f).Within(1e-3f));
            var before = _health.Current;
            Tick(1.5f);
            Assert.That(_health.Current, Is.LessThan(before), "it hit the player");
            Assert.That(_champions.Healed, Is.GreaterThan(0f));
            Assert.That(_horde.Health[walker], Is.GreaterThan(35f));
            Assert.That(_horde.Health[walker], Is.LessThanOrEqualTo(60f), "never past full");
        }

        [Test]
        public void Fiery_SetsThePlayerOnFire_LeavesATrail_ItsOwnFireSparesIt()
        {
            _mass.Stats.Damage = 5;
            Build();
            var walker = Champion(_mass, _fiery, 12f, 10f);
            Tick(4f); // runs to the player and hits
            Assert.That(_champions.TrailCells, Is.GreaterThan(0), "a burning trail");
            Assert.That(_zones.Count, Is.GreaterThan(0));
            Assert.That(_playerStatuses.Left(_burning), Is.GreaterThan(0f), "the hit set the player on fire");
            Assert.That(_horde.Health[walker], Is.EqualTo(60f).Within(1e-3f), "its own fire does not hurt it");
            Assert.That(_statuses.Left(walker, _burning), Is.EqualTo(0f), "it does not burn");

            // Another walker in its trail burns.
            var other = Spawn(_mass, 30f, 10f);
            _zones.Place(new GridPosition(30, 10), _fire, 2f, 1f, hostile: true);
            Tick(0.5f);
            Assert.That(_horde.Health[other], Is.LessThan(30f));
        }

        [Test]
        public void Explosive_GoesOffAfterTheDelay_HurtsThePlayerAndTheMonstersAround()
        {
            Build();
            var walker = Champion(_mass, _explosive, 6.5f, 10f);
            var neighbour = Spawn(_mass, 7.5f, 10f);
            _horde.Damage(walker, 10000f);
            Assert.That(_champions.BlastCount, Is.EqualTo(1), "the warning circle");
            var before = _health.Current;
            Tick(0.3f);
            Assert.That(_champions.Blasts, Is.EqualTo(0), "not yet");
            Assert.That(_champions.BlastProgress(0), Is.InRange(0.3f, 0.7f));
            Tick(0.4f);
            Assert.That(_champions.Blasts, Is.EqualTo(1));
            Assert.That(_champions.BlastCount, Is.EqualTo(0));
            Assert.That(_health.Current, Is.LessThan(before), "the player in reach");
            Assert.That(HordeSystem.IsAlive(_horde.States[neighbour]), Is.False, "30 kills a walker next to it");
        }

        [Test]
        public void Explosive_GodSparesThePlayer()
        {
            Build();
            _horde.GodMode = true;
            var walker = Champion(_mass, _explosive, 6f, 10f);
            _horde.Damage(walker, 10000f);
            var before = _health.Current;
            Tick(1f);
            Assert.That(_champions.Blasts, Is.EqualTo(1));
            Assert.That(_health.Current, Is.EqualTo(before));
        }

        [Test]
        public void Drop_ARankHigher()
        {
            var rules = _hordeData.Champions;
            Assert.That(rules.DropOf(MonsterRank.Mass), Is.SameAs(_massDrop));
            Assert.That(rules.DropOf(MonsterRank.Officer), Is.SameAs(_officerDrop));
            Assert.That(rules.DropOf(MonsterRank.Elite), Is.Null);
            Assert.That(ChampionRules.CanBe(MonsterRank.Elite), Is.False);
            Assert.That(ChampionRules.CanBe(MonsterRank.Boss), Is.False);
        }

        [Test]
        public void Waves_MakeChampionsByChance()
        {
            _hordeData.ConfigureMonsters(new[] { new HordeSpawn(_mass, 1f) });
            _stress.ConfigureSandbox(true, 0, 20);
            _hordeData.Champions.Configure(1f, new[] { _fast, _armored }, 2f, 1.15f, _massDrop, _officerDrop);
            Build();
            Assert.That(_horde.AliveCount, Is.EqualTo(20));
            Assert.That(_horde.ChampionsMade, Is.EqualTo(20), "chance 1: every one");

            TearDown();
            SetUp();
            _hordeData.ConfigureMonsters(new[] { new HordeSpawn(_mass, 1f) });
            _stress.ConfigureSandbox(true, 0, 20);
            Build();
            Assert.That(_horde.ChampionsMade, Is.EqualTo(0), "chance 0: none");
        }
    }
}
