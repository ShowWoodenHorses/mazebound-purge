using System.Collections.Generic;
using System.Threading;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visibility;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Spatial;
using Maze.Gameplay.Weapons;
using Maze.Tests.EditMode.Player;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Combat
{
    /// <summary>Player attacks, bullets, spatial queries and combat sounds (ТЗ §66, §68–71, §81).</summary>
    public class CombatTests
    {
        private const float Dt = 1f / 60f;

        private LevelData _level;
        private PlayerDefinition _definition;
        private WeaponDefinition _knife, _pistol;
        private FakePlayerInput _input;
        private DoorSystem _doors;
        private SpatialQueryService _spatial;
        private SoundEventBus _sounds;
        private WeaponSystem _weapons;
        private PlayerSystem _player;
        private BulletSystem _bullets;
        private PlayerCombat _combat;
        private TestAim _aim;
        private readonly List<SoundEvent> _heard = new List<SoundEvent>();
        private readonly List<(Vector2 Point, BulletEnd Reason)> _ended = new List<(Vector2, BulletEnd)>();

        private sealed class TestAim : IAimSettings
        {
            public AimMode AimMode { get; set; }
            public bool AimAssist { get; set; }
        }

        private sealed class Target : IDamageable
        {
            public Target(string id, Vector2 position) { Id = id; Position = position; }
            public string Id { get; }
            public Vector2 Position { get; set; }
            public float Radius => 0.35f;
            public float Health { get; private set; } = 100f;
            public int Hits { get; private set; }
            public bool IsAlive => Health > 0f;

            public void ApplyDamage(float amount, Vector2 direction)
            {
                Health -= amount;
                Hits++;
            }
        }

        /// <summary>
        /// 11x5, room x = 1..9, y = 1..3. Column x = 7 is wall with a closed door at (7,2). Start (2,2).
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _level = ScriptableObject.CreateInstance<LevelData>();
            var geometry = new LevelGeometry(11, 5);
            for (var y = 1; y <= 3; y++)
            for (var x = 1; x <= 9; x++)
                geometry.SetCell(new GridPosition(x, y), x == 7 ? CellType.Wall : CellType.Floor);
            geometry.SetCell(new GridPosition(7, 2), CellType.Door);
            _level.ReplaceGeometry(geometry);
            _level.MutableDoors.Add(new DoorData("door_1", new GridPosition(7, 2)));
            _level.MutablePlayerStarts.Add(new PlayerStartData("start_1", new GridPosition(2, 2)));

            _knife = ScriptableObject.CreateInstance<WeaponDefinition>();
            _knife.Configure("knife", WeaponSlot.Melee);
            _knife.ConfigureCombat(damage: 25f, attackSpeed: 2f, meleeRange: 1.2f, meleeArc: 100f, soundRadius: 3f);
            _pistol = ScriptableObject.CreateInstance<WeaponDefinition>();
            _pistol.Configure("pistol", WeaponSlot.Ranged, magazineSize: 2);
            _pistol.ConfigureCombat(damage: 40f, fireInterval: 0.25f, reloadTime: 1f, fireMode: FireMode.Single,
                projectileSpeed: 20f, soundRadius: 8f);

            _definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _definition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _input = new FakePlayerInput();
            _aim = new TestAim();
            var grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            var occupancy = new OccupancyMap(grid);
            _spatial = new SpatialQueryService(grid, _doors);
            _sounds = new SoundEventBus();
            _sounds.Emitted += _heard.Add;
            _weapons = new WeaponSystem(_input);
            _player = new PlayerSystem(_level, _definition, _input, new LevelPassability(grid, _doors), occupancy,
                new LevelLaunchOptions(startIndex: 0), _aim);
            _bullets = new BulletSystem(_spatial);
            _bullets.Ended += (bullet, point, reason) => _ended.Add((point, reason));
            _combat = new PlayerCombat(_input, _player, _weapons, _spatial, _bullets, _sounds, _aim, _doors);
            _heard.Clear();
            _ended.Clear();
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_definition);
            Object.DestroyImmediate(_knife);
            Object.DestroyImmediate(_pistol);
        }

        private void Equip(WeaponDefinition definition, string id = "weapon_1") =>
            _weapons.Equip(new WeaponRuntime(new WeaponPickupData(id, new GridPosition(1, 1), definition)));

        private Target AddTarget(float x, float y)
        {
            var target = new Target("zombie_" + _spatial.Objects.Count, new Vector2(x, y));
            _spatial.Add(target);
            return target;
        }

        /// <summary>One frame in the level's tick order: attack → movement → bullets.</summary>
        private void Frame()
        {
            _weapons.Tick(Dt);
            _combat.Tick(Dt);
            _player.Tick(Dt);
            _bullets.Tick(Dt);
            _input.EndFrame();
        }

        private void FlyBullets()
        {
            for (var i = 0; i < 600 && _bullets.Active.Count > 0; i++)
                _bullets.Tick(Dt);
            Assert.AreEqual(0, _bullets.Active.Count, "Every bullet ends in a closed maze.");
        }

        // ------------------------------------------------------------------ Melee

        [Test]
        public void Melee_HitsTargetsInFront_NotBehind()
        {
            Equip(_knife);
            var front = AddTarget(3f, 2f);
            var frontSide = AddTarget(2.8f, 2.6f);
            var behind = AddTarget(1f, 2f);

            Assert.IsTrue(_combat.TryAttack(Vector2.right));

            Assert.AreEqual(75f, front.Health);
            Assert.AreEqual(75f, frontSide.Health, "Inside the 100° sector.");
            Assert.AreEqual(100f, behind.Health);
            Assert.AreEqual(2, _combat.LastMeleeHits.Count);
        }

        [Test]
        public void Melee_DoesNotReachThroughAClosedDoor()
        {
            _knife.ConfigureCombat(damage: 25f, attackSpeed: 2f, meleeRange: 2.5f);
            Equip(_knife);
            var beyond = AddTarget(8f, 2f);
            for (var i = 0; i < 300 && _player.Position.x < 6f; i++)
            {
                _input.Move = Vector2.right;
                _player.Tick(Dt);
            }

            _input.Move = Vector2.zero;
            _combat.TryAttack(Vector2.right);
            Assert.AreEqual(100f, beyond.Health, "Closed door blocks.");

            _doors.SetOpen("door_1", true);
            _combat.Tick(1f); // cooldown over
            _combat.TryAttack(Vector2.right);
            Assert.AreEqual(75f, beyond.Health, "Open door does not.");
        }

        private void FreezeDoor(int hits)
        {
            _level.MutableDoors[0].IceHits = hits;
            _doors = new DoorSystem(_level);
            _combat = new PlayerCombat(_input, _player, _weapons, _spatial, _bullets, _sounds, _aim, _doors);
        }

        private void WalkToDoor()
        {
            for (var i = 0; i < 300 && _player.Position.x < 6f; i++)
            {
                _input.Move = Vector2.right;
                _player.Tick(Dt);
            }

            _input.Move = Vector2.zero;
        }

        [Test]
        public void Melee_BreaksTheIceOfAFrozenDoor_HitByHit_AndIsHeard()
        {
            FreezeDoor(2);
            Equip(_knife);
            WalkToDoor();
            Assert.Throws<System.InvalidOperationException>(() => _doors.SetOpen("door_1", true), "Frozen: cannot open.");

            var broken = 0;
            _doors.IceBroken += _ => broken++;
            _heard.Clear();
            Assert.IsTrue(_combat.TryAttack(Vector2.right));
            Assert.AreEqual("door_1", _combat.LastIceHit?.Id);
            Assert.AreEqual(1, _doors.IceLeft("door_1"));
            Assert.IsTrue(_heard.Exists(e => e.Type == SoundType.Door), "Zombies hear the ice.");

            _combat.Tick(1f);
            _combat.TryAttack(Vector2.right);
            Assert.AreEqual(1, broken);
            Assert.IsFalse(_doors.IsFrozen("door_1"));
            Assert.IsFalse(_doors.IsOpen("door_1"), "Broken ice leaves the door closed.");
            _doors.SetOpen("door_1", true);
            Assert.IsTrue(_doors.IsOpen("door_1"));
        }

        [Test]
        public void Ice_NotHitWhenAttackingAway_OrOutOfReach_OrWithBullets()
        {
            FreezeDoor(1);
            Equip(_knife);
            WalkToDoor();
            _combat.TryAttack(Vector2.left);
            Assert.IsNull(_combat.LastIceHit, "Attack away from the door.");
            Assert.IsTrue(_doors.IsFrozen("door_1"));

            FreezeDoor(1);
            Equip(_pistol);
            _combat.TryAttack(Vector2.right);
            FlyBullets();
            Assert.IsTrue(_doors.IsFrozen("door_1"), "Bullets do not break ice.");
        }

        [Test]
        public void Ice_FarFromTheDoor_IsNotHit()
        {
            FreezeDoor(1);
            Equip(_knife); // start (2,2), door at (7,2)
            _combat.TryAttack(Vector2.right);
            Assert.IsNull(_combat.LastIceHit);
            Assert.IsTrue(_doors.IsFrozen("door_1"));
        }

        [Test]
        public void Cooldown_BlocksOnlyTheNextAttack()
        {
            Equip(_knife); // 2 attacks per second
            Assert.IsTrue(_combat.TryAttack(Vector2.right));
            Assert.IsFalse(_combat.TryAttack(Vector2.right));

            for (var t = 0f; t < 0.5f; t += Dt) _combat.Tick(Dt);
            Assert.IsTrue(_combat.TryAttack(Vector2.right));
        }

        [Test]
        public void Attack_StandsForStandTime_StickOnlyTurns_ThenMovesAgain()
        {
            _pistol.ConfigureCombat(damage: 40f, fireInterval: 1f, reloadTime: 1f, fireMode: FireMode.Single);
            Equip(_pistol);
            var start = _player.Position;
            _input.Move = Vector2.right;
            _input.Press(PlayerAction.Attack);
            Frame();

            Assert.AreEqual(start, _player.Position, "No movement during the attack.");
            Assert.AreEqual(Vector2.right, _player.Facing, "Attacked towards the stick.");

            _input.Move = Vector2.up;
            for (var t = Dt; t < PlayerCombat.StandTime - Dt; t += Dt) Frame();
            Assert.AreEqual(start, _player.Position, "Still standing after the attack.");
            Assert.AreEqual(Vector2.up, _player.Facing, "The stick turns the player meanwhile.");

            for (var i = 0; i < 3; i++) Frame();
            Assert.Greater(_player.Position.y, start.y, "Moves again after StandTime (the cooldown is still on).");
        }

        [Test]
        public void HoldingRepeatingAttack_StandsTheWholeTime_ReloadLetsMove()
        {
            _pistol.Configure("pistol", WeaponSlot.Ranged, magazineSize: 3);
            _pistol.ConfigureCombat(damage: 40f, fireInterval: 0.25f, reloadTime: 1f, fireMode: FireMode.Automatic);
            Equip(_pistol);
            var start = _player.Position;
            _input.Move = Vector2.right;
            _input.AttackHeld = true;
            for (var t = 0f; t < 0.6f; t += Dt) Frame();

            Assert.AreEqual(3, _bullets.Active.Count + _ended.Count, "Three shots, then the magazine is empty.");
            Assert.AreEqual(start, _player.Position, "Stands while the burst goes on.");

            for (var t = 0f; t < 0.5f; t += Dt) Frame();
            Assert.Greater(_player.Position.x, start.x, "Reloading: the player may move although the button is held.");
        }

        [Test]
        public void Melee_RepeatsWhileHeld_AtItsAttackSpeed()
        {
            Equip(_knife);
            var target = AddTarget(3f, 2f);
            _player.HoldStill(Vector2.right);
            _player.Tick(Dt); // face the target
            _input.AttackHeld = true;
            for (var t = 0f; t < 1.1f; t += Dt) Frame();

            Assert.AreEqual(3, target.Hits, "At t ≈ 0, 0.5 and 1.0 with 2 attacks per second.");
        }

        // ------------------------------------------------------------------ Ranged

        [Test]
        public void Bullet_HitsTheFirstTarget_AndStops()
        {
            Equip(_pistol);
            var near = AddTarget(4f, 2f);
            var far = AddTarget(5.5f, 2f);

            _combat.TryAttack(Vector2.right);
            FlyBullets();

            Assert.AreEqual(60f, near.Health);
            Assert.AreEqual(100f, far.Health, "The bullet is destroyed on the first target.");
            Assert.AreEqual(BulletEnd.Hit, _ended[0].Reason);
            Assert.AreEqual(4f - 0.35f, _ended[0].Point.x, 0.01f);
        }

        [Test]
        public void Bullet_StopsAtClosedDoor_PassesOpenDoor_StopsAtWall()
        {
            Equip(_pistol);
            _combat.TryAttack(Vector2.right);
            FlyBullets();
            Assert.AreEqual(BulletEnd.Blocked, _ended[0].Reason);
            Assert.AreEqual(6.5f, _ended[0].Point.x, 0.01f, "Closed door cell starts at x = 6.5.");

            _doors.SetOpen("door_1", true);
            _combat.Tick(1f);
            _combat.TryAttack(Vector2.right);
            FlyBullets();
            Assert.AreEqual(9.5f, _ended[1].Point.x, 0.01f, "Through the open door to the outer wall.");
        }

        [Test]
        public void DeadTargets_AreIgnoredByBullets()
        {
            Equip(_pistol);
            var target = AddTarget(4f, 2f);
            target.ApplyDamage(1000f, Vector2.right);
            _combat.TryAttack(Vector2.right);
            FlyBullets();
            Assert.AreEqual(BulletEnd.Blocked, _ended[0].Reason);
        }

        [Test]
        public void EmptyMagazine_ReloadsAutomatically()
        {
            Equip(_pistol); // magazine 2, reload 1 s
            var weapon = _weapons.Active;
            Assert.IsTrue(_combat.TryAttack(Vector2.right));
            _combat.Tick(0.25f);
            Assert.IsTrue(_combat.TryAttack(Vector2.right));

            Assert.AreEqual(0, weapon.Ammo);
            Assert.IsTrue(weapon.IsReloading);
            _combat.Tick(0.5f);
            Assert.IsFalse(_combat.TryAttack(Vector2.right), "Cannot fire while reloading.");

            _combat.Tick(0.6f);
            Assert.IsFalse(weapon.IsReloading);
            Assert.AreEqual(2, weapon.Ammo, "Infinite reserve: a full magazine.");
        }

        [Test]
        public void ReloadButton_OrActiveRangedSlotAgain_ReloadsEarly_NotWhenFull()
        {
            Equip(_knife, "weapon_k");
            Equip(_pistol); // magazine 2, reload 1 s
            var weapon = _weapons.Active;
            _input.Press(PlayerAction.Reload);
            Frame();
            Assert.IsFalse(weapon.IsReloading, "A full magazine does not reload.");

            Assert.IsTrue(_combat.TryAttack(Vector2.right));
            _input.Press(PlayerAction.Reload);
            Frame();
            Assert.IsTrue(weapon.IsReloading, "R reloads a magazine that is not full.");
            for (var t = 0f; t < 1.1f; t += Dt) Frame();
            Assert.AreEqual(2, weapon.Ammo);

            Assert.IsTrue(_combat.TryAttack(Vector2.right));
            _input.Press(PlayerAction.SwitchRanged);
            Frame();
            Assert.IsTrue(weapon.IsReloading, "Choosing the active ranged slot again reloads it.");

            for (var t = 0f; t < 1.1f; t += Dt) Frame();
            Assert.IsTrue(_combat.TryAttack(Vector2.right));
            for (var t = 0f; t < 0.3f; t += Dt) Frame();
            _weapons.Activate(WeaponSlot.Melee);
            _input.Press(PlayerAction.SwitchRanged);
            Frame();
            Assert.AreEqual(WeaponSlot.Ranged, _weapons.ActiveSlot);
            Assert.IsFalse(weapon.IsReloading, "Switching to the ranged slot only switches.");
        }

        [Test]
        public void SingleFire_NeedsAPressPerShot_AutomaticFiresWhileHeld()
        {
            Equip(_pistol);
            _input.AttackHeld = true;
            for (var t = 0f; t < 0.6f; t += Dt) Frame();
            Assert.AreEqual(0, _bullets.Active.Count + _ended.Count, "Single: holding alone does not fire.");

            _input.Press(PlayerAction.Attack);
            Frame();
            Assert.AreEqual(1, _bullets.Active.Count + _ended.Count);

            _pistol.Configure("pistol", WeaponSlot.Ranged, magazineSize: 100);
            _pistol.ConfigureCombat(damage: 40f, fireInterval: 0.25f, reloadTime: 1f, fireMode: FireMode.Automatic);
            Equip(_pistol, "weapon_2");
            _input.AttackHeld = true;
            for (var t = 0f; t < 1.1f; t += Dt) Frame();
            Assert.AreEqual(1 + 5, _bullets.Active.Count + _ended.Count, "Automatic: t ≈ 0, .25, .5, .75, 1.0.");
        }

        // ------------------------------------------------------------------ Sounds

        [Test]
        public void Attacks_AndSteps_EmitSounds()
        {
            Equip(_knife);
            _combat.TryAttack(Vector2.right);
            Assert.AreEqual(SoundType.Melee, _heard[0].Type);
            Assert.AreEqual(3f, _heard[0].Radius);

            var footsteps = new PlayerFootsteps(_player, _sounds);
            _input.Move = Vector2.right;
            for (var t = 0f; t < 1f; t += Dt)
            {
                _player.Tick(Dt);
                footsteps.Tick(Dt);
            }

            var steps = _heard.FindAll(s => s.Type == SoundType.Step).Count;
            Assert.AreEqual(3, steps, "3 cells walked, a step every 0.8 cells.");
            Assert.IsTrue(new SoundEvent(SoundType.Step, Vector2.zero, 2f).IsHeardAt(new Vector2(1.9f, 0f)));
        }

        // ------------------------------------------------------------------ Spatial queries

        // ------------------------------------------------------------------ Aim

        [Test]
        public void AimSnap_EightAndFourDirections()
        {
            Vector2 Dir(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

            Assert.AreEqual(Vector2.right, Aim.Snap(Dir(10f), AimMode.Eight));
            AssertDirection(Dir(45f), Aim.Snap(Dir(30f), AimMode.Eight));
            Assert.AreEqual(Vector2.right, Aim.Snap(Dir(40f), AimMode.Four));
            Assert.AreEqual(Vector2.up, Aim.Snap(Dir(50f), AimMode.Four));
            Assert.AreEqual(Vector2.down, Aim.Snap(new Vector2(0.2f, -3f), AimMode.Four));
            AssertDirection(Dir(30f), Aim.Snap(Dir(30f) * 3f, AimMode.Free));
        }

        [Test]
        public void Facing_SnapsWhenThePlayerStops_AttackDirectionIsSnapped()
        {
            _aim.AimMode = AimMode.Eight;
            Equip(_pistol);
            _input.Move = new Vector2(1f, 0.3f);
            Frame();
            AssertDirection(new Vector2(1f, 0.3f).normalized, _player.Facing, "Free while moving.");

            _input.Move = Vector2.zero;
            Frame();
            Assert.AreEqual(Vector2.right, _player.Facing, "Snapped on stop.");

            Vector2? attacked = null;
            _combat.Attacked += (_, direction) => attacked = direction;
            _input.Move = new Vector2(0.2f, 1f);
            _input.Press(PlayerAction.Attack);
            Frame();
            Assert.AreEqual(Vector2.up, attacked);
        }

        [Test]
        public void AimAssist_TurnsToTheTarget_FallsBackToSnapping()
        {
            _aim.AimMode = AimMode.Eight;
            var target = AddTarget(5f, 2.8f); // About 15 degrees off east.
            var origin = _player.Position;

            Assert.AreEqual(Vector2.right, _combat.AimDirection(_pistol, Vector2.right), "No assist: snapped.");

            _aim.AimAssist = true;
            AssertDirection((target.Position - origin).normalized, _combat.AimDirection(_pistol, Vector2.right));
            Assert.AreEqual(Vector2.up, _combat.AimDirection(_pistol, new Vector2(0.1f, 1f)), "Outside the cone.");

            Equip(_pistol);
            _input.Move = Vector2.right;
            _input.Press(PlayerAction.Attack);
            Frame();
            FlyBullets();
            Assert.AreEqual(1, target.Hits, "The assisted shot hits.");
        }

        [Test]
        public void AimAssist_PrefersNearTargets_IgnoresTargetsBehindWallsAndOutOfReach()
        {
            _aim.AimAssist = true;
            var origin = _player.Position;

            var behindDoor = AddTarget(8f, 2.4f);
            AssertDirection(Vector2.right, _combat.AimDirection(_pistol, Vector2.right), "Closed door blocks the assist.");
            _doors.SetOpen("door_1", true);
            AssertDirection((behindDoor.Position - origin).normalized, _combat.AimDirection(_pistol, Vector2.right));

            var near = AddTarget(4f, 2.4f);
            AddTarget(6f, 2f);
            AssertDirection((near.Position - origin).normalized, _combat.AimDirection(_pistol, Vector2.right), "Nearer wins.");

            AssertDirection(Vector2.right, _combat.AimDirection(_knife, Vector2.right), "Melee reach is short.");
        }

        private static void AssertDirection(Vector2 expected, Vector2 actual, string message = null)
        {
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(1e-4f), $"{message} expected {expected}, got {actual}");
        }

        [Test]
        public void SpatialQueries_CellAroundRadius()
        {
            var a = AddTarget(3f, 2f);
            var b = AddTarget(3.2f, 2.1f);
            var c = AddTarget(6f, 1f);
            var results = new List<ISpatialObject>();

            _spatial.GetObjectsInCell(new GridPosition(3, 2), results);
            CollectionAssert.AreEquivalent(new ISpatialObject[] { a, b }, results);

            _spatial.GetObjectsAroundCell(new GridPosition(5, 2), 1, results);
            CollectionAssert.AreEquivalent(new ISpatialObject[] { c }, results);

            _spatial.QueryRadius(new Vector2(2f, 2f), 0.7f, results);
            CollectionAssert.AreEquivalent(new ISpatialObject[] { a }, results, "Circles touching count.");

            Assert.IsTrue(_spatial.Remove(a));
            _spatial.GetObjectsInCell(new GridPosition(3, 2), results);
            CollectionAssert.AreEquivalent(new ISpatialObject[] { b }, results);
        }

        [Test]
        public void GridRaycast_HitPoint_CornerRule_StartInside()
        {
            var opaque = new bool[5 * 5];
            opaque[2 * 5 + 3] = true; // (3,2)
            var opacity = new CellMaskOpacity(5, 5, opaque);

            Assert.IsTrue(GridRaycast.Cast(new Vector2(1f, 2f), new Vector2(4f, 2f), opacity, out var t, out var cell));
            Assert.AreEqual(new GridPosition(3, 2), cell);
            Assert.AreEqual(1.5f / 3f, t, 1e-4f, "Enters the cell at x = 2.5.");

            Assert.IsFalse(GridRaycast.Cast(new Vector2(1f, 1f), new Vector2(2f, 1f), opacity, out t, out _));
            Assert.AreEqual(1f, t);

            var corner = new bool[3 * 3];
            corner[0 * 3 + 1] = corner[1 * 3 + 0] = true; // (1,0) and (0,1)
            Assert.IsTrue(GridRaycast.Cast(Vector2.zero, new Vector2(1.2f, 1.2f), new CellMaskOpacity(3, 3, corner), out t, out _),
                "No shooting through a diagonal gap.");

            Assert.IsTrue(GridRaycast.Cast(new Vector2(3f, 2f), new Vector2(4f, 2f), opacity, out t, out _));
            Assert.AreEqual(0f, t, "Starting inside a wall.");
        }
    }
}
