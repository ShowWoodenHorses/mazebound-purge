using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Content;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Objects;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Level;
using Purge.Gameplay.Spatial;
using UnityEngine;

namespace Purge.Gameplay.Objects
{
    /// <summary>
    /// A barrel standing in its cell (stage 7в): a target of the player's attacks (not of auto-aim, not of the
    /// monsters), it blocks movement until it blows up.
    /// </summary>
    public sealed class Barrel : IDamageable
    {
        internal Barrel(BarrelData data, BarrelDefinition definition, float health)
        {
            Data = data;
            Definition = definition;
            Health = health;
            Position = new Vector2(data.Position.X, data.Position.Y);
        }

        public BarrelData Data { get; }

        /// <summary>Its kind's HP and blast; null — no asset of the kind (it breaks with nothing happening).</summary>
        public BarrelDefinition Definition { get; }

        public string Id => Data.Id;
        public GridPosition Cell => Data.Position;
        public Vector2 Position { get; }
        public float Radius => 0.4f;
        public int Floor => Data.Position.Floor;
        public float Health { get; internal set; }

        /// <summary>Blew up (or about to: hit to 0, waiting for its moment in a chain).</summary>
        public bool IsBroken { get; internal set; }

        public bool IsAlive => !IsBroken;
        public bool IsAimTarget => false;
        public bool HurtByMonsters => false;

        internal Action<Barrel> Broke;

        public void ApplyDamage(float amount, Vector2 direction)
        {
            if (IsBroken || amount <= 0f) return;
            Health -= amount;
            if (Health > 0f) return;
            IsBroken = true;
            Broke?.Invoke(this);
        }
    }

    /// <summary>
    /// The level's explosive barrels (stage 7в, ТЗ «Бочки»): each <see cref="BarrelData"/> of the level is a
    /// <see cref="Barrel"/> in <see cref="SpatialQueryService"/> (bullets, melee, streams, chains and blasts of the player
    /// hit it) and a <see cref="CellBlockers"/> entry. Burning or any damaging zone of the player's in its cell hurts it.
    /// At 0 HP it blows up a moment later (<see cref="ChainDelay"/>: a blast that breaks its neighbours goes on as a chain):
    /// its kind's effects happen at it as the player's own (<see cref="EffectSystem"/>, they never hurt the player), it
    /// is gone and the cell is free. Barrels broken earlier in the run (a continued run) are not there.
    /// </summary>
    public sealed class BarrelSystem : ILevelLoadStep, ILevelTickable, IDisposable
    {
        /// <summary>Seconds from the hit that broke it to the blast.</summary>
        public const float ChainDelay = 0.12f;

        private const float ZoneCheckInterval = 0.25f;

        private readonly LevelData _level;
        private readonly SpatialQueryService _spatial;
        private readonly CellBlockers _blockers;
        private readonly EffectSystem _effects;
        private readonly ZoneSystem _zones;
        private readonly ContentCatalog _content;
        private readonly LevelLaunchOptions _options;
        private readonly List<Barrel> _barrels = new List<Barrel>();
        private readonly List<Barrel> _pending = new List<Barrel>();
        private readonly List<float> _pendingAt = new List<float>();
        private readonly List<string> _broken = new List<string>();
        private readonly List<BarrelDefinition> _definitions = new List<BarrelDefinition>();
        private float _time;
        private float _zoneTimer;

        public BarrelSystem(LevelData level, SpatialQueryService spatial, CellBlockers blockers, EffectSystem effects,
            ZoneSystem zones, ContentCatalog content = null, LevelLaunchOptions options = null)
        {
            _level = level;
            _spatial = spatial;
            _blockers = blockers;
            _effects = effects;
            _zones = zones;
            _content = content;
            _options = options;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        /// <summary>Barrels still standing (in level order).</summary>
        public IReadOnlyList<Barrel> Barrels => _barrels;

        /// <summary>Ids of the barrels blown up in this run (the run's save).</summary>
        public IReadOnlyList<string> BrokenIds => _broken;

        /// <summary>(barrel) it blew up: its effects have happened, it is gone.</summary>
        public event Action<Barrel> Exploded;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _definitions.Clear();
            _content?.GetAll(_definitions);
            var resume = _options?.Resume;
            foreach (var data in _level.Barrels)
            {
                if (resume != null && resume.BrokenBarrels.Contains(data.Id))
                {
                    _broken.Add(data.Id);
                    continue;
                }

                var definition = DefinitionOf(data.Kind);
                if (definition == null)
                    GameLog.Warning(LogChannel.Gameplay, $"No barrel asset of kind {data.Kind}: '{data.Id}' blows up with nothing.");
                var barrel = new Barrel(data, definition, definition != null ? definition.Health : 30f);
                barrel.Broke = OnBroke;
                _barrels.Add(barrel);
                _spatial.Add(barrel);
                _blockers.Add(data.Position);
            }

            if (_barrels.Count > 0) GameLog.Info(LogChannel.Gameplay, $"Barrels: {_barrels.Count}.");
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            _time += deltaTime;
            for (var i = 0; i < _pending.Count; i++)
            {
                if (_pendingAt[i] > _time) continue;
                var barrel = _pending[i];
                _pending.RemoveAt(i);
                _pendingAt.RemoveAt(i);
                i--;
                Explode(barrel); // may break others: they are appended and wait their delay
            }

            _zoneTimer -= deltaTime;
            if (_zoneTimer > 0f || _barrels.Count == 0) return;
            _zoneTimer = ZoneCheckInterval;
            BurnInZones(ZoneCheckInterval);
        }

        public void Dispose()
        {
            foreach (var barrel in _barrels)
            {
                barrel.Broke = null;
                _spatial.Remove(barrel);
            }

            _barrels.Clear();
            _pending.Clear();
            _pendingAt.Clear();
        }

        /// <summary>Its kind's asset; null when the content has none.</summary>
        public BarrelDefinition DefinitionOf(BarrelKind kind)
        {
            foreach (var definition in _definitions)
                if (definition != null && definition.Kind == kind)
                    return definition;
            return null;
        }

        /// <summary>A damaging zone of the player's in the cell hurts the barrel (fire on the grass reaches it).</summary>
        private void BurnInZones(float interval)
        {
            for (var i = _barrels.Count - 1; i >= 0; i--)
            {
                var barrel = _barrels[i];
                if (barrel.IsBroken) continue;
                var cell = barrel.Cell;
                if (!_zones.Grid.IsInside(cell)) continue;
                var index = _zones.Grid.ToIndex(cell);
                var zone = _zones.ZoneAt(cell);
                if (zone?.Damage == null || _zones.IsHostileAt(index)) continue;
                barrel.ApplyDamage(zone.Damage.DamagePerSecond * interval * _zones.StrengthAt(index), Vector2.zero);
            }
        }

        private void OnBroke(Barrel barrel)
        {
            _pending.Add(barrel);
            _pendingAt.Add(_time + ChainDelay);
        }

        private void Explode(Barrel barrel)
        {
            _barrels.Remove(barrel);
            _spatial.Remove(barrel);
            _blockers.Remove(barrel.Cell);
            _broken.Add(barrel.Id);
            if (barrel.Definition != null)
                _effects.Apply(barrel.Definition.Effects, barrel.Position, barrel.Floor, hostile: false);
            Exploded?.Invoke(barrel);
        }
    }
}
