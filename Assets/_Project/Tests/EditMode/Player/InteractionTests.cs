using System.Collections.Generic;
using System.Threading;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Level;
using Maze.Gameplay.Map;
using Maze.Gameplay.Pickups;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Weapons;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Player
{
    /// <summary>Doors with keys, pickups (keys, medkits, weapons) and the Interact action (ТЗ §40, §63, §67, §72).</summary>
    public class InteractionTests
    {
        private const float Dt = 1f / 60f;

        private LevelData _level;
        private PlayerDefinition _definition;
        private WeaponDefinition _knife, _bat, _pistol;
        private FakePlayerInput _input;
        private DoorSystem _doors;
        private LevelPassability _passability;
        private OccupancyMap _occupancy;
        private PlayerHealth _health;
        private PlayerInventory _inventory;
        private WeaponSystem _weapons;
        private PlayerSystem _player;
        private PickupSystem _pickups;
        private MapSystem _map;
        private PlayerInteraction _interaction;
        private SoundEventBus _sounds;
        private readonly List<InteractionResult> _results = new List<InteractionResult>();

        /// <summary>
        /// 12x3 corridor along y = 1, x = 1..10:
        /// start (1,1) · door_plain (2,1) · key_2 (3,1, opens nothing) · medkit (5,1) · knife + bat (6,1) ·
        /// pistol (7,1) · door_locked (8,1, key_1) · key_1 (9,1). Map fragments: (4,1) for x 0..5, (10,1) for x 6..11.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(12, 3);
            for (var x = 1; x <= 10; x++)
                geometry.SetCell(new GridPosition(x, 1), CellType.Floor);
            geometry.SetCell(new GridPosition(2, 1), CellType.Door);
            geometry.SetCell(new GridPosition(8, 1), CellType.Door);
            _level.ReplaceGeometry(geometry);

            _knife = Weapon("knife", WeaponSlot.Melee);
            _bat = Weapon("bat", WeaponSlot.Melee);
            _pistol = Weapon("pistol", WeaponSlot.Ranged);
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(1, 1)));
            _level.MutableDoors.Add(new DoorData("door_plain", new GridPosition(2, 1)));
            _level.MutableDoors.Add(new DoorData("door_locked", new GridPosition(8, 1), keyId: "key_1"));
            _level.MutableKeys.Add(new KeyData("key_2", new GridPosition(3, 1)));
            _level.MutableKeys.Add(new KeyData("key_1", new GridPosition(9, 1)));
            _level.MutableMedkits.Add(new MedkitData("medkit_1", new GridPosition(5, 1)));
            _level.MutableWeapons.Add(new WeaponPickupData("weapon_1", new GridPosition(6, 1), _knife));
            _level.MutableWeapons.Add(new WeaponPickupData("weapon_2", new GridPosition(6, 1), _bat));
            _level.MutableWeapons.Add(new WeaponPickupData("weapon_3", new GridPosition(7, 1), _pistol));
            _level.MutableMapFragments.Add(new MapFragmentData("fragment_1", new GridPosition(4, 1), new GridRect(0, 0, 6, 3)));
            _level.MutableMapFragments.Add(new MapFragmentData("fragment_2", new GridPosition(10, 1), new GridRect(6, 0, 6, 3)));

            _definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _definition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _input = new FakePlayerInput();
            var grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            _passability = new LevelPassability(grid, _doors);
            _occupancy = new OccupancyMap(grid);
            _health = new PlayerHealth(_definition);
            _inventory = new PlayerInventory();
            _weapons = new WeaponSystem(_input);
            _sounds = new SoundEventBus();
            _player = new PlayerSystem(_level, _definition, _input, _passability, _occupancy, new LevelLaunchOptions(startIndex: 0),
                Aim.FreeNoAssist);
            _map = new MapSystem(_level);
            _pickups = new PickupSystem(_level, _player, _health, _inventory, _weapons, _sounds, _map);
            _interaction = new PlayerInteraction(_input, _player, _pickups, _doors, _inventory, _occupancy, _sounds);
            _interaction.Interacted += (result, door) => _results.Add(result);
            _results.Clear();

            // Load order of the real level: InitializeSystems before SpawnPlayer.
            _pickups.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _pickups.Dispose();
            _map.Dispose();
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_definition);
            Object.DestroyImmediate(_knife);
            Object.DestroyImmediate(_bat);
            Object.DestroyImmediate(_pistol);
        }

        private static WeaponDefinition Weapon(string id, WeaponSlot slot)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.Configure(id, slot, magazineSize: 6);
            return weapon;
        }

        /// <summary>Walks along the corridor until the player stands at the centre of column <paramref name="x"/>.</summary>
        private void WalkTo(int x)
        {
            for (var i = 0; i < 2000 && Mathf.Abs(_player.Position.x - x) > 0.03f; i++)
            {
                _input.Move = new Vector2(Mathf.Sign(x - _player.Position.x), 0f);
                _player.Tick(Dt);
            }

            _input.Move = Vector2.zero;
            Assert.AreEqual(new GridPosition(x, 1), _player.Cell, "Could not walk there.");
        }

        private InteractionResult? Interact()
        {
            var before = _results.Count;
            _input.Press(PlayerAction.Interact);
            _weapons.Tick(Dt);
            _player.Tick(Dt);
            _interaction.Tick(Dt);
            _input.EndFrame();
            return _results.Count > before ? _results[_results.Count - 1] : (InteractionResult?)null;
        }

        [Test]
        public void FrozenDoor_CannotBeOpened_UntilTheIceIsBroken()
        {
            _level.MutableDoors[0].IceHits = 1;
            var doors = new DoorSystem(_level);
            var interaction = new PlayerInteraction(_input, _player, _pickups, doors, _inventory, _occupancy, _sounds);
            var results = new List<InteractionResult>();
            interaction.Interacted += (result, door) => results.Add(result);

            interaction.Interact();
            Assert.AreEqual(InteractionResult.DoorFrozen, results[0]);
            Assert.IsFalse(doors.IsOpen("door_plain"));

            doors.HitIce("door_plain");
            interaction.Interact();
            Assert.AreEqual(InteractionResult.DoorOpened, results[1]);
        }

        [Test]
        public void PlainDoor_OpensAndCloses_NotOnThePlayer_NorOnAZombie()
        {
            Assert.AreEqual(InteractionResult.DoorOpened, Interact(), "The door next to the start.");
            Assert.IsTrue(_passability.IsWalkable(new GridPosition(2, 1)));

            WalkTo(2);
            Assert.AreEqual(InteractionResult.DoorBlocked, Interact(), "Standing in the doorway.");
            Assert.IsTrue(_doors.IsOpen("door_plain"));

            WalkTo(3);
            _occupancy.AddZombie(new GridPosition(2, 1));
            Assert.AreEqual(InteractionResult.DoorBlocked, Interact(), "A zombie is in the doorway.");

            _occupancy.RemoveZombie(new GridPosition(2, 1));
            Assert.AreEqual(InteractionResult.DoorClosed, Interact());
            Assert.IsFalse(_passability.IsWalkable(new GridPosition(2, 1)));
            Assert.AreEqual(InteractionResult.DoorOpened, Interact(), "Can be used any number of times.");
        }

        [Test]
        public void LockedDoor_NeedsItsKey_KeyOnlyUnlocks_ThenOpensSeparately()
        {
            Interact();
            WalkTo(7);
            Interact(); // takes the pistol lying here first
            Assert.AreEqual(InteractionResult.DoorLocked, Interact(), "No key_1 yet; key_2 opens nothing.");
            Assert.IsTrue(_doors.IsLocked("door_locked"));
            Assert.IsTrue(_inventory.HasKey("key_2"), "Picked up on the way.");

            _inventory.AddKey(_level.Keys[1]);
            Assert.AreEqual(InteractionResult.DoorUnlocked, Interact());
            Assert.IsFalse(_doors.IsLocked("door_locked"));
            Assert.IsFalse(_doors.IsOpen("door_locked"), "The key only unlocks: the door stays closed.");
            Assert.IsFalse(_inventory.HasKey("key_1"), "1 key = 1 door: the key is spent.");
            Assert.IsTrue(_inventory.HasKey("key_2"));

            Assert.AreEqual(InteractionResult.DoorOpened, Interact());
            Assert.AreEqual(InteractionResult.DoorClosed, Interact());
            Assert.AreEqual(InteractionResult.DoorOpened, Interact(), "No key needed any more.");
        }

        [Test]
        public void Keys_ArePickedUpOnEntering()
        {
            var removed = new List<string>();
            _pickups.Removed += pickup => removed.Add(pickup.Id);
            Interact();
            WalkTo(3);

            Assert.IsTrue(_inventory.HasKey("key_2"));
            Assert.AreEqual(0, _pickups.At(new GridPosition(3, 1)).Count);
            CollectionAssert.AreEqual(new[] { "key_2" }, removed);
        }

        [Test]
        public void MapFragment_IsCollectedOnEntering_RevealsOnlyItsRegion()
        {
            var collected = new List<string>();
            _map.FragmentCollected += fragment => collected.Add(fragment.Id);
            Assert.AreEqual(2, _map.TotalCount);
            Assert.IsFalse(_map.IsRevealed(new GridPosition(1, 1)));

            Interact();
            WalkTo(4);

            CollectionAssert.AreEqual(new[] { "fragment_1" }, collected);
            Assert.AreEqual(1, _map.CollectedCount);
            Assert.IsFalse(_map.AllCollected);
            Assert.AreEqual(0, _pickups.At(new GridPosition(4, 1)).Count, "The fragment no longer lies in the cell.");
            Assert.IsTrue(_map.IsRevealed(new GridPosition(0, 0)));
            Assert.IsTrue(_map.IsRevealed(new GridPosition(5, 2)));
            Assert.IsFalse(_map.IsRevealed(new GridPosition(6, 1)), "Region of the other fragment.");

            WalkTo(3);
            WalkTo(4);
            Assert.AreEqual(1, collected.Count, "Collected once.");
        }

        [Test]
        public void Medkit_StaysAtFullHp_HealsToFullAndDisappearsWhenHurt()
        {
            Interact();
            WalkTo(5);
            Assert.AreEqual(1, _pickups.At(new GridPosition(5, 1)).Count, "Full HP: the medkit stays.");

            WalkTo(4);
            _health.Damage(60);
            WalkTo(5);
            Assert.AreEqual(_health.Max, _health.Current, "Medkit heals to full (decision: always full).");
            Assert.AreEqual(0, _pickups.At(new GridPosition(5, 1)).Count, "Used up.");
        }

        [Test]
        public void Weapons_PickedByInteract_BecomeActive_SameSlotDropsOld()
        {
            var added = new List<Pickup>();
            _pickups.Added += added.Add;
            Interact();
            WalkTo(6);

            Assert.AreEqual(InteractionResult.WeaponTaken, Interact());
            Assert.AreSame(_knife, _weapons.Active.Definition);
            Assert.AreEqual(WeaponSlot.Melee, _weapons.ActiveSlot);

            Assert.AreEqual(InteractionResult.WeaponTaken, Interact());
            Assert.AreSame(_bat, _weapons.Get(WeaponSlot.Melee).Definition, "The new one takes the slot.");
            Assert.AreEqual(1, added.Count, "The old one drops…");
            Assert.AreEqual("weapon_1", added[0].Id);
            Assert.AreEqual(new GridPosition(6, 1), added[0].Cell, "…into the player's cell.");

            WalkTo(7);
            Interact();
            Assert.AreSame(_pistol, _weapons.Active.Definition);
            Assert.AreSame(_bat, _weapons.Get(WeaponSlot.Melee).Definition, "Other slot is kept.");

            _input.Press(PlayerAction.SwitchMelee);
            _weapons.Tick(Dt);
            _input.EndFrame();
            Assert.AreEqual(WeaponSlot.Melee, _weapons.ActiveSlot);
        }

        [Test]
        public void DroppedWeapon_IsFullyReady_WhenPickedAgain()
        {
            var weapon = new WeaponRuntime(_level.Weapons[2]);
            Assert.AreEqual(6, weapon.Ammo);

            _weapons.Equip(weapon);
            var other = new WeaponRuntime(new WeaponPickupData("weapon_9", new GridPosition(1, 1), _pistol));
            var dropped = _weapons.Equip(other);
            Assert.AreSame(weapon, dropped);
            Assert.AreEqual(_pistol.MagazineSize, dropped.Ammo, "Reset when dropped (ТЗ §67).");
        }

        [Test]
        public void Interact_WithNothingAround_DoesNothing()
        {
            Interact();
            WalkTo(4);
            Assert.IsNull(Interact());
        }
    }
}
