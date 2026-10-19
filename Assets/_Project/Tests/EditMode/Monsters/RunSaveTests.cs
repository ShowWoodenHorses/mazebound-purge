using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Cards;
using Purge.Core.Content;
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
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Run;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Waves;
using Purge.Gameplay.Weapons;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Monsters
{
    /// <summary>Stage 6e: the run's checkpoints, the start weapon and a continued run.</summary>
    public class RunSaveTests
    {
        private readonly List<Object> _created = new List<Object>();
        private readonly List<RunSnapshot> _reached = new List<RunSnapshot>();
        private LevelData _level;
        private PlayerDefinition _playerDefinition;
        private CardDeck _deck;
        private ContentCatalog _content;
        private WeaponDefinition _rifle;
        private ThrowableDefinition _grenade;
        private CardDefinition _vitality;
        private WaveScenario _scenario;

        private PlayerHealth _health;
        private WeaponSystem _weapons;
        private ThrowableSlot _slot;
        private RunWallet _wallet;
        private RunScore _score;
        private RunExperience _experience;
        private CardSystem _cards;
        private WaveSystem _waves;
        private LevelProgress _progress;
        private RunCheckpoints _checkpoints;
        private PickupSystem _pickups;
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
            var geometry = new LevelGeometry(30, 30);
            for (var y = 1; y < 29; y++)
            for (var x = 1; x < 29; x++)
                geometry.SetCell(new GridPosition(x, y), CellType.Floor);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(15, 15)));
            _playerDefinition = Create<PlayerDefinition>();
            _playerDefinition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);

            _rifle = Create<WeaponDefinition>();
            _rifle.Configure("rifle", new BulletAttack(), new AmmoModule { Magazine = 30 });
            _grenade = Create<ThrowableDefinition>();
            _grenade.Configure("grenade", 10, 8f);
            _vitality = Create<CardDefinition>();
            _vitality.Configure("vitality", Rarity.Common, 5, new StatCard { Stat = RunStat.MaxHealth, PerLevel = 15f });
            _deck = Create<CardDeck>();
            _deck.Configure(10f, 1.25f, 3);
            _deck.SetCards(new[] { _vitality });
            _content = Create<ContentCatalog>();
            _content.MutableItems.AddRange(new ContentDefinition[] { _rifle, _grenade, _vitality });

            var mass = Create<MonsterDefinition>();
            mass.Configure("mass", MonsterRank.Mass, MonsterBiome.Common, new MonsterStats { MaxHp = 30f, Speed = 2f });
            _scenario = Create<WaveScenario>();
            var wave = new WaveDefinition(5f, new[] { new WaveMonster(mass, 1f) });
            _scenario.Configure(new[] { wave, wave, wave }, pauseTime: 7f, firstDelay: 2f);
            var horde = Create<HordeDefinition>();
            horde.ConfigureCapacity(50, 4);
            horde.SetWaves(_scenario);
            _level.Horde = horde;
        }

        [TearDown]
        public void TearDown()
        {
            _checkpoints?.Dispose();
            _cards?.Dispose();
            _experience?.Dispose();
            _score?.Dispose();
            _wallet?.Dispose();
            _pickups?.Dispose();
            _checkpoints = null;
            _reached.Clear();
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

        private void Load(LevelLaunchOptions options)
        {
            var grid = new LevelGrid(_level.Geometry);
            var doors = new DoorSystem(_level);
            var passability = new LevelPassability(grid, doors);
            var player = new PlayerSystem(_level, _playerDefinition, new FakePlayerInput(), passability, new OccupancyMap(grid),
                new LevelLaunchOptions(startIndex: 0), new Aim());
            _health = new PlayerHealth(_playerDefinition);
            _horde = new HordeSystem(_level, grid, passability, doors, player, _health, options: options);
            _slot = new ThrowableSlot();
            _weapons = new WeaponSystem(new FakePlayerInput(), _playerDefinition);
            _pickups = new PickupSystem(_level, player, _health, new PlayerInventory(), _weapons, new SoundEventBus(),
                new AmmoStock(), _slot);
            var stats = new RunStats();
            _experience = new RunExperience(_horde, _deck);
            _cards = new CardSystem(_experience, stats, _health, player, _slot, _pickups, _horde, options, _deck);
            _wallet = new RunWallet(_horde);
            _waves = new WaveSystem(_level, _horde, player, options, _pickups, _wallet);
            _score = new RunScore(_horde, _waves);
            var revival = new PlayerRevival(_health, player);
            _progress = new LevelProgress(null, _score, _wallet, _waves, revival);
            var loadout = new RunLoadout(_level, _weapons, _pickups, _health, _slot, _cards, options, revival, _content);
            _checkpoints = new RunCheckpoints(_waves, _wallet, _score, _experience, _cards, _health, _weapons, _slot, _progress,
                options, revival);
            _checkpoints.Reached += _reached.Add;
            var runtime = new LevelRuntime(_level,
                new ILevelLoadStep[] { player, _pickups, loadout, _horde, _wallet, _score, _experience, _cards, _waves, _checkpoints },
                new ILevelTickable[] { _pickups, _horde, _waves, _progress });
            runtime.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            runtime.StartGameplay();
        }

        [Test]
        public void NewRun_StartWeapon_CheckpointsAtWaveStartAndClear()
        {
            _level.StartWeapon = _rifle;
            Load(new LevelLaunchOptions(seed: 11));

            Assert.That(_checkpoints.IsActive);
            Assert.That(_weapons.Active.Definition, Is.SameAs(_rifle), "the level's start weapon in hands");
            Assert.That(_weapons.IsEmpty(WeaponSlot.Second), "the pistol in the other slot");

            _waves.StartNextWave();
            Assert.That(_reached.Count, Is.EqualTo(1));
            var start = _reached[0];
            Assert.That(start.WaveInProgress && start.Wave == 0);
            Assert.That(start.Seed, Is.EqualTo(11));
            Assert.That(start.Weapons[0], Is.EqualTo("rifle"));
            Assert.That(start.Weapons[1], Is.Null.Or.Empty);
            Assert.That(start.NextWave, Is.EqualTo(0), "left mid-wave: the wave again");
        }

        [Test]
        public void SkippedPause_ItsBonusLeftOut_OfTheWaveStart()
        {
            Load(new LevelLaunchOptions(seed: 3, resume: new RunSnapshot { Seed = 3, Wave = 0, Money = 10 }));
            Assert.That(_waves.Phase, Is.EqualTo(WavePhase.Pause), "continued: the pause before wave 2");
            Assert.That(_waves.WaveIndex, Is.EqualTo(0));
            Assert.That(_waves.TimeLeft, Is.EqualTo(_scenario.PauseTime));

            var bonus = _waves.SkipBonus;
            Assert.That(bonus, Is.GreaterThan(0));
            Assert.That(_waves.Skip());
            Assert.That(_wallet.Money, Is.EqualTo(10 + bonus));
            Assert.That(_reached[_reached.Count - 1].Money, Is.EqualTo(10), "the continued pause can be skipped again");
            Assert.That(_reached[_reached.Count - 1].Wave, Is.EqualTo(1));
        }

        [Test]
        public void ContinuedRun_GetsItsStateBack()
        {
            var run = new RunSnapshot
            {
                Seed = 9, Wave = 1, WaveInProgress = true, Money = 42, Score = 300, Kills = 25, WavesCleared = 1,
                Experience = 4, Level = 3, Health = 40, AdReviveUsed = true, Time = 75f, ActiveSlot = 1,
                ThrowableId = "grenade", ThrowableCount = 2,
            };
            run.Cards.Add("vitality");
            run.Cards.Add("vitality");
            run.Weapons[1] = "rifle";
            run.Ammo[1] = 7;
            Load(new LevelLaunchOptions(resume: run));

            Assert.That(_wallet.Money, Is.EqualTo(42));
            Assert.That(_score.Kills, Is.EqualTo(25));
            Assert.That(_score.Score, Is.EqualTo(300));
            Assert.That(_score.WavesCleared, Is.EqualTo(1));
            Assert.That(_experience.Level, Is.EqualTo(3));
            Assert.That(_experience.Experience, Is.EqualTo(4));
            Assert.That(_cards.LevelOf(_vitality), Is.EqualTo(2));
            Assert.That(_health.Max, Is.EqualTo(_playerDefinition.MaxHealth + 30));
            Assert.That(_health.Current, Is.EqualTo(40), "HP of the checkpoint, not the cards' heal");
            Assert.That(_weapons.ActiveSlot, Is.EqualTo(WeaponSlot.Second));
            Assert.That(_weapons.Active.Definition, Is.SameAs(_rifle));
            Assert.That(_weapons.Active.Ammo, Is.EqualTo(7));
            Assert.That(_weapons.IsEmpty(WeaponSlot.First));
            Assert.That(_slot.Definition, Is.SameAs(_grenade));
            Assert.That(_slot.Count, Is.EqualTo(2));
            Assert.That(_progress.Elapsed, Is.EqualTo(75f));
            Assert.That(_waves.Seed, Is.EqualTo(9));
            Assert.That(_waves.Phase, Is.EqualTo(WavePhase.Pause));
            Assert.That(_waves.WaveIndex + 1, Is.EqualTo(1), "wave 2 (left in progress) again after the pause");
        }
    }
}
