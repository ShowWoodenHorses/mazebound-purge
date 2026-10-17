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
    /// <summary>Stage 5c: abilities (warning, projectiles, strike, breaking), ranged tactics, states on the player.</summary>
    public class MonsterAbilitiesTests
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
        private MonsterDefinition _monster;
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

        /// <summary>40×21 open floor, a wall x = 3..8 at y = 13 (north of the player at (5, 10)).</summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(40, 21);
            for (var y = 1; y < 20; y++)
            for (var x = 1; x < 39; x++)
                geometry.SetCell(new GridPosition(x, y), y == 13 && x >= 3 && x <= 8 ? CellType.Wall : CellType.Floor);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(5, 10)));

            _stress = ScriptableObject.CreateInstance<StressTestDefinition>();
            _created.Add(_stress);
            _level.StressTest = _stress;
            _monster = ScriptableObject.CreateInstance<MonsterDefinition>();
            _monster.Configure("shooter", MonsterRank.Officer, MonsterBiome.Common,
                new MonsterStats { MaxHp = 100f, Speed = 3f, AttackReach = 0.8f, Damage = 5 });
            _monster.SetPerception(MonsterPerception.Hunter);
            _created.Add(_monster);
            _stress.ConfigureSandbox(true, 0, 0);
            _stress.ConfigureMonsters(new[] { new HordeSpawn(_monster, 1f) });

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

        private void Build()
        {
            _horde = new HordeSystem(_level, _grid, _passability, _doors, _player, _health, new SoundEventBus());
            _horde.GodMode = false;
            _targets = new CombatTargets(new SpatialQueryService(_grid, _doors), _horde);
            _statuses = new StatusSystem(_horde, _targets);
            _zones = new ZoneSystem(_grid, _doors, _targets, _horde, _statuses, _player, _health);
            _effects = new EffectSystem(_targets, _zones, _statuses, new SoundEventBus());
            _playerStatuses = new PlayerStatusSystem(_player, _health, _zones, _horde);
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

        private int Spawn(float x, float y)
        {
            var agent = _horde.SpawnAt(new Vector2(x, y), MonsterPerception.Hunter);
            Assert.That(agent, Is.GreaterThanOrEqualTo(0));
            return agent;
        }

        private static ProjectileAbility Shot(ProjectileForm form = ProjectileForm.Straight) => new ProjectileAbility
        {
            Form = form, FirstDelay = 0f, Warning = 0.5f, Recovery = 0.2f, Cooldown = 10f, MinRange = 0f, MaxRange = 12f,
            Speed = 20f, Damage = 10f, Range = 14f,
        };

        private ZoneDefinition AcidZone(out StatusDefinition acid)
        {
            acid = ScriptableObject.CreateInstance<StatusDefinition>();
            acid.Configure("acid", new DamageOverTimeStatus { DamagePerSecond = 20f });
            _created.Add(acid);
            var zone = ScriptableObject.CreateInstance<ZoneDefinition>();
            zone.Configure("acid_zone", new ZoneStatus { Status = acid, Duration = 1f });
            _created.Add(zone);
            return zone;
        }

        [Test]
        public void StraightShot_WarnsWhileStanding_ThenHitsThePlayer_FromItsSide()
        {
            _monster.SetAbilities(Shot());
            Build();
            var shooter = Spawn(12f, 10f);
            var hurtFrom = Vector2.zero;
            _health.Hurt += (amount, source) => hurtFrom = source;

            Tick(0.3f);
            Assert.That(_abilities.Phases[shooter], Is.EqualTo(AbilityPhase.Warning));
            Assert.That(_horde.Casting[shooter], Is.True, "stands while it warns");
            Assert.That(_health.Current, Is.EqualTo(_health.Max), "nothing yet: only the warning");

            Tick(0.8f);
            Assert.That(_abilities.Casts, Is.EqualTo(1));
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 10));
            Assert.That(hurtFrom.x, Is.GreaterThan(PlayerAt.x), "the blow came from the east");
            Assert.That(_horde.Casting[shooter], Is.False, "back to the chase after the recovery");
        }

        [Test]
        public void NoCast_WithoutLineOfSight()
        {
            _monster.SetAbilities(Shot());
            Build();
            Spawn(5f, 16f); // behind the wall north of the player
            Tick(1f);
            Assert.That(_abilities.Casts, Is.Zero);
            Assert.That(_health.Current, Is.EqualTo(_health.Max));
        }

        [Test]
        public void Fan_ShootsItsCount()
        {
            var fan = Shot(ProjectileForm.Fan);
            fan.Count = 3;
            fan.Speed = 2f; // slow: still flying when counted
            _monster.SetAbilities(fan);
            Build();
            Spawn(14f, 10f);
            Tick(0.6f);
            Assert.That(_abilities.ProjectileCount, Is.EqualTo(3));
        }

        [Test]
        public void Wall_StopsAStraightShot()
        {
            var shot = Shot();
            shot.NeedsSight = false;
            _monster.SetAbilities(shot);
            Build();
            Spawn(5f, 16f);
            Tick(1.5f);
            Assert.That(_abilities.Casts, Is.EqualTo(1));
            Assert.That(_health.Current, Is.EqualTo(_health.Max), "the wall took it");
            Assert.That(_abilities.ProjectileCount, Is.Zero);
        }

        [Test]
        public void Arc_LandsWherePlayerStood_LeavesAHostileZoneThatEatsThePlayer()
        {
            var spit = Shot(ProjectileForm.Arc);
            spit.Damage = 0f;
            spit.SetImpact(new PlaceZoneEffect { Zone = AcidZone(out var acid), Duration = 3f });
            _monster.SetAbilities(spit);
            Build();
            Spawn(12f, 10f);

            Tick(1.2f);
            Assert.That(_abilities.Casts, Is.EqualTo(1));
            Assert.That(_zones.IsHostileAt(_grid.ToIndex(_player.Cell)), Is.True, "a monsters' puddle under the player");
            Tick(0.6f);
            Assert.That(_playerStatuses.Left(acid), Is.GreaterThan(0f));
            Assert.That(_health.Current, Is.LessThan(_health.Max), "acid eats away");
        }

        [Test]
        public void Stun_BreaksTheCast()
        {
            _monster.SetAbilities(Shot());
            Build();
            var shooter = Spawn(12f, 10f);
            var stun = ScriptableObject.CreateInstance<StatusDefinition>();
            stun.Configure("stun", new StunStatus());
            _created.Add(stun);

            Tick(0.2f);
            Assert.That(_abilities.Phases[shooter], Is.EqualTo(AbilityPhase.Warning));
            _statuses.Apply(CombatTarget.OfAgent(shooter), stun, 2f);
            Tick(0.6f);
            Assert.That(_abilities.Phases[shooter], Is.EqualTo(AbilityPhase.None));
            Assert.That(_abilities.Casts, Is.Zero);
            Assert.That(_health.Current, Is.EqualTo(_health.Max));
        }

        [Test]
        public void Strike_HitsThePlayerInFront_NotOneOutOfReach()
        {
            _monster.SetAbilities(new StrikeAbility
            {
                FirstDelay = 0f, Warning = 0.4f, Cooldown = 10f, MinRange = 0f, MaxRange = 2.5f, Reach = 2f, Angle = 90f, Damage = 20f,
            });
            Build();
            Spawn(6.6f, 10f);
            Tick(0.8f);
            Assert.That(_abilities.Casts, Is.EqualTo(1));
            Assert.That(_health.Current, Is.EqualTo(_health.Max - 20));
        }

        [Test]
        public void GodMode_SparesThePlayer()
        {
            _monster.SetAbilities(Shot());
            Build();
            _horde.GodMode = true;
            Spawn(12f, 10f);
            Tick(1.2f);
            Assert.That(_abilities.Casts, Is.EqualTo(1));
            Assert.That(_health.Current, Is.EqualTo(_health.Max));
        }

        [Test]
        public void Ranged_StopsAtItsHoldRange_AndStepsBackFromAClosePlayer()
        {
            _monster.Stats.HoldRange = 7f;
            _monster.Stats.RetreatRange = 3f;
            Build();
            var far = Spawn(20f, 10f);
            var near = Spawn(7f, 10f);
            Tick(4f);

            var farDistance = (_horde.Positions[far] - _player.Position).magnitude;
            var nearDistance = (_horde.Positions[near] - _player.Position).magnitude;
            Assert.That(farDistance, Is.InRange(5.5f, 7.2f), "came within its range and stood");
            Assert.That(nearDistance, Is.GreaterThanOrEqualTo(2.9f), "stepped back");
        }

        [Test]
        public void Ranged_CorneredAgainstAWall_FightsInMelee()
        {
            _monster.Stats.HoldRange = 7f;
            _monster.Stats.RetreatRange = 3f;
            Build();
            var cornered = Spawn(5f, 12f); // the wall at y = 13 behind it
            Tick(3f);
            Assert.That(_horde.States[cornered], Is.EqualTo(HordeAgentState.Attacking));
        }

        [Test]
        public void OnDemandKind_NeverInTheWaves_ButSpawnsWhenAsked()
        {
            var officer = ScriptableObject.CreateInstance<MonsterDefinition>();
            officer.Configure("officer", MonsterRank.Officer, MonsterBiome.Common, new MonsterStats());
            _created.Add(officer);
            _stress.SetOnDemandMonsters(new[] { officer });
            _stress.ConfigureSandbox(true, 0, 20);
            Build();

            var kind = _horde.KindIndexOf(officer);
            Assert.That(kind, Is.GreaterThan(0));
            for (var i = 0; i < _horde.HighWater; i++)
                Assert.That(_horde.Kinds[i], Is.Not.EqualTo(kind), "weight 0: not in the waves");
            var slot = _horde.SpawnKind(new Vector2(20f, 10f), kind);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            Assert.That(_horde.DefinitionOf(slot), Is.SameAs(officer));
        }

        [Test]
        public void PlayerStatuses_SlowAndStun_ThenImmunityToControl()
        {
            Build();
            var slow = ScriptableObject.CreateInstance<StatusDefinition>();
            slow.Configure("slow", new SlowStatus { SpeedFactor = 0.5f });
            var stun = ScriptableObject.CreateInstance<StatusDefinition>();
            stun.Configure("stun", new StunStatus());
            _created.Add(slow);
            _created.Add(stun);

            Assert.That(_playerStatuses.Apply(slow, 1f), Is.True);
            Assert.That(_player.StatusSpeed, Is.EqualTo(0.5f).Within(1e-4f));

            Assert.That(_playerStatuses.Apply(stun, 0.5f), Is.True);
            Assert.That(_player.Stunned, Is.True);
            Assert.That(_playerStatuses.Apply(stun, 2f), Is.False, "a control state is not renewed while it holds");

            Tick(0.6f);
            Assert.That(_player.Stunned, Is.False);
            Assert.That(_playerStatuses.IsImmune, Is.True);
            Assert.That(_playerStatuses.Apply(stun, 1f), Is.False, "immune for a while after it");
            Assert.That(_playerStatuses.Apply(slow, 1f), Is.True, "a slow is not control");

            Tick(PlayerStatusSystem.ControlImmunity + 0.1f);
            Assert.That(_playerStatuses.Apply(stun, 0.5f), Is.True);
        }
    }
}
