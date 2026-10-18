using System.Collections.Generic;
using System.Threading;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visibility;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Weapons;
using Purge.Tests.EditMode.Player;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Combat
{
    /// <summary>Player attacks (attack modules), ammo, bullets, spatial queries and combat sounds.</summary>
    public class CombatTests
    {
        private const float Dt = 1f / 60f;

        private LevelData _level;
        private PlayerDefinition _definition;
        private AmmoDefinition _9mm;
        private WeaponDefinition _knife, _pistol;
        private readonly List<Object> _created = new List<Object>();
        private FakePlayerInput _input;
        private DoorSystem _doors;
        private SpatialQueryService _spatial;
        private CombatTargets _targets;
        private SoundEventBus _sounds;
        private AmmoStock _stock;
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

            _9mm = ScriptableObject.CreateInstance<AmmoDefinition>();
            _9mm.Configure("ammo_9mm", 8f);
            _created.Add(_9mm);
            _knife = Weapon("knife", new MeleeAttack { Damage = 25f, Interval = 0.5f, Range = 1.2f, Arc = 100f, SoundRadius = 3f });
            _pistol = Weapon("pistol", Bullets(), Ammo(2));

            _definition = ScriptableObject.CreateInstance<PlayerDefinition>();
            _definition.Configure(moveSpeed: 3f, bodyHalfSize: 0.3f, cornerAssist: 0.35f);
            _input = new FakePlayerInput();
            _aim = new TestAim();
            var grid = new LevelGrid(_level.Geometry);
            _doors = new DoorSystem(_level);
            var occupancy = new OccupancyMap(grid);
            _spatial = new SpatialQueryService(grid, _doors);
            _targets = new CombatTargets(_spatial);
            _sounds = new SoundEventBus();
            _sounds.Emitted += _heard.Add;
            _stock = new AmmoStock();
            _stock.Add(_9mm, 100);
            _weapons = new WeaponSystem(_input, _definition);
            _player = new PlayerSystem(_level, _definition, _input, new LevelPassability(grid, _doors), occupancy,
                new LevelLaunchOptions(startIndex: 0), _aim);
            _bullets = new BulletSystem(_targets);
            _bullets.Ended += (bullet, point, reason) => _ended.Add((point, reason));
            _combat = NewCombat();
            _heard.Clear();
            _ended.Clear();
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_level);
            Object.DestroyImmediate(_definition);
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private PlayerCombat NewCombat() =>
            new PlayerCombat(_input, _player, _weapons, _targets, _bullets, _sounds, _aim, _doors, _stock);

        private WeaponDefinition Weapon(string id, params WeaponModule[] modules)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.Configure(id, modules);
            _created.Add(weapon);
            return weapon;
        }

        private static BulletAttack Bullets(float interval = 0.25f, bool automatic = false) =>
            new BulletAttack { Damage = 40f, Interval = interval, Automatic = automatic, Speed = 20f, Range = 30f, Spread = 0f };

        private AmmoModule Ammo(int magazine, float reload = 1f) =>
            new AmmoModule { Ammo = _9mm, Magazine = magazine, ReloadTime = reload };

        private WeaponRuntime Equip(WeaponDefinition definition, string id = "weapon_1")
        {
            var weapon = new WeaponRuntime(new WeaponPickupData(id, new GridPosition(1, 1), definition));
            _weapons.Equip(weapon);
            return weapon;
        }

        private Target AddTarget(float x, float y)
        {
            var target = new Target("zombie_" + _spatial.Objects.Count, new Vector2(x, y));
            _spatial.Add(target);
            return target;
        }

        /// <summary>One frame in the level's tick order: slots → attack → movement → bullets.</summary>
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
            Assert.AreEqual(0, _bullets.Active.Count, "Every bullet ends in a closed room.");
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
            Assert.AreEqual(2, _combat.LastMeleeTargets.Count);
        }

        [Test]
        public void Melee_DoesNotReachThroughAClosedDoor()
        {
            var spear = Weapon("spear", new MeleeAttack { Damage = 25f, Interval = 0.5f, Range = 2.5f });
            Equip(spear);
            var beyond = AddTarget(8f, 2f);
            WalkToDoor();
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
            _combat = NewCombat();
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
            Assert.IsTrue(_heard.Exists(e => e.Type == SoundType.Door), "Monsters hear the ice.");

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
        public void Melee_RepeatsWhileHeld_AtItsAttackSpeed()
        {
            Equip(_knife);
            var target = AddTarget(3f, 2f);
            _input.Look = Vector2.right; // gamepad right stick
            _input.AttackHeld = true;
            for (var t = 0f; t < 1.1f; t += Dt) Frame();

            Assert.AreEqual(3, target.Hits, "At t ≈ 0, 0.5 and 1.0 with 2 attacks per second.");
        }

        // ------------------------------------------------------------------ Shooting on the move, mobility

        [Test]
        public void ShootingOnTheMove_LegsWalk_BodyFacesTheAim()
        {
            Equip(Weapon("rifle", Bullets(0.1f, automatic: true), Ammo(30)));
            var start = _player.Position;
            _input.Move = Vector2.up;
            _input.Look = Vector2.right;
            _input.AttackHeld = true;
            for (var i = 0; i < 10; i++) Frame();

            Assert.Greater(_player.Position.y, start.y + 0.3f, "Walks while shooting.");
            Assert.AreEqual(Vector2.up, _player.MoveDirection, "Legs: the stick.");
            AssertDirection(Vector2.right, _player.Facing, "Body: the aim.");
            Assert.IsTrue(_combat.IsFiring);
            Assert.Greater(_bullets.Active.Count + _ended.Count, 1);

            _input.AttackHeld = false;
            _input.Look = Vector2.zero;
            for (var t = 0f; t < PlayerCombat.FiringLinger + 0.05f; t += Dt) Frame();
            Assert.IsFalse(_combat.IsFiring);
            AssertDirection(Vector2.right, _player.Facing, "The combat stance keeps the aim.");
            for (var t = 0f; t < PlayerCombat.StanceTime; t += Dt) Frame();
            Assert.IsFalse(_combat.InStance);
            Assert.AreEqual(Vector2.up, _player.Facing, "After the stance the body turns where it walks.");
        }

        [Test]
        public void Retreating_ShootsTheCrowdBehind_AndKeepsFacingItBetweenShots()
        {
            _aim.AimAssist = true;
            Equip(Weapon("slow", Bullets(0.6f), Ammo(10)));
            var behind = AddTarget(1.2f, 2f); // west; the player runs east
            _input.Move = Vector2.right;
            _input.Press(PlayerAction.Attack);
            Frame();
            AssertDirection(Vector2.left, _combat.AimDirection, "Nobody ahead: the nearest target all around.");

            for (var t = 0f; t < 0.5f; t += Dt) Frame();
            Assert.Greater(_player.Position.x, 2.5f, "Kept running.");
            Assert.Less(_player.Facing.x, -0.9f, "Between shots the body still faces the crowd.");
            FlyBullets();
            Assert.AreEqual(1, behind.Hits);
        }

        private sealed class FixedPointer : IAimPointer
        {
            public Vector2 Point;

            public bool TryGetGroundPoint(Vector2 screenPosition, out Vector2 gridPoint)
            {
                gridPoint = Point;
                return true;
            }
        }

        [Test]
        public void Mouse_TheBodyAlwaysFacesThePointer_AndShotsGoThere()
        {
            var pointer = new FixedPointer { Point = new Vector2(2f, 0f) }; // south of the start (2,2)
            _combat = new PlayerCombat(_input, _player, _weapons, _targets, _bullets, _sounds, _aim, _doors, _stock, pointer);
            Equip(_pistol);
            _input.LookIsPointer = true;
            _input.Move = Vector2.right;
            Frame();
            Assert.Greater(_player.Position.x, 2f, "Walks east…");
            AssertDirection(Vector2.down, _player.Facing, "…facing the mouse without shooting.");
            Assert.IsTrue(_player.IsAiming);

            _input.Press(PlayerAction.Attack);
            Frame();
            Assert.Less(_combat.AimDirection.y, -0.99f, "The shot goes to the mouse.");
        }

        [Test]
        public void Mobility_SlowsInHands_AndWhileFiring_OnlyTheWeaponInHands()
        {
            var minigun = Weapon("minigun", Bullets(0.1f, automatic: true), Ammo(100),
                new MobilityModule { InHands = 0.5f, WhileFiring = 0f });
            Equip(minigun);
            Equip(_knife, "weapon_k"); // the second slot; the knife is in hands
            float Walk(float seconds)
            {
                var from = _player.Position.x;
                _input.Move = Vector2.right;
                for (var t = 0f; t < seconds; t += Dt) Frame();
                return _player.Position.x - from;
            }

            var knife = Walk(0.5f);
            _weapons.Activate(WeaponSlot.First);
            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult(); // back to the start
            var minigunWalk = Walk(0.5f);
            Assert.AreEqual(knife * 0.5f, minigunWalk, 0.05f, "Half speed with the minigun in hands.");

            _player.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            _input.AttackHeld = true;
            Assert.AreEqual(0f, Walk(0.3f), 0.01f, "Stands while the minigun fires.");
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
        public void PiercingBullet_PassesThroughTargets()
        {
            var sniper = Weapon("sniper", new BulletAttack { Damage = 30f, Speed = 30f, Range = 30f, Spread = 0f, Pierce = 1 }, Ammo(5));
            Equip(sniper);
            var first = AddTarget(3.5f, 2f);
            var second = AddTarget(4.5f, 2f);
            var third = AddTarget(5.5f, 2f);

            _combat.TryAttack(Vector2.right);
            FlyBullets();
            Assert.AreEqual(70f, first.Health);
            Assert.AreEqual(70f, second.Health, "Pierce 1: through one target into the next.");
            Assert.AreEqual(100f, third.Health);
        }

        [Test]
        public void Bullet_FliesItsRange()
        {
            Equip(Weapon("short", new BulletAttack { Damage = 10f, Speed = 20f, Range = 2f, Spread = 0f }, Ammo(5)));
            _combat.TryAttack(Vector2.right);
            FlyBullets();
            Assert.AreEqual(BulletEnd.Expired, _ended[0].Reason);
            Assert.AreEqual(4f, _ended[0].Point.x, 0.01f);
        }

        [Test]
        public void Pellets_FlyInAFan()
        {
            var shotgun = Weapon("shotgun", new BulletAttack { Damage = 8f, Speed = 20f, Range = 5f, Pellets = 5, Spread = 40f }, Ammo(6));
            Equip(shotgun);
            Assert.IsTrue(_combat.TryAttack(Vector2.right));
            Assert.AreEqual(5, _bullets.Active.Count);
            var up = 0;
            var down = 0;
            foreach (var bullet in _bullets.Active)
            {
                var angle = Vector2.SignedAngle(Vector2.right, bullet.Direction);
                Assert.LessOrEqual(Mathf.Abs(angle), 20f + 1e-3f, "Within the fan.");
                if (angle > 5f) up++;
                if (angle < -5f) down++;
            }

            Assert.AreEqual(2, up);
            Assert.AreEqual(2, down);
            Assert.AreEqual(5, _weapons.Active.Ammo, "One shot takes one round.");
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
        public void Explosive_DamagesAroundTheHit_LessAtTheEdge_NotBehindAClosedDoor()
        {
            var launcher = Weapon("launcher", new ExplosiveAttack
            {
                Speed = 20f, Range = 20f, DirectDamage = 10f, ExplosionDamage = 50f, Radius = 2.5f, EdgeDamage = 0f,
            }, Ammo(4));
            Equip(launcher);
            var hit = AddTarget(5f, 2f);
            var side = AddTarget(5f, 3f);
            var behindDoor = AddTarget(8f, 2f);
            var explosions = 0;
            _bullets.Exploded += (_, _, _) => explosions++;

            _combat.TryAttack(Vector2.right);
            FlyBullets();
            Assert.AreEqual(1, explosions);
            Assert.Less(hit.Health, 100f - 10f - 40f, "Direct hit + the blast near its centre.");
            Assert.Less(side.Health, 100f, "The blast reaches the side.");
            Assert.Greater(side.Health, hit.Health, "Weaker farther away.");
            Assert.AreEqual(100f, behindDoor.Health, "A closed door covers.");
        }

        [Test]
        public void Explosive_ExplodesAtAWall_And_AtTheEndOfItsRange()
        {
            var launcher = Weapon("launcher", new ExplosiveAttack { Speed = 20f, Range = 3f, Radius = 1f }, Ammo(4));
            Equip(launcher);
            var points = new List<Vector2>();
            _bullets.Exploded += (at, _, _) => points.Add(at);
            _combat.TryAttack(Vector2.right);
            FlyBullets();
            Assert.AreEqual(5f, points[0].x, 0.01f, "Range 3 from x = 2.");

            _combat.Tick(2f);
            _combat.TryAttack(Vector2.left);
            FlyBullets();
            Assert.AreEqual(0.55f, points[1].x, 0.01f, "On the player's side of the wall at x = 0.5.");
        }

        [Test]
        public void Stream_DamagesEveryTargetInTheCone_EachTick()
        {
            var flamer = Weapon("flamer", new StreamAttack { DamagePerSecond = 50f, Range = 3f, Angle = 40f, Interval = 0.1f }, Ammo(100));
            Equip(flamer);
            var a = AddTarget(4f, 2f);
            var b = AddTarget(4.5f, 2.6f);
            var outside = AddTarget(2f, 3.5f);
            var far = AddTarget(6.2f, 2f);
            _input.Look = Vector2.right;
            _input.AttackHeld = true;
            for (var t = 0f; t < 0.95f; t += Dt) Frame();

            Assert.AreEqual(10, a.Hits, "A tick every 0.1 s.");
            Assert.AreEqual(50f, a.Health, 0.01f);
            Assert.AreEqual(10, b.Hits);
            Assert.AreEqual(0, outside.Hits);
            Assert.AreEqual(0, far.Hits, "Out of reach.");
            Assert.AreEqual(90, _weapons.Active.Ammo, "A round per tick.");
        }

        [Test]
        public void Chain_HitsTheFirstInFront_ThenJumpsToTheNearest_WeakerEachJump()
        {
            var tesla = Weapon("tesla", new ChainAttack
            {
                Damage = 40f, Range = 5f, AimAngle = 20f, Jumps = 2, JumpRange = 1.5f, JumpFalloff = 0.5f,
            }, Ammo(10));
            Equip(tesla);
            var first = AddTarget(4f, 2f);
            var second = AddTarget(5f, 2.5f);
            var third = AddTarget(5.5f, 3.2f);
            var tooFar = AddTarget(2f, 1f);

            Assert.IsTrue(_combat.TryAttack(Vector2.right));
            Assert.AreEqual(60f, first.Health);
            Assert.AreEqual(80f, second.Health);
            Assert.AreEqual(90f, third.Health);
            Assert.AreEqual(100f, tooFar.Health);
            Assert.AreEqual(4, _combat.ChainCount, "The player and three targets.");
        }

        [Test]
        public void Chain_Miss_GoesStraightToAWall()
        {
            Equip(Weapon("tesla", new ChainAttack { Range = 9f }, Ammo(10)));
            Assert.IsTrue(_combat.TryAttack(Vector2.right));
            Assert.AreEqual(2, _combat.ChainCount);
            Assert.AreEqual(6.5f, _combat.ChainPoints[1].x, 0.01f, "Stops at the closed door.");
        }

        // ------------------------------------------------------------------ Ammo and reload

        [Test]
        public void EmptyMagazine_ReloadsAutomatically_FromTheReserve()
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
            Assert.AreEqual(2, weapon.Ammo);
            Assert.AreEqual(98, _stock.Get(_9mm), "Taken from the reserve.");
        }

        [Test]
        public void EmptyReserve_PartialReload_ThenOutOfAmmo_ClicksOnly()
        {
            _stock.Take(_9mm, 99); // 1 left
            Equip(_pistol);
            var weapon = _weapons.Active;
            var dry = 0;
            _combat.DryFired += _ => dry++;
            _combat.TryAttack(Vector2.right);
            _combat.Tick(0.3f);
            _combat.TryAttack(Vector2.right);
            _combat.Tick(1.1f);
            Assert.AreEqual(1, weapon.Ammo, "Only one round was left.");
            Assert.AreEqual(0, _stock.Get(_9mm));

            _combat.TryAttack(Vector2.right);
            _combat.Tick(0.3f);
            Assert.IsTrue(_combat.IsOutOfAmmo(weapon));
            Assert.IsFalse(_combat.TryReload());
            _input.Press(PlayerAction.Attack);
            Frame();
            Assert.AreEqual(1, dry, "An empty gun clicks.");
            Assert.AreEqual(3, _ended.Count + _bullets.Active.Count, "Two from the magazine and the last of the reserve.");
        }

        [Test]
        public void InfiniteReserve_AlwaysReloads()
        {
            var basePistol = Weapon("base_pistol", Bullets(), new AmmoModule { Magazine = 2, ReloadTime = 0.5f, InfiniteReserve = true });
            Equip(basePistol);
            var weapon = _weapons.Active;
            for (var i = 0; i < 6; i++)
            {
                _combat.Tick(1f);
                Assert.IsTrue(_combat.TryAttack(Vector2.right), $"Shot {i + 1}.");
            }

            Assert.IsFalse(_combat.IsOutOfAmmo(weapon));
            Assert.AreEqual(100, _stock.Get(_9mm), "No reserve used.");
        }

        [Test]
        public void ReloadButton_OrActiveSlotAgain_ReloadsEarly_NotWhenFull()
        {
            Equip(_knife, "weapon_k"); // slot 1
            Equip(_pistol); // slot 2, magazine 2, reload 1 s
            var weapon = _weapons.Active;
            Assert.AreEqual(WeaponSlot.Second, _weapons.ActiveSlot);
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
            _input.Press(PlayerAction.Slot2);
            Frame();
            Assert.IsTrue(weapon.IsReloading, "Choosing the active slot again reloads it.");

            for (var t = 0f; t < 1.1f; t += Dt) Frame();
            Assert.IsTrue(_combat.TryAttack(Vector2.right));
            for (var t = 0f; t < 0.3f; t += Dt) Frame();
            _weapons.Activate(WeaponSlot.First);
            _input.Press(PlayerAction.Slot2);
            Frame();
            Assert.AreEqual(WeaponSlot.Second, _weapons.ActiveSlot);
            Assert.IsFalse(weapon.IsReloading, "Switching to the slot only switches.");
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

            Equip(Weapon("smg", Bullets(0.25f, automatic: true), Ammo(100)), "weapon_2");
            _input.AttackHeld = true;
            for (var t = 0f; t < 1.1f; t += Dt) Frame();
            Assert.AreEqual(1 + 5, _bullets.Active.Count + _ended.Count, "Automatic: t ≈ 0, .25, .5, .75, 1.0.");
        }

        // ------------------------------------------------------------------ Slots

        [Test]
        public void Slots_BasePistolInEmptySlots_PickupFillsThemFirst_DropBringsItBack()
        {
            var basePistol = Weapon("glock", Bullets(), new AmmoModule { Magazine = 12, InfiniteReserve = true });
            _definition.BaseWeapon = basePistol;
            _weapons = new WeaponSystem(_input, _definition);
            Assert.AreSame(basePistol, _weapons.Get(WeaponSlot.First).Definition);
            Assert.AreSame(basePistol, _weapons.Get(WeaponSlot.Second).Definition);
            Assert.IsTrue(_weapons.IsEmpty(WeaponSlot.First));

            Assert.IsNull(_weapons.Equip(new WeaponRuntime(new WeaponPickupData("w1", default, _knife))), "The base pistol is not dropped.");
            Assert.AreEqual(WeaponSlot.First, _weapons.ActiveSlot);
            Assert.IsNull(_weapons.Equip(new WeaponRuntime(new WeaponPickupData("w2", default, _pistol))));
            Assert.AreEqual(WeaponSlot.Second, _weapons.ActiveSlot, "The other slot had the base pistol.");

            var dropped = _weapons.Equip(new WeaponRuntime(new WeaponPickupData("w3", default, _knife)));
            Assert.AreEqual("w2", dropped?.Id, "Both slots full: the weapon in hands is replaced.");

            Assert.AreEqual("w3", _weapons.Drop(WeaponSlot.Second)?.Id);
            Assert.IsTrue(_weapons.Get(WeaponSlot.Second).IsBase, "The base pistol is back.");
            Assert.IsNull(_weapons.Drop(WeaponSlot.Second), "The base pistol cannot be dropped.");
        }

        [Test]
        public void HoldingASlotKey_AsksToDropItsWeapon()
        {
            Equip(_knife);
            var asked = new List<WeaponSlot>();
            _weapons.DropRequested += asked.Add;
            _input.Press(PlayerAction.Slot1);
            _input.Hold(PlayerAction.Slot1, true);
            for (var t = 0f; t < WeaponSystem.DropHoldTime - 0.1f; t += Dt) Frame();
            Assert.AreEqual(0, asked.Count, "Not yet.");
            for (var t = 0f; t < 0.2f; t += Dt) Frame();
            CollectionAssert.AreEqual(new[] { WeaponSlot.First }, asked);
            for (var t = 0f; t < 1f; t += Dt) Frame();
            Assert.AreEqual(1, asked.Count, "Once per hold.");
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

        [Test]
        public void ShotNoise_IsTheCalibers_TimesTheSilencer_OrTheWeaponsOwn()
        {
            Assert.AreEqual(8f, _pistol.SoundRadius, "The ammo type's.");
            var silenced = Weapon("silenced", Bullets(), Ammo(10), new SilencerModule { Noise = 0.25f });
            Assert.AreEqual(2f, silenced.SoundRadius);
            var loud = Weapon("loud", Bullets(), new AmmoModule { Ammo = _9mm, Magazine = 5, SoundRadius = 12f });
            Assert.AreEqual(12f, loud.SoundRadius, "Own override.");
            Assert.AreEqual(3f, _knife.SoundRadius, "Melee: the swing's.");

            Equip(silenced);
            _combat.TryAttack(Vector2.right);
            Assert.AreEqual(SoundType.Ranged, _heard[0].Type);
            Assert.AreEqual(2f, _heard[0].Radius);
        }

        [Test]
        public void WeaponValidation_NeedsOneAttack_AndAmmoForRanged()
        {
            var problems = new List<string>();
            _pistol.Validate(problems);
            _knife.Validate(problems);
            CollectionAssert.IsEmpty(problems);

            Weapon("none").Validate(problems);
            Assert.AreEqual(1, problems.Count, "No attack.");
            problems.Clear();
            Weapon("no_ammo", Bullets()).Validate(problems);
            Assert.AreEqual(1, problems.Count, "Ranged without ammo.");
            problems.Clear();
            Weapon("two", Bullets(), new MeleeAttack(), Ammo(5)).Validate(problems);
            Assert.AreEqual(1, problems.Count, "Two attacks.");
        }

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

            Assert.AreEqual(Vector2.right, _combat.AssistedDirection(_pistol, Vector2.right), "No assist: snapped.");

            _aim.AimAssist = true;
            AssertDirection((target.Position - origin).normalized, _combat.AssistedDirection(_pistol, Vector2.right));
            Assert.AreEqual(Vector2.up, _combat.AssistedDirection(_pistol, new Vector2(0.1f, 1f)), "Outside the cone.");

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
            AssertDirection(Vector2.right, _combat.AssistedDirection(_pistol, Vector2.right), "Closed door blocks the assist.");
            _doors.SetOpen("door_1", true);
            AssertDirection((behindDoor.Position - origin).normalized, _combat.AssistedDirection(_pistol, Vector2.right));

            var near = AddTarget(4f, 2.4f);
            AddTarget(6f, 2f);
            AssertDirection((near.Position - origin).normalized, _combat.AssistedDirection(_pistol, Vector2.right), "Nearer wins.");

            AssertDirection(Vector2.right, _combat.AssistedDirection(_knife, Vector2.right), "Melee reach is short.");
        }

        private static void AssertDirection(Vector2 expected, Vector2 actual, string message = null)
        {
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(1e-4f), $"{message} expected {expected}, got {actual}");
        }

        // ------------------------------------------------------------------ Spatial queries

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
