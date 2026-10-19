using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Run;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Weapons;
using UnityEngine;

namespace Purge.Gameplay.Combat
{
    /// <summary>
    /// The player's attacks with the weapon in hands, written once per attack module (<see cref="WeaponAttack"/>):
    /// <list type="bullet">
    /// <item><see cref="BulletAttack"/> — bullets or a fan of pellets (<see cref="BulletSystem"/>);
    /// <see cref="ExplosiveAttack"/> — a projectile that explodes where it ends.</item>
    /// <item><see cref="StreamAttack"/> — every target in a cone in front, each tick of the stream; with
    /// <see cref="IgniteCellsModule"/> the flammable cells in the cone catch fire (<see cref="ZoneSystem"/>).</item>
    /// <item><see cref="ChainAttack"/> — the first target in a cone, then jumps to the nearest ones
    /// (<see cref="ChainPoints"/> for views).</item>
    /// <item><see cref="MeleeAttack"/> — every target in a sector; also one hit on a frozen door in front.</item>
    /// </list>
    /// Targets — the horde and damageable objects (<see cref="CombatTargets"/>); walls and closed doors cover.
    /// <para>Shooting on the move (decision 2026-10-14): the player keeps walking; the body turns to the aim
    /// (<see cref="PlayerSystem.AimAt"/>) while attacking and for <see cref="StanceTime"/> after the last attack (combat
    /// stance: no turning back and forth between shots); on a computer it always faces the mouse. The weapon in hands
    /// sets the walking speed (<see cref="MobilityModule"/>: in hands, and while firing).</para>
    /// <para>Aim: the mouse on a computer (exactly at the pointer), the right stick of a gamepad, otherwise the movement
    /// stick or the facing — the latter two with aim assist (<see cref="IAimSettings"/>: the best living target within
    /// <see cref="AssistAngle"/>, in the attack's reach, not behind a wall; for the movement stick then the nearest one
    /// all around) or snapped (<see cref="Aim.Snap"/>).</para>
    /// <para>Ammo (<see cref="AmmoModule"/>): an attack takes rounds from the magazine; an empty magazine reloads
    /// itself from the reserve (<see cref="AmmoStock"/>; the base pistol's is infinite). Reload (or the active slot's key
    /// again) reloads earlier. No rounds left anywhere — the attack only clicks (<see cref="DryFired"/>).</para>
    /// Must be registered before <see cref="PlayerSystem"/> (it sets this tick's aim and speed).
    /// </summary>
    public sealed class PlayerCombat : ILevelTickable
    {
        private const float DeadZone = 0.1f;

        /// <summary>Half-angle of the aim assist cone, degrees.</summary>
        public const float AssistAngle = 30f;

        /// <summary>Seconds after the last attack the player still counts as firing (weapon speed).</summary>
        public const float FiringLinger = 0.25f;

        /// <summary>
        /// Seconds after the last attack the body keeps facing the aim (combat stance, decision 2026-10-17): no turning
        /// to the run direction and back between shots while retreating.
        /// </summary>
        public const float StanceTime = 1f;

        public const int MaxChainPoints = 16;

        /// <summary>Most of a frame's remainder the next attack keeps (no double shot after a long frame).</summary>
        private const float MaxCooldownCarry = 0.05f;

        private readonly IPlayerInput _input;
        private readonly PlayerSystem _player;
        private readonly WeaponSystem _weapons;
        private readonly CombatTargets _targets;
        private readonly BulletSystem _bullets;
        private readonly SoundEventBus _sounds;
        private readonly IAimSettings _aim;
        private readonly DoorSystem _doors;
        private readonly AmmoStock _ammo;
        private readonly IAimPointer _pointer;
        private readonly PlayerThrowing _throwing;
        private readonly ZoneSystem _zones;
        private readonly RunStats _stats;
        private readonly List<CombatTarget> _found = new List<CombatTarget>();
        private readonly List<CombatTarget> _meleeTargets = new List<CombatTarget>();
        private readonly List<IDamageable> _meleeHits = new List<IDamageable>();
        private readonly Vector2[] _chainPoints = new Vector2[MaxChainPoints];
        private readonly CombatTarget[] _chainTargets = new CombatTarget[MaxChainPoints];
        private readonly DeterministicRandom _random = new DeterministicRandom(0x5eed);
        private float _firingLeft;
        private float _stanceLeft;

        public PlayerCombat(IPlayerInput input, PlayerSystem player, WeaponSystem weapons, CombatTargets targets,
            BulletSystem bullets, SoundEventBus sounds, IAimSettings aim, DoorSystem doors, AmmoStock ammo,
            IAimPointer pointer = null, PlayerThrowing throwing = null, ZoneSystem zones = null, RunStats stats = null)
        {
            _zones = zones;
            _stats = stats;
            _input = input;
            _player = player;
            _weapons = weapons;
            _targets = targets;
            _bullets = bullets;
            _sounds = sounds;
            _aim = aim;
            _doors = doors;
            _ammo = ammo;
            _pointer = pointer;
            _throwing = throwing;
        }

        /// <summary>(weapon, direction) after every attack — for views, animation and HUD.</summary>
        public event Action<WeaponRuntime, Vector2> Attacked;

        /// <summary>(weapon) when a reload starts or ends.</summary>
        public event Action<WeaponRuntime> ReloadChanged;

        /// <summary>(weapon) an attack was wanted, but there are no rounds left at all (the click of an empty gun).</summary>
        public event Action<WeaponRuntime> DryFired;

        /// <summary>Damageable objects hit by the last melee attack (the horde's monsters are in <see cref="LastMeleeTargets"/>).</summary>
        public IReadOnlyList<IDamageable> LastMeleeHits => _meleeHits;

        /// <summary>Every target hit by the last melee attack.</summary>
        public IReadOnlyList<CombatTarget> LastMeleeTargets => _meleeTargets;

        /// <summary>Frozen door whose ice the last melee attack hit; null if none.</summary>
        public DoorData LastIceHit { get; private set; }

        /// <summary>
        /// Path of the last chain lightning: the player's position, then every target it hit (or where a miss ended).
        /// Valid until the next chain attack.
        /// </summary>
        public ReadOnlySpan<Vector2> ChainPoints => new ReadOnlySpan<Vector2>(_chainPoints, 0, ChainCount);

        public int ChainCount { get; private set; }

        /// <summary>The player attacked within <see cref="FiringLinger"/> (or holds an automatic attack).</summary>
        public bool IsFiring => _firingLeft > 0f;

        /// <summary>The body faces the aim: within <see cref="StanceTime"/> after an attack.</summary>
        public bool InStance => _stanceLeft > 0f;

        /// <summary>Direction of the last attack (or of the aim while the attack is held).</summary>
        public Vector2 AimDirection { get; private set; } = Vector2.up;

        public void Tick(float deltaTime)
        {
            _firingLeft = Mathf.Max(0f, _firingLeft - deltaTime);
            _stanceLeft = Mathf.Max(0f, _stanceLeft - deltaTime);
            // A computer: the body always faces the mouse, the legs walk where the keys say (decision 2026-10-17).
            var pointerAim = TryPointerAim(out var toPointer);
            var weapon = _weapons.Active;
            if (weapon == null || !_player.IsSpawned)
            {
                if (pointerAim) _player.AimAt(toPointer);
                _player.WeaponSpeed = 1f;
                return;
            }

            UpdateTimers(weapon, deltaTime);
            if (_input.WasPressed(PlayerAction.Reload) || _weapons.ReloadRequested)
                TryReload();

            var definition = weapon.Definition;
            var wantsToAttack = WantsToAttack(weapon);
            if (wantsToAttack || _stanceLeft > 0f || pointerAim)
            {
                // In the stance without attacking, the aim stays where the last attack went (unless the mouse or the
                // right stick aims): the body does not follow the run direction.
                var direction = wantsToAttack || pointerAim || RightStick().sqrMagnitude > 0f ? AimFor(definition) : AimDirection;
                if (wantsToAttack && weapon.Cooldown <= 0f && !weapon.IsReloading && !_player.Stunned)
                {
                    if (HasRounds(weapon)) Attack(weapon, direction);
                    else if (!TryReload() && _input.WasPressed(PlayerAction.Attack)) DryFired?.Invoke(weapon);
                }

                AimDirection = direction;
                _player.AimAt(direction);
            }

            var firing = IsFiring || (wantsToAttack && definition.Attack != null && definition.Attack.Automatic);
            _player.WeaponSpeed = definition.SpeedInHands * (firing ? definition.SpeedWhileFiring : 1f);
        }

        /// <summary>Performs an attack with the active weapon now, ignoring input and aim (tests, tools).</summary>
        public bool TryAttack(Vector2 direction)
        {
            var weapon = _weapons.Active;
            if (weapon == null || !_player.IsSpawned || weapon.Cooldown > 0f || weapon.IsReloading || !HasRounds(weapon))
                return false;

            Attack(weapon, direction.normalized);
            return true;
        }

        /// <summary>
        /// Starts reloading the weapon in hands unless it needs no ammo, its magazine is full, it is reloading already or
        /// there is nothing in the reserve.
        /// </summary>
        public bool TryReload()
        {
            var weapon = _weapons.Active;
            if (weapon == null || !_player.IsSpawned || !weapon.Definition.UsesAmmo || weapon.IsReloading ||
                weapon.Ammo >= MagazineOf(weapon.Definition) || !HasReserve(weapon.Definition))
                return false;

            weapon.ReloadDuration = Mathf.Max(weapon.Definition.ReloadTime * (_stats != null ? _stats.ReloadFactor : 1f), 1e-4f);
            weapon.ReloadRemaining = weapon.ReloadDuration;
            ReloadChanged?.Invoke(weapon);
            return true;
        }

        /// <summary>Rounds of the weapon's full magazine, with the run's cards.</summary>
        public int MagazineOf(WeaponDefinition definition) =>
            _stats != null ? _stats.MagazineOf(definition) : definition != null ? definition.MagazineSize : 0;

        /// <summary>Rounds in the reserve for this weapon (int.MaxValue — infinite).</summary>
        public int ReserveOf(WeaponDefinition definition)
        {
            var ammo = definition.Ammo;
            if (ammo == null) return 0;
            return ammo.InfiniteReserve ? int.MaxValue : _ammo.Get(ammo.Ammo);
        }

        /// <summary>A gun with no rounds in the magazine and none in the reserve (ТЗ: красная обводка).</summary>
        public bool IsOutOfAmmo(WeaponRuntime weapon) =>
            weapon != null && weapon.Definition.UsesAmmo && !weapon.IsReloading && !HasRounds(weapon) &&
            !HasReserve(weapon.Definition);

        /// <summary>Direction of an attack the player wants to make towards <paramref name="wanted"/> (assist or snap).</summary>
        public Vector2 AssistedDirection(WeaponDefinition definition, Vector2 wanted)
        {
            if (_aim.AimAssist && TryFindAssistTarget(definition, wanted, AssistAngle, out var toTarget))
                return toTarget;
            return Aim.Snap(wanted, _aim.AimMode);
        }

        private Vector2 AimFor(WeaponDefinition definition)
        {
            if (TryPointerAim(out var toPointer)) return toPointer;

            var look = RightStick();
            if (look.sqrMagnitude > 0f) return AssistedDirection(definition, look.normalized);

            // The movement stick (a phone): the cone ahead first, then the nearest target all around — a player running
            // from the crowd shoots at it, not ahead.
            var move = _input.Move;
            var wanted = move.sqrMagnitude >= DeadZone * DeadZone ? move.normalized : _player.Facing;
            if (_aim.AimAssist && (TryFindAssistTarget(definition, wanted, AssistAngle, out var toTarget) ||
                                   TryFindAssistTarget(definition, wanted, 180f, out toTarget)))
                return toTarget;
            return Aim.Snap(wanted, _aim.AimMode);
        }

        /// <summary>Direction to the mouse on the floor; false without a mouse (touch, gamepad) or right at the player.</summary>
        private bool TryPointerAim(out Vector2 direction)
        {
            direction = default;
            if (_pointer == null || !_input.LookIsPointer || !_player.IsSpawned ||
                !_pointer.TryGetGroundPoint(_input.Look, out var point))
                return false;
            var offset = point - _player.Position;
            if (offset.sqrMagnitude <= 0.01f) return false;
            direction = offset.normalized;
            return true;
        }

        /// <summary>The gamepad's right stick past the dead zone; zero otherwise (and for a mouse).</summary>
        private Vector2 RightStick() =>
            !_input.LookIsPointer && _input.Look.sqrMagnitude >= DeadZone * DeadZone ? _input.Look : Vector2.zero;

        /// <summary>
        /// Best target within <paramref name="halfAngle"/> of <paramref name="wanted"/>: nearest, with off-axis targets
        /// counted farther (up to twice at the cone edge).
        /// </summary>
        private bool TryFindAssistTarget(WeaponDefinition definition, Vector2 wanted, float halfAngle, out Vector2 direction)
        {
            direction = default;
            if (wanted.sqrMagnitude <= 0f || definition.Attack == null) return false;
            wanted.Normalize();

            var origin = _player.Position;
            var floor = _player.Floor;
            var minCos = halfAngle >= 180f ? -1f : Mathf.Cos(halfAngle * Mathf.Deg2Rad);
            var bestScore = float.MaxValue;
            _targets.Collect(origin, definition.Attack.AimRange, floor, _found, wanted, minCos);
            foreach (var target in _found)
            {
                var offset = _targets.PositionOf(target) - origin;
                var distance = offset.magnitude;
                if (distance <= 1e-4f) continue;
                var toTarget = offset / distance;
                var cos = Vector2.Dot(toTarget, wanted);
                var offAxis = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg / halfAngle;
                var score = distance * (1f + offAxis);
                if (score >= bestScore || !_targets.IsClear(origin, origin + offset, floor)) continue;
                bestScore = score;
                direction = toTarget;
            }

            _found.Clear();
            return bestScore < float.MaxValue;
        }

        private bool WantsToAttack(WeaponRuntime weapon)
        {
            if (_throwing != null && _throwing.ConsumedAttack) return false; // that click threw
            if (_input.WasPressed(PlayerAction.Attack)) return true;
            return _input.AttackHeld && weapon.Definition.Attack != null && weapon.Definition.Attack.Automatic;
        }

        private static bool HasRounds(WeaponRuntime weapon) =>
            !weapon.Definition.UsesAmmo || weapon.Ammo >= weapon.Definition.Ammo.PerAttack;

        private bool HasReserve(WeaponDefinition definition) => ReserveOf(definition) > 0;

        /// <summary>Seconds between attacks, with the run's cards.</summary>
        public float CooldownOf(WeaponDefinition definition) =>
            definition.Cooldown * (_stats != null ? _stats.CooldownFactor : 1f);

        private void UpdateTimers(WeaponRuntime weapon, float deltaTime)
        {
            // May go a frame below zero: the next attack keeps the remainder, so the fire rate does not depend on the
            // frame rate (0.1 s at 60 FPS would otherwise be 7 frames).
            var carry = Mathf.Min(deltaTime, MaxCooldownCarry);
            if (weapon.Cooldown > -carry)
                weapon.Cooldown = Mathf.Max(-carry, weapon.Cooldown - deltaTime);

            if (!weapon.IsReloading)
                return;

            weapon.ReloadRemaining = Mathf.Max(0f, weapon.ReloadRemaining - deltaTime);
            if (weapon.IsReloading) return;

            var definition = weapon.Definition;
            var need = MagazineOf(definition) - weapon.Ammo;
            weapon.Ammo += definition.Ammo.InfiniteReserve ? need : _ammo.Take(definition.Ammo.Ammo, need);
            ReloadChanged?.Invoke(weapon);
        }

        private void Attack(WeaponRuntime weapon, Vector2 direction)
        {
            var definition = weapon.Definition;
            weapon.Cooldown = Mathf.Min(weapon.Cooldown, 0f) + CooldownOf(definition);
            _firingLeft = FiringLinger;
            _stanceLeft = StanceTime;
            AimDirection = direction;
            _player.AimAt(direction);

            var origin = _player.Position;
            var floor = _player.Floor;
            switch (definition.Attack)
            {
                case BulletAttack bullets:
                    FireBullets(definition, bullets, origin, direction, floor);
                    break;
                case ExplosiveAttack explosive:
                    _bullets.Spawn(origin, direction, explosive.Speed, explosive.DirectDamage, floor, explosive.Range,
                        explosive: explosive, weapon: definition);
                    break;
                case StreamAttack stream:
                    Stream(definition, stream, origin, direction, floor);
                    break;
                case ChainAttack chain:
                    Chain(definition, chain, origin, direction, floor);
                    break;
                case MeleeAttack melee:
                    Melee(definition, melee, origin, direction, floor);
                    break;
            }

            _sounds.Emit(definition.IsMelee ? SoundType.Melee : SoundType.Ranged, origin, definition.SoundRadius, floor);
            if (definition.UsesAmmo)
            {
                weapon.Ammo = Mathf.Max(0, weapon.Ammo - definition.Ammo.PerAttack);
                if (!HasRounds(weapon)) TryReload();
            }

            Attacked?.Invoke(weapon, direction);
        }

        private void FireBullets(WeaponDefinition definition, BulletAttack attack, Vector2 origin, Vector2 direction, int floor)
        {
            var pellets = Mathf.Max(1, attack.Pellets);
            var spread = attack.Spread;
            for (var i = 0; i < pellets; i++)
            {
                float degrees;
                if (pellets == 1)
                {
                    degrees = ((float)_random.NextDouble() - 0.5f) * spread;
                }
                else
                {
                    // An even fan, each pellet shaken within its share.
                    var share = spread / pellets;
                    degrees = -spread * 0.5f + share * (i + 0.5f) + ((float)_random.NextDouble() - 0.5f) * share * 0.6f;
                }

                _bullets.Spawn(origin, Rotate(direction, degrees * Mathf.Deg2Rad), attack.Speed, attack.Damage, floor,
                    attack.Range, attack.Pierce, weapon: definition);
            }
        }

        private void Stream(WeaponDefinition weapon, StreamAttack stream, Vector2 origin, Vector2 direction, int floor)
        {
            _targets.Collect(origin, stream.Range, floor, _found, direction, Mathf.Cos(stream.Angle * 0.5f * Mathf.Deg2Rad));
            foreach (var target in _found)
                if (_targets.IsClear(origin, _targets.PositionOf(target), floor))
                    _targets.Damage(target, stream.DamagePerTick, direction, weapon);
            _found.Clear();

            var ignite = weapon.Ignite;
            if (ignite != null && ignite.Zone != null && _zones != null)
                Ignite(ignite, stream, origin, direction, floor);
        }

        /// <summary>Flammable cells in the stream's cone and reach, not behind what stops bullets, catch fire.</summary>
        private void Ignite(IgniteCellsModule ignite, StreamAttack stream, Vector2 origin, Vector2 direction, int floor)
        {
            var grid = _zones.Grid;
            var cos = Mathf.Cos(stream.Angle * 0.5f * Mathf.Deg2Rad);
            var range = stream.Range;
            var reach = Mathf.CeilToInt(range);
            var cx = Mathf.FloorToInt(origin.x + 0.5f);
            var cy = Mathf.FloorToInt(origin.y + 0.5f);
            for (var y = cy - reach; y <= cy + reach; y++)
            for (var x = cx - reach; x <= cx + reach; x++)
            {
                var cell = new GridPosition(x, y, floor);
                if (!grid.Has(cell, CellFlags.Flammable)) continue;
                var offset = new Vector2(x, y) - origin;
                var distance = offset.magnitude;
                // The player's own cell burns when they stand in a bush; the rest must be in the cone.
                if (distance > range || distance > 0.5f && Vector2.Dot(offset / distance, direction) < cos) continue;
                if (distance > 0.5f && !_targets.IsClear(origin, new Vector2(x, y), floor)) continue;
                _zones.Place(cell, ignite.Zone, ignite.Duration, ignite.Strength);
            }
        }

        private void Chain(WeaponDefinition weapon, ChainAttack chain, Vector2 origin, Vector2 direction, int floor)
        {
            ChainCount = 0;
            _chainPoints[ChainCount++] = origin;
            var hits = 0;
            var from = origin;
            var damage = chain.Damage;
            var target = Nearest(from, chain.Range, floor, direction, Mathf.Cos(chain.AimAngle * Mathf.Deg2Rad), hits);
            if (!target.IsValid)
            {
                // A miss: the bolt goes straight until a wall or its reach.
                var end = origin + direction * chain.Range;
                var hit = _targets.Segment(origin, end, floor);
                _chainPoints[ChainCount++] = hit.Kind == CombatHitKind.Blocker ? hit.Point : end;
                return;
            }

            while (target.IsValid)
            {
                var at = _targets.PositionOf(target);
                _chainTargets[hits++] = target;
                _chainPoints[ChainCount++] = at;
                _targets.Damage(target, damage, (at - from).normalized, weapon);
                if (hits > chain.Jumps || ChainCount >= MaxChainPoints) break;
                damage *= chain.JumpFalloff;
                from = at;
                target = Nearest(from, chain.JumpRange, floor, Vector2.zero, -1f, hits);
            }
        }

        /// <summary>The nearest living target in the cone, not behind a wall and not among the first <paramref name="skip"/> chain targets.</summary>
        private CombatTarget Nearest(Vector2 from, float range, int floor, Vector2 direction, float cosHalfAngle, int skip)
        {
            _targets.Collect(from, range, floor, _found, direction, cosHalfAngle);
            var best = CombatTarget.None;
            var bestDistance = float.MaxValue;
            foreach (var candidate in _found)
            {
                var already = false;
                for (var i = 0; i < skip; i++)
                    if (_chainTargets[i].Equals(candidate)) already = true;
                if (already || !_targets.IsAlive(candidate)) continue;
                var distance = (_targets.PositionOf(candidate) - from).sqrMagnitude;
                if (distance >= bestDistance || !_targets.IsClear(from, _targets.PositionOf(candidate), floor)) continue;
                bestDistance = distance;
                best = candidate;
            }

            _found.Clear();
            return best;
        }

        private void Melee(WeaponDefinition weapon, MeleeAttack melee, Vector2 origin, Vector2 direction, int floor)
        {
            _meleeTargets.Clear();
            _meleeHits.Clear();
            _targets.Collect(origin, melee.Range, floor, _found, direction, Mathf.Cos(melee.Arc * 0.5f * Mathf.Deg2Rad));
            foreach (var target in _found)
                if (_targets.IsClear(origin, _targets.PositionOf(target), floor))
                    _meleeTargets.Add(target);
            _found.Clear();

            foreach (var target in _meleeTargets)
            {
                if (target.Object != null) _meleeHits.Add(target.Object);
                _targets.Damage(target, melee.Damage, direction, weapon, PlayerDamage.Precise);
            }

            LastIceHit = null;
            var door = FrozenDoorInFront(melee, direction);
            if (door != null)
            {
                LastIceHit = door;
                _doors.HitIce(door.Id);
                _sounds.Emit(SoundType.Door, new Vector2(door.Position.X, door.Position.Y), melee.SoundRadius, door.Position.Floor);
            }
        }

        /// <summary>
        /// The frozen door in a neighbouring cell the attack points at (within the melee arc), whose near face is in
        /// melee range.
        /// </summary>
        private DoorData FrozenDoorInFront(MeleeAttack melee, Vector2 direction)
        {
            DoorData best = null;
            var bestDot = Mathf.Max(Mathf.Cos(melee.Arc * 0.5f * Mathf.Deg2Rad), 0.1f);
            var origin = _player.Position;
            foreach (var side in DirectionExtensions.All)
            {
                var cell = _player.Cell.Neighbour(side);
                if (!_doors.TryGetDoor(cell, out var door) || !_doors.IsFrozen(door.Id))
                    continue;

                var offset = new Vector2(cell.X, cell.Y) - origin;
                var face = Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) - 0.5f;
                var dot = Vector2.Dot(offset.normalized, direction);
                if (face > melee.Range || dot < bestDot)
                    continue;

                best = door;
                bestDot = dot;
            }

            return best;
        }

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }
}
