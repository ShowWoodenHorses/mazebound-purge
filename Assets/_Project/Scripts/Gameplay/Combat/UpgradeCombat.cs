using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Definitions;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using UnityEngine;
namespace Purge.Gameplay.Combat
{
    public sealed class UpgradeCombat : ILevelLoadStep, ILevelTickable, IDisposable
    {
        private readonly CombatTargets _targets; private readonly IItemUpgrades _upgrades;
        private readonly Purge.Gameplay.Horde.CombatSandbox _sandbox;
        private readonly StatusSystem _statuses; private readonly PlayerHealth _health; private readonly PlayerSystem _player;
        private readonly List<CombatTarget> _found=new List<CombatTarget>();
        private float _healCredit, _healFraction;
        public event Action<Vector2,Vector2,WeaponDefinition> Ricochet;
        public UpgradeCombat(CombatTargets targets,IItemUpgrades upgrades,StatusSystem statuses,PlayerHealth health,PlayerSystem player,
            Purge.Gameplay.Horde.CombatSandbox sandbox = null)
        {_targets=targets;_upgrades=upgrades;_statuses=statuses;_health=health;_player=player;_sandbox=sandbox;}
        public LevelLoadStage Stage=>LevelLoadStage.InitializeSystems;
        public UniTask ExecuteAsync(CancellationToken cancellation){_targets.Upgrades=this;return UniTask.CompletedTask;}
        public void Dispose(){if(_targets.Upgrades==this)_targets.Upgrades=null;_found.Clear();}
        public void Tick(float deltaTime){_healCredit=Mathf.Min(1f,_healCredit+deltaTime);}
        public float Modify(CombatTarget target,WeaponDefinition weapon,float damage)
        {
            if(_upgrades.Has(weapon,WeaponUpgrade.Shatter) && weapon.UpgradeFreeze!=null && target.Agent>=0 && _statuses.Left(target.Agent,weapon.UpgradeFreeze)>0)
                damage*=weapon.UpgradeParameter("shatter_damage_factor");
            return damage;
        }
        public void Hit(CombatTarget target,Vector2 at,Vector2 direction,WeaponDefinition weapon,float dealt,bool killed)
        {
            if(dealt<=0f || target.Agent<0)return;
            if(_upgrades.Has(weapon,WeaponUpgrade.Lifesteal))
            {
                float limit=_health.Max*weapon.UpgradeParameter("lifesteal_max_hp_per_second_share");
                float heal=Mathf.Min(_healCredit*limit,dealt*weapon.UpgradeParameter("lifesteal_share"));if(limit>0f)_healCredit-=heal/limit;
                if(!_health.IsFull){_healFraction+=heal;int whole=Mathf.FloorToInt(_healFraction);if(whole>0){_health.Heal(whole);_healFraction-=whole;}}
            }
            if(!killed && _upgrades.Has(weapon,WeaponUpgrade.Burn) && weapon.UpgradeBurn!=null)
                _statuses.Apply(target,weapon.UpgradeBurn,weapon.UpgradeParameter("burn_seconds"),dealt*weapon.UpgradeParameter("burn_damage_share_per_second")/Mathf.Max(0.001f,weapon.UpgradeBurn.DamagePerSecond));
            if (!killed && weapon.UpgradeElectrify != null &&
                (_upgrades.Has(weapon, WeaponUpgrade.Electrify) || _sandbox?.Electrify == true))
                _statuses.Apply(target, weapon.UpgradeElectrify, weapon.UpgradeParameter("electrify_seconds"),
                    dealt * weapon.UpgradeParameter("electrify_damage_share"));
            if(_upgrades.Has(weapon,WeaponUpgrade.Ricochet))
            {
                _targets.Collect(at,weapon.UpgradeParameter("ricochet_range"),_player.Floor,_found);
                var best=CombatTarget.None;float distance=float.MaxValue;
                foreach(var candidate in _found)
                {if(candidate.Equals(target)||!_targets.IsAlive(candidate))continue;var point=_targets.PositionOf(candidate);float d=(point-at).sqrMagnitude;if(d<distance&&_targets.IsClear(at,point,_player.Floor)){best=candidate;distance=d;}}
                _found.Clear();if(best.IsValid){var point=_targets.PositionOf(best);_targets.Damage(best,dealt*weapon.UpgradeParameter("ricochet_damage_share"),(point-at).normalized);Ricochet?.Invoke(at,point,weapon);}
            }
        }
    }
}
