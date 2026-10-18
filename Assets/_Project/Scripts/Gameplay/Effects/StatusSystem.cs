using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using UnityEngine;

namespace Purge.Gameplay.Effects
{
    /// <summary>
    /// States on the horde's monsters (ТЗ «Состояния»: горение, оглушение, замедление…) in flat arrays: seconds left per
    /// (kind, monster). Applying a state again keeps the longer duration (no stacking). Every tick the monster's speed
    /// factor and stun go to <see cref="HordeSystem.SetModifiers"/>; damage over time is dealt every
    /// <see cref="DamageInterval"/> through <see cref="CombatTargets"/> (numbers, kills and drops as from any damage).
    /// A new monster in a slot starts clean (<see cref="HordeSystem.Generations"/>). Sources: zones, effects and the
    /// weapons' <see cref="StatusOnHitModule"/>. Kinds are registered on first use (at most <see cref="MaxKinds"/>).
    /// Registered after <see cref="HordeSystem"/>: its arrays are sized by the horde, its modifiers act from the next
    /// tick. Allocation-free per tick.
    /// </summary>
    public sealed class StatusSystem : ILevelLoadStep, ILevelTickable, IDisposable
    {
        public const int MaxKinds = 16;
        public const float DamageInterval = 0.25f;

        private readonly HordeSystem _horde;
        private readonly CombatTargets _targets;
        private readonly List<StatusDefinition> _kinds = new List<StatusDefinition>();
        private readonly List<CombatTarget> _found = new List<CombatTarget>();
        private readonly DeterministicRandom _random = new DeterministicRandom(0x57a7);
        private float[] _left; // kind · capacity + agent
        private int[] _generation;
        private bool[] _has;
        private int _capacity;
        private float _damageTimer;
        private bool _subscribed;

        public StatusSystem(HordeSystem horde, CombatTargets targets)
        {
            _horde = horde;
            _targets = targets;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool IsActive => _left != null;

        /// <summary>Kinds seen so far; a kind's index is its place here.</summary>
        public IReadOnlyList<StatusDefinition> Kinds => _kinds;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_horde.IsActive)
                return UniTask.CompletedTask;

            _capacity = _horde.Capacity;
            _left = new float[_capacity * MaxKinds];
            _generation = new int[_capacity];
            _has = new bool[_capacity];
            if (!_subscribed)
            {
                _targets.WeaponHit += OnWeaponHit;
                _subscribed = true;
            }

            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _targets.WeaponHit -= OnWeaponHit;
            _subscribed = false;
        }

        /// <summary>Seconds of the state left on the monster (0 = none).</summary>
        public float Left(int agent, int kind)
        {
            if (!IsActive || (uint)agent >= (uint)_capacity || (uint)kind >= (uint)_kinds.Count) return 0f;
            return _has[agent] && _generation[agent] == _horde.Generations[agent] ? _left[kind * _capacity + agent] : 0f;
        }

        public float Left(int agent, StatusDefinition status) => Left(agent, IndexOf(status));

        /// <summary>
        /// Puts the state on a target for <paramref name="duration"/> × the monster's resistance (or keeps its longer
        /// one). Horde monsters only.
        /// </summary>
        public void Apply(in CombatTarget target, StatusDefinition status, float duration)
        {
            if (!IsActive || status == null || duration <= 0f || target.Agent < 0 || !_targets.IsAlive(target)) return;
            // The monster's resistance (0 = immune).
            var definition = _horde.DefinitionOf(target.Agent);
            if (definition != null) duration *= definition.DurationFactor(status);
            // A champion's affix (the fiery does not burn).
            var affix = _horde.AffixOf(target.Agent);
            if (affix != null) duration *= affix.DurationFactor(status);
            if (duration <= 0f) return;
            var kind = Register(status);
            if (kind < 0) return;
            var agent = target.Agent;
            Own(agent);
            ref var left = ref _left[kind * _capacity + agent];
            if (left < duration) left = duration;
            if (status.Stuns || status.SpeedFactor < 1f) Refresh(agent);
        }

        /// <summary>The state on every monster in the radius not behind something that stops bullets.</summary>
        public void ApplyInRadius(Vector2 at, float radius, int floor, StatusDefinition status, float duration)
        {
            if (!IsActive || status == null) return;
            _targets.Collect(at, radius, floor, _found);
            foreach (var target in _found)
                if (_targets.IsClear(at, _targets.PositionOf(target), floor))
                    Apply(target, status, duration);
            _found.Clear();
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive || deltaTime <= 0f)
                return;

            _damageTimer -= deltaTime;
            var damage = _damageTimer <= 0f;
            if (damage) _damageTimer += DamageInterval;

            var states = _horde.States;
            var generations = _horde.Generations;
            var high = _horde.HighWater;
            for (var agent = 0; agent < high; agent++)
            {
                if (!_has[agent]) continue;
                if (!HordeSystem.IsAlive(states[agent]) || _generation[agent] != generations[agent])
                {
                    Clear(agent);
                    continue;
                }

                var any = false;
                var dps = 0f;
                for (var kind = 0; kind < _kinds.Count; kind++)
                {
                    ref var left = ref _left[kind * _capacity + agent];
                    if (left <= 0f) continue;
                    dps += _kinds[kind].DamagePerSecond;
                    left -= deltaTime;
                    if (left > 0f) any = true;
                    else left = 0f;
                }

                Refresh(agent);
                if (damage && dps > 0f)
                    _targets.Damage(CombatTarget.OfAgent(agent), dps * DamageInterval, Vector2.zero); // no direction: armor does not cover
                if (!any) _has[agent] = false;
            }
        }

        private void OnWeaponHit(CombatTarget target, WeaponDefinition weapon)
        {
            var modules = weapon.Modules;
            for (var i = 0; i < modules.Count; i++)
            {
                if (!(modules[i] is StatusOnHitModule onHit) || onHit.Status == null) continue;
                if (onHit.Chance < 1f && _random.NextDouble() >= onHit.Chance) continue;
                Apply(target, onHit.Status, onHit.Duration);
            }
        }

        /// <summary>Speed and stun of the monster from its current states.</summary>
        private void Refresh(int agent)
        {
            var factor = 1f;
            var stunned = false;
            for (var kind = 0; kind < _kinds.Count; kind++)
            {
                if (_left[kind * _capacity + agent] <= 0f) continue;
                var status = _kinds[kind];
                factor *= status.SpeedFactor;
                stunned |= status.Stuns;
            }

            _horde.SetModifiers(agent, factor, stunned);
        }

        /// <summary>The slot's states belong to the monster in it now.</summary>
        private void Own(int agent)
        {
            var generation = _horde.Generations[agent];
            if (_has[agent] && _generation[agent] == generation) return;
            Clear(agent);
            _generation[agent] = generation;
            _has[agent] = true;
        }

        private void Clear(int agent)
        {
            for (var kind = 0; kind < _kinds.Count; kind++)
                _left[kind * _capacity + agent] = 0f;
            _has[agent] = false;
            if (HordeSystem.IsAlive(_horde.States[agent]) && _generation[agent] == _horde.Generations[agent])
                _horde.SetModifiers(agent, 1f, false);
        }

        private int IndexOf(StatusDefinition status)
        {
            if (status == null) return -1;
            for (var i = 0; i < _kinds.Count; i++)
                if (_kinds[i] == status || _kinds[i].Id == status.Id && !string.IsNullOrEmpty(status.Id))
                    return i;
            return -1;
        }

        private int Register(StatusDefinition status)
        {
            var index = IndexOf(status);
            if (index >= 0) return index;
            if (_kinds.Count >= MaxKinds)
            {
                GameLog.Warning(LogChannel.Gameplay, $"More than {MaxKinds} kinds of states: '{status.name}' is ignored.");
                return -1;
            }

            _kinds.Add(status);
            return _kinds.Count - 1;
        }
    }
}
