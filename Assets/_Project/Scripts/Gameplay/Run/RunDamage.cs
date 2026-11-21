using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Cards;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Level;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Run
{
    /// <summary>
    /// The cards' part of the player's damage (stage 6c), plugged into <see cref="CombatTargets.Modifier"/>: weapons ×
    /// <see cref="RunStats.WeaponDamageFactor"/>, explosions × <see cref="RunStats.ExplosionDamageFactor"/> (radius too),
    /// + the bonus against a monster's states (burning…), then a bullet or a melee blow may be critical
    /// (<see cref="RunStats.CritChance"/>, × <see cref="RunStats.CriticalFactor"/>). Zones, states and the monsters' own
    /// effects are never changed. Deterministic (its own random).
    /// </summary>
    public sealed class RunDamage : IDamageModifier, ILevelLoadStep, ILevelTickable, IDisposable
    {
        private readonly RunStats _stats;
        private readonly CombatTargets _targets;
        private readonly StatusSystem _statuses;
        private readonly HordeSystem _horde;
        private readonly PlayerHealth _health;
        private readonly EffectSystem _effects;
        private readonly DeterministicRandom _random = new DeterministicRandom(0xc417);
        private readonly List<CombatTarget> _nearby = new List<CombatTarget>();
        private readonly List<(Vector2 Position, int Floor, bool Inferno)> _pending = new();
        private readonly ExplosionEffect _infernoLook = new ExplosionEffect();
        private float[] _cursedUntil;
        private int[] _cursedGeneration;
        private bool _applyingInferno;
        private float _vampireWindow;
        private float _vampireCredit;
        private int _vampireHealed;

        public RunDamage(RunStats stats, CombatTargets targets, StatusSystem statuses = null,
            HordeSystem horde = null, PlayerHealth health = null, EffectSystem effects = null)
        {
            _stats = stats;
            _targets = targets;
            _statuses = statuses;
            _horde = horde;
            _health = health;
            _effects = effects;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public float ExplosionRadiusFactor => _stats.ExplosionRadiusFactor;
        public bool CursesActive => _cursedUntil != null && _stats.Has(RunPerk.Cursed);

        public bool IsCursed(int agent) => _cursedUntil != null && agent >= 0 &&
            _cursedGeneration[agent] == _horde.Generations[agent] && _cursedUntil[agent] > _horde.Time;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _targets.Modifier = this;
            _targets.WeaponDamageResolved += OnWeaponDamage;
            _targets.BlastKilledMonster += OnBlastKill;
            if (_horde?.IsActive == true)
            {
                _cursedUntil = new float[_horde.Capacity];
                _cursedGeneration = new int[_horde.Capacity];
                _horde.Killed += OnKilled;
            }
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_targets.Modifier == this) _targets.Modifier = null;
            _targets.WeaponDamageResolved -= OnWeaponDamage;
            _targets.BlastKilledMonster -= OnBlastKill;
            if (_horde != null) _horde.Killed -= OnKilled;
        }

        public float Modify(in CombatTarget target, float amount, PlayerDamage kind, out bool critical)
        {
            critical = false;
            var explosion = kind == PlayerDamage.Explosion || kind == PlayerDamage.WeaponExplosion;
            var factor = explosion ? _stats.ExplosionDamageFactor : _stats.WeaponDamageFactor;
            if (target.Agent >= 0 && _statuses != null && _statuses.IsActive)
            {
                var bonuses = _stats.StatusDamage;
                for (var i = 0; i < bonuses.Count; i++)
                    if (_statuses.Left(target.Agent, bonuses[i].Status) > 0f)
                        factor += bonuses[i].Bonus;
            }

            if (_stats.TacticianActive && kind != PlayerDamage.Explosion) factor *= 1.15f;
            if (kind != PlayerDamage.Explosion && IsCursed(target.Agent))
                factor *= 1.15f;
            amount *= factor;
            var chance = _stats.CritChance;
            if (kind == PlayerDamage.Precise && chance > 0f && _random.NextDouble() < chance)
            {
                critical = true;
                amount *= _stats.Has(RunPerk.Executioner) ? 3f : RunStats.CriticalFactor;
            }

            return amount;
        }

        private void OnWeaponDamage(CombatTarget target, WeaponDefinition weapon, float amount, bool killed)
        {
            if (target.Agent < 0 || amount <= 0f) return;
            if (killed && weapon.IsMelee && _stats.Has(RunPerk.Grinder)) _stats.GrindTime = 2f;
            if (!killed && _cursedUntil != null && _stats.Has(RunPerk.Cursed))
            {
                var agent = target.Agent;
                var generation = _horde.Generations[agent];
                if (_cursedGeneration[agent] != generation || _cursedUntil[agent] <= _horde.Time)
                {
                    _cursedGeneration[agent] = generation;
                    _cursedUntil[agent] = _horde.Time + 4f;
                }
            }

            if (_health == null || _health.IsDead || _health.IsFull || !_stats.Has(RunPerk.Vampire)) return;
            _vampireCredit += amount * 0.03f;
            var healing = Mathf.Min(10 - _vampireHealed, Mathf.FloorToInt(_vampireCredit));
            if (healing <= 0) return;
            _vampireCredit -= healing;
            _vampireHealed += healing;
            _health.Heal(healing);
        }

        private void OnKilled(int agent, Vector2 position)
        {
            if (_applyingInferno || !_stats.Has(RunPerk.Inferno) || _statuses == null) return;
            foreach (var bonus in _stats.StatusDamage)
            {
                if (_statuses.Left(agent, bonus.Status) <= 0f) continue;
                _pending.Add((position, _horde.FloorOf(agent), true));
                break;
            }
        }

        private void OnBlastKill(Vector2 position, int floor)
        {
            if (_stats.Has(RunPerk.ChainReaction)) _pending.Add((position, floor, false));
        }

        public void Tick(float deltaTime)
        {
            _vampireWindow += deltaTime;
            if (_vampireWindow >= 1f)
            {
                _vampireWindow %= 1f;
                _vampireCredit %= 1f;
                _vampireHealed = 0;
            }

            for (var i = 0; i < _pending.Count; i++)
            {
                var (position, floor, inferno) = _pending[i];
                if (inferno)
                {
                    _applyingInferno = true;
                    _targets.Blast(position, 60f, 1.5f, 1f, floor);
                    _applyingInferno = false;
                    _effects?.ShowExplosion(position, floor, 1.5f, _infernoLook);
                }
                else ApplyChain(position, floor);
            }
            _pending.Clear();
        }

        private void ApplyChain(Vector2 position, int floor)
        {
            _targets.Collect(position, 2f, floor, _nearby);
            var best = CombatTarget.None;
            var distance = 4f;
            foreach (var target in _nearby)
            {
                if (target.Agent < 0 || !_targets.IsAlive(target)) continue;
                var at = _targets.PositionOf(target);
                var candidate = (at - position).sqrMagnitude;
                if (candidate > distance || !_targets.IsClear(position, at, floor)) continue;
                best = target;
                distance = candidate;
            }
            _nearby.Clear();
            if (best.IsValid)
                _targets.Damage(best, 40f, (_targets.PositionOf(best) - position).normalized);
        }
    }
}
