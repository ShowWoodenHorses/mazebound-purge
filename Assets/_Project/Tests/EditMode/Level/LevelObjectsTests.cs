using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Drops;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Objects;
using Purge.Core.Waves;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Drops;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Objects;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Waves;
using Purge.Gameplay.Weapons;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Level
{
    /// <summary>Stage 7в: barrels, chests (common, locked, challenge), the chest key, the safe dome.</summary>
    public class LevelObjectsTests
    {
        private const float Dt = 1f / 60f;

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private PlayerDefinition _playerDefinition;
        private MonsterDefinition _mass, _officer, _elite;
        private HordeDefinition _hordeData;
        private ContentCatalog _catalog;
        private ZoneDefinition _poison;
        private DropTable _table;
        private WeaponDefinition _rifle;

        private LevelGrid _grid;
        private CellBlockers _blockers;
        private LevelPassability _passability;
        private SpatialQueryService _spatial;
        private PlayerSystem _player;
        private PlayerHealth _health;
        private PlayerInventory _inventory;
        private HordeSystem _horde;
        private CombatTargets _targets;
        private ZoneSystem _zones;
        private EffectSystem _effects;
        private PickupSystem _pickups;
        private DropPool _pool;
        private WaveSystem _waves;
        private BarrelSystem _barrels;
        private ChestSystem _chests;
        private RefugeSystem _refuge;
        private LevelRuntime _runtime;
        private readonly List<System.IDisposable> _disposables = new List<System.IDisposable>();

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        /// <summary>40×21 open floor, the player at (5, 10); waves (one rank-and-file each) after a long delay.</summary>
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

            _playerDefinition = Create<PlayerDefinition>();
            _playerDefinition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _mass = Monster("mass", MonsterRank.Mass, 30f);
            _officer = Monster("officer", MonsterRank.Officer, 60f);
            _elite = Monster("elite", MonsterRank.Elite, 100f);

            _poison = Create<ZoneDefinition>();
            _poison.Configure("poison", new ZoneDamage { DamagePerSecond = 5f });
            _table = Create<DropTable>();
            _table.Configure("table", nothing: 100f, ammo: 0f, medkit: 0f, throwable: 0f, weapon: 1f);
            _rifle = Create<WeaponDefinition>();
            _rifle.Configure("rifle", new BulletAttack(), new AmmoModule());

            _hordeData = Create<HordeDefinition>();
            _hordeData.ConfigureCapacity(64, 8);
            _hordeData.ConfigureMonsters(new[] { new HordeSpawn(_mass, 1f), new HordeSpawn(_officer, 1f), new HordeSpawn(_elite, 0f) });
            _hordeData.ConfigureDrops(_table, new[] { _rifle }, new ThrowableDefinition[0]);
            var scenario = Create<WaveScenario>();
            scenario.Configure(new[]
            {
                new WaveDefinition(1f, new[] { new WaveMonster(_mass, 1f), new WaveMonster(_officer, 1f) }),
                new WaveDefinition(1f, new[] { new WaveMonster(_mass, 1f) }),
            }, pauseTime: 5f, firstDelay: 100f);
            _hordeData.SetWaves(scenario);
            _level.Horde = _hordeData;

            _catalog = Create<ContentCatalog>();
            _catalog.MutableItems.Add(Barrel(BarrelKind.Gas));
            foreach (ChestKind kind in System.Enum.GetValues(typeof(ChestKind)))
            {
                var chest = Create<ChestDefinition>();
                chest.Configure("chest_" + kind, kind, _table, openTime: 1f, champions: 3, championRadius: 4f);
                _catalog.MutableItems.Add(chest);
            }

            var key = Create<ChestKeyDefinition>();
            key.Configure("chest_key");
            _catalog.MutableItems.Add(key);
            var refuge = Create<RefugeDefinition>();
            refuge.Configure("refuge", waveTime: 2f, healPerSecond: 0.1f, pushTime: 0.3f);
            _catalog.MutableItems.Add(refuge);
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = _disposables.Count - 1; i >= 0; i--) _disposables[i].Dispose();
            _disposables.Clear();
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

        private MonsterDefinition Monster(string id, MonsterRank rank, float hp)
        {
            var monster = Create<MonsterDefinition>();
            monster.Configure(id, rank, MonsterBiome.Common, new MonsterStats { MaxHp = hp, Speed = 2f, Damage = 0, EmergeTime = 0.1f });
            monster.SetPerception(MonsterPerception.Hunter);
            return monster;
        }

        private BarrelDefinition Barrel(BarrelKind kind)
        {
            var explosion = new ExplosionEffect { Damage = 60f, EdgeDamage = 1f, SoundRadius = 5f };
            explosion.SetRadius(2f);
            var zone = new PlaceZoneEffect { Zone = _poison, Duration = 6f, Strength = 1f };
            zone.SetRadius(2f);
            var barrel = Create<BarrelDefinition>();
            barrel.Configure("barrel_" + kind, kind, 30f, explosion, zone);
            return barrel;
        }

        private void Load(LevelLaunchOptions options = null)
        {
            options ??= new LevelLaunchOptions(startIndex: 0, seed: 5);
            _grid = new LevelGrid(_level.Geometry);
            var doors = new DoorSystem(_level);
            _blockers = new CellBlockers(_grid);
            _passability = new LevelPassability(_grid, doors, _blockers);
            _spatial = new SpatialQueryService(_grid, doors);
            _player = new PlayerSystem(_level, _playerDefinition, new FakePlayerInput(), _passability, new OccupancyMap(_grid),
                options, new Aim());
            _health = new PlayerHealth(_playerDefinition);
            _inventory = new PlayerInventory();
            _horde = new HordeSystem(_level, _grid, _passability, doors, _player, _health, options: options);
            _horde.GodMode = false;
            _targets = new CombatTargets(_spatial, _horde);
            var statuses = new StatusSystem(_horde, _targets);
            _zones = new ZoneSystem(_grid, doors, _targets, _horde, statuses, _player, _health);
            _effects = new EffectSystem(_targets, _zones, statuses, new SoundEventBus(), _player, _health, null, _horde);
            _pickups = new PickupSystem(_level, _player, _health, _inventory, new WeaponSystem(new FakePlayerInput(), _playerDefinition),
                new SoundEventBus());
            _pool = new DropPool(_level, _catalog);
            _waves = new WaveSystem(_level, _horde, _player, options, _pickups);
            _barrels = new BarrelSystem(_level, _spatial, _blockers, _effects, _zones, _catalog, options);
            _chests = new ChestSystem(_level, _blockers, _player, _inventory, _pickups, _pool, null, _horde, _passability, _waves,
                _catalog, options);
            _refuge = new RefugeSystem(_level, _spatial, _horde, _zones, _player, _health, _waves, _catalog);
            _disposables.AddRange(new System.IDisposable[] { _pickups, _horde, statuses, _barrels, _chests, _refuge });
            _runtime = new LevelRuntime(_level,
                new ILevelLoadStep[] { _player, _pickups, _pool, _zones, _barrels, _chests, _horde, statuses, _waves, _refuge },
                new ILevelTickable[] { _player, _pickups, _chests, _horde, statuses, _zones, _waves, _refuge, _barrels });
            _runtime.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            _runtime.StartGameplay();
        }

        private void Tick(float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt) _runtime.Tick(Dt);
        }

        /// <summary>Walks the player there in small steps, along x first, then y (walls and objects stop it).</summary>
        private void MovePlayer(float x, float y)
        {
            for (var i = 0; i < 400 && Mathf.Abs(_player.Position.x - x) > 1e-3f; i++)
                _player.Displace(new Vector2(Mathf.Clamp(x - _player.Position.x, -0.25f, 0.25f), 0f));
            for (var i = 0; i < 400 && Mathf.Abs(_player.Position.y - y) > 1e-3f; i++)
                _player.Displace(new Vector2(0f, Mathf.Clamp(y - _player.Position.y, -0.25f, 0.25f)));
            Assert.That((_player.Position - new Vector2(x, y)).magnitude, Is.LessThan(0.01f), "the player got there");
        }

        // ------------------------------------------------------------------ Barrels

        [Test]
        public void Barrel_StopsShots_BlocksTheCell_BlowsUpFromThePlayersDamage_TheCellIsFree()
        {
            _level.MutableBarrels.Add(new BarrelData("barrel_1", new GridPosition(12, 10), BarrelKind.Gas));
            Load();
            var cell = new GridPosition(12, 10);
            Assert.IsFalse(_passability.IsWalkable(cell), "a barrel stands in its cell");
            var hit = _targets.Segment(new Vector2(8f, 10f), new Vector2(16f, 10f), 0);
            Assert.AreEqual(CombatHitKind.Target, hit.Kind);
            Assert.IsInstanceOf<Barrel>(hit.Target.Object);

            var monster = _horde.SpawnAt(new Vector2(13.5f, 10f), MonsterPerception.Hunter, _horde.KindIndexOf(_mass));
            _targets.Damage(hit.Target, 20f, Vector2.right);
            Assert.AreEqual(1, _barrels.Barrels.Count, "30 HP: still standing");
            _targets.Damage(hit.Target, 20f, Vector2.right);
            Assert.AreEqual(1, _barrels.Barrels.Count, "it blows up a moment later");
            Tick(0.2f);
            Assert.AreEqual(0, _barrels.Barrels.Count);
            CollectionAssert.AreEqual(new[] { "barrel_1" }, _barrels.BrokenIds);
            Assert.IsTrue(_passability.IsWalkable(cell), "the cell is free");
            Assert.IsFalse(HordeSystem.IsAlive(_horde.States[monster]), "60 damage kills the monster next to it");
            Assert.IsNotNull(_zones.ZoneAt(cell), "its zone covers the cells around");
            Assert.IsTrue(_health.IsFull, "the player's own blast never hurts the player");
        }

        [Test]
        public void Barrel_MonstersBlastsSpareIt_ABlastBreaksTheNextOne_AsAChain()
        {
            _level.MutableBarrels.Add(new BarrelData("barrel_1", new GridPosition(12, 10), BarrelKind.Gas));
            _level.MutableBarrels.Add(new BarrelData("barrel_2", new GridPosition(13, 11), BarrelKind.Gas));
            Load();
            _targets.Blast(new Vector2(12.5f, 10.5f), 500f, 3f, 1f, 0, hostile: true);
            Tick(0.3f);
            Assert.AreEqual(2, _barrels.Barrels.Count, "a monster's blast does not break barrels");

            _targets.Blast(new Vector2(11f, 10f), 40f, 1.2f, 1f, 0, kind: PlayerDamage.Explosion);
            Tick(0.05f);
            Assert.AreEqual(2, _barrels.Barrels.Count, "waits its moment");
            Tick(0.1f);
            Assert.AreEqual(1, _barrels.Barrels.Count, "the first one is gone");
            Tick(0.2f);
            Assert.AreEqual(0, _barrels.Barrels.Count, "its blast broke the second one");
        }

        [Test]
        public void Barrel_BrokenInTheRun_IsNotThereInAContinuedOne()
        {
            _level.MutableBarrels.Add(new BarrelData("barrel_1", new GridPosition(12, 10), BarrelKind.Gas));
            var resume = new Purge.Gameplay.Run.RunSnapshot { Seed = 5 };
            resume.BrokenBarrels.Add("barrel_1");
            Load(new LevelLaunchOptions(startIndex: 0, seed: 5, resume: resume));
            Assert.AreEqual(0, _barrels.Barrels.Count);
            Assert.IsTrue(_passability.IsWalkable(new GridPosition(12, 10)));
        }

        // ------------------------------------------------------------------ Chests

        [Test]
        public void CommonChest_OpensAfterItsTime_StepAwayStops_GivesAnItemThatAWaveDoesNotClear()
        {
            _level.MutableChests.Add(new ChestData("chest_1", new GridPosition(7, 10), ChestKind.Common));
            Load();
            Assert.IsFalse(_passability.IsWalkable(new GridPosition(7, 10)), "a chest stands in its cell");
            MovePlayer(6f, 10f);
            var chest = _chests.FindUsable();
            Assert.IsNotNull(chest);
            Assert.AreEqual(ChestUse.Opening, _chests.TryUse(chest));
            Tick(0.5f);
            Assert.AreEqual(ChestState.Opening, chest.State);
            MovePlayer(3f, 10f);
            Tick(0.1f);
            Assert.AreEqual(ChestState.Closed, chest.State, "stepped away: the opening stops");

            MovePlayer(6f, 10f);
            _chests.TryUse(chest);
            _health.Damage(30);
            Tick(1.1f);
            Assert.AreEqual(ChestState.Open, chest.State, "damage does not stop it");
            Assert.AreEqual(1, _pickups.Count, "one item, never nothing (the table is 100 nothing : 1 weapon)");
            CollectionAssert.AreEqual(new[] { "chest_1" }, _chests.OpenedIds);
            Assert.IsNull(_chests.FindUsable(), "once per run");

            _pickups.ClearDropped();
            Assert.AreEqual(1, _pickups.Count, "the reward lies until taken");
        }

        [Test]
        public void LockedChest_NeedsTheKey_TheKeyDropsFromTheFirstEliteOnly()
        {
            _level.MutableChests.Add(new ChestData("chest_1", new GridPosition(7, 8), ChestKind.Locked));
            Load();
            MovePlayer(6f, 8f);
            var chest = _chests.FindUsable();
            Assert.AreEqual(ChestUse.NeedsKey, _chests.TryUse(chest));

            var officer = _horde.SpawnAt(new Vector2(20f, 10f), MonsterPerception.Hunter, _horde.KindIndexOf(_officer));
            _horde.Damage(officer, 1000f);
            Assert.IsFalse(_chests.KeyDropped, "an officer has no key");
            var elite = _horde.SpawnAt(new Vector2(20f, 12f), MonsterPerception.Hunter, _horde.KindIndexOf(_elite));
            _horde.Damage(elite, 1000f);
            Assert.IsTrue(_chests.KeyDropped);
            Assert.IsTrue(_pickups.ChestKeyLying);
            var second = _horde.SpawnAt(new Vector2(20f, 14f), MonsterPerception.Hunter, _horde.KindIndexOf(_elite));
            _horde.Damage(second, 1000f);
            Assert.AreEqual(1, CountKeys(), "one key per level");

            _pickups.ClearDropped();
            Assert.IsTrue(_pickups.ChestKeyLying, "the key does not go with the wave's drop");
            MovePlayer(6f, 12f);
            MovePlayer(20f, 12f);
            Assert.AreEqual(1, _inventory.ChestKeys);

            MovePlayer(6f, 12f);
            MovePlayer(6f, 8f);
            Assert.AreEqual(ChestUse.Opening, _chests.TryUse(chest));
            Tick(1.1f);
            Assert.AreEqual(ChestState.Open, chest.State);
            Assert.AreEqual(0, _inventory.ChestKeys, "the key is spent");
            var lying = new List<Pickup>();
            _pickups.GetAll(lying);
            Assert.IsTrue(lying.Exists(p => p.Kind == PickupKind.Weapon), "a weapon");
        }

        private int CountKeys()
        {
            var lying = new List<Pickup>();
            _pickups.GetAll(lying);
            return lying.FindAll(p => p.Kind == PickupKind.ChestKey).Count;
        }

        [Test]
        public void ChallengeChest_CallsChampions_TheyDoNotHoldTheWave_TheRewardWhenTheyAreDead()
        {
            _level.MutableChests.Add(new ChestData("chest_1", new GridPosition(20, 10), ChestKind.Challenge));
            Load();
            MovePlayer(19f, 10f);
            var chest = _chests.FindUsable();
            _chests.TryUse(chest);
            Tick(1.1f);
            Assert.AreEqual(ChestState.Challenge, chest.State);
            Assert.AreEqual(3, chest.ChampionsLeft);
            Assert.AreEqual(3, _horde.OutsideWaveAlive);
            Assert.AreEqual(0, _horde.WaveAliveCount, "they are not the wave's");
            CollectionAssert.IsEmpty(_chests.OpenedIds, "not opened until the challenge is done");

            for (var i = 0; i < _horde.HighWater; i++)
                if (HordeSystem.IsAlive(_horde.States[i])) _horde.Damage(i, 10000f);
            Assert.AreEqual(ChestState.Open, chest.State);
            Assert.AreEqual(0, _horde.OutsideWaveAlive);
            var lying = new List<Pickup>();
            _pickups.GetAll(lying);
            Assert.IsTrue(lying.Exists(p => p.Kind == PickupKind.Weapon), "the locked chest's reward");
        }

        [Test]
        public void ContinuedRun_OpenedChestsStayOpen_TheDroppedKeyIsTheplayers()
        {
            _level.MutableChests.Add(new ChestData("chest_1", new GridPosition(7, 10), ChestKind.Common));
            _level.MutableChests.Add(new ChestData("chest_2", new GridPosition(7, 14), ChestKind.Locked));
            var resume = new Purge.Gameplay.Run.RunSnapshot { Seed = 5, ChestKeyDropped = true };
            resume.OpenedChests.Add("chest_1");
            Load(new LevelLaunchOptions(startIndex: 0, seed: 5, resume: resume));
            Assert.AreEqual(ChestState.Open, _chests.Chests[0].State);
            Assert.AreEqual(ChestState.Closed, _chests.Chests[1].State);
            Assert.AreEqual(1, _inventory.ChestKeys);
        }

        // ------------------------------------------------------------------ The safe dome

        [Test]
        public void Refuge_StopsShotsAcrossItsEdge_NotSight_MonstersStayOut_Heals()
        {
            _level.Settings.Refuge = true;
            _level.Settings.RefugeRadius = 2;
            Load();
            Assert.IsTrue(_refuge.IsUp);
            Assert.AreEqual(2.5f, _refuge.Edge);

            var shot = _spatial.QuerySegment(new Vector2(12f, 10f), new Vector2(5f, 10f));
            Assert.AreEqual(SegmentHitKind.Blocker, shot.Kind, "a shot from outside stops at the edge");
            Assert.That(shot.Point.x, Is.EqualTo(7.5f).Within(0.01f));
            Assert.AreEqual(SegmentHitKind.Blocker, _spatial.QuerySegment(new Vector2(5f, 10f), new Vector2(12f, 10f)).Kind,
                "from inside too");
            Assert.IsFalse(_spatial.IsClear(new Vector2(12f, 10f), new Vector2(5f, 10f), CellFlags.BlocksBullets));
            Assert.IsTrue(_spatial.IsClear(new Vector2(12f, 10f), new Vector2(5f, 10f)), "sight passes");
            Assert.AreEqual(SegmentHitKind.Blocker, _spatial.QuerySegment(new Vector2(5f, 16f), new Vector2(5f, 4f)).Kind,
                "a shot through the dome stops where it enters");

            Assert.AreEqual(-1, _horde.SpawnAt(new Vector2(6f, 10f), MonsterPerception.Hunter, 0), "no monster under it");
            var monster = _horde.SpawnAt(new Vector2(10f, 10f), MonsterPerception.Hunter, _horde.KindIndexOf(_mass));
            _health.Damage(50);
            var hurt = _health.Current;
            Tick(3f);
            Assert.GreaterOrEqual((_horde.Positions[monster] - _refuge.Center).magnitude, _refuge.Edge,
                "the monster crowds at the edge");
            Assert.Greater(_health.Current, hurt, "it heals the player inside");
        }

        [Test]
        public void Refuge_InAWave_LimitedTimeInside_ThenPushesOut_AndGoesOut_UntilThePause()
        {
            _level.Settings.Refuge = true;
            Load();
            Tick(1f);
            Assert.AreEqual(2f, _refuge.TimeLeft, "before the first wave: no limit");
            _waves.StartNextWave();
            Tick(1f);
            Assert.That(_refuge.TimeLeft, Is.EqualTo(1f).Within(0.05f), "the time inside counts in a wave");
            MovePlayer(12f, 10f);
            Tick(1f);
            Assert.That(_refuge.TimeLeft, Is.EqualTo(1f).Within(0.05f), "outside it does not");
            MovePlayer(5f, 10f);
            Tick(1.5f);
            Assert.IsFalse(_refuge.IsUp, "the time is over: it went out");
            Assert.GreaterOrEqual((_player.Position - _refuge.Center).magnitude, 2.5f, "the player was pushed out");

            for (var i = 0; i < _horde.HighWater; i++)
                if (HordeSystem.IsAlive(_horde.States[i])) _horde.Damage(i, 10000f);
            Tick(3f);
            Assert.AreEqual(WavePhase.Pause, _waves.Phase);
            Assert.IsTrue(_refuge.IsUp, "the pause lights it up again");
            Assert.AreEqual(2f, _refuge.TimeLeft);
        }
    }
}
