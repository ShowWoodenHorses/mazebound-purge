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
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Weapons;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Monsters
{
    /// <summary>Stage 6c: experience of the run, the cards' offer, their stats, damage and the magnet.</summary>
    public class CardTests
    {
        private const float Dt = 1f / 30f;

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private PlayerDefinition _playerDefinition;
        private CardDeck _deck;
        private RunExperience _experience;
        private CardSystem _cards;
        private RunStats _stats;
        private PlayerSystem _player;
        private PlayerHealth _health;
        private PickupSystem _pickups;
        private ThrowableSlot _slot;
        private AmmoStock _ammo;
        private LevelRuntime _runtime;

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
            _deck = Create<CardDeck>();
            _deck.Configure(10f, 1.25f, 3);
        }

        [TearDown]
        public void TearDown()
        {
            _cards?.Dispose();
            _experience?.Dispose();
            _pickups?.Dispose();
            _cards = null;
            _experience = null;
            _pickups = null;
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

        private CardDefinition Card(string id, Rarity rarity, int maxLevel, params CardModule[] modules)
        {
            var card = Create<CardDefinition>();
            card.Configure(id, rarity, maxLevel, modules);
            return card;
        }

        private CardDefinition StatCard(string id, RunStat stat, float perLevel, int maxLevel = 5) =>
            Card(id, Rarity.Common, maxLevel, new StatCard { Stat = stat, PerLevel = perLevel });

        /// <summary>A run level (waves) with the deck's cards.</summary>
        private void Run(params CardDefinition[] cards)
        {
            _deck.SetCards(cards);
            var mass = Create<MonsterDefinition>();
            mass.Configure("mass", MonsterRank.Mass, MonsterBiome.Common, new MonsterStats { MaxHp = 30f, Speed = 2f });
            var scenario = Create<WaveScenario>();
            scenario.Configure(new[] { new WaveDefinition(5f, new[] { new WaveMonster(mass, 1f) }) }, pauseTime: 5f, firstDelay: 100f);
            var horde = Create<HordeDefinition>();
            horde.ConfigureCapacity(50, 4);
            horde.SetWaves(scenario);
            _level.Horde = horde;

            var grid = new LevelGrid(_level.Geometry);
            var doors = new DoorSystem(_level);
            var passability = new LevelPassability(grid, doors);
            _player = new PlayerSystem(_level, _playerDefinition, new FakePlayerInput(), passability, new OccupancyMap(grid),
                new LevelLaunchOptions(startIndex: 0), new Aim());
            _health = new PlayerHealth(_playerDefinition);
            var system = new HordeSystem(_level, grid, passability, doors, _player, _health, options: new LevelLaunchOptions(seed: 5));
            _slot = new ThrowableSlot();
            _ammo = new AmmoStock();
            _pickups = new PickupSystem(_level, _player, _health, new PlayerInventory(),
                new WeaponSystem(new FakePlayerInput(), _playerDefinition), new SoundEventBus(), _ammo, _slot);
            _stats = new RunStats();
            _experience = new RunExperience(system, _deck);
            _cards = new CardSystem(_experience, _stats, _health, _player, _slot, _pickups, system, new LevelLaunchOptions(seed: 3), _deck);
            _runtime = new LevelRuntime(_level, new ILevelLoadStep[] { _player, _pickups, system, _experience, _cards },
                new ILevelTickable[] { _pickups, system });
            _runtime.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            _runtime.StartGameplay();
        }

        [Test]
        public void Experience_ByRank_AndCurve()
        {
            Assert.That(MonsterDefinition.ExperienceOf(MonsterRank.Mass), Is.EqualTo(1f));
            Assert.That(MonsterDefinition.ExperienceOf(MonsterRank.Officer), Is.EqualTo(4f));
            Assert.That(MonsterDefinition.ExperienceOf(MonsterRank.Elite), Is.EqualTo(15f));
            Assert.That(MonsterDefinition.ExperienceOf(MonsterRank.Boss), Is.EqualTo(50f));
            Assert.That(_deck.ExperienceFor(1), Is.EqualTo(10));
            Assert.That(_deck.ExperienceFor(3), Is.EqualTo(16), "10 × 1.25²");
        }

        [Test]
        public void LevelUp_OffersDistinctCards_ChoiceApplies()
        {
            Run(StatCard("a", RunStat.WeaponDamage, 0.1f), StatCard("b", RunStat.FireRate, 0.1f),
                StatCard("c", RunStat.Magazine, 0.2f), StatCard("d", RunStat.CritChance, 0.05f));
            Assert.That(_experience.IsActive, "a wave level with a deck");

            _experience.Add(9.5f);
            Assert.That(_experience.Pending, Is.EqualTo(0));
            _experience.Add(0.5f);
            Assert.That(_experience.Level, Is.EqualTo(2));
            Assert.That(_experience.Pending, Is.EqualTo(1));

            Assert.That(_cards.OpenNext());
            Assert.That(_cards.Offer.Count, Is.EqualTo(3));
            Assert.That(new HashSet<CardDefinition>(_cards.Offer).Count, Is.EqualTo(3), "no card twice");
            var chosen = _cards.Offer[0];
            _cards.Choose(0);
            Assert.That(_cards.HasOffer, Is.False);
            Assert.That(_experience.Pending, Is.EqualTo(0));
            Assert.That(_cards.LevelOf(chosen), Is.EqualTo(1));
            var stat = ((StatCard)chosen.Modules[0]).Stat;
            Assert.That(_stats.Get(stat), Is.GreaterThan(0f));
        }

        [Test]
        public void SeveralLevelUps_WaitInTurn()
        {
            Run(StatCard("a", RunStat.WeaponDamage, 0.1f), StatCard("b", RunStat.FireRate, 0.1f));
            _experience.Add(10f + 13f); // 1 → 2 → 3 (12.5 rounds to 12)
            Assert.That(_experience.Pending, Is.EqualTo(2));
            Assert.That(_cards.OpenNext());
            _cards.Choose(0);
            Assert.That(_cards.OpenNext(), "the second level-up's cards");
            _cards.Choose(0);
            Assert.That(_experience.Pending, Is.EqualTo(0));
        }

        [Test]
        public void LastLevel_LeavesTheChoice_NothingLeft_SpendsLevelUps()
        {
            var once = StatCard("once", RunStat.WeaponDamage, 0.1f, maxLevel: 1);
            var twice = StatCard("twice", RunStat.FireRate, 0.1f, maxLevel: 2);
            Run(once, twice);

            for (var pick = 0; pick < 3; pick++)
            {
                _experience.Add(_experience.Next);
                Assert.That(_cards.OpenNext(), $"pick {pick}");
                Assert.That(_cards.Offer.Count, Is.EqualTo(pick == 0 ? 2 : 1));
                _cards.Choose(new List<CardDefinition>(_cards.Offer).IndexOf(pick == 0 ? once : twice));
            }

            Assert.That(_cards.LevelOf(once), Is.EqualTo(1));
            Assert.That(_cards.LevelOf(twice), Is.EqualTo(2));
            _experience.Add(_experience.Next);
            Assert.That(_cards.OpenNext(), Is.False, "every card at its last level");
            Assert.That(_experience.Pending, Is.EqualTo(0), "the level-up is spent");
        }

        [Test]
        public void Reroll_OtherCards_OncePerOffer()
        {
            Run(StatCard("a", RunStat.WeaponDamage, 0.1f), StatCard("b", RunStat.FireRate, 0.1f),
                StatCard("c", RunStat.Magazine, 0.2f), StatCard("d", RunStat.CritChance, 0.05f),
                StatCard("e", RunStat.MoveSpeed, 0.05f), StatCard("f", RunStat.ReloadTime, 0.1f));
            _experience.Add(10f);
            _cards.OpenNext();
            var first = new List<CardDefinition>(_cards.Offer);
            Assert.That(_cards.Reroll());
            foreach (var card in _cards.Offer)
                Assert.That(first.Contains(card), Is.False, "the deck has others");
            Assert.That(_cards.Reroll(), Is.False, "once per offer");

            _cards.Choose(0);
            _experience.Add(_experience.Next);
            _cards.OpenNext();
            Assert.That(_cards.RerollUsed, Is.False, "a new offer — a new reroll");
        }

        [Test]
        public void Cards_IntoThePlayer_HealthSpeedMagnetThrowables()
        {
            var grenade = Create<ThrowableDefinition>();
            grenade.Configure("grenade", 5, 8f);
            var health = StatCard("health", RunStat.MaxHealth, 15f);
            var speed = StatCard("speed", RunStat.MoveSpeed, 0.06f);
            var magnet = StatCard("magnet", RunStat.PickupRadius, 1f);
            var throwables = Card("throwables", Rarity.Uncommon, 5, new ThrowablesCard { Count = 2, Fallback = grenade });
            Run(health, speed, magnet, throwables);

            _health.Damage(30);
            _cards.Apply(health);
            Assert.That(_health.Max, Is.EqualTo(_playerDefinition.MaxHealth + 15));
            Assert.That(_health.Current, Is.EqualTo(_playerDefinition.MaxHealth - 30 + 15), "heals as much");
            _cards.Apply(speed);
            Assert.That(_player.BonusSpeed, Is.EqualTo(1.06f).Within(1e-4f));
            _cards.Apply(magnet);
            _cards.Apply(magnet);
            Assert.That(_pickups.MagnetRadius, Is.EqualTo(2f));
            _cards.Apply(throwables);
            Assert.That(_slot.Definition, Is.SameAs(grenade), "an empty slot gets the fallback");
            Assert.That(_slot.Count, Is.EqualTo(2));
            _cards.Apply(throwables);
            Assert.That(_slot.Count, Is.EqualTo(4));
        }

        [Test]
        public void Magnet_DropsFlyToThePlayer()
        {
            Run(StatCard("a", RunStat.PickupRadius, 1f));
            var ammo = Create<AmmoDefinition>();
            ammo.Configure("ammo_9mm", 8f);
            var near = _pickups.DropAmmo(new Vector2(17f, 15f), 0, ammo, 10, 100f);
            var far = _pickups.DropAmmo(new Vector2(22f, 15f), 0, ammo, 10, 100f);
            for (var t = 0f; t < 1f; t += Dt) _runtime.Tick(Dt);
            Assert.That(_ammo.Get(ammo), Is.EqualTo(0), "no magnet without the card");

            _pickups.MagnetRadius = 3f;
            for (var t = 0f; t < 1f; t += Dt) _runtime.Tick(Dt);
            Assert.That(_ammo.Get(ammo), Is.EqualTo(10), "the near one flew in");
            Assert.That(near.WasTaken);
            Assert.That(far.WasTaken, Is.False, "the far one stays");
            Assert.That(_pickups.AttractedCount, Is.EqualTo(0));
        }

        [Test]
        public void Damage_WeaponsExplosionsCritsAndStates()
        {
            var stats = new RunStats();
            var damage = new RunDamage(stats, new CombatTargets(null));
            stats.Add(RunStat.WeaponDamage, 0.5f);
            stats.Add(RunStat.ExplosionDamage, 0.2f);
            stats.Add(RunStat.ExplosionRadius, 0.1f);

            Assert.That(damage.Modify(CombatTarget.None, 10f, PlayerDamage.Weapon, out var critical), Is.EqualTo(15f).Within(1e-4f));
            Assert.That(critical, Is.False);
            Assert.That(damage.Modify(CombatTarget.None, 10f, PlayerDamage.Explosion, out _), Is.EqualTo(12f).Within(1e-4f));
            Assert.That(damage.ExplosionRadiusFactor, Is.EqualTo(1.1f).Within(1e-4f));

            stats.Add(RunStat.CritChance, 1f);
            Assert.That(damage.Modify(CombatTarget.None, 10f, PlayerDamage.Precise, out critical), Is.EqualTo(30f).Within(1e-4f));
            Assert.That(critical, "always critical at 100 %");
            Assert.That(damage.Modify(CombatTarget.None, 10f, PlayerDamage.Weapon, out critical), Is.EqualTo(15f).Within(1e-4f));
            Assert.That(critical, Is.False, "streams and chains are never critical");
        }

        [Test]
        public void RunStats_FireRateReloadMagazine()
        {
            var stats = new RunStats();
            stats.Add(RunStat.FireRate, 0.25f);
            stats.Add(RunStat.ReloadTime, 0.9f);
            Assert.That(stats.CooldownFactor, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(stats.ReloadFactor, Is.EqualTo(RunStats.MinReloadFactor), "never below the shortest reload");
            Assert.That(new StatCard { Stat = RunStat.WeaponDamage, PerLevel = 0.1f }.ShownValue, Is.EqualTo(10f));
            Assert.That(new StatCard { Stat = RunStat.MaxHealth, PerLevel = 15f }.ShownValue, Is.EqualTo(15f));
        }
    }
}
