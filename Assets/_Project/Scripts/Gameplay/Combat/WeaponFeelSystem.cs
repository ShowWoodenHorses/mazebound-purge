using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Definitions;
using Purge.Core.Feel;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Weapons;
using UnityEngine;

namespace Purge.Gameplay.Combat
{
    /// <summary>
    /// Gameplay part of the shooting feel (<see cref="FeelModule"/>): a hit knocks the monster back and staggers it, a
    /// kill flings the corpse, an attack pushes the shooter back (recoil). Horde monsters only (the zombies of Maze stay
    /// as they are). Scaled by the stand's <see cref="FeelChannels"/>.
    /// </summary>
    public sealed class WeaponFeelSystem : ILevelLoadStep, IDisposable
    {
        private readonly CombatTargets _targets;
        private readonly PlayerCombat _combat;
        private readonly PlayerSystem _player;
        private readonly HordeSystem _horde;
        private readonly FeelChannels _channels;
        private bool _subscribed;

        public WeaponFeelSystem(CombatTargets targets, PlayerCombat combat, PlayerSystem player, HordeSystem horde = null,
            FeelChannels channels = null)
        {
            _targets = targets;
            _combat = combat;
            _player = player;
            _horde = horde;
            _channels = channels ?? new FeelChannels();
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        public FeelChannels Channels => _channels;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_subscribed)
            {
                _subscribed = true;
                _targets.WeaponDamaged += OnWeaponDamaged;
                _combat.Attacked += OnAttacked;
            }

            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _subscribed = false;
            _targets.WeaponDamaged -= OnWeaponDamaged;
            _combat.Attacked -= OnAttacked;
        }

        private void OnAttacked(WeaponRuntime weapon, Vector2 direction)
        {
            var recoil = weapon.Definition.Feel.Recoil * _channels[FeelChannel.Recoil];
            if (recoil > 0f && direction.sqrMagnitude > 0f) _player.Push(-direction.normalized * recoil);
        }

        private void OnWeaponDamaged(CombatTarget target, Vector2 at, Vector2 direction, WeaponDefinition weapon, bool killed)
        {
            if (_horde == null || target.Agent < 0) return;
            var feel = weapon.Feel;
            var push = direction.sqrMagnitude > 1e-6f ? direction.normalized : (at - _player.Position).normalized;
            if (killed)
            {
                _horde.Push(target.Agent, push * (feel.KillFling * _channels[FeelChannel.KillFling]));
                return;
            }

            _horde.Push(target.Agent, push * (feel.Knockback * _channels[FeelChannel.Knockback]));
            _horde.Stagger(target.Agent, feel.Stagger * _channels[FeelChannel.Stagger]);
        }
    }
}
