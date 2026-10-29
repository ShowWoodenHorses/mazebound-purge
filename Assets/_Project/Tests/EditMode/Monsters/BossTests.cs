using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Navigation;
using Purge.Core.Waves;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Run;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Waves;
using Purge.Gameplay.Weapons;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Monsters
{
    /// <summary>
    /// Stage 8a: the boss's frame — a wide body and its own path field, a boss wave (the boss in the arena's centre, its
    /// escort from the points nearest the arena, the budget waits for its death), the arena's gates, the slow-down of its
    /// death, a phase by HP, control × 0.25 and no stagger.
    /// </summary>
    public class BossTests
    {
        private const float Dt = 1f / 30f;

        /// <summary>The arena's open cells (12×12) with a ring 2 deep; one entry, 4 wide, on its south side.</summary>
        private static readonly GridRect ArenaRect = new GridRect(30, 30, 12, 12);

        private static readonly GridPosition Outside = new GridPosition(10, 10);
        private static readonly GridPosition Inside = new GridPosition(33, 33);

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private PlayerDefinition _playerDefinition;
        private MonsterDefinition _boss;
        private MonsterDefinition _mass;
        private MonsterDefinition _escort;
        private WavesClearedRule _rule;
        private PickupSystem _pickups;
        private RunWallet _wallet;
        private HordeSystem _horde;
        private BossSystem _bosses;
        private CellBlockers _blockers;
        private LevelClock _clock;
        private PlayerSystem _player;
        private LevelGrid _grid;
        private DoorSystem _doors;
        private bool _closeArena;

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        [SetUp]
        public void SetUp()
        {
            _closeArena = true;
            _playerDefinition = Create<PlayerDefinition>();
            _playerDefinition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _boss = Monster("boss", MonsterRank.Boss, new MonsterStats { MaxHp = 100f, Speed = 2f, Radius = 1f, AttackReach = 2f, Damage = 0, EmergeTime = 0.5f });
            _mass = Monster("mass", MonsterRank.Mass, new MonsterStats { MaxHp = 30f, Speed = 2f, Damage = 0, EmergeTime = 0.2f });
            _escort = Monster("escort", MonsterRank.Mass, new MonsterStats { MaxHp = 30f, Speed = 2f, Damage = 0, EmergeTime = 0.2f });
        }

        [TearDown]
        public void TearDown()
        {
            _rule?.Dispose();
            _wallet?.Dispose();
            _pickups?.Dispose();
            _bosses?.Dispose();
            _horde?.Dispose();
            _rule = null;
            _wallet = null;
            _pickups = null;
            _bosses = null;
            _horde = null;
            if (_level != null) Object.DestroyImmediate(_level);
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

        /// <summary>50×50 open floor with the arena; spawn points: (35, 10) — nearest the arena, (5, 45).</summary>
        private void ArenaLevel(GridPosition start)
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(50, 50);
            for (var y = 1; y < 49; y++)
            for (var x = 1; x < 49; x++)
            {
                var inRing = x >= ArenaRect.X - 2 && x < ArenaRect.XMax + 2 && y >= ArenaRect.Y - 2 && y < ArenaRect.YMax + 2 &&
                             !ArenaRect.Contains(new GridPosition(x, y));
                var entry = y < ArenaRect.Y && x >= 34 && x <= 37;
                geometry.SetCell(new GridPosition(x, y), inRing && !entry ? CellType.Wall : CellType.Floor);
            }

            _level.ReplaceGeometry(geometry);
            _level.SetArena(new ArenaData(ArenaRect, 2));
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", start));
            LevelEditing.AddMonsterSpawn(_level, new GridPosition(35, 10));
            LevelEditing.AddMonsterSpawn(_level, new GridPosition(5, 45));
        }

        private (LevelRuntime Runtime, WaveSystem Waves) Run(WaveScenario scenario)
        {
            var horde = Create<HordeDefinition>();
            horde.ConfigureCapacity(100, 8);
            horde.ConfigurePerception(null, 3, 25f);
            horde.SetWaves(scenario);
            _level.Horde = horde;

            var grid = new LevelGrid(_level.Geometry);
            var doors = new DoorSystem(_level);
            _blockers = new CellBlockers(grid);
            var passability = new LevelPassability(grid, doors, _blockers);
            var player = new PlayerSystem(_level, _playerDefinition, new FakePlayerInput(), passability, new OccupancyMap(grid),
                new LevelLaunchOptions(startIndex: 0), new Aim());
            _player = player;
            var health = new PlayerHealth(_playerDefinition);
            _horde = new HordeSystem(_level, grid, passability, doors, player, health, options: new LevelLaunchOptions(seed: 5));
            _horde.GodMode = true;
            _pickups = new PickupSystem(_level, player, health, new PlayerInventory(), new WeaponSystem(new FakePlayerInput(), _playerDefinition),
                new SoundEventBus());
            _wallet = new RunWallet(_horde);
            _clock = new LevelClock();
            _bosses = new BossSystem(_level, _horde, player, _blockers, _clock, options: new LevelLaunchOptions(closeArena: _closeArena));
            var waves = new WaveSystem(_level, _horde, player, new LevelLaunchOptions(seed: 5), _pickups, _wallet, _bosses);
            var runtime = new LevelRuntime(_level, new ILevelLoadStep[] { player, _pickups, _horde, _wallet, _bosses, waves },
                new ILevelTickable[] { _pickups, _horde, waves, _bosses }, null, _clock);
            _rule = new WavesClearedRule(waves, runtime);
            runtime.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            runtime.StartGameplay();
            return (runtime, waves);
        }

        /// <summary>A boss wave: the boss, 3 of its escort at once, a budget of 5 rank-and-file; then an ordinary wave.</summary>
        private WaveScenario BossScenario()
        {
            var wave = new WaveDefinition(5f, new[] { new WaveMonster(_mass, 1f) }, spawnTime: 0f, packSize: 5);
            wave.SetBoss(new WaveBoss(_boss, new WaveGroup(_escort, 3, 0f)));
            var scenario = Create<WaveScenario>();
            scenario.Configure(new[] { wave, new WaveDefinition(3f, new[] { new WaveMonster(_mass, 1f) }, spawnTime: 0f) },
                pauseTime: 5f, firstDelay: 1f);
            return scenario;
        }

        private static void Tick(LevelRuntime runtime, float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt) runtime.Tick(Dt);
        }

        private int CountAlive(MonsterDefinition kind)
        {
            var n = 0;
            for (var i = 0; i < _horde.HighWater; i++)
                if (HordeSystem.IsAlive(_horde.States[i]) && _horde.DefinitionOf(i) == kind) n++;
            return n;
        }

        private static GridPosition EntryCell => new GridPosition(35, ArenaRect.Y - 1);

        // ---------------------------------------------------------------- the boss wave

        [Test]
        public void BossWave_BossInTheArena_EscortNearIt_BudgetWaitsForItsDeath()
        {
            ArenaLevel(Inside);
            var (runtime, waves) = Run(BossScenario());
            Tick(runtime, 1.1f);

            Assert.That(waves.IsBossWave, Is.True);
            Assert.That(_bosses.IsBossAlive, Is.True, "the boss came with the wave");
            var center = new Vector2(ArenaRect.X + ArenaRect.Width / 2, ArenaRect.Y + ArenaRect.Height / 2);
            Assert.That((_bosses.Position - center).magnitude, Is.LessThan(1.5f), "in the arena's centre");
            Assert.That(CountAlive(_escort), Is.EqualTo(3), "the escort at once");
            for (var i = 0; i < _horde.HighWater; i++)
                if (_horde.DefinitionOf(i) == _escort)
                    Assert.That((_horde.Positions[i] - new Vector2(35f, 10f)).magnitude, Is.LessThan(3f), "from the point nearest the arena");

            Tick(runtime, 2f);
            Assert.That(CountAlive(_mass), Is.Zero, "the budget waits while the boss lives");
            Assert.That(waves.PendingCount, Is.EqualTo(5));

            _horde.Damage(_bosses.Boss, 1e6f);
            Assert.That(_bosses.IsBossAlive, Is.False);
            Assert.That(_bosses.Kills, Is.EqualTo(1));
            Tick(runtime, 0.5f);
            Assert.That(CountAlive(_mass), Is.EqualTo(5), "the budget comes once it is dead");
            Assert.That(waves.Phase, Is.EqualTo(WavePhase.Running), "the escort and the budget still live");

            for (var i = 0; i < _horde.HighWater; i++)
                if (HordeSystem.IsAlive(_horde.States[i])) _horde.Damage(i, 1e6f);
            Tick(runtime, 0.2f);
            Assert.That(waves.Phase, Is.EqualTo(WavePhase.Pause), "cleared: the boss, the escort and the budget are dead");
        }

        [Test]
        public void Arena_ClosesWithThePlayerInside_OpensWhenTheBossDies_DeathSlowsTheSimulation()
        {
            ArenaLevel(Inside);
            var (runtime, _) = Run(BossScenario());
            Assert.That(_bosses.Entries.Count, Is.EqualTo(1), "the entry is found in the cells");
            Tick(runtime, 1.2f);

            Assert.That(_bosses.GatesClosed, Is.True, "the player is inside: the gates come down");
            Assert.That(_blockers.BlocksMovement(EntryCell), Is.True, "the entry's cells stop movement");
            Assert.That(_horde.IsHeld(_bosses.Boss), Is.False, "the boss fights");

            _horde.Damage(_bosses.Boss, 1e6f);
            Assert.That(_clock.Scale, Is.EqualTo(BossSystem.DeathSlowFactor), "the death slows the simulation");
            Assert.That(_bosses.GatesClosed, Is.False);
            Assert.That(_blockers.BlocksMovement(EntryCell), Is.False, "the gates go up");

            var before = _horde.Time;
            runtime.Tick(0.1f);
            Assert.That(_horde.Time - before, Is.EqualTo(0.1f * BossSystem.DeathSlowFactor).Within(1e-4f));
            Tick(runtime, BossSystem.DeathSlowTime);
            Assert.That(_clock.Scale, Is.EqualTo(1f), "for half a second of real time");
        }

        [Test]
        public void Arena_PlayerOutside_TheBossWaits_ArrowToTheArena()
        {
            ArenaLevel(Outside);
            var (runtime, _) = Run(BossScenario());
            Tick(runtime, 1.1f);
            var at = _bosses.Position;
            Tick(runtime, 2f);

            Assert.That(_bosses.GatesClosed, Is.False, "open while the player is outside");
            Assert.That(_horde.IsHeld(_bosses.Boss), Is.True);
            Assert.That((_bosses.Position - at).magnitude, Is.LessThan(0.05f), "waits in its place");
            Assert.That(_bosses.ShowArenaArrow, Is.True);
            Assert.That(CountAlive(_escort), Is.EqualTo(3));
        }

        [Test]
        public void Arena_ClosesOnlyOnceTheBodyIsOffTheEntry_ThePlayerStillWalks()
        {
            ArenaLevel(EntryCell);
            var (runtime, _) = Run(BossScenario());
            Tick(runtime, 1.2f);
            var player = _player;

            player.Displace(new Vector2(0f, 0.6f));
            Tick(runtime, 0.1f);
            Assert.That(_bosses.PlayerInArena, Is.True, "the centre is inside");
            Assert.That(_bosses.GatesClosed, Is.False, "the body still overlaps the entry: no gate on it");

            player.Displace(new Vector2(0f, 0.5f));
            Tick(runtime, 0.1f);
            Assert.That(_bosses.GatesClosed, Is.True, "the whole body is inside");

            var at = player.Position;
            player.Displace(new Vector2(0.5f, 0.5f));
            Assert.That((player.Position - at).magnitude, Is.GreaterThan(0.6f), "walks on in the arena");
            player.Displace(new Vector2(0f, -3f));
            Assert.That(player.Position.y, Is.GreaterThan(ArenaRect.Y - 0.5f), "the gate stops the way out");
        }

        [Test]
        public void Arena_Closing_PutsMonstersInTheEntryOutToTheNearerEnd()
        {
            ArenaLevel(Inside);
            var (runtime, _) = Run(BossScenario());
            Tick(runtime, 0.95f);
            var kind = _horde.KindIndexOf(_escort);
            // The entry runs y 28..29 (edges 27.5..29.5), the arena from y 30.
            var inner = _horde.SpawnSummoned(new Vector2(35f, 29.1f), kind);
            var outer = _horde.SpawnSummoned(new Vector2(36f, 27.9f), kind);
            Assert.That(inner, Is.GreaterThanOrEqualTo(0));
            Assert.That(outer, Is.GreaterThanOrEqualTo(0));
            Tick(runtime, 0.2f);

            Assert.That(_bosses.GatesClosed, Is.True);
            Assert.That(_horde.Positions[inner].y, Is.GreaterThan(29.5f), "nearer the arena: put in");
            Assert.That(_horde.Positions[outer].y, Is.LessThan(27.5f), "nearer the outside: put out");

            var at = _horde.Positions[inner];
            Tick(runtime, 0.5f);
            Assert.That((_horde.Positions[inner] - at).magnitude, Is.GreaterThan(0.3f), "walks on to the player");
        }

        [Test]
        public void Arena_ClosingOff_TheBossChasesAtOnce()
        {
            ArenaLevel(Outside);
            _closeArena = false;
            var (runtime, _) = Run(BossScenario());
            Tick(runtime, 1.1f);
            var at = _bosses.Position;
            Tick(runtime, 1f);

            Assert.That(_horde.IsHeld(_bosses.Boss), Is.False);
            Assert.That((_bosses.Position - at).magnitude, Is.GreaterThan(0.5f), "comes out after the player");
            Assert.That(_bosses.GatesClosed, Is.False);
        }

        // ---------------------------------------------------------------- the wide body

        /// <summary>A wall across a 30×20 floor at x = 15 with a gap of <paramref name="gap"/> cells from y = 8.</summary>
        private void WallLevel(int gap)
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(30, 20);
            for (var y = 1; y < 19; y++)
            for (var x = 1; x < 29; x++)
                geometry.SetCell(new GridPosition(x, y), x == 15 && (y < 8 || y >= 8 + gap) ? CellType.Wall : CellType.Floor);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(25, 10)));
        }

        private void SandboxHorde()
        {
            var stress = Create<StressTestDefinition>();
            stress.ConfigureSandbox(true, 0, 0);
            _level.StressTest = stress;
            var horde = Create<HordeDefinition>();
            horde.ConfigureMonsters(new[] { new HordeSpawn(_boss, 1f), new HordeSpawn(_mass, 1f) });
            _level.Horde = horde;
            _grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            var passability = new LevelPassability(_grid, _doors);
            _player = new PlayerSystem(_level, _playerDefinition, new FakePlayerInput(), passability, new OccupancyMap(_grid),
                new LevelLaunchOptions(startIndex: 0), new Aim());
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _horde = new HordeSystem(_level, _grid, passability, _doors, _player, new PlayerHealth(_playerDefinition), new SoundEventBus());
            _horde.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TestCase(2, false)]
        [TestCase(3, true)]
        public void WideBody_WalksOnlyWhereItFits(int gap, bool passes)
        {
            WallLevel(gap);
            SandboxHorde();
            var boss = _horde.SpawnBoss(new Vector2(5f, 10f), _horde.KindIndexOf(_boss));
            var walker = _horde.SpawnAt(new Vector2(5f, 4f), MonsterPerception.Hunter, _horde.KindIndexOf(_mass));
            for (var t = 0f; t < 0.2f; t += Dt) _horde.Tick(Dt);

            Assert.That(_horde.BodyFlow, Is.Not.Null, "built while a wide body lives");
            Assert.That(_horde.Flow.DistanceAt(10 * 30 + 5), Is.Not.EqualTo(FlowField.Unreachable), "the narrow gap lets the crowd through");
            Assert.That(_horde.BodyFlow.DistanceAt(10 * 30 + 5) != FlowField.Unreachable, Is.EqualTo(passes),
                "a 2×2 body needs 3 free cells across (its footprint covers the cells around its centre)");
            if (!passes) return;

            for (var t = 0f; t < 12f; t += Dt) _horde.Tick(Dt);
            Assert.That(_horde.Positions[boss].x, Is.GreaterThan(16f), "came through the wide gap");
            Assert.That(_horde.Positions[walker].x, Is.GreaterThan(16f));
        }

        [Test]
        public void WideBody_CrowdKeepsClear_TheBossIsNotPushed()
        {
            WallLevel(18);
            _boss.Stats.Radius = 1.5f; // 3×3: a monster two cells from its centre is outside the buckets' reach
            SandboxHorde();
            var boss = _horde.SpawnBoss(new Vector2(10f, 10f), _horde.KindIndexOf(_boss));
            var walker = _horde.SpawnAt(new Vector2(11.6f, 10f), MonsterPerception.Hunter, _horde.KindIndexOf(_mass));
            _horde.SetModifiers(walker, 1f, true); // stunned: stands, only the crowd moves it
            var at = _horde.Positions[boss];
            _horde.Push(boss, new Vector2(10f, 0f));
            _horde.Stagger(boss, 1f);
            Assert.That(_horde.IsStaggered(boss), Is.False, "a hit does not stop a boss");
            _horde.SetHeld(boss, true);
            for (var t = 0f; t < 0.5f; t += Dt) _horde.Tick(Dt);

            Assert.That((_horde.Positions[boss] - at).magnitude, Is.LessThan(0.05f), "immovable: neither pushed nor shoved by the crowd");
            Assert.That((_horde.Positions[walker] - _horde.Positions[boss]).magnitude, Is.GreaterThan(1.7f),
                "the rank-and-file two cells from the centre still keeps out of the body (1.5 + 0.3)");
        }

        // ---------------------------------------------------------------- phase, control

        [Test]
        public void Control_OnABoss_LastsAQuarter()
        {
            var stun = Create<StatusDefinition>();
            stun.Configure("stun", new StunStatus());
            var slow = Create<StatusDefinition>();
            slow.Configure("slow", new SlowStatus { SpeedFactor = 0.5f });
            Assert.That(_boss.DurationFactor(stun), Is.EqualTo(0.25f));
            Assert.That(_boss.DurationFactor(slow), Is.EqualTo(1f), "a slow is not control");
            Assert.That(_mass.DurationFactor(stun), Is.EqualTo(1f));
        }

        [Test]
        public void Phase_AtItsThreshold_Roars_ThenTheNextSetAndTempo()
        {
            WallLevel(18);
            _boss.Stats.Radius = 0.45f; // a narrow body: the test is about the phase
            var strike = new StrikeAbility { Cooldown = 1f, FirstDelay = 0f, MinRange = 0f, MaxRange = 30f, NeedsSight = false, Warning = 0.2f, Recovery = 0.1f };
            strike.UntilPhase = 0; // the first phase only
            var phase = new PhaseAbility { HealthBelow = 0.5f, Warning = 0.3f, DamageFactor = 2f, CooldownFactor = 0.5f, Rage = true };
            _boss.SetAbilities(phase, strike);
            SandboxHorde();
            var abilities = new MonsterAbilities(_horde, new CombatTargets(new SpatialQueryService(_grid, _doors), _horde), _player,
                new PlayerHealth(_playerDefinition));
            abilities.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            var changed = -1;
            abilities.PhaseChanged += (slot, number) => changed = number;
            var boss = _horde.SpawnAt(new Vector2(20f, 10f), MonsterPerception.Hunter, _horde.KindIndexOf(_boss));

            void Tick(float seconds)
            {
                for (var t = 0f; t < seconds; t += Dt)
                {
                    _horde.Tick(Dt);
                    abilities.Tick(Dt);
                }
            }

            Tick(1f);
            Assert.That(abilities.HealthPhaseOf(boss), Is.Zero);
            Assert.That(abilities.Casts, Is.GreaterThan(0), "the strike of the first phase");

            _horde.Damage(boss, 60f);
            Tick(1f);
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(abilities.HealthPhaseOf(boss), Is.EqualTo(1));
            Assert.That(_horde.DamageFactorOf(boss), Is.EqualTo(2f));
            Assert.That(_horde.IsEnraged(boss), Is.True, "a rage phase glows");

            var casts = abilities.Casts;
            Tick(3f);
            Assert.That(abilities.Casts, Is.EqualTo(casts), "the strike was the first phase's only; the phase itself once");
        }
    }
}
