using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Level;

namespace Purge.Gameplay.Run
{
    /// <summary>
    /// The cards' part of the player's damage (stage 6c), plugged into <see cref="CombatTargets.Modifier"/>: weapons ×
    /// <see cref="RunStats.WeaponDamageFactor"/>, explosions × <see cref="RunStats.ExplosionDamageFactor"/> (radius too),
    /// + the bonus against a monster's states (burning…), then a bullet or a melee blow may be critical
    /// (<see cref="RunStats.CritChance"/>, × <see cref="RunStats.CriticalFactor"/>). Zones, states and the monsters' own
    /// effects are never changed. Deterministic (its own random).
    /// </summary>
    public sealed class RunDamage : IDamageModifier, ILevelLoadStep, IDisposable
    {
        private readonly RunStats _stats;
        private readonly CombatTargets _targets;
        private readonly StatusSystem _statuses;
        private readonly DeterministicRandom _random = new DeterministicRandom(0xc417);

        public RunDamage(RunStats stats, CombatTargets targets, StatusSystem statuses = null)
        {
            _stats = stats;
            _targets = targets;
            _statuses = statuses;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public float ExplosionRadiusFactor => _stats.ExplosionRadiusFactor;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _targets.Modifier = this;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_targets.Modifier == this) _targets.Modifier = null;
        }

        public float Modify(in CombatTarget target, float amount, PlayerDamage kind, out bool critical)
        {
            critical = false;
            var factor = kind == PlayerDamage.Explosion ? _stats.ExplosionDamageFactor : _stats.WeaponDamageFactor;
            if (target.Agent >= 0 && _statuses != null && _statuses.IsActive)
            {
                var bonuses = _stats.StatusDamage;
                for (var i = 0; i < bonuses.Count; i++)
                    if (_statuses.Left(target.Agent, bonuses[i].Status) > 0f)
                        factor += bonuses[i].Bonus;
            }

            amount *= factor;
            var chance = _stats.CritChance;
            if (kind == PlayerDamage.Precise && chance > 0f && _random.NextDouble() < chance)
            {
                critical = true;
                amount *= RunStats.CriticalFactor;
            }

            return amount;
        }
    }
}
