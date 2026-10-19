using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Authoring;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Waves;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Waves;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Monsters
{
    /// <summary>Stage 6a: waves on a budget — the generator, the difficulty factors, spawn points, the end of a wave and the level.</summary>
    public class WaveTests
    {
        private const float Dt = 1f / 30f;

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private PlayerDefinition _playerDefinition;
        private MonsterDefinition _mass;
        private MonsterDefinition _officer;
        private WavesClearedRule _rule;

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        /// <summary>50×50 open floor, the player at (25, 25).</summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(50, 50);
            for (var y = 1; y < 49; y++)
            for (var x = 1; x < 49; x++)
                geometry.SetCell(new GridPosition(x, y), CellType.Floor);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(25, 25)));
            _playerDefinition = Create<PlayerDefinition>();
            _playerDefinition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _mass = Monster("mass", MonsterRank.Mass);
            _officer = Monster("officer", MonsterRank.Officer);
        }

        [TearDown]
        public void TearDown()
        {
            _rule?.Dispose();
            _rule = null;
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

        private MonsterDefinition Monster(string id, MonsterRank rank)
        {
            var monster = Create<MonsterDefinition>();
            monster.Configure(id, rank, MonsterBiome.Common, new MonsterStats { MaxHp = 30f, Speed = 2f, EmergeTime = 0.5f });
            return monster;
        }

        private DifficultyDefinition Difficulty(float health, float count, float senior)
        {
            var difficulty = Create<DifficultyDefinition>();
            difficulty.ConfigureWaves(health, count, senior, 1f, 1f);
            return difficulty;
        }

        // ---------------------------------------------------------------- generator

        [Test]
        public void Generator_SpendsTheBudget_OnWhatFits()
        {
            _officer.SetCost(10f);
            var wave = new WaveDefinition(25f, new[] { new WaveMonster(_mass, 1f), new WaveMonster(_officer, 1f) }, spawnTime: 30f);
            var spawns = WaveGenerator.Generate(wave, null, 7);

            var cost = 0f;
            foreach (var spawn in spawns) cost += spawn.Monster.Cost;
            Assert.That(cost, Is.EqualTo(25f), "spent to the last point: a rank-and-file (1) always fits");
            for (var i = 1; i < spawns.Count; i++)
                Assert.That(spawns[i].Time, Is.GreaterThanOrEqualTo(spawns[i - 1].Time), "ordered by time");
            Assert.That(spawns[spawns.Count - 1].Time, Is.LessThanOrEqualTo(30f * 1.25f));
            Assert.That(spawns[0].Time, Is.EqualTo(0f), "the first pack at once");
        }

        [Test]
        public void Generator_SameSeed_SameWave()
        {
            var wave = new WaveDefinition(60f, new[] { new WaveMonster(_mass, 3f), new WaveMonster(_officer, 1f) });
            var a = WaveGenerator.Generate(wave, null, 42);
            var b = WaveGenerator.Generate(wave, null, 42);
            Assert.That(a.Count, Is.EqualTo(b.Count));
            for (var i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].Monster, Is.SameAs(b[i].Monster));
                Assert.That(a[i].Time, Is.EqualTo(b[i].Time));
            }
        }

        [Test]
        public void Generator_Difficulty_CountsAndSeniorShare()
        {
            _officer.SetCost(1f); // same price: the share is the weights' alone
            var wave = new WaveDefinition(400f, new[] { new WaveMonster(_mass, 1f), new WaveMonster(_officer, 1f) },
                new[] { new WaveGroup(_officer, 2, 10f, 3) });

            var normal = WaveGenerator.Generate(wave, null, 1);
            var hard = WaveGenerator.Generate(wave, Difficulty(1f, 1.5f, 3f), 1);
            Assert.That(normal.Count, Is.EqualTo(400 + 2));
            Assert.That(hard.Count, Is.EqualTo(600 + 3), "budget and groups × 1.5");

            int Officers(List<WaveSpawn> list)
            {
                var n = 0;
                foreach (var spawn in list) if (spawn.Monster == _officer && spawn.Point < 0) n++;
                return n;
            }

            Assert.That(Officers(normal) / 400f, Is.InRange(0.4f, 0.6f));
            Assert.That(Officers(hard) / 600f, Is.InRange(0.65f, 0.85f), "senior weights × 3 → 3 : 1");
            var group = hard.FindAll(spawn => spawn.Point == 3);
            Assert.That(group.Count, Is.EqualTo(3));
            Assert.That(group.TrueForAll(spawn => spawn.Time == 10f && spawn.Pack == group[0].Pack), "a group is one pack at its time");
        }

        [Test]
        public void Generator_PacksShareTheirTime()
        {
            var wave = new WaveDefinition(18f, new[] { new WaveMonster(_mass, 1f) }, spawnTime: 30f, packSize: 6);
            var spawns = WaveGenerator.Generate(wave, null, 3);
            Assert.That(spawns.Count, Is.EqualTo(18));
            for (var pack = 0; pack < 3; pack++)
            {
                var members = spawns.FindAll(spawn => spawn.Pack == pack);
                Assert.That(members.Count, Is.EqualTo(6));
                Assert.That(members.TrueForAll(spawn => spawn.Time == members[0].Time));
            }
        }

        // ---------------------------------------------------------------- the wave system

        private (LevelRuntime Runtime, HordeSystem Horde, WaveSystem Waves) Run(WaveScenario scenario, DifficultyDefinition difficulty = null)
        {
            var horde = Create<HordeDefinition>();
            horde.ConfigureCapacity(200, 8);
            horde.ConfigurePerception(difficulty, 3, 25f);
            horde.SetWaves(scenario);
            _level.Horde = horde;

            var grid = new LevelGrid(_level.Geometry);
            var doors = new DoorSystem(_level);
            var passability = new LevelPassability(grid, doors);
            var player = new PlayerSystem(_level, _playerDefinition, new FakePlayerInput(), passability, new OccupancyMap(grid),
                new LevelLaunchOptions(startIndex: 0), new Aim());
            var health = new PlayerHealth(_playerDefinition);
            var system = new HordeSystem(_level, grid, passability, doors, player, health, options: new LevelLaunchOptions(seed: 5));
            system.GodMode = true;
            var waves = new WaveSystem(_level, system, player, new LevelLaunchOptions(seed: 5));
            var runtime = new LevelRuntime(_level, new ILevelLoadStep[] { player, system, waves }, new ILevelTickable[] { system, waves });
            _rule = new WavesClearedRule(waves, runtime);
            runtime.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            runtime.StartGameplay();
            return (runtime, system, waves);
        }

        private WaveScenario Scenario(params WaveDefinition[] waves)
        {
            var scenario = Create<WaveScenario>();
            scenario.Configure(waves, pauseTime: 5f, firstDelay: 1f);
            return scenario;
        }

        private static void Tick(LevelRuntime runtime, float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt) runtime.Tick(Dt);
        }

        private static void KillAll(HordeSystem horde)
        {
            for (var i = 0; i < horde.HighWater; i++)
                if (HordeSystem.IsAlive(horde.States[i])) horde.Damage(i, 1e6f);
        }

        [Test]
        public void Waves_SpawnAtFarPoints_ClimbingOut()
        {
            LevelEditing.AddMonsterSpawn(_level, new GridPosition(28, 25)); // 3 cells from the player: waits
            LevelEditing.AddMonsterSpawn(_level, new GridPosition(5, 5));
            var (runtime, horde, waves) = Run(Scenario(new WaveDefinition(12f, new[] { new WaveMonster(_mass, 1f) }, spawnTime: 0f, packSize: 12)));

            Assert.That(waves.Phase, Is.EqualTo(WavePhase.Delay));
            Assert.That(horde.AliveCount, Is.EqualTo(0), "a wave level starts empty");
            Tick(runtime, 1.2f);
            Assert.That(waves.Phase, Is.EqualTo(WavePhase.Running));
            Assert.That(waves.WaveIndex, Is.EqualTo(0));
            Tick(runtime, 0.2f);
            Assert.That(horde.AliveCount, Is.EqualTo(12));
            var emerging = 0;
            for (var i = 0; i < horde.HighWater; i++)
            {
                if (!HordeSystem.IsAlive(horde.States[i])) continue;
                Assert.That((horde.Positions[i] - new Vector2(5f, 5f)).magnitude, Is.LessThan(WaveSystem.SpawnRadius * 1.5f + 0.5f),
                    "the pack comes out at the far point");
                if (horde.States[i] == HordeAgentState.Emerging) emerging++;
            }

            Assert.That(emerging, Is.GreaterThan(0), "they climb out of the ground");
        }

        [Test]
        public void Waves_ClearedByKilling_PauseThenNext_LastCompletes()
        {
            LevelEditing.AddMonsterSpawn(_level, new GridPosition(5, 5));
            var cleared = new List<int>();
            var outcome = (LevelOutcome?)null;
            var (runtime, horde, waves) = Run(Scenario(
                new WaveDefinition(6f, new[] { new WaveMonster(_mass, 1f) }, spawnTime: 0f),
                new WaveDefinition(8f, new[] { new WaveMonster(_mass, 1f) }, spawnTime: 0f)));
            waves.WaveCleared += cleared.Add;
            runtime.Finished += result => outcome = result;

            Tick(runtime, 1.5f);
            Assert.That(horde.AliveCount, Is.EqualTo(6));
            KillAll(horde);
            Tick(runtime, 0.1f);
            Assert.That(cleared, Is.EqualTo(new[] { 0 }));
            Assert.That(waves.Phase, Is.EqualTo(WavePhase.Pause));
            Assert.That(waves.TimeLeft, Is.InRange(4.5f, 5f));

            Assert.That(waves.StartNextWave(), Is.True, "skipping the pause starts the next wave");
            Assert.That(waves.WaveIndex, Is.EqualTo(1));
            Tick(runtime, 0.2f);
            Assert.That(horde.AliveCount, Is.EqualTo(8));
            Assert.That(outcome, Is.Null);
            KillAll(horde);
            Tick(runtime, 0.1f);
            Assert.That(waves.Phase, Is.EqualTo(WavePhase.Completed));
            Assert.That(outcome, Is.EqualTo(LevelOutcome.Completed));
        }

        [Test]
        public void Waves_MaxAlive_TheRestWait()
        {
            LevelEditing.AddMonsterSpawn(_level, new GridPosition(5, 5));
            LevelEditing.AddMonsterSpawn(_level, new GridPosition(44, 44));
            var (runtime, horde, waves) = Run(Scenario(new WaveDefinition(30f, new[] { new WaveMonster(_mass, 1f) }, spawnTime: 0f,
                packSize: 30, maxAlive: 10)));

            Tick(runtime, 2f);
            Assert.That(horde.AliveCount, Is.EqualTo(10));
            Assert.That(waves.PendingCount, Is.EqualTo(20));
            KillAll(horde);
            Tick(runtime, 0.2f);
            Assert.That(horde.AliveCount, Is.EqualTo(10), "the next ten come as the first die");
            Assert.That(waves.Phase, Is.EqualTo(WavePhase.Running));
        }

        [Test]
        public void Waves_Difficulty_Health()
        {
            LevelEditing.AddMonsterSpawn(_level, new GridPosition(5, 5));
            var (runtime, horde, _) = Run(Scenario(new WaveDefinition(3f, new[] { new WaveMonster(_mass, 1f) }, spawnTime: 0f)),
                Difficulty(1.5f, 1f, 1f));
            Tick(runtime, 1.5f);
            for (var i = 0; i < horde.HighWater; i++)
                if (HordeSystem.IsAlive(horde.States[i]))
                    Assert.That(horde.Health[i], Is.EqualTo(45f).Within(0.01f), "HP × 1.5");
        }
    }
}
