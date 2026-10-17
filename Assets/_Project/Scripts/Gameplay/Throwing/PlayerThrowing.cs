using System;
using System.Collections.Generic;
using Purge.Core.Definitions;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using UnityEngine;

namespace Purge.Gameplay.Throwing
{
    /// <summary>
    /// Throwing from the throwable slot (ТЗ «Бросок»: режим прицела, дуга до клетки, тап по месту — бросок).
    /// <list type="bullet">
    /// <item>Throw (G, the left trigger, the HUD's throw button) starts aiming: the arc goes to <see cref="Target"/>.</item>
    /// <item>Held for <see cref="HoldTime"/> and released — the throw goes there (a computer: at the mouse; a gamepad:
    /// where the right stick points, the stick's tilt is the distance).</item>
    /// <item>A quick press keeps aiming: a touch on the game (a phone) or a mouse click throws there; the throw key again
    /// cancels.</item>
    /// </list>
    /// Without a pointer or a stick the target is the nearest monster ahead in reach, otherwise a cell ahead. The target
    /// is a cell centre within <see cref="ThrowableDefinition.Range"/>; whatever stops bullets on the straight line cuts it
    /// short (the item lands in front of a wall, it does not fly over it). In flight it is a <see cref="BulletSystem"/>
    /// bullet that flies over monsters; where it ends its effects happen (<see cref="EffectSystem"/>; the player's own —
    /// they never hurt the player). Registered before <see cref="PlayerCombat"/>: a click that throws is not a shot.
    /// </summary>
    public sealed class PlayerThrowing : ILevelTickable, IDisposable
    {
        /// <summary>A press held at least this long is a hold-and-release throw.</summary>
        public const float HoldTime = 0.25f;

        private const float DeadZone = 0.15f;
        private const float AutoAngle = 45f;
        private const float DefaultShare = 0.6f;
        private const float LineStep = 0.25f;

        private readonly IPlayerInput _input;
        private readonly PlayerSystem _player;
        private readonly ThrowableSlot _slot;
        private readonly BulletSystem _bullets;
        private readonly CombatTargets _targets;
        private readonly EffectSystem _effects;
        private readonly SoundEventBus _sounds;
        private readonly IAimPointer _pointer;
        private readonly List<CombatTarget> _found = new List<CombatTarget>();
        private float _held;
        private float _cooldown;
        private bool _subscribed;

        public PlayerThrowing(IPlayerInput input, PlayerSystem player, ThrowableSlot slot, BulletSystem bullets,
            CombatTargets targets, EffectSystem effects, SoundEventBus sounds, IAimPointer pointer = null)
        {
            _input = input;
            _player = player;
            _slot = slot;
            _bullets = bullets;
            _targets = targets;
            _effects = effects;
            _sounds = sounds;
            _pointer = pointer;
            _bullets.Ended += OnBulletEnded;
            _subscribed = true;
        }

        public ThrowableSlot Slot => _slot;

        /// <summary>The arc is shown: the player is choosing where to throw.</summary>
        public bool IsAiming { get; private set; }

        /// <summary>Aiming after a quick press: waiting for a touch, a click or the key again.</summary>
        public bool WaitsForTap { get; private set; }

        /// <summary>Where the throwable would land now (grid units, a cell centre); valid while <see cref="IsAiming"/>.</summary>
        public Vector2 Target { get; private set; }

        /// <summary>The attack press of this tick threw instead (PlayerCombat skips it).</summary>
        public bool ConsumedAttack { get; private set; }

        /// <summary>Seconds until the next throw is possible.</summary>
        public float Cooldown => _cooldown;

        /// <summary>(kind, direction) right after a throw.</summary>
        public event Action<ThrowableDefinition, Vector2> Thrown;

        /// <summary>(kind, where) a thrown item landed, after its effects.</summary>
        public event Action<ThrowableDefinition, Vector2> Landed;

        public void Tick(float deltaTime)
        {
            ConsumedAttack = false;
            _cooldown = Mathf.Max(0f, _cooldown - deltaTime);
            if (!_player.IsSpawned || _slot.IsEmpty)
            {
                StopAiming();
                return;
            }

            if (_input.WasPressed(PlayerAction.Throw))
            {
                if (IsAiming && WaitsForTap)
                {
                    StopAiming(); // the key again: changed one's mind
                    return;
                }

                IsAiming = true;
                WaitsForTap = false;
                _held = 0f;
            }

            if (!IsAiming) return;

            var held = _input.IsHeld(PlayerAction.Throw);
            if (held) _held += deltaTime;
            Target = FindTarget();

            if (!WaitsForTap && !held)
            {
                if (_held >= HoldTime) TryThrow(Target);
                else WaitsForTap = true; // a quick press: aim until a touch or a click
                return;
            }

            if (!WaitsForTap) return;
            if (_input.TryGetGroundTap(out var screen) && _pointer != null && _pointer.TryGetGroundPoint(screen, out var point))
            {
                Target = Clamp(point);
                TryThrow(Target);
            }
            else if (_input.LookIsPointer && _input.WasPressed(PlayerAction.Attack))
            {
                ConsumedAttack = true;
                TryThrow(Target);
            }
        }

        /// <summary>Throws one now at <paramref name="target"/> (clamped to reach and walls), ignoring input (tests, tools).</summary>
        public bool TryThrow(Vector2 target)
        {
            if (!_player.IsSpawned || _slot.IsEmpty || _cooldown > 0f || _player.Stunned) return false;
            var definition = _slot.Definition;
            var origin = _player.Position;
            target = Clamp(target);
            var offset = target - origin;
            var distance = offset.magnitude;
            var direction = distance > 1e-3f ? offset / distance : _player.Facing;

            _slot.TakeOne();
            _cooldown = definition.Cooldown;
            _bullets.Spawn(origin, direction, definition.Speed, 0f, _player.Floor, Mathf.Max(distance, 0.05f),
                throwable: definition);
            _sounds.Emit(SoundType.Throw, origin, definition.SoundRadius, _player.Floor);
            _player.AimAt(direction);
            StopAiming();
            Thrown?.Invoke(definition, direction);
            return true;
        }

        /// <summary>
        /// The landing point for a wanted one: within reach, a cell centre, and short of whatever stops bullets on the line.
        /// </summary>
        public Vector2 Clamp(Vector2 wanted)
        {
            var origin = _player.Position;
            var range = _slot.Definition != null ? _slot.Definition.Range : 1f;
            var offset = wanted - origin;
            if (offset.sqrMagnitude > range * range) offset = offset.normalized * range;
            var floor = _player.Floor;
            var cell = CellCentre(origin + offset);
            if (_targets.IsClear(origin, cell, floor)) return cell;

            // Walk the line and keep the last cell centre still in the open.
            var distance = offset.magnitude;
            var direction = distance > 1e-4f ? offset / distance : Vector2.zero;
            var best = CellCentre(origin);
            for (var d = LineStep; d <= distance; d += LineStep)
            {
                var point = origin + direction * d;
                if (!_targets.IsClear(origin, point, floor)) break;
                var centre = CellCentre(point);
                if (_targets.IsClear(origin, centre, floor)) best = centre;
            }

            return best;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _bullets.Ended -= OnBulletEnded;
            _subscribed = false;
        }

        private Vector2 FindTarget()
        {
            var origin = _player.Position;
            var range = _slot.Definition.Range;
            if (_input.LookIsPointer && _pointer != null && _pointer.TryGetGroundPoint(_input.Look, out var point))
                return Clamp(point);

            var look = _input.LookIsPointer ? Vector2.zero : _input.Look;
            if (look.sqrMagnitude >= DeadZone * DeadZone)
                return Clamp(origin + Vector2.ClampMagnitude(look, 1f) * range);

            var move = _input.Move;
            var ahead = move.sqrMagnitude >= DeadZone * DeadZone ? move.normalized : _player.Facing;
            return Clamp(NearestAhead(origin, ahead, range, out var monster) ? monster : origin + ahead * (range * DefaultShare));
        }

        private bool NearestAhead(Vector2 origin, Vector2 ahead, float range, out Vector2 position)
        {
            position = default;
            var floor = _player.Floor;
            var best = float.MaxValue;
            _targets.Collect(origin, range, floor, _found, ahead, Mathf.Cos(AutoAngle * Mathf.Deg2Rad));
            foreach (var target in _found)
            {
                var at = _targets.PositionOf(target);
                var distance = (at - origin).sqrMagnitude;
                if (distance >= best || distance < 1f || !_targets.IsClear(origin, at, floor)) continue;
                best = distance;
                position = at;
            }

            _found.Clear();
            return best < float.MaxValue;
        }

        private void StopAiming()
        {
            IsAiming = false;
            WaitsForTap = false;
            _held = 0f;
        }

        private void OnBulletEnded(Bullet bullet, Vector2 point, BulletEnd end)
        {
            var definition = bullet.Throwable;
            if (definition == null || end == BulletEnd.Cleared) return;
            _effects.Apply(definition.Effects, point, bullet.Floor);
            Landed?.Invoke(definition, point);
        }

        private static Vector2 CellCentre(Vector2 point) =>
            new Vector2(Mathf.Floor(point.x + 0.5f), Mathf.Floor(point.y + 0.5f));
    }
}
