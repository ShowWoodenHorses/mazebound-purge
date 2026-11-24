using System;
using Purge.Core.Boosters;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Monsters;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Run;
using Purge.Gameplay.Waves;
using UnityEngine;

namespace Purge.Gameplay.Throwing
{
    public sealed class ThrowableRunEffects
    {
        private readonly LevelLaunchOptions _options;
        private readonly RunStats _stats;
        private readonly EffectSystem _effects;
        private readonly StatusSystem _statuses;
        private readonly HordeSystem _horde;
        private readonly WaveSystem _waves;
        private readonly ThrowableSlot _slot;
        private readonly PickupSystem _pickups;
        private readonly ContentCatalog _content;
        private readonly StatusDefinition _burning;
        private readonly StatusDefinition _slow;
        private readonly ZoneDefinition _fire;
        private readonly Action<CombatTarget, bool> _onBlastResolved;
        private ThrowableDefinition _detonating;
        private int _floor;
        private int _refundedWave = -1;

        public ThrowableRunEffects(LevelLaunchOptions options, RunStats stats, EffectSystem effects, StatusSystem statuses,
            HordeSystem horde, WaveSystem waves, ThrowableSlot slot, PickupSystem pickups, ContentCatalog content)
        {
            _options = options;
            _stats = stats;
            _effects = effects;
            _statuses = statuses;
            _horde = horde;
            _waves = waves;
            _slot = slot;
            _pickups = pickups;
            _content = content;
            _burning = content.Find<StatusDefinition>("status_burning");
            _slow = content.Find<StatusDefinition>("status_slow");
            _fire = content.Find<ZoneDefinition>("zone_fire");
            _onBlastResolved = OnBlastResolved;
        }

        public void Detonate(ThrowableDefinition definition, Vector2 at, int floor, bool cluster)
        {
            var explosion = DamageExplosion(definition);
            _detonating = definition;
            _floor = floor;
            _effects.Apply(definition.Effects, at, floor, damageFactor: _slot.DamageFactor(definition),
                radiusFactor: _slot.RadiusFactor(definition), cluster: cluster,
                blastResolved: explosion == null ? null : _onBlastResolved);
            _detonating = null;
            if (explosion == null) return;

            if (_fire != null && IsGrenade(definition) && _options.HasSkill(CharacterSkills.ScorchedEarth))
                _effects.PlaceZone(at, floor, _fire, 1.5f, 3f);
            if (!_options.HasBoost(SpecialBoosterKind.Volley) || _stats.VolleyUsed) return;

            _stats.VolleyUsed = true;
            _stats.PendingVolleyItem = definition.Id;
            _stats.PendingVolleyPosition = at;
            _stats.PendingVolleyFloor = floor;
            _stats.PendingVolleyTime = 0.4f;
        }

        public void Tick(float deltaTime)
        {
            if (string.IsNullOrEmpty(_stats.PendingVolleyItem)) return;
            _stats.PendingVolleyTime -= deltaTime;
            if (_stats.PendingVolleyTime > 0f) return;

            var definition = _content.Find<ThrowableDefinition>(_stats.PendingVolleyItem);
            _stats.PendingVolleyItem = null;
            var explosion = DamageExplosion(definition);
            if (explosion != null)
                _effects.ApplyExplosion(explosion, _stats.PendingVolleyPosition, _stats.PendingVolleyFloor,
                    _slot.DamageFactor(definition) * 0.5f, _slot.RadiusFactor(definition));
        }

        private void OnBlastResolved(CombatTarget target, bool killed)
        {
            if (!killed)
            {
                if (_burning != null && _options.HasSkill(CharacterSkills.Pyrotechnic))
                    _statuses.Apply(target, _burning, 3f);
                if (_slow != null && IsGrenade(_detonating) && _options.HasSkill(CharacterSkills.ConcussionCharge))
                    _statuses.Apply(target, _slow, 2f);
                return;
            }

            if (!_options.HasSkill(CharacterSkills.OrdnanceReserve) || _waves.Phase != WavePhase.Running ||
                _refundedWave == _waves.WaveIndex) return;
            var rank = _horde.DefinitionOf(target.Agent)?.Rank;
            if (rank != MonsterRank.Officer && rank != MonsterRank.Elite) return;

            if (_slot.Add(_detonating, 1) > 0 ||
                _pickups.DropThrowable(_horde.Positions[target.Agent], _floor, _detonating, 1, 30f) != null)
                _refundedWave = _waves.WaveIndex;
        }

        private static bool IsGrenade(ThrowableDefinition definition) => definition.Id == "grenade";

        private static ExplosionEffect DamageExplosion(ThrowableDefinition definition)
        {
            if (definition != null)
                foreach (var effect in definition.Effects)
                    if (effect is ExplosionEffect explosion && explosion.Damage > 0f) return explosion;
            return null;
        }
    }
}
