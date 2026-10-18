using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Visual;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Presentation.Visual;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Monsters
{
    /// <summary>
    /// Stage Э3: the horde's room for called-up monsters (the sandbox bench's tails), a corpse's sinking, explosions sized
    /// by spread.
    /// </summary>
    public class MonsterEffectsTests
    {
        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private PlayerDefinition _playerDefinition;
        private StressTestDefinition _stress;
        private MonsterDefinition _walker;
        private HordeSystem _horde;

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(40, 40);
            for (var y = 1; y < 39; y++)
            for (var x = 1; x < 39; x++)
                geometry.SetCell(new GridPosition(x, y), CellType.Floor);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(20, 20)));

            _stress = Create<StressTestDefinition>();
            _level.StressTest = _stress;
            _walker = Create<MonsterDefinition>();
            _walker.Configure("walker", MonsterRank.Mass, MonsterBiome.Common, new MonsterStats { MaxHp = 30f, Speed = 2f, Damage = 0 });
            _walker.SetPerception(MonsterPerception.Hunter);
            _stress.ConfigureMonsters(new[] { new HordeSpawn(_walker, 1f) });
            _playerDefinition = Create<PlayerDefinition>();
            _playerDefinition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
        }

        [TearDown]
        public void TearDown()
        {
            _horde?.Dispose();
            _horde = null;
            Object.DestroyImmediate(_level);
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private T Create<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            _created.Add(asset);
            return asset;
        }

        private void Build()
        {
            var grid = new LevelGrid(_level.Geometry);
            var doors = new DoorSystem(_level);
            var passability = new LevelPassability(grid, doors);
            var player = new PlayerSystem(_level, _playerDefinition, new FakePlayerInput(), passability, new OccupancyMap(grid),
                new LevelLaunchOptions(startIndex: 0), new Aim());
            player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _horde = new HordeSystem(_level, grid, passability, doors, player, new PlayerHealth(_playerDefinition), new SoundEventBus());
            _horde.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [Test]
        public void Capacity_LeavesRoomForCalledUpMonsters_AboveTheWaves()
        {
            _stress.ConfigureSandbox(true, 0, 10);
            _stress.ConfigureCounts(new[] { 0, 10 }, 6);
            Build();
            Assert.That(_horde.Capacity, Is.EqualTo(16), "the largest count + the reserve");
            Assert.That(_horde.AliveCount, Is.EqualTo(10), "the first wave");

            _horde.SetTargetCount(1000);
            Assert.That(_horde.TargetCount, Is.EqualTo(10), "the waves never take the reserve");
            for (var n = 0; n < 6; n++)
                Assert.That(_horde.SpawnAt(new Vector2(25f + n, 25f), MonsterPerception.Hunter), Is.GreaterThanOrEqualTo(0),
                    "officer buttons and raises still find room with the waves full");
            Assert.That(_horde.SpawnAt(new Vector2(30f, 30f), MonsterPerception.Hunter), Is.LessThan(0), "until the reserve is used up");
        }

        [Test]
        public void CorpseSink_StillUntilTheEnd_ThenDown()
        {
            var visual = Create<CombatVisualDefinition>();
            visual.CorpseSinkTime = 0.4f;
            Assert.That(visual.CorpseSink(1f, 3f), Is.EqualTo(0f), "lies still");
            Assert.That(visual.CorpseSink(2.6f, 3f), Is.EqualTo(0f).Within(1e-4f), "starts at the last 0.4 s");
            Assert.That(visual.CorpseSink(2.8f, 3f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(visual.CorpseSink(3f, 3f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(visual.CorpseSink(0.3f, 0.4f), Is.EqualTo(0.5f).Within(1e-4f), "a short corpse time sinks in its second half");
        }

        [Test]
        public void SetSpread_GrowsReachAndCount_ParticlesOnlyBySquareRoot()
        {
            var go = new GameObject("spread");
            try
            {
                var system = go.AddComponent<ParticleSystem>();
                var main = system.main;
                main.startSizeMultiplier = 1f;
                main.maxParticles = 64;
                var emission = system.emission;
                emission.rateOverTimeMultiplier = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });
                var effect = go.AddComponent<CombatEffect>();
                go.transform.localScale = Vector3.one * 0.5f; // built at half the pack's size

                effect.SetSpread(4f);
                Assert.That(go.transform.localScale.x, Is.EqualTo(2f), "reach and shape follow the root, × the built size");
                Assert.That(system.main.startSizeMultiplier * 4f, Is.EqualTo(2f).Within(1e-4f), "each particle only ×√4");
                Assert.That(system.emission.GetBurst(0).count.constantMax, Is.EqualTo(25f), "×scale particles, capped at ×2.5");
                Assert.That(system.main.maxParticles, Is.EqualTo(160));

                effect.SetSpread(1f);
                Assert.That(go.transform.localScale.x, Is.EqualTo(0.5f), "the built size is kept");
                Assert.That(system.main.startSizeMultiplier, Is.EqualTo(1f).Within(1e-4f), "as built again");
                Assert.That(system.emission.GetBurst(0).count.constantMax, Is.EqualTo(10f));
                Assert.That(system.main.maxParticles, Is.EqualTo(64));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
