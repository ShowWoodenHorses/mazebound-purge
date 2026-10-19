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
using Purge.Gameplay.Visibility;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Monsters
{
    /// <summary>Stage 5b: wanderers, hearing along the passages, sight, noticing, the caller's call.</summary>
    public class HordePerceptionTests
    {
        private const float Dt = 1f / 60f;
        private static readonly Vector2 Player = new Vector2(5f, 10f);

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private LevelGrid _grid;
        private PlayerDefinition _playerDefinition;
        private DoorSystem _doors;
        private LevelPassability _passability;
        private PlayerSystem _player;
        private PlayerHealth _health;
        private SoundEventBus _sounds;
        private VisibilitySystem _visibility;
        private StressTestDefinition _stress;
        private HordeDefinition _hordeData;
        private MonsterDefinition _monster;
        private HordeSystem _horde;

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        /// <summary>40×21 floor, a wall along x = 20 with a closed door at (20, 10); the player at (5, 10), west of it.</summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(40, 21);
            for (var y = 1; y < 20; y++)
            for (var x = 1; x < 39; x++)
                geometry.SetCell(new GridPosition(x, y), x == 20 ? (y == 10 ? CellType.Door : CellType.Wall) : CellType.Floor);
            _level.ReplaceGeometry(geometry);
            _level.MutableDoors.Add(new DoorData("door_1", new GridPosition(20, 10)));
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(5, 10)));

            _stress = ScriptableObject.CreateInstance<StressTestDefinition>();
            _created.Add(_stress);
            _level.StressTest = _stress;
            _hordeData = ScriptableObject.CreateInstance<HordeDefinition>();
            _created.Add(_hordeData);
            _level.Horde = _hordeData;
            _monster = ScriptableObject.CreateInstance<MonsterDefinition>();
            _monster.Configure("walker", MonsterRank.Mass, MonsterBiome.Common,
                new MonsterStats { MaxHp = 100f, Speed = 3f, SightRange = 7f, CallRadius = 12f });
            _monster.SetPerception(MonsterPerception.Wanderer);
            _created.Add(_monster);
            _stress.ConfigureSandbox(true, 0, 0);
            _hordeData.ConfigureMonsters(new[] { new HordeSpawn(_monster, 1f) });

            _playerDefinition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _playerDefinition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            _passability = new LevelPassability(_grid, _doors);
            _player = new PlayerSystem(_level, _playerDefinition, new FakePlayerInput(), _passability, new OccupancyMap(_grid),
                new LevelLaunchOptions(startIndex: 0), new Aim());
            _health = new PlayerHealth(_playerDefinition);
            _sounds = new SoundEventBus();
            _visibility = new VisibilitySystem(_grid, _doors, _player);
            _visibility.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _horde?.Dispose();
            _horde = null;
            _visibility.Dispose();
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_playerDefinition);
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private HordeSystem Horde()
        {
            _horde = new HordeSystem(_level, _grid, _passability, _doors, _player, _health, _sounds, _visibility);
            _horde.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            return _horde;
        }

        private void Tick(float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt) _horde.Tick(Dt);
        }

        private HordeAwareness Of(int agent) => _horde.Perception.AwarenessOf(agent);

        private int Spawn(float x, float y, MonsterPerception perception = MonsterPerception.Wanderer)
        {
            var agent = _horde.SpawnAt(new Vector2(x, y), perception);
            Assert.That(agent, Is.GreaterThanOrEqualTo(0));
            return agent;
        }

        [Test]
        public void Hunter_ChasesAtOnce_AndNeverLosesThePlayer()
        {
            Horde();
            var hunter = Spawn(35f, 18f, MonsterPerception.Hunter); // behind the wall, out of view and earshot
            Assert.That(Of(hunter), Is.EqualTo(HordeAwareness.Chase));

            Tick(8f);
            Assert.That(Of(hunter), Is.EqualTo(HordeAwareness.Chase));
        }

        [Test]
        public void Wanderer_DoesNotKnow_UntilItSeesThePlayer_ThenRoarsAndChasesForGood()
        {
            Horde();
            var far = Spawn(15f, 10f); // in view, 10 cells: beyond its sight (7)
            var near = Spawn(10f, 10f);
            Tick(0.3f);

            Assert.That(Of(far), Is.EqualTo(HordeAwareness.Wander));
            Assert.That(Of(near), Is.EqualTo(HordeAwareness.Alert), "noticed: roars first");
            var roarAt = _horde.Positions[near];
            Tick(0.15f);
            Assert.That((_horde.Positions[near] - roarAt).magnitude, Is.LessThan(0.05f), "stands while it roars");

            Tick(0.4f);
            Assert.That(Of(near), Is.EqualTo(HordeAwareness.Chase));
            Tick(6f);
            Assert.That(Of(near), Is.EqualTo(HordeAwareness.Chase), "never loses the player");
        }

        [Test]
        public void Hit_MakesAWandererChase()
        {
            Horde();
            var agent = Spawn(35f, 4f); // behind the wall
            _horde.Damage(agent, 1f);
            Tick(0.6f);

            Assert.That(Of(agent), Is.EqualTo(HordeAwareness.Chase));
        }

        [Test]
        public void Wanderer_HearsASound_RunsToIt_NobodyThere_WandersAgain()
        {
            Horde();
            var agent = Spawn(25f, 4f); // east of the wall: the player never sees it
            _sounds.Emit(SoundType.Explosion, new Vector2(31f, 4f), 8f);
            Tick(0.25f);
            Assert.That(Of(agent), Is.EqualTo(HordeAwareness.Investigate));

            Tick(4f);
            Assert.That((_horde.Positions[agent] - new Vector2(31f, 4f)).magnitude, Is.LessThan(1.6f), "came to the source");
            Assert.That(Of(agent), Is.EqualTo(HordeAwareness.Wander));
        }

        [Test]
        public void ClosedDoor_Muffles_WallStops()
        {
            Horde();
            var throughDoor = Spawn(22f, 10f); // shot at (17, 10), radius 6: (6 − 3) × 0.5 − 2 < 0
            var nearDoor = Spawn(21f, 10f);    // (6 − 3) × 0.5 − 1 > 0
            var behindWall = Spawn(22f, 6f);   // straight 5 cells, but the wall is in the way
            _sounds.Emit(SoundType.Ranged, new Vector2(17f, 10f), 6f);
            Tick(0.25f);

            Assert.That(Of(throughDoor), Is.EqualTo(HordeAwareness.Wander));
            Assert.That(Of(nearDoor), Is.EqualTo(HordeAwareness.Investigate));
            Assert.That(Of(behindWall), Is.EqualTo(HordeAwareness.Wander));

            _doors.SetOpen("door_1", true);
            _sounds.Emit(SoundType.Ranged, new Vector2(17f, 10f), 6f);
            Tick(0.25f);
            Assert.That(Of(throughDoor), Is.EqualTo(HordeAwareness.Investigate), "the open door lets the whole sound through");
        }

        [Test]
        public void Difficulty_ScalesHearing()
        {
            var difficulty = ScriptableObject.CreateInstance<DifficultyDefinition>();
            difficulty.Configure(DifficultyLevel.Hard, hearing: 2f, sight: 1f, call: 1f);
            _created.Add(difficulty);
            _hordeData.ConfigurePerception(difficulty, 3, 25f);
            Horde();
            var agent = Spawn(30f, 4f);
            _sounds.Emit(SoundType.Ranged, new Vector2(24f, 4f), 4f); // 6 cells: × 2 → heard
            Tick(0.25f);

            Assert.That(Of(agent), Is.EqualTo(HordeAwareness.Investigate));
        }

        [Test]
        public void Caller_CallsOnce_EveryoneInEarshotChases_NoChain()
        {
            Horde();
            var caller = Spawn(11f, 10f, MonsterPerception.Caller); // sees the player (6 cells)
            var listener = Spawn(16f, 4f);                          // out of sight (11 cells), the call reaches
            var secondCaller = Spawn(14f, 16f, MonsterPerception.Caller); // called — does not call on
            var beyond = Spawn(30f, 10f);                           // past the closed door: the call fades
            Tick(1.2f);

            Assert.That(Of(caller), Is.EqualTo(HordeAwareness.Chase));
            Assert.That(_horde.Perception.Calls, Is.EqualTo(1));
            Assert.That(Of(listener), Is.EqualTo(HordeAwareness.Chase));
            Assert.That(Of(secondCaller), Is.EqualTo(HordeAwareness.Chase));
            Assert.That(Of(beyond), Is.EqualTo(HordeAwareness.Wander));

            Tick(1f);
            Assert.That(_horde.Perception.Calls, Is.EqualTo(1), "a called caller does not call: no chain");
        }

        [Test]
        public void AllHunters_TurnsEveryoneToChase()
        {
            Horde();
            var a = Spawn(30f, 4f);
            var b = Spawn(30f, 16f);
            _horde.Perception.AllHunters = true;
            var c = Spawn(32f, 10f);

            Assert.That(Of(a), Is.EqualTo(HordeAwareness.Chase));
            Assert.That(Of(b), Is.EqualTo(HordeAwareness.Chase));
            Assert.That(Of(c), Is.EqualTo(HordeAwareness.Chase), "new ones too");
        }

        [Test]
        public void Wanderers_WalkWithTheirGroup()
        {
            Horde();
            var agent = Spawn(12f, 3f); // west of the wall, 9.9 cells from the player: beyond its sight
            var start = _horde.Positions[agent];
            Tick(3f);

            Assert.That(Of(agent), Is.EqualTo(HordeAwareness.Wander));
            Assert.That(_horde.Paces[agent], Is.EqualTo(HordePace.Walk).Or.EqualTo(HordePace.Still));
            Assert.That((_horde.Positions[agent] - start).magnitude, Is.GreaterThan(0.3f).Or.LessThan(0.01f),
                "walks to its group's point (or already stands there)");
            Assert.That((_horde.Positions[agent] - start).magnitude, Is.LessThan(3f * MonsterStats.WalkClipSpeed + 0.2f),
                "at the wander speed, not running");
        }
    }
}
