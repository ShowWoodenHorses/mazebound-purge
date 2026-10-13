using System;
using System.Collections.Generic;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Spatial;
using Maze.Gameplay.Weapons;
using UnityEngine;

namespace Maze.Gameplay.Combat
{
    /// <summary>
    /// The player's attacks (ТЗ §66, §68, §69). Attack = Attack button; the direction is the facing (movement
    /// direction, ТЗ §65), or the stick direction pressed in the same frame. The player stands while attacking
    /// (ТЗ §66): for <see cref="StandTime"/> after every attack, and all the time the button is held with a weapon
    /// that repeats on hold (melee, automatic; not while reloading). Standing, the stick only turns the player (aims).
    /// The weapon's cooldown blocks only the next attack.
    /// <list type="bullet">
    /// <item>Melee: every living target in a sector in front of the player (range, arc) with no wall or closed
    /// door in between takes the damage. Repeats while the button is held. A frozen door next to the player in the
    /// attack direction, within reach, takes one hit on its ice (<see cref="DoorSystem.HitIce"/>) — heard by zombies as
    /// a door sound. Bullets do not break ice.</item>
    /// <item>Ranged: a bullet (<see cref="BulletSystem"/>). Single fires once per press, Automatic while held.
    /// An empty magazine reloads automatically (ReloadTime); reload runs only while the weapon is in hands.
    /// Reload (or SwitchRanged again, <see cref="WeaponSystem.ReloadRequested"/>) starts it earlier.</item>
    /// </list>
    /// Aiming (<see cref="IAimSettings"/>): with aim assist the attack turns to the best living target within
    /// <see cref="AssistAngle"/> of the wanted direction, in reach and not behind a wall or closed door; otherwise
    /// the wanted direction is snapped (<see cref="Aim.Snap"/>).
    /// Must be registered before <see cref="PlayerSystem"/> (it stops this tick's movement).
    /// </summary>
    public sealed class PlayerCombat : ILevelTickable
    {
        private const float DeadZone = 0.1f;

        /// <summary>Seconds the player stands after an attack.</summary>
        public const float StandTime = 0.25f;

        /// <summary>Half-angle of the aim assist cone, degrees.</summary>
        public const float AssistAngle = 30f;

        /// <summary>Aim assist reach of ranged weapons, cells (about the view window).</summary>
        public const float AssistRange = 6f;

        private readonly IPlayerInput _input;
        private readonly PlayerSystem _player;
        private readonly WeaponSystem _weapons;
        private readonly ISpatialQueryService _spatial;
        private readonly BulletSystem _bullets;
        private readonly SoundEventBus _sounds;
        private readonly IAimSettings _aim;
        private readonly DoorSystem _doors;
        private readonly List<ISpatialObject> _candidates = new List<ISpatialObject>();
        private readonly List<IDamageable> _hits = new List<IDamageable>();
        private float _standLeft;

        public PlayerCombat(IPlayerInput input, PlayerSystem player, WeaponSystem weapons, ISpatialQueryService spatial,
            BulletSystem bullets, SoundEventBus sounds, IAimSettings aim, DoorSystem doors)
        {
            _doors = doors;
            _aim = aim;
            _input = input;
            _player = player;
            _weapons = weapons;
            _spatial = spatial;
            _bullets = bullets;
            _sounds = sounds;
        }

        /// <summary>(weapon, direction) after every attack — for views, animation and HUD.</summary>
        public event Action<WeaponRuntime, Vector2> Attacked;

        /// <summary>(weapon) when a reload starts or ends.</summary>
        public event Action<WeaponRuntime> ReloadChanged;

        /// <summary>Targets hit by the last melee attack.</summary>
        public IReadOnlyList<IDamageable> LastMeleeHits => _hits;

        /// <summary>Frozen door whose ice the last melee attack hit; null if none.</summary>
        public DoorData LastIceHit { get; private set; }

        /// <summary>The player stands this tick because of attacking (see the class remarks).</summary>
        public bool IsStanding { get; private set; }

        public void Tick(float deltaTime)
        {
            _standLeft = Mathf.Max(0f, _standLeft - deltaTime);
            IsStanding = false;
            var weapon = _weapons.Active;
            if (weapon == null || !_player.IsSpawned)
                return;

            UpdateTimers(weapon, deltaTime);
            if (_input.WasPressed(PlayerAction.Reload) || _weapons.ReloadRequested)
                TryReload();
            var move = _input.Move;
            var stick = move.sqrMagnitude >= DeadZone * DeadZone;
            if (WantsToAttack(weapon) && weapon.Cooldown <= 0f && !weapon.IsReloading)
            {
                Attack(weapon, AimDirection(weapon.Definition, stick ? move.normalized : _player.Facing));
                return;
            }

            // Between attacks: still standing, the stick turns the player.
            if (_standLeft > 0f || HoldsRepeatingAttack(weapon))
            {
                _player.HoldStill(stick ? move.normalized : Vector2.zero);
                IsStanding = true;
            }
        }

        private bool HoldsRepeatingAttack(WeaponRuntime weapon) =>
            _input.AttackHeld && !weapon.IsReloading &&
            (weapon.Slot == WeaponSlot.Melee || weapon.Definition.FireMode == FireMode.Automatic);

        /// <summary>Performs an attack with the active weapon now, ignoring input (tests, tools).</summary>
        public bool TryAttack(Vector2 direction)
        {
            var weapon = _weapons.Active;
            if (weapon == null || !_player.IsSpawned || weapon.Cooldown > 0f || weapon.IsReloading)
                return false;

            Attack(weapon, direction.normalized);
            return true;
        }

        /// <summary>
        /// Starts reloading the active ranged weapon unless its magazine is full or it is reloading already.
        /// </summary>
        public bool TryReload()
        {
            var weapon = _weapons.Active;
            if (weapon == null || !_player.IsSpawned || weapon.Slot != WeaponSlot.Ranged || weapon.IsReloading ||
                weapon.Ammo >= weapon.Definition.MagazineSize)
                return false;

            weapon.ReloadRemaining = Mathf.Max(weapon.Definition.ReloadTime, 1e-4f);
            ReloadChanged?.Invoke(weapon);
            return true;
        }

        /// <summary>Direction of an attack the player wants to make towards <paramref name="wanted"/>.</summary>
        public Vector2 AimDirection(WeaponDefinition definition, Vector2 wanted)
        {
            if (_aim.AimAssist && TryFindAssistTarget(definition, wanted, out var toTarget))
                return toTarget;
            return Aim.Snap(wanted, _aim.AimMode);
        }

        /// <summary>
        /// Best target in the cone: nearest, with off-axis targets counted farther (up to twice at the cone edge).
        /// </summary>
        private bool TryFindAssistTarget(WeaponDefinition definition, Vector2 wanted, out Vector2 direction)
        {
            direction = default;
            if (wanted.sqrMagnitude <= 0f) return false;
            wanted.Normalize();

            var origin = _player.Position;
            var range = definition.Slot == WeaponSlot.Melee ? definition.MeleeRange : AssistRange;
            var minCos = Mathf.Cos(AssistAngle * Mathf.Deg2Rad);
            var bestScore = float.MaxValue;
            _spatial.QueryRadius(origin, range, _candidates);

            foreach (var candidate in _candidates)
            {
                if (!(candidate is IDamageable target) || !target.IsAlive)
                    continue;

                var offset = target.Position - origin;
                var distance = offset.magnitude;
                if (distance <= 1e-4f)
                    continue;
                var toTarget = offset / distance;
                var cos = Vector2.Dot(toTarget, wanted);
                if (cos < minCos)
                    continue;

                var offAxis = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg / AssistAngle;
                var score = distance * (1f + offAxis);
                if (score >= bestScore || !_spatial.IsClear(origin, target.Position))
                    continue;

                bestScore = score;
                direction = toTarget;
            }

            _candidates.Clear();
            return bestScore < float.MaxValue;
        }

        private bool WantsToAttack(WeaponRuntime weapon)
        {
            if (_input.WasPressed(PlayerAction.Attack))
                return true;
            if (!_input.AttackHeld)
                return false;
            return weapon.Slot == WeaponSlot.Melee || weapon.Definition.FireMode == FireMode.Automatic;
        }

        private void UpdateTimers(WeaponRuntime weapon, float deltaTime)
        {
            if (weapon.Cooldown > 0f)
                weapon.Cooldown = Mathf.Max(0f, weapon.Cooldown - deltaTime);

            if (!weapon.IsReloading)
                return;

            weapon.ReloadRemaining = Mathf.Max(0f, weapon.ReloadRemaining - deltaTime);
            if (!weapon.IsReloading)
            {
                weapon.Ammo = weapon.Definition.MagazineSize;
                ReloadChanged?.Invoke(weapon);
            }
        }

        private void Attack(WeaponRuntime weapon, Vector2 direction)
        {
            _player.HoldStill(direction);
            _standLeft = StandTime;
            IsStanding = true;
            var definition = weapon.Definition;
            weapon.Cooldown = definition.Cooldown;

            if (weapon.Slot == WeaponSlot.Melee)
            {
                Melee(definition, direction);
                _sounds.Emit(SoundType.Melee, _player.Position, definition.SoundRadius);
            }
            else
            {
                _bullets.Spawn(_player.Position, direction, definition.ProjectileSpeed, definition.Damage);
                _sounds.Emit(SoundType.Ranged, _player.Position, definition.SoundRadius);
                weapon.Ammo--;
                if (weapon.Ammo <= 0)
                {
                    weapon.Ammo = 0;
                    weapon.ReloadRemaining = Mathf.Max(definition.ReloadTime, 1e-4f);
                    ReloadChanged?.Invoke(weapon);
                }
            }

            Attacked?.Invoke(weapon, direction);
        }

        private void Melee(WeaponDefinition definition, Vector2 direction)
        {
            _hits.Clear();
            var origin = _player.Position;
            var halfArcCos = Mathf.Cos(definition.MeleeArc * 0.5f * Mathf.Deg2Rad);
            _spatial.QueryRadius(origin, definition.MeleeRange, _candidates);

            foreach (var candidate in _candidates)
            {
                if (!(candidate is IDamageable target) || !target.IsAlive)
                    continue;

                var offset = target.Position - origin;
                var distance = offset.magnitude;
                // A target overlapping the player is always in front; otherwise check the sector.
                if (distance > target.Radius && Vector2.Dot(offset / distance, direction) < halfArcCos)
                    continue;
                if (!_spatial.IsClear(origin, target.Position))
                    continue;

                _hits.Add(target);
            }

            foreach (var target in _hits)
                target.ApplyDamage(definition.Damage, direction);

            LastIceHit = null;
            var door = FrozenDoorInFront(definition, direction);
            if (door != null)
            {
                LastIceHit = door;
                _doors.HitIce(door.Id);
                _sounds.Emit(SoundType.Door, new Vector2(door.Position.X, door.Position.Y), definition.SoundRadius);
            }
        }

        /// <summary>
        /// The frozen door in a neighbouring cell the attack points at (within the melee arc), whose near face is in
        /// melee range.
        /// </summary>
        private DoorData FrozenDoorInFront(WeaponDefinition definition, Vector2 direction)
        {
            DoorData best = null;
            var bestDot = Mathf.Max(Mathf.Cos(definition.MeleeArc * 0.5f * Mathf.Deg2Rad), 0.1f);
            var origin = _player.Position;
            foreach (var side in DirectionExtensions.All)
            {
                var cell = _player.Cell.Neighbour(side);
                if (!_doors.TryGetDoor(cell, out var door) || !_doors.IsFrozen(door.Id))
                    continue;

                var offset = new Vector2(cell.X, cell.Y) - origin;
                var face = Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) - 0.5f;
                var dot = Vector2.Dot(offset.normalized, direction);
                if (face > definition.MeleeRange || dot < bestDot)
                    continue;

                best = door;
                bestDot = dot;
            }

            return best;
        }
    }
}
