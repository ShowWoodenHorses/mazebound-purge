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
    /// <summary>Stage 8b: the hook (pull), the summon, the gaze (stone).</summary>
    public class HookTests
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
        private HordeDefinition _hordeData;
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
            _hordeData = ScriptableObject.CreateInstance<HordeDefinition>();
            _created.Add(_hordeData);
            _level.Horde = _hordeData;
            _elite = Monster("elite", MonsterRank.Elite, new MonsterStats { MaxHp = 200f, Speed = 2f, Radius = 0.4f, Damage = 0 });
            _mass = Monster("walker", MonsterRank.Mass, new MonsterStats { MaxHp = 30f, Speed = 2f, Damage = 0 });
            _officer = Monster("officer", MonsterRank.Officer, new MonsterStats { MaxHp = 60f, Speed = 2f, Damage = 0 });
            _stress.ConfigureSandbox(true, 0, 0);
            _hordeData.ConfigureMonsters(new[] { new HordeSpawn(_elite, 1f), new HordeSpawn(_mass, 1f), new HordeSpawn(_officer, 1f) });

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

        // ---- Pull ----

        private PullAbility Pull() => new PullAbility
        {
            FirstDelay = 0f, Cooldown = 30f, MinRange = 0f, MaxRange = 20f, NeedsSight = true, Warning = 0.6f, Recovery = 0.1f,
            Speed = 16f, Range = 9f, Damage = 6f, PullTime = 0.3f,
        };

        [Test]
        public void Pull_HookCatchesThePlayer_DragsThemToTheMonster_ThenImmunity()
        {
            _elite.SetAbilities(Pull());
            Build();
            var hook = Spawn(_elite, 12f, 10f);
            Tick(0.3f);
            Assert.That(_abilities.Phases[hook], Is.EqualTo(AbilityPhase.Warning));
            Assert.That(_abilities.ChargeLength(hook), Is.EqualTo(9f).Within(0.01f), "the line as far as the head flies");

            Tick(0.8f); // released at 0.6 s, the head flies ~6.3 cells in ~0.4 s
            Assert.That(_abilities.Pulls, Is.EqualTo(1));
            Assert.That(_abilities.HookOf(hook), Is.EqualTo(HookStage.Pull));
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 6));
            Assert.That(_playerStatuses.IsControlled, Is.True, "held while dragged");

            Tick(0.5f);
            var edge = 12f - (0.4f + MonsterAbilities.PlayerRadius + 0.15f);
            Assert.That(_player.Position.x, Is.EqualTo(edge).Within(0.2f), "at the monster's body");
            Assert.That(_playerStatuses.IsControlled, Is.False);
            Assert.That(_playerStatuses.IsImmune, Is.True, "immunity to control after the pull");

            Tick(1f);
            Assert.That(_abilities.HookOf(hook), Is.EqualTo(HookStage.None), "drawn back");
            Assert.That(_abilities.Phases[hook], Is.Not.EqualTo(AbilityPhase.Active));
        }

        [Test]
        public void Pull_ImmunePlayer_IsOnlyHurt_AndTheHookComesBack()
        {
            _elite.SetAbilities(Pull());
            Build();
            var stun = Stun("stun");
            _playerStatuses.Apply(stun, 0.05f);
            Tick(0.1f);
            Assert.That(_playerStatuses.IsImmune, Is.True);

            var hook = Spawn(_elite, 12f, 10f);
            Tick(1.3f);
            Assert.That(_abilities.Pulls, Is.Zero);
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 6), "hurt all the same");
            Assert.That(_player.Position.x, Is.EqualTo(PlayerAt.x).Within(0.01f), "not dragged");
            Tick(1f);
            Assert.That(_abilities.HookOf(hook), Is.EqualTo(HookStage.None));
        }

        [Test]
        public void Pull_OutOfRange_TheHeadComesBackEmpty()
        {
            var pull = Pull();
            pull.Range = 4f;
            pull.Speed = 8f;
            _elite.SetAbilities(pull);
            Build();
            var hook = Spawn(_elite, 12f, 10f);
            Tick(0.65f);
            Assert.That(_abilities.HookOf(hook), Is.EqualTo(HookStage.Out));
            Tick(0.5f);
            Assert.That(_abilities.HookOf(hook), Is.EqualTo(HookStage.Back), "4 cells out, nothing caught");
            Assert.That((_abilities.HookHead(hook) - new Vector2(12f, 10f)).magnitude, Is.LessThan(4.05f));
            Tick(1f);
            Assert.That(_abilities.Pulls, Is.Zero);
            Assert.That(_health.Current, Is.EqualTo(_health.Max));
        }

        // ---- Summon ----

        private SummonAbility Summon(int count, int limit) => new SummonAbility
        {
            FirstDelay = 0f, Cooldown = 0.5f, MinRange = 0f, MaxRange = 30f, NeedsSight = false, Warning = 0.3f, Recovery = 0.1f,
            Monster = _mass, Count = count, Limit = limit, Radius = 3f,
        };

        [Test]
        public void Summon_CallsTheKindAroundItself_NoMoreThanItsLimitAlive()
        {
            _mass.Stats.EmergeTime = 1f;
            _elite.SetAbilities(Summon(4, 6));
            Build();
            _horde.Respawn = false;
            var boss = Spawn(_elite, 25f, 10f);
            Tick(0.1f);
            Assert.That(_abilities.SummonCount(boss), Is.EqualTo(4), "the spots are chosen when the cast begins");
            Tick(0.35f);
            Assert.That(_abilities.Summoned, Is.EqualTo(4));
            for (var i = 0; i < _horde.HighWater; i++)
            {
                if (i == boss || !HordeSystem.IsAlive(_horde.States[i])) continue;
                Assert.That(_horde.DefinitionOf(i), Is.SameAs(_mass));
                Assert.That(_horde.States[i], Is.EqualTo(HordeAgentState.Emerging));
                Assert.That((_horde.Positions[i] - new Vector2(25f, 10f)).magnitude, Is.LessThanOrEqualTo(3.01f));
                Assert.That(_abilities.SummonerOf(i), Is.EqualTo(boss));
            }

            Tick(1f);
            Assert.That(_abilities.Summoned, Is.EqualTo(6), "only 2 more: the limit is 6 alive");
            Assert.That(_abilities.SummonedAlive(boss), Is.EqualTo(6));
            Tick(1f);
            Assert.That(_abilities.Summoned, Is.EqualTo(6), "no cast at the limit");

            for (var i = 0; i < _horde.HighWater; i++)
                if (_abilities.SummonerOf(i) == boss)
                {
                    Kill(i);
                    break;
                }

            Tick(1f);
            Assert.That(_abilities.Summoned, Is.EqualTo(7), "room for one");
        }

        /// <summary>Stage 8c (the Mutant): the rage switches to a summon of more a cast and a higher limit.</summary>
        [Test]
        public void Summon_TheNextPhase_CallsMore_UpToItsOwnLimit()
        {
            var phase = new PhaseAbility { HealthBelow = 0.5f, Warning = 0.3f, Recovery = 0.1f, DamageFactor = 1.5f, Rage = true };
            var calm = Summon(2, 4);
            calm.UntilPhase = 0;
            var raging = Summon(4, 8);
            raging.FromPhase = 1;
            _elite.SetAbilities(phase, calm, raging);
            Build();
            _horde.Respawn = false;
            var boss = Spawn(_elite, 25f, 10f);
            Tick(3f);
            Assert.That(_abilities.Summoned, Is.EqualTo(4), "the first set: 2 a cast, at most 4 alive");

            _horde.Damage(boss, 120f);
            Tick(0.6f);
            Assert.That(_abilities.HealthPhaseOf(boss), Is.EqualTo(1));
            Tick(3f);
            Assert.That(_abilities.Summoned, Is.EqualTo(8), "the raging set: 4 a cast, at most 8 alive");
            Assert.That(_abilities.SummonedAlive(boss), Is.EqualTo(8));
        }

        /// <summary>Stage 8c: where a projectile ends, its ability comes with the event (the boulder's crash).</summary>
        [Test]
        public void ArcProjectile_Ends_WithItsAbility()
        {
            var boulder = new ProjectileAbility
            {
                FirstDelay = 0f, Cooldown = 30f, MinRange = 0f, MaxRange = 20f, NeedsSight = true, Warning = 0.3f, Recovery = 0.1f,
                Form = ProjectileForm.Arc, Speed = 10f, Damage = 25f, Range = 14f, ArcRadius = 1.5f, ImpactSound = "boss_smash",
            };
            _elite.SetAbilities(boulder);
            Build();
            ProjectileAbility ended = null;
            var where = Vector2.zero;
            _abilities.ProjectileEnded += (at, kind, ability, hit) =>
            {
                ended = ability;
                where = at;
            };
            Spawn(_elite, 12f, 10f);
            Tick(1.5f);
            Assert.That(ended, Is.SameAs(boulder));
            Assert.That((where - PlayerAt).magnitude, Is.LessThan(0.6f), "lands where the player stood");
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 25));
        }

        [Test]
        public void HordeMonsters_IncludeWhomTheySummon()
        {
            _elite.SetAbilities(Summon(4, 6));
            _hordeData.ConfigureMonsters(new[] { new HordeSpawn(_elite, 1f) });
            Assert.That(_hordeData.AllMonsters(), Does.Contain(_mass));
        }

        // ---- Gaze ----

        private StatusDefinition Stun(string id)
        {
            var stun = ScriptableObject.CreateInstance<StatusDefinition>();
            stun.Configure(id, new StunStatus());
            _created.Add(stun);
            return stun;
        }

        private GazeAbility Gaze() => new GazeAbility
        {
            FirstDelay = 0f, Cooldown = 30f, MinRange = 0f, MaxRange = 20f, NeedsSight = true, Warning = 0.5f, Recovery = 0.1f,
            Reach = 8f, Angle = 60f, Status = Stun("stone"), StatusDuration = 1f,
        };

        [Test]
        public void Gaze_PlayerInTheCone_TurnsToStone_ThenImmunity()
        {
            _elite.SetAbilities(Gaze());
            Build();
            Spawn(_elite, 11f, 10f);
            Tick(0.3f);
            Assert.That(_playerStatuses.IsControlled, Is.False, "the cone is only filling");
            Tick(0.3f);
            Assert.That(_abilities.GazeHits, Is.EqualTo(1));
            Assert.That(_player.Stunned, Is.True, "stone: no step, no shot");
            Tick(1.1f);
            Assert.That(_player.Stunned, Is.False);
            Assert.That(_playerStatuses.IsImmune, Is.True);
        }

        [Test]
        public void Gaze_BeyondItsReach_NothingHappens()
        {
            _elite.SetAbilities(Gaze());
            Build();
            Spawn(_elite, 14f, 10f); // 9 cells: cast, but the cone is 8 long
            Tick(0.8f);
            Assert.That(_abilities.Casts, Is.EqualTo(1));
            Assert.That(_abilities.GazeHits, Is.Zero);
            Assert.That(_playerStatuses.IsControlled, Is.False);
        }
    }
}
