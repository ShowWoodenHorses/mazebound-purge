using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Core.Monsters;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Horde
{
    /// <summary>
    /// What champions' affixes do beyond their numbers (stage 5e2; HP, speed, attack rate and armor are the horde's,
    /// <see cref="HordeSystem.MakeChampion"/>): a hit on the player puts the affix's state on them
    /// (<c>HitStatusAffix</c>) and heals a vampire (<c>LifestealAffix</c>, <see cref="HordeSystem.PlayerHurt"/>); a
    /// trail champion — and a kind with a <see cref="TrailAbility"/> (stage 8d, the Demon: every cell of its footprint) —
    /// leaves its zone on every cell it walks off (the monsters' zone; it spares the champion itself —
    /// <see cref="HordeSystem.IgnoresZone"/>); an explosive one goes off where it died after the affix's delay — a
    /// warning circle meanwhile (<see cref="BlastCount"/>, drawn by the abilities' warning view), then its effects as the
    /// monsters' (<see cref="EffectSystem.Apply"/>, hostile). The horde lives on the player's floor. Allocation-free per tick.
    /// </summary>
    public sealed class ChampionSystem : ILevelLoadStep, ILevelTickable, IDisposable
    {
        /// <summary>Death blasts waiting at once (more are dropped).</summary>
        public const int MaxBlasts = 32;

        private readonly HordeSystem _horde;
        private readonly EffectSystem _effects;
        private readonly ZoneSystem _zones;
        private readonly PlayerSystem _player;
        private readonly PlayerStatusSystem _playerStatuses;

        private readonly Vector2[] _blastAt = new Vector2[MaxBlasts];
        private readonly float[] _blastStart = new float[MaxBlasts];
        private readonly DeathBlastAffix[] _blast = new DeathBlastAffix[MaxBlasts];
        private readonly List<int> _due = new List<int>(MaxBlasts);
        private int[] _trailCell; // per slot: the cell it stood on last tick (-1 = none yet)
        private int[] _trailGeneration;
        private bool _subscribed;

        public ChampionSystem(HordeSystem horde, EffectSystem effects, ZoneSystem zones, PlayerSystem player,
            PlayerStatusSystem playerStatuses)
        {
            _horde = horde;
            _effects = effects;
            _zones = zones;
            _player = player;
            _playerStatuses = playerStatuses;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool IsActive => _subscribed;

        /// <summary>Death blasts waiting to go off.</summary>
        public int BlastCount { get; private set; }

        /// <summary>Blasts gone off, cells of trail laid, health a vampire got back (tests, metrics).</summary>
        public int Blasts { get; private set; }
        public int TrailCells { get; private set; }
        public float Healed { get; private set; }

        public Vector2 BlastPosition(int index) => _blastAt[index];

        /// <summary>Radius of the waiting blast's warning circle.</summary>
        public float BlastRadius(int index) => _blast[index] != null ? Mathf.Max(0.3f, _blast[index].Radius) : 0.3f;

        /// <summary>How far the blast's delay has run, 0..1.</summary>
        public float BlastProgress(int index) =>
            _blast[index] != null ? Mathf.Clamp01((_horde.Time - _blastStart[index]) / Mathf.Max(0.05f, _blast[index].Delay)) : 1f;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var rules = _horde.Champions;
            if (!_horde.IsActive || _subscribed) return UniTask.CompletedTask;
            // Also for the kinds with a trail of their own (stage 8d, the Demon).
            var trails = false;
            foreach (var kind in _horde.KindList) trails |= kind.Trail != null;
            if (!trails && (rules == null || rules.Affixes.Length == 0)) return UniTask.CompletedTask;

            _trailCell = new int[_horde.Capacity];
            _trailGeneration = new int[_horde.Capacity];
            for (var i = 0; i < _trailCell.Length; i++) _trailGeneration[i] = -1;
            _horde.Killed += OnKilled;
            _horde.PlayerHurt += OnPlayerHurt;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _horde.Killed -= OnKilled;
            _horde.PlayerHurt -= OnPlayerHurt;
            _subscribed = false;
        }

        public void Tick(float deltaTime)
        {
            if (!_subscribed || deltaTime <= 0f) return;
            LayTrails();
            GoOff();
        }

        private void LayTrails()
        {
            var affixes = _horde.Affixes;
            var states = _horde.States;
            var positions = _horde.Positions;
            var generations = _horde.Generations;
            var kinds = _horde.Kinds;
            var kindList = _horde.KindList;
            var floor = _player.Floor;
            for (var i = 0; i < _horde.HighWater; i++)
            {
                var kind = kindList[kinds[i]];
                ZoneDefinition zone;
                float duration, strength;
                var width = 1;
                var affix = affixes[i] != 0 ? _horde.AffixOf(i)?.Trail : null;
                if (affix != null && affix.Zone != null)
                {
                    zone = affix.Zone;
                    duration = affix.Duration;
                    strength = affix.Strength;
                }
                else if (kind.Trail != null)
                {
                    // The kind's own (stage 8d): under all of its footprint.
                    zone = kind.Trail.Zone;
                    duration = kind.Trail.Duration;
                    strength = kind.Trail.Strength;
                    width = kind.TrailWidth;
                }
                else continue;

                var state = states[i];
                if (state != HordeAgentState.Moving && state != HordeAgentState.Attacking) continue;
                var p = positions[i];
                // The corner cell of the footprint: the width × width cells nearest the body's centre.
                var shift = (width - 1) * 0.5f;
                var cell = new GridPosition(Mathf.FloorToInt(p.x - shift + 0.5f), Mathf.FloorToInt(p.y - shift + 0.5f), floor);
                var code = cell.Y * 65536 + cell.X;
                if (_trailGeneration[i] != generations[i])
                {
                    // A new champion in the slot: its trail starts here.
                    _trailGeneration[i] = generations[i];
                    _trailCell[i] = code;
                    continue;
                }

                if (_trailCell[i] == code) continue;
                var leftX = _trailCell[i] % 65536;
                var leftY = _trailCell[i] / 65536;
                _trailCell[i] = code;
                // The cells of the old footprint the new one no longer covers.
                for (var dy = 0; dy < width; dy++)
                for (var dx = 0; dx < width; dx++)
                {
                    var x = leftX + dx;
                    var y = leftY + dy;
                    if (x >= cell.X && x < cell.X + width && y >= cell.Y && y < cell.Y + width) continue;
                    if (_zones.Place(new GridPosition(x, y, floor), zone, duration, strength, hostile: true)) TrailCells++;
                }
            }
        }

        private void GoOff()
        {
            if (BlastCount == 0) return;
            var time = _horde.Time;
            // Blasts of this batch may kill more explosive ones: those are queued for later ticks.
            for (var b = BlastCount - 1; b >= 0; b--)
                if (time - _blastStart[b] >= _blast[b].Delay)
                    _due.Add(b);
            if (_due.Count == 0) return;

            var floor = _player.Floor;
            foreach (var b in _due)
            {
                var blast = _blast[b];
                var at = _blastAt[b];
                // Removed first (swap with the last; _due runs from the highest index down, so the rest stay valid).
                var last = --BlastCount;
                _blastAt[b] = _blastAt[last];
                _blastStart[b] = _blastStart[last];
                _blast[b] = _blast[last];
                _blast[last] = null;
                Blasts++;
                _effects.Apply(blast.Effects, at, floor, hostile: true);
            }

            _due.Clear();
        }

        private void OnKilled(int agent, Vector2 position)
        {
            var blast = _horde.AffixOf(agent)?.DeathBlast;
            if (blast == null || blast.Effects.Count == 0 || BlastCount >= MaxBlasts) return;
            _blastAt[BlastCount] = position;
            _blastStart[BlastCount] = _horde.Time;
            _blast[BlastCount] = blast;
            BlastCount++;
        }

        private void OnPlayerHurt(int agent, int amount)
        {
            var affix = _horde.AffixOf(agent);
            if (affix == null) return;
            var hit = affix.HitStatus;
            if (hit != null && hit.Status != null) _playerStatuses.Apply(hit.Status, hit.Duration);
            if (affix.Lifesteal > 0f && amount > 0)
            {
                var before = _horde.Health[agent];
                _horde.Heal(agent, amount * affix.Lifesteal);
                Healed += _horde.Health[agent] - before;
            }
        }
    }
}
