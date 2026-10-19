using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Purge.Core.Common;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Drops;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Drops;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Weapons;
using Purge.Tests.EditMode.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Combat
{
    /// <summary>Stage 4б: throwing, effects, zones on cells, the throwable slot, drops and picking them up.</summary>
    public class ThrowingZonesDropTests
    {
        private const float Dt = 1f / 60f;

        private readonly List<Object> _created = new List<Object>();
        private LevelData _level;
        private LevelGrid _grid;
        private PlayerDefinition _definition;
        private FakePlayerInput _input;
        private DoorSystem _doors;
        private SpatialQueryService _spatial;
        private CombatTargets _targets;
        private SoundEventBus _sounds;
        private PlayerSystem _player;
        private PlayerHealth _health;
        private HordeSystem _horde;
        private StatusSystem _statuses;
        private ZoneSystem _zones;
        private EffectSystem _effects;
        private BulletSystem _bullets;
        private ThrowableSlot _slot;
        private PlayerThrowing _throwing;
        private AmmoStock _ammo;
        private WeaponSystem _weapons;
        private PickupSystem _pickups;
        private ObstacleDefinition _bush;
        private ZoneDefinition _fire;

        private sealed class Target : IDamageable
        {
            public Target(Vector2 position) => Position = position;
            public string Id => "target";
            public Vector2 Position { get; set; }
            public float Radius => 0.35f;
            public float Health { get; private set; } = 100f;
            public bool IsAlive => Health > 0f;
            public void ApplyDamage(float amount, Vector2 direction) => Health -= amount;
        }

        private sealed class Aim : IAimSettings
        {
            public AimMode AimMode => AimMode.Free;
            public bool AimAssist => false;
        }

        private sealed class Pointer : IAimPointer
        {
            public Vector2 Point;
            public bool TryGetGroundPoint(Vector2 screenPosition, out Vector2 gridPoint)
            {
                gridPoint = Point;
                return true;
            }
        }

        private Pointer _pointer;

        /// <summary>
        /// 20×7, room x = 1..18, y = 1..5, start (2,3). A wall column at x = 12 (y = 1..5) except a gap at y = 5;
        /// a hedge of bushes (flammable) along y = 1 from x = 3 to x = 11.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _bush = ScriptableObject.CreateInstance<ObstacleDefinition>();
            _bush.Configure("bush", movement: true, bullets: false, sight: false, flammable: true);
            _created.Add(_bush);

            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(20, 7);
            for (var y = 1; y <= 5; y++)
            for (var x = 1; x <= 18; x++)
                geometry.SetCell(new GridPosition(x, y), x == 12 && y < 5 ? CellType.Wall : CellType.Floor);
            for (var x = 3; x <= 11; x++)
                geometry.SetObstacle(new GridPosition(x, 1), _bush);
            _level.ReplaceGeometry(geometry);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(2, 3)));

            _definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _definition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _input = new FakePlayerInput();
            _pointer = new Pointer();
            _grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            var passability = new LevelPassability(_grid, _doors);
            _spatial = new SpatialQueryService(_grid, _doors);
            _targets = new CombatTargets(_spatial);
            _sounds = new SoundEventBus();
            _player = new PlayerSystem(_level, _definition, _input, passability, new OccupancyMap(_grid),
                new LevelLaunchOptions(startIndex: 0), new Aim());
            _health = new PlayerHealth(_definition);
            _horde = new HordeSystem(_level, _grid, passability, _doors, _player, _health); // no stress test: inactive
            _statuses = new StatusSystem(_horde, _targets);
            _zones = new ZoneSystem(_grid, _doors, _targets, _horde, _statuses, _player, _health);
            _effects = new EffectSystem(_targets, _zones, _statuses, _sounds);
            _bullets = new BulletSystem(_targets);
            _slot = new ThrowableSlot();
            _throwing = new PlayerThrowing(_input, _player, _slot, _bullets, _targets, _effects, _sounds, _pointer);
            _ammo = new AmmoStock();
            _weapons = new WeaponSystem(_input, _definition);
            _pickups = new PickupSystem(_level, _player, _health, new PlayerInventory(), _weapons, _sounds, _ammo, _slot);

            _fire = ScriptableObject.CreateInstance<ZoneDefinition>();
            _fire.Configure("fire", new ZoneDamage { DamagePerSecond = 16f },
                new ZoneSpread { ChancePerSecond = 10f, MaxSteps = 3, FlammableDuration = 1f });
            _created.Add(_fire);

            _zones.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _pickups.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _throwing.Dispose();
            _pickups.Dispose();
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_definition);
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private ThrowableDefinition Grenade(float damage = 50f, float radius = 2f, int stack = 3, float range = 8f)
        {
            var grenade = ScriptableObject.CreateInstance<ThrowableDefinition>();
            var explosion = new ExplosionEffect { Damage = damage, EdgeDamage = 1f };
            explosion.SetRadius(radius);
            grenade.Configure("grenade", stack, range, explosion);
            grenade.ConfigureFlight(speed: 12f, cooldown: 0.5f);
            _created.Add(grenade);
            return grenade;
        }

        private ThrowableDefinition Molotov()
        {
            var molotov = ScriptableObject.CreateInstance<ThrowableDefinition>();
            var zone = new PlaceZoneEffect { Zone = _fire, Duration = 4f, Strength = 1f };
            zone.SetRadius(1f);
            molotov.Configure("molotov", 3, 8f, zone);
            _created.Add(molotov);
            return molotov;
        }

        private void Fly()
        {
            for (var i = 0; i < 600 && _bullets.Active.Count > 0; i++)
                _bullets.Tick(Dt);
            Assert.AreEqual(0, _bullets.Active.Count, "The thrown item lands.");
        }

        private void Frame()
        {
            _throwing.Tick(Dt);
            _player.Tick(Dt);
            _bullets.Tick(Dt);
            _input.EndFrame();
        }

        // ------------------------------------------------------------------ Slot

        [Test]
        public void Slot_StacksTheSameKind_UpToItsMax_AnotherKindHasNoRoom()
        {
            var grenade = Grenade(stack: 3);
            var molotov = Molotov();
            Assert.AreEqual(2, _slot.Add(grenade, 2));
            Assert.AreEqual(1, _slot.Add(grenade, 5), "Only up to the stack's max.");
            Assert.AreEqual(3, _slot.Count);
            Assert.AreEqual(0, _slot.Room(molotov), "Another kind is taken only by Use (it replaces).");

            var (old, count) = _slot.Replace(molotov, 2);
            Assert.AreSame(grenade, old);
            Assert.AreEqual(3, count);
            Assert.AreSame(molotov, _slot.Definition);
            Assert.IsTrue(_slot.TakeOne());
            Assert.IsTrue(_slot.TakeOne());
            Assert.IsTrue(_slot.IsEmpty);
            Assert.IsFalse(_slot.TakeOne());
        }

        // ------------------------------------------------------------------ Throwing

        [Test]
        public void Throw_LandsAtTheCell_ExplodesThere_OnlyOnTargetsInTheRadius()
        {
            _slot.Add(Grenade(damage: 50f, radius: 1.5f), 1);
            var near = new Target(new Vector2(6f, 3f));
            var far = new Target(new Vector2(9f, 3f));
            _spatial.Add(near);
            _spatial.Add(far);

            Assert.IsTrue(_throwing.TryThrow(new Vector2(6.2f, 3.3f)));
            Assert.IsTrue(_slot.IsEmpty);
            Assert.AreEqual(1, _bullets.Active.Count);
            Assert.AreEqual(100f, near.Health, "Flies over targets: nothing is hit before it lands.");
            Fly();

            Assert.AreEqual(50f, near.Health, 1e-3f, "Explodes at the cell centre (6, 3).");
            Assert.AreEqual(100f, far.Health);
        }

        [Test]
        public void Throw_IsClampedToRange_AndShortOfAWall()
        {
            _slot.Add(Grenade(range: 4f), 3);
            Assert.AreEqual(new Vector2(6f, 3f), _throwing.Clamp(new Vector2(16f, 3f)), "4 cells from (2,3).");

            _slot.Replace(Grenade(range: 15f), 3);
            var target = _throwing.Clamp(new Vector2(15f, 3f));
            Assert.AreEqual(3f, target.y);
            Assert.AreEqual(11f, target.x, "The wall at x = 12 cuts the throw short.");
        }

        [Test]
        public void Throw_HoldAndRelease_Throws_QuickPress_WaitsForATap()
        {
            _slot.Add(Grenade(), 3);
            _pointer.Point = new Vector2(5f, 3f);

            // Hold for longer than the hold time, then release.
            _input.Press(PlayerAction.Throw);
            _input.Hold(PlayerAction.Throw, true);
            for (var t = 0f; t < PlayerThrowing.HoldTime + 0.05f; t += Dt) Frame();
            Assert.IsTrue(_throwing.IsAiming);
            _input.Hold(PlayerAction.Throw, false);
            Frame();
            Assert.AreEqual(2, _slot.Count, "Released after a hold: thrown.");
            Assert.IsFalse(_throwing.IsAiming);
            Fly();

            for (var t = 0f; t < 1f; t += Dt) Frame(); // cooldown
            // A quick press keeps aiming; a touch on the game throws where it points.
            _input.Press(PlayerAction.Throw);
            _input.Hold(PlayerAction.Throw, true);
            Frame();
            _input.Hold(PlayerAction.Throw, false);
            Frame();
            Assert.IsTrue(_throwing.WaitsForTap);
            Assert.AreEqual(2, _slot.Count);
            _pointer.Point = new Vector2(7f, 4f);
            _input.Tap = new Vector2(100f, 100f);
            Frame();
            Assert.AreEqual(1, _slot.Count, "The tap threw.");
            Assert.AreEqual(1, _bullets.Active.Count);
            Fly();

            // The key again cancels.
            for (var t = 0f; t < 1f; t += Dt) Frame();
            _input.Press(PlayerAction.Throw);
            Frame();
            Frame();
            _input.Press(PlayerAction.Throw);
            Frame();
            Assert.IsFalse(_throwing.IsAiming);
            Assert.AreEqual(1, _slot.Count);
        }

        [Test]
        public void Throw_AimStick_PointsTheThrow_ByDeflection_ReleaseThrows()
        {
            _slot.Add(Grenade(range: 8f), 3);
            _input.Press(PlayerAction.Throw);
            _input.Hold(PlayerAction.Throw, true);
            Frame();
            _input.Hold(PlayerAction.Throw, false);
            Frame();
            Assert.IsTrue(_throwing.WaitsForTap, "A quick press: aiming.");

            _input.AimStick = new Vector2(0.5f, 0f);
            Frame();
            Assert.AreEqual(new Vector2(6f, 3f), _throwing.Target, "Half the deflection: half of the 8-cell reach.");
            Assert.AreEqual(3, _slot.Count, "Not thrown while the stick is held.");

            _input.AimStick = Vector2.zero;
            Frame();
            Assert.AreEqual(2, _slot.Count, "Letting the stick go throws.");
            Assert.IsFalse(_throwing.IsAiming);
            Fly();
        }

        // ------------------------------------------------------------------ Zones

        [Test]
        public void Zone_Overlap_KeepsLongerDuration_AndStrongerStrength_NotTheirSum()
        {
            var cell = new GridPosition(5, 3);
            Assert.IsTrue(_zones.Place(cell, _fire, 2f, strength: 1f));
            Assert.IsTrue(_zones.Place(cell, _fire, 5f, strength: 0.5f));
            Assert.AreEqual(1, _zones.Count);
            var index = _grid.ToIndex(cell);
            Assert.AreEqual(1f, _zones.StrengthAt(index), "The stronger of the two.");
            Assert.AreEqual(1f, _zones.Strength01(index), 1e-4f);

            Tick(2.5f);
            Assert.AreEqual(1, _zones.Count, "The longer duration (5 s) counts.");
            Tick(3f);
            Assert.AreEqual(0, _zones.Count);
        }

        [Test]
        public void Zone_NotInWalls_AndWallsCoverTheArea()
        {
            Assert.IsFalse(_zones.Place(new GridPosition(12, 3), _fire, 3f), "Wall.");
            var placed = _zones.PlaceArea(new Vector2(11f, 3f), 0, 2f, _fire, 3f);
            Assert.Greater(placed, 0);
            Assert.IsNull(_zones.ZoneAt(new GridPosition(13, 3)), "Behind the wall.");
            Assert.IsNotNull(_zones.ZoneAt(new GridPosition(10, 3)));
        }

        [Test]
        public void Zone_Spreads_OnlyOverFlammableCells_UpToItsSteps()
        {
            _zones.Place(new GridPosition(3, 1), _fire, 4f);
            Tick(3.5f);
            var reached = 0;
            for (var x = 3; x <= 11; x++)
                if (_zones.ZoneAt(new GridPosition(x, 1)) != null) reached = x;
            Assert.AreEqual(6, reached, "Three steps along the hedge (MaxSteps = 3).");
            Assert.IsNull(_zones.ZoneAt(new GridPosition(3, 2)), "The floor next to it does not burn.");
        }

        [Test]
        public void Molotov_PutsFireOnTheCellsAround()
        {
            _slot.Add(Molotov(), 1);
            _throwing.TryThrow(new Vector2(6f, 3f));
            Fly();
            Assert.AreEqual(5, _zones.Count, "Radius 1: the cell and its 4 neighbours.");
        }

        private void Tick(float seconds)
        {
            for (var t = 0f; t < seconds; t += Dt) _zones.Tick(Dt);
        }

        // ------------------------------------------------------------------ Flamethrower on cells (stage Э2)

        /// <summary>A flamethrower aimed from the start (2,3) down-right at the hedge (y = 1); one stream tick.</summary>
        private void FlameOnce(bool ignite)
        {
            var gas = ScriptableObject.CreateInstance<AmmoDefinition>();
            gas.Configure("ammo_gas", 6f);
            _created.Add(gas);
            var modules = new List<WeaponModule>
            {
                new StreamAttack { DamagePerSecond = 40f, Range = 3f, Angle = 60f, Interval = 0.1f },
                new AmmoModule { Ammo = gas, Magazine = 50, ReloadTime = 1f },
            };
            if (ignite) modules.Add(new IgniteCellsModule { Zone = _fire, Duration = 3f });
            var flamer = ScriptableObject.CreateInstance<WeaponDefinition>();
            flamer.Configure("flamer", modules.ToArray());
            _created.Add(flamer);
            _weapons.Equip(new WeaponRuntime(new WeaponPickupData("weapon_f", new GridPosition(1, 1), flamer)));

            var combat = new PlayerCombat(_input, _player, _weapons, _targets, _bullets, _sounds, new Aim(), _doors, _ammo,
                zones: _zones);
            _input.Look = new Vector2(1f, -1f).normalized;
            _input.AttackHeld = true;
            _weapons.Tick(Dt);
            combat.Tick(Dt);
            _input.EndFrame();
        }

        [Test]
        public void Flamethrower_IgnitesFlammableCellsInItsCone_NotBareFloor()
        {
            FlameOnce(ignite: true);

            Assert.AreEqual(_fire, _zones.ZoneAt(new GridPosition(3, 1)), "A bush in the cone catches fire.");
            Assert.AreEqual(_fire, _zones.ZoneAt(new GridPosition(4, 1)));
            Assert.IsNull(_zones.ZoneAt(new GridPosition(3, 2)), "Bare floor in the cone does not burn.");
            Assert.IsNull(_zones.ZoneAt(new GridPosition(8, 1)), "Out of the stream's reach.");
        }

        [Test]
        public void Flamethrower_WithoutTheModule_SetsNothingAlight()
        {
            FlameOnce(ignite: false);

            Assert.AreEqual(0, _zones.Count);
        }

        // ------------------------------------------------------------------ Drops and pickups

        [Test]
        public void Drops_AmmoAndThrowables_ArePickedUpOnEntering_AnotherKindByUse()
        {
            var nine = ScriptableObject.CreateInstance<AmmoDefinition>();
            nine.Configure("ammo_9mm", 6f);
            _created.Add(nine);
            var grenade = Grenade(stack: 3);
            var molotov = Molotov();
            _slot.Add(grenade, 2);

            _pickups.DropAmmo(new Vector2(3f, 3f), 0, nine, 15, 30f);
            _pickups.DropThrowable(new Vector2(3f, 3f), 0, grenade, 2, 30f);
            _pickups.DropThrowable(new Vector2(3f, 3f), 0, molotov, 1, 30f);
            Assert.AreEqual(3, _pickups.Count);

            _input.Move = Vector2.right;
            for (var i = 0; i < 40 && _player.Cell.X < 3; i++) Frame();
            _input.Move = Vector2.zero;
            Assert.AreEqual(3, _player.Cell.X);
            Assert.AreEqual(15, _ammo.Get(nine), "Ammo — always.");
            Assert.AreEqual(3, _slot.Count, "One grenade fitted; the other stays.");
            Assert.AreEqual(2, _pickups.At(_player.Cell).Count);

            Assert.IsTrue(_pickups.TryTakeThrowable(_player.Cell), "Use takes the other kind.");
            Assert.AreSame(molotov, _slot.Definition);
            var lying = _pickups.At(_player.Cell);
            var grenades = 0;
            foreach (var pickup in lying)
                if (pickup.Kind == PickupKind.Throwable && pickup.Throwable == grenade) grenades += pickup.Amount;
            Assert.AreEqual(4, grenades, "The replaced stack (3) lies next to the one left (1).");
        }

        [Test]
        public void Drops_Expire_AndTheOldestMakesRoom()
        {
            var nine = ScriptableObject.CreateInstance<AmmoDefinition>();
            nine.Configure("ammo_9mm", 6f);
            _created.Add(nine);
            var removed = new List<Pickup>();
            _pickups.Removed += removed.Add;
            _pickups.MaxDropped = 2;

            var first = _pickups.DropAmmo(new Vector2(10f, 3f), 0, nine, 5, 1f);
            _pickups.DropAmmo(new Vector2(11f, 3f), 0, nine, 5, 10f);
            _pickups.DropAmmo(new Vector2(10f, 4f), 0, nine, 5, 10f);
            Assert.AreEqual(2, _pickups.Count);
            Assert.AreSame(first, removed[0], "The oldest made room.");
            Assert.IsFalse(removed[0].WasTaken);

            for (var t = 0f; t < 11f; t += 0.1f) _pickups.Tick(0.1f);
            Assert.AreEqual(0, _pickups.Count, "Expired.");
        }

        [Test]
        public void Drops_Medkit_HealsByItsKind_OnlyWhenHurt()
        {
            var small = ScriptableObject.CreateInstance<MedkitDefinition>();
            small.Configure("small", 25);
            _created.Add(small);
            _pickups.DropMedkit(new Vector2(2f, 3f), 0, small, 30f);
            Assert.AreEqual(1, _pickups.Count, "Full health: it stays.");

            _health.Damage(60);
            _pickups.DropMedkit(new Vector2(2f, 3f), 0, small, 30f);
            Assert.AreEqual(65, _health.Current, "Dropped where the player stands: used at once, +25.");
            Assert.AreEqual(1, _pickups.Count);
        }

        [Test]
        public void DropRoller_SmartAmmo_IsForTheWeaponInHands_RarityCapped_SlotsNotDroppedAgain()
        {
            var nine = ScriptableObject.CreateInstance<AmmoDefinition>();
            nine.Configure("ammo_9mm", 6f);
            var shells = ScriptableObject.CreateInstance<AmmoDefinition>();
            shells.Configure("ammo_shells", 9f);
            _created.Add(nine);
            _created.Add(shells);
            var pistol = WeaponWith("pistol", nine, 12, Rarity.Common);
            var shotgun = WeaponWith("shotgun", shells, 6, Rarity.Legendary);

            var table = ScriptableObject.CreateInstance<DropTable>();
            _created.Add(table);
            var pool = new DropPool(_level);
            var random = new DeterministicRandom(7);

            table.Configure("ammo_only", 0f, 1f, 0f, 0f, 0f);
            table.ConfigureAmmo(1f, new Vector2(1f, 1f), 10);
            pool.Configure(table, new[] { pistol, shotgun }, null);
            var context = new DropContext { First = pistol, ActiveSlot = 0, FirstReserve = 100 };
            for (var i = 0; i < 50; i++)
            {
                var roll = DropRoller.Roll(table, pool, context, random);
                Assert.AreEqual(DropKind.Ammo, roll.Kind);
                Assert.AreSame(nine, roll.Item, "Held share 1: the weapon in the slot's ammo.");
                Assert.AreEqual(12, roll.Amount, "One magazine.");
            }

            table.Configure("weapons_only", 0f, 0f, 0f, 0f, 1f);
            for (var i = 0; i < 50; i++)
                Assert.AreSame(shotgun, DropRoller.Roll(table, pool, context, random).Item, "The pistol is in a slot.");

            context.First = null;
            table.Configure("nothing", 1f, 0f, 0f, 0f, 1f);
            var nothing = 0;
            for (var i = 0; i < 200; i++)
                if (DropRoller.Roll(table, pool, context, random).Kind == DropKind.Nothing) nothing++;
            Assert.Greater(nothing, 60);
            Assert.Less(nothing, 140, "Half and half by weight.");
        }

        [Test]
        public void Drop_DifficultyRarityFactor_RaisesTheRarer()
        {
            var nine = ScriptableObject.CreateInstance<AmmoDefinition>();
            nine.Configure("ammo_9mm", 6f);
            _created.Add(nine);
            var grey = WeaponWith("grey", nine, 12, Rarity.Common);
            var orange = WeaponWith("orange", nine, 12, Rarity.Legendary);
            var table = ScriptableObject.CreateInstance<DropTable>();
            _created.Add(table);
            table.Configure("weapons_only", 0f, 0f, 0f, 0f, 1f);
            var pool = new DropPool(_level);
            pool.Configure(table, new[] { grey, orange }, null);

            int Oranges(float factor)
            {
                var random = new DeterministicRandom(11);
                var context = new DropContext { RarityFactor = factor };
                var count = 0;
                for (var i = 0; i < 2000; i++)
                    if (DropRoller.Roll(table, pool, context, random).Item == orange) count++;
                return count;
            }

            // Weights 100 : 3 → ~3 %; × 10 on the rarer → 100 : 30 → ~23 %.
            Assert.That(Oranges(0f), Is.InRange(20, 110), "0 = no factor");
            Assert.That(Oranges(10f), Is.InRange(360, 560));
        }

        private WeaponDefinition WeaponWith(string id, AmmoDefinition ammo, int magazine, Rarity rarity)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.Configure(id, new BulletAttack(), new AmmoModule { Ammo = ammo, Magazine = magazine });
            weapon.ConfigureLook(rarity, WeaponHold.Rifle);
            _created.Add(weapon);
            return weapon;
        }
    }
}
