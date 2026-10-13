using System.Collections.Generic;
using System.Threading;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Level;
using Maze.Gameplay.Navigation;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Spatial;
using Maze.Gameplay.Zombies;
using Maze.Tests.EditMode.Player;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Zombies
{
    /// <summary>Zombie navigation, detection, state machine, attacks and death (ТЗ §52, §73–77, §88).</summary>
    public class ZombieTests
    {
        private const float Dt = 1f / 60f;

        private LevelData _level;
        private PlayerDefinition _playerDefinition;
        private readonly List<ZombieDefinition> _definitions = new List<ZombieDefinition>();
        private LevelGrid _grid;
        private DoorSystem _doors;
        private OccupancyMap _occupancy;
        private SpatialQueryService _spatial;
        private SoundEventBus _sounds;
        private PlayerHealth _health;
        private PlayerSystem _player;
        private NavigationSystem _navigation;
        private ZombieSystem _zombies;

        /// <summary>13x7, room x = 1..11, y = 1..5. Column x = 8 is wall with an open door at (8,3). Player at (2,3).</summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(13, 7);
            for (var y = 1; y <= 5; y++)
            for (var x = 1; x <= 11; x++)
                geometry.SetCell(new GridPosition(x, y), x == 8 ? CellType.Wall : CellType.Floor);
            geometry.SetCell(new GridPosition(8, 3), CellType.Door);
            _level.ReplaceGeometry(geometry);
            _level.MutableDoors.Add(new DoorData("door_1", new GridPosition(8, 3), isInitiallyOpen: true));
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(2, 3)));
            _playerDefinition = ScriptableObject.CreateInstance<PlayerDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            _zombies?.Dispose();
            _navigation?.Dispose();
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_playerDefinition);
            foreach (var definition in _definitions)
                Object.DestroyImmediate(definition);
            _definitions.Clear();
        }

        private ZombieDefinition Definition(ZombieDetectionType detection, float visionAngle = 45f, float visionRange = 5f,
            float hearingRadius = 5f, float detectionRadius = 4f, float damage = 10f, float attackInterval = 1f, float moveSpeed = 2f, float chaseSpeed = 0f)
        {
            var definition = ScriptableObject.CreateInstance<ZombieDefinition>();
            definition.Configure("z", detection, visionAngle, visionRange, hearingRadius, detectionRadius, 30f, damage,
                attackInterval, moveSpeed, chaseSpeed);
            _definitions.Add(definition);
            return definition;
        }

        private void AddZombie(int x, int y, ZombieDefinition definition, Direction facing = Direction.West,
            params GridPosition[] patrol)
        {
            string patrolId = null;
            if (patrol.Length > 0)
            {
                patrolId = "patrol_" + _level.Patrols.Count;
                _level.MutablePatrols.Add(new PatrolData(patrolId, patrol));
            }

            _level.MutableZombieSpawns.Add(new ZombieSpawnData("zombie_" + _level.ZombieSpawns.Count, new GridPosition(x, y),
                definition, facing, patrolId));
        }

        private void Build()
        {
            _grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            var passability = new LevelPassability(_grid, _doors);
            _occupancy = new OccupancyMap(_grid);
            _spatial = new SpatialQueryService(_grid, _doors);
            _sounds = new SoundEventBus();
            _health = new PlayerHealth(_playerDefinition);
            _player = new PlayerSystem(_level, _playerDefinition, new FakePlayerInput(), passability, _occupancy,
                new LevelLaunchOptions(startIndex: 0), Aim.FreeNoAssist);
            _navigation = new NavigationSystem(_grid, passability, _doors);
            _zombies = new ZombieSystem(_level, _navigation, _player, _health, _occupancy, _spatial, _sounds);
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _zombies.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private ZombieRuntime Zombie(int index = 0) => _zombies.Zombies[index];

        private void Run(float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt)
                _zombies.Tick(Dt);
        }

        private void RunUntil(System.Func<bool> condition, float maxSeconds = 20f)
        {
            for (var t = 0f; t < maxSeconds && !condition(); t += Dt)
                _zombies.Tick(Dt);
            Assert.IsTrue(condition(), "Condition not reached in time.");
        }

        // ------------------------------------------------------------------ Navigation

        [Test]
        public void Navigation_ClosedDoorBlocks_DoorChangeInvalidatesPaths()
        {
            Build();
            var path = new List<GridPosition>();
            Assert.IsTrue(_navigation.TryFindPath(new GridPosition(2, 3), new GridPosition(10, 3), path));

            var version = _navigation.Version;
            _doors.SetOpen("door_1", false);
            Assert.Greater(_navigation.Version, version);
            Assert.IsFalse(_navigation.TryFindPath(new GridPosition(2, 3), new GridPosition(10, 3), path),
                "Zombies do not open doors.");
        }

        // ------------------------------------------------------------------ Detection (noticing → Alert, the roar before a chase)

        [Test]
        public void Vision_SeesThePlayerInItsCone_NotBehind()
        {
            AddZombie(6, 3, Definition(ZombieDetectionType.VisionOnly), Direction.West);
            AddZombie(6, 1, Definition(ZombieDetectionType.VisionOnly), Direction.East);
            Build();
            Run(0.2f);

            Assert.AreEqual(ZombieState.Alert, Zombie(0).State, "Player 4 cells ahead.");
            Assert.AreEqual(ZombieState.Idle, Zombie(1).State, "Player behind.");
        }

        [Test]
        public void Vision_IsBlockedByAClosedDoor()
        {
            AddZombie(10, 3, Definition(ZombieDetectionType.VisionOnly, visionRange: 9f), Direction.West);
            _level.MutableDoors[0].IsInitiallyOpen = false;
            Build();
            Run(0.3f);
            Assert.AreEqual(ZombieState.Idle, Zombie().State);

            _doors.SetOpen("door_1", true);
            Run(0.2f);
            Assert.AreEqual(ZombieState.Alert, Zombie().State);
        }

        [Test]
        public void Hearing_ReactsToSoundsWithinBothRadii()
        {
            AddZombie(6, 3, Definition(ZombieDetectionType.HearingOnly, hearingRadius: 5f), Direction.East);
            Build();

            _sounds.Emit(SoundType.Step, new Vector2(2f, 3f), 3f);
            Run(0.1f);
            Assert.AreEqual(ZombieState.Idle, Zombie().State, "4 cells away, the step carries 3.");

            _sounds.Emit(SoundType.Ranged, new Vector2(2f, 3f), 8f);
            Run(0.1f);
            Assert.AreEqual(ZombieState.Alert, Zombie().State, "A shot carries 8, the zombie hears 5.");
        }

        [Test]
        public void VisionAndHearing_UsesOneRadius_FacingDoesNotMatter()
        {
            AddZombie(6, 3, Definition(ZombieDetectionType.VisionAndHearing, detectionRadius: 4f), Direction.East);
            AddZombie(7, 5, Definition(ZombieDetectionType.VisionAndHearing, detectionRadius: 4f), Direction.West);
            Build();
            Run(0.2f);

            Assert.AreEqual(ZombieState.Alert, Zombie(0).State, "Exactly 4 cells away, facing away.");
            Assert.AreEqual(ZombieState.Idle, Zombie(1).State, "5.4 cells away.");
        }

        [Test]
        public void VisionAndHearing_WallsAndClosedDoorsBlock_ButSoundsAreHeard()
        {
            AddZombie(10, 3, Definition(ZombieDetectionType.VisionAndHearing, detectionRadius: 12f, hearingRadius: 5f));
            AddZombie(9, 1, Definition(ZombieDetectionType.VisionAndHearing, detectionRadius: 12f, hearingRadius: 5f));
            Build();
            _doors.SetOpen("door_1", false);
            Run(0.3f);

            Assert.AreEqual(ZombieState.Idle, Zombie(0).State, "In the radius, but behind the closed door.");
            Assert.AreEqual(ZombieState.Idle, Zombie(1).State, "In the radius, but behind the wall.");

            _sounds.Emit(SoundType.Ranged, new Vector2(6f, 3f), 8f);
            Run(Dt);
            Assert.AreEqual(ZombieState.Alert, Zombie(0).State, "Hears a sound 4 cells away (hearing radius 5).");
            Assert.AreEqual(ZombieState.Alert, Zombie(1).State, "3.6 cells away.");
        }

        [Test]
        public void DamageWithoutDetection_CausesNoReaction()
        {
            AddZombie(6, 3, Definition(ZombieDetectionType.VisionOnly), Direction.East);
            Build();
            Zombie().ApplyDamage(10f, Vector2.right);
            Run(0.5f);

            Assert.AreEqual(ZombieState.Idle, Zombie().State, "ТЗ §76: no automatic reaction.");
            Assert.AreEqual(20f, Zombie().Health);
        }

        // ------------------------------------------------------------------ Chase, attack, occupancy

        [Test]
        public void Chase_StopsNextToThePlayer_AndAttacksAtItsInterval()
        {
            AddZombie(6, 3, Definition(ZombieDetectionType.VisionAndHearing, damage: 10f, attackInterval: 1f));
            AddZombie(6, 3, Definition(ZombieDetectionType.VisionAndHearing, damage: 10f, attackInterval: 1f));
            Build();
            var attacks = 0;
            _zombies.Attacked += zombie => attacks++;

            RunUntil(() => Zombie(0).State == ZombieState.Attack && Zombie(1).State == ZombieState.Attack);
            Assert.AreEqual(new GridPosition(3, 3), Zombie(0).Cell, "Stops in the neighbouring cell.");
            Assert.AreEqual(new GridPosition(3, 3), Zombie(1).Cell, "Zombies share cells.");
            Assert.AreEqual(2, _occupancy.ZombieCount(new GridPosition(3, 3)));
            Assert.AreEqual(new GridPosition(2, 3), _occupancy.PlayerCell, "Never in the player's cell.");

            // Detection samples are spread over zombies, so the second one arrived a few frames later.
            var hp = _health.Current;
            Run(0.3f);
            Assert.AreEqual(hp, _health.Current, "First hit only after half an interval.");
            Run(0.3f);
            Assert.AreEqual(hp - 20, _health.Current, "Both zombies hit.");
            Run(1f);
            Assert.AreEqual(hp - 40, _health.Current, "Then once per interval each.");
            Assert.AreEqual(4, attacks);
        }

        [Test]
        public void Patrol_Walks_Chase_RunsAtChaseSpeed()
        {
            AddZombie(10, 3, Definition(ZombieDetectionType.VisionOnly, visionAngle: 10f, visionRange: 1f, moveSpeed: 1f,
                chaseSpeed: 3f), Direction.North, new GridPosition(10, 1), new GridPosition(10, 5));
            AddZombie(7, 3, Definition(ZombieDetectionType.VisionAndHearing, detectionRadius: 12f, moveSpeed: 1f,
                chaseSpeed: 3f));
            Build();
            var patrolStart = Zombie(0).Position;

            Run(0.5f);
            Assert.AreEqual(ZombieState.Patrol, Zombie(0).State);
            Assert.AreEqual(0.5f, (Zombie(0).Position - patrolStart).magnitude, 0.05f, "Patrol walks at MoveSpeed.");

            RunUntil(() => Zombie(1).State == ZombieState.Chase);
            var chaseStart = Zombie(1).Position;
            Run(0.5f);
            Assert.AreEqual(ZombieState.Chase, Zombie(1).State);
            Assert.AreEqual(1.5f, (Zombie(1).Position - chaseStart).magnitude, 0.05f, "Chase runs at ChaseSpeed.");
        }

        [Test]
        public void Alert_RoarsInPlace_ThenChases()
        {
            AddZombie(7, 3, Definition(ZombieDetectionType.VisionAndHearing, detectionRadius: 12f), Direction.North);
            Build();

            RunUntil(() => Zombie().State == ZombieState.Alert);
            var position = Zombie().Position;
            Assert.AreEqual(-1f, Zombie().Facing.x, 1e-3f, "Turns toward the player.");

            Run(ZombieController.AlertDuration - 0.05f);
            Assert.AreEqual(ZombieState.Alert, Zombie().State, "Still roaring.");
            Assert.AreEqual(position, Zombie().Position, "Does not move while roaring.");
            Assert.AreEqual(0f, Zombie().SpeedFactor);

            Run(0.1f);
            Assert.AreEqual(ZombieState.Chase, Zombie().State, "Chases after the roar.");
        }

        [Test]
        public void Alert_PlayerNextToIt_AttacksAtOnce()
        {
            AddZombie(3, 3, Definition(ZombieDetectionType.VisionAndHearing, detectionRadius: 4f));
            Build();

            RunUntil(() => Zombie().State != ZombieState.Idle);
            Run(Dt * 2f);
            Assert.AreEqual(ZombieState.Attack, Zombie().State, "The roar is cut short.");
        }

        [Test]
        public void Alert_RepeatsBeforeEveryNewChase()
        {
            AddZombie(10, 3, Definition(ZombieDetectionType.VisionOnly, visionAngle: 360f, visionRange: 9f), Direction.West,
                new GridPosition(10, 1), new GridPosition(10, 5));
            Build();
            RunUntil(() => Zombie().State == ZombieState.Alert);
            RunUntil(() => Zombie().State == ZombieState.Chase);

            _doors.SetOpen("door_1", false); // lost: returns to the patrol
            RunUntil(() => Zombie().State == ZombieState.Patrol);

            _doors.SetOpen("door_1", true); // seen again
            RunUntil(() => Zombie().State != ZombieState.Patrol);
            Assert.AreEqual(ZombieState.Alert, Zombie().State, "Roars again before the new chase.");
        }

        [Test]
        public void Chase_DoesNotSearchPathsEveryFrame()
        {
            AddZombie(10, 3, Definition(ZombieDetectionType.VisionAndHearing, detectionRadius: 12f));
            Build();
            RunUntil(() => Zombie().State == ZombieState.Attack);
            var searches = _navigation.SearchCount;
            Run(2f);

            Assert.LessOrEqual(searches, 3, "One search for a still target (plus at most a re-search).");
            Assert.AreEqual(searches, _navigation.SearchCount, "No searches while attacking.");
        }

        [Test]
        public void LostTarget_ReturnsToNearestPatrolPoint_AndContinuesPatrol()
        {
            AddZombie(10, 3, Definition(ZombieDetectionType.VisionOnly, visionAngle: 360f, visionRange: 9f), Direction.West,
                new GridPosition(10, 1), new GridPosition(10, 5));
            Build();
            RunUntil(() => Zombie().State == ZombieState.Chase);

            _doors.SetOpen("door_1", false); // the player is now unreachable and out of sight
            RunUntil(() => Zombie().State == ZombieState.Return);
            RunUntil(() => Zombie().State == ZombieState.Patrol);
            Assert.AreEqual(new GridPosition(10, 1), Zombie().Cell, "Nearest patrol point (first of two equal).");

            RunUntil(() => Zombie().Cell == new GridPosition(10, 5));
            Assert.AreEqual(ZombieState.Patrol, Zombie().State, "Continues the loop A → B → A.");
        }

        [Test]
        public void LostTarget_WithoutPatrol_ReturnsToSpawnAndIdles()
        {
            AddZombie(10, 3, Definition(ZombieDetectionType.VisionOnly, visionAngle: 360f, visionRange: 9f), Direction.North);
            Build();
            RunUntil(() => Zombie().State == ZombieState.Chase);
            RunUntil(() => Zombie().Cell.X <= 9);

            _doors.SetOpen("door_1", false);
            RunUntil(() => Zombie().State == ZombieState.Idle);
            Assert.AreEqual(new GridPosition(10, 3), Zombie().Cell);
            Assert.AreEqual(Vector2.up, Zombie().Facing, "Back to its spawn facing.");
        }

        // ------------------------------------------------------------------ Death

        [Test]
        public void Killed_ZombieLeavesOccupancyAndQueries()
        {
            AddZombie(6, 3, Definition(ZombieDetectionType.VisionOnly), Direction.East);
            Build();
            Assert.AreEqual(1, _occupancy.ZombieCount(new GridPosition(6, 3)));

            Zombie().ApplyDamage(100f, Vector2.right);

            Assert.AreEqual(ZombieState.Dead, Zombie().State);
            Assert.AreEqual(0, _occupancy.ZombieCount(new GridPosition(6, 3)));
            Assert.AreEqual(0, _spatial.Objects.Count);
            Assert.AreEqual(1, _zombies.KilledCount);
            Assert.IsTrue(_zombies.AllKilled);
        }

        [Test]
        public void PlayerDeath_FailsTheLevel_AndStopsTheFrame()
        {
            Build();
            var runtime = new LevelRuntime(_level, new ILevelLoadStep[0], new ILevelTickable[0]);
            runtime.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            runtime.StartGameplay();
            LevelOutcome? outcome = null;
            runtime.Finished += result => outcome = result;
            var rule = new PlayerDeathRule(_health, runtime);

            _health.Damage(_health.Max);

            Assert.AreEqual(LevelOutcome.Failed, outcome);
            Assert.AreEqual(LevelRunState.Stopped, runtime.State);
            rule.Dispose();
        }
    }
}
