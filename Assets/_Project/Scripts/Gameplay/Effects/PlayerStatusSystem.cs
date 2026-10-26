using System.Collections.Generic;
using Purge.Core.Effects;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Effects
{
    /// <summary>
    /// States on the player (ТЗ «у игрока — состояния от монстров»): acid, slow, stun, burning… — the same
    /// <see cref="StatusDefinition"/>s the monsters get. Applying a state again keeps the longer duration (no stacking).
    /// Every tick the speed factor and the stun go to <see cref="PlayerSystem.StatusSpeed"/> / <see cref="PlayerSystem.Stunned"/>
    /// (registered before the player's systems, so they act this tick); damage over time is dealt every
    /// <see cref="StatusSystem.DamageInterval"/>. Monsters' zones (<see cref="ZoneSystem.IsHostileAt"/>) put their state
    /// on the player standing in them.
    /// <para><b>Immunity to control</b> (ТЗ: иначе комбинации монстров запирают игрока до смерти): a control state (stun,
    /// freezing, stone — states with a stun; a hook's pull, <see cref="Pull"/>) cannot be renewed while it holds, and for <see cref="ControlImmunity"/> seconds after it
    /// ends no control state takes.</para>
    /// Allocation-free per tick (kinds are registered on first use).
    /// </summary>
    public sealed class PlayerStatusSystem : ILevelTickable
    {
        public const float ControlImmunity = 2.5f;

        private readonly PlayerSystem _player;
        private readonly PlayerHealth _health;
        private readonly ZoneSystem _zones;
        private readonly HordeSystem _horde;
        private readonly List<StatusDefinition> _kinds = new List<StatusDefinition>();
        private readonly List<float> _left = new List<float>();
        private float _damageTimer;
        private float _damage;
        private float _zoneTimer;
        private bool _controlled;
        private float _pullLeft;
        private Vector2 _pullVelocity;
        private bool _pullTick;

        public PlayerStatusSystem(PlayerSystem player, PlayerHealth health, ZoneSystem zones = null, HordeSystem horde = null)
        {
            _horde = horde;
            _player = player;
            _health = health;
            _zones = zones;
        }

        /// <summary>States seen so far (an index for <see cref="Left(int)"/>).</summary>
        public IReadOnlyList<StatusDefinition> Kinds => _kinds;

        /// <summary>Seconds of immunity to control left (0 = none).</summary>
        public float ImmunityLeft { get; private set; }

        public bool IsImmune => ImmunityLeft > 0f;

        /// <summary>A control state holds the player now.</summary>
        public bool IsControlled => _controlled;

        /// <summary>× speed of the states now (1 = none).</summary>
        public float SpeedFactor { get; private set; } = 1f;

        public float Left(int kind) => (uint)kind < (uint)_left.Count ? _left[kind] : 0f;

        public float Left(StatusDefinition status) => Left(IndexOf(status));

        /// <summary>
        /// Puts the state on the player for <paramref name="duration"/> (or keeps the longer one). A control state is
        /// refused while one holds or the immunity lasts. True when it took.
        /// </summary>
        public bool Apply(StatusDefinition status, float duration)
        {
            if (status == null || duration <= 0f || _health.IsDead) return false;
            // The stress test's and the sandbox's god mode spares the player from everything the monsters do.
            if (_horde != null && _horde.IsActive && _horde.GodMode) return false;
            if (status.Stuns && (_controlled || IsImmune)) return false;
            var kind = Register(status);
            if (_left[kind] < duration) _left[kind] = duration;
            Refresh();
            return true;
        }

        /// <summary>
        /// A monster's hook drags the player (stage 8b): at <paramref name="velocity"/> cells per second for
        /// <paramref name="duration"/> s, held meanwhile — a control, refused while one holds or the immunity lasts. Unlike
        /// the states it works in god mode too (as a knock: the stand must feel it). True when it took.
        /// </summary>
        public bool Pull(Vector2 velocity, float duration)
        {
            if (duration <= 0f || _health.IsDead || _controlled || IsImmune) return false;
            _pullLeft = duration;
            _pullVelocity = velocity;
            Refresh();
            return true;
        }

        /// <summary>Seconds the hook still drags the player (0 = none).</summary>
        public float PullLeft => _pullLeft;

        /// <summary>Removes every state and the immunity (level restart, tests).</summary>
        public void Clear()
        {
            for (var i = 0; i < _left.Count; i++) _left[i] = 0f;
            _pullLeft = 0f;
            ImmunityLeft = 0f;
            _controlled = false;
            _damage = 0f;
            Refresh();
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            ImmunityLeft = Mathf.Max(0f, ImmunityLeft - deltaTime);
            TouchZones(deltaTime);

            var dps = 0f;
            for (var i = 0; i < _left.Count; i++)
            {
                if (_left[i] <= 0f) continue;
                dps += _kinds[i].DamagePerSecond;
                _left[i] = Mathf.Max(0f, _left[i] - deltaTime);
            }

            // The drag acts on every tick its time covers, the last one too.
            _pullTick = _pullLeft > 0f;
            if (_pullTick) _pullLeft = Mathf.Max(0f, _pullLeft - deltaTime);
            var wasControlled = _controlled;
            Refresh();
            _pullTick = false;
            // A control state just ended: a moment of freedom before the next one.
            if (wasControlled && !_controlled) ImmunityLeft = ControlImmunity;

            _damageTimer -= deltaTime;
            if (_damageTimer > 0f) return;
            _damageTimer += StatusSystem.DamageInterval;
            if (dps <= 0f || _health.IsDead) return;
            _damage += dps * StatusSystem.DamageInterval;
            var whole = Mathf.FloorToInt(_damage);
            if (whole <= 0) return;
            _damage -= whole;
            _health.Damage(whole);
        }

        /// <summary>A monsters' zone under the player puts its state on them (as zones do on monsters, 8 times a second).</summary>
        private void TouchZones(float deltaTime)
        {
            if (_zones == null || _zones.Count == 0 || !_player.IsSpawned) return;
            _zoneTimer -= deltaTime;
            if (_zoneTimer > 0f) return;
            _zoneTimer += ZoneSystem.TickInterval;
            var grid = _zones.Grid;
            if (!grid.IsInside(_player.Cell)) return;
            var index = grid.ToIndex(_player.Cell);
            if (!_zones.IsHostileAt(index)) return;
            var zone = _zones.ZoneAt(_player.Cell);
            if (zone?.Status != null && zone.Status.Status != null) Apply(zone.Status.Status, zone.Status.Duration);
        }

        private void Refresh()
        {
            var factor = 1f;
            var stunned = false;
            for (var i = 0; i < _left.Count; i++)
            {
                if (_left[i] <= 0f) continue;
                factor *= _kinds[i].SpeedFactor;
                stunned |= _kinds[i].Stuns;
            }

            var pulled = _pullLeft > 0f || _pullTick;
            _controlled = stunned || pulled;
            SpeedFactor = factor;
            _player.StatusSpeed = factor;
            _player.Stunned = _controlled;
            _player.Drag = pulled ? _pullVelocity : Vector2.zero;
        }

        private int IndexOf(StatusDefinition status)
        {
            if (status == null) return -1;
            for (var i = 0; i < _kinds.Count; i++)
                if (_kinds[i] == status || !string.IsNullOrEmpty(status.Id) && _kinds[i].Id == status.Id)
                    return i;
            return -1;
        }

        private int Register(StatusDefinition status)
        {
            var index = IndexOf(status);
            if (index >= 0) return index;
            _kinds.Add(status);
            _left.Add(0f);
            return _kinds.Count - 1;
        }
    }
}
