using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Effects;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Effects
{
    /// <summary>
    /// A killed horde monster's death effects (<c>MonsterDefinition.DeathEffects</c>: explosion, zone, state) happen
    /// where it died on the next tick — not inside the hit that killed it, so a chain of exploding monsters never
    /// recurses into the horde's damage. Monster effects: their zones hurt the player too.
    /// </summary>
    public sealed class MonsterDeathEffects : ILevelLoadStep, ILevelTickable, IDisposable
    {
        /// <summary>Death effects applied in one tick at most; the rest wait for the next ones.</summary>
        public const int MaxPerTick = 6;

        private readonly HordeSystem _horde;
        private readonly EffectSystem _effects;
        private readonly PlayerSystem _player;
        private readonly List<(IReadOnlyList<EffectModule> Effects, Vector2 At)> _pending =
            new List<(IReadOnlyList<EffectModule>, Vector2)>();
        private readonly List<(IReadOnlyList<EffectModule> Effects, Vector2 At)> _applying =
            new List<(IReadOnlyList<EffectModule>, Vector2)>();
        private bool _subscribed;

        public MonsterDeathEffects(HordeSystem horde, EffectSystem effects, PlayerSystem player)
        {
            _horde = horde;
            _effects = effects;
            _player = player;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>Effects waiting for the next tick.</summary>
        public int Pending => _pending.Count;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_horde.IsActive || _subscribed)
                return UniTask.CompletedTask;

            var any = false;
            foreach (var kind in _horde.KindList)
                any |= kind.Definition != null && kind.Definition.DeathEffects.Count > 0;
            if (!any)
                return UniTask.CompletedTask;

            _horde.Killed += OnKilled;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            if (_pending.Count == 0) return;
            // Effects of this batch may kill more: those wait for the next tick. A burst of deaths (a ramming vehicle,
            // stage 9b) goes out at most MaxPerTick a tick, the oldest first.
            var count = Mathf.Min(_pending.Count, MaxPerTick);
            for (var i = 0; i < count; i++) _applying.Add(_pending[i]);
            _pending.RemoveRange(0, count);
            var floor = _player.Floor; // the horde lives on the player's floor
            foreach (var (effects, at) in _applying)
                _effects.Apply(effects, at, floor, hostile: true);
            _applying.Clear();
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _horde.Killed -= OnKilled;
            _subscribed = false;
        }

        private void OnKilled(int agent, Vector2 position)
        {
            var definition = _horde.DefinitionOf(agent);
            if (definition != null && definition.DeathEffects.Count > 0)
                _pending.Add((definition.DeathEffects, position));
        }
    }
}
