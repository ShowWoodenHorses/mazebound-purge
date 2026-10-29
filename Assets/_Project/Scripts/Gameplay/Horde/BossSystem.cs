using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Horde
{
    /// <summary>
    /// The boss of the level (stage 8a; ТЗ: «босс-волна», «закрытие арены»). A boss wave (<c>WaveSystem</c>) or the
    /// sandbox hands it the boss's slot (<see cref="Begin"/>); then:
    /// <list type="bullet">
    /// <item>While the player is outside the arena (<see cref="LevelData.Arena"/>, the closing on —
    /// <see cref="LevelLaunchOptions.CloseArena"/>, the run's «Закрытая арена») the boss waits in it (<see cref="HordeSystem.SetHeld"/>; its escort chases as
    /// usual) and <see cref="ShowArenaArrow"/>. Once the player is inside, the gates come down on every entry
    /// (<see cref="CellBlockers"/>: movement stops, bullets and sight go through) until the boss dies.</item>
    /// <item>Its death: the simulation × <see cref="DeathSlowFactor"/> for <see cref="DeathSlowTime"/> s
    /// (<see cref="LevelClock"/>), the gates go up, <see cref="Defeated"/>. Its drop, money, experience and score — by
    /// rank, as every monster's.</item>
    /// </list>
    /// One boss at a time (a new one replaces the shown one). Ticks after the horde and the waves.
    /// </summary>
    public sealed class BossSystem : ILevelLoadStep, ILevelTickable, IDisposable
    {
        /// <summary>ТЗ: замедление симуляции ×0,3 на 0,5 с при смерти босса.</summary>
        public const float DeathSlowFactor = 0.3f;
        public const float DeathSlowTime = 0.5f;

        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly PlayerSystem _player;
        private readonly CellBlockers _blockers;
        private readonly LevelClock _clock;
        private readonly MonsterAbilities _abilities;
        private readonly bool _closeArena;
        private readonly List<GridPosition> _gateCells = new List<GridPosition>();
        private readonly List<ArenaEntry> _entries = new List<ArenaEntry>();
        private readonly List<float> _thresholds = new List<float>();
        private int _generation;
        private bool _subscribed;

        public BossSystem(LevelData level, HordeSystem horde, PlayerSystem player, CellBlockers blockers = null,
            LevelClock clock = null, MonsterAbilities abilities = null, LevelLaunchOptions options = null)
        {
            _level = level;
            _closeArena = (options ?? LevelLaunchOptions.Default).CloseArena;
            _horde = horde;
            _player = player;
            _blockers = blockers;
            _clock = clock;
            _abilities = abilities;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>The boss's slot; -1 = none alive.</summary>
        public int Boss { get; private set; } = -1;

        public bool IsBossAlive => Boss >= 0;

        public MonsterDefinition Definition { get; private set; }

        /// <summary>Its HP share 0..1 (0 when none).</summary>
        public float HealthShare =>
            IsBossAlive ? Mathf.Clamp01(_horde.Health[Boss] / Mathf.Max(1f, _horde.MaxHealthOf(Boss))) : 0f;

        public Vector2 Position => IsBossAlive ? _horde.Positions[Boss] : Vector2.zero;

        /// <summary>HP shares where its phases begin (Phase and Rage modules), for the bar's notches.</summary>
        public IReadOnlyList<float> Thresholds => _thresholds;

        /// <summary>Its phase by HP (0 — the first).</summary>
        public int Phase => IsBossAlive && _abilities != null ? _abilities.HealthPhaseOf(Boss) : 0;

        /// <summary>The level has an arena on some floor.</summary>
        public bool HasArena => !_level.Arena.IsEmpty;

        public Vector2 ArenaCenter => new Vector2(_level.Arena.Center.X, _level.Arena.Center.Y);

        /// <summary>The arena's entries (gaps through its ring) the gates close.</summary>
        public IReadOnlyList<ArenaEntry> Entries => _entries;

        public bool GatesClosed { get; private set; }

        /// <summary>The player stands on the arena's open cells.</summary>
        public bool PlayerInArena
        {
            get
            {
                if (!HasArena || !_player.IsSpawned || _player.Floor != _level.Arena.Floor) return false;
                return _level.Arena.Rect.Contains(_player.Cell);
            }
        }

        /// <summary>A boss waits in the arena the player is not in yet: point them there.</summary>
        public bool ShowArenaArrow => IsBossAlive && HasArena && !PlayerInArena && !GatesClosed;

        /// <summary>(slot) a boss came (its bar, music, the roar).</summary>
        public event Action<int> Appeared;

        /// <summary>(slot, where) the boss died.</summary>
        public event Action<int, Vector2> Defeated;

        /// <summary>(closed) the arena's gates came down / went up.</summary>
        public event Action<bool> GatesChanged;

        /// <summary>(phase) the boss went into its next phase by HP.</summary>
        public event Action<int> PhaseChanged;

        /// <summary>Bosses killed (tests, metrics).</summary>
        public int Kills { get; private set; }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_horde.IsActive || _subscribed) return UniTask.CompletedTask;
            _entries.Clear();
            _gateCells.Clear();
            if (HasArena)
            {
                _entries.AddRange(ArenaLayout.FindEntries(_level.Geometry, _level.Arena));
                foreach (var entry in _entries)
                {
                    var gap = entry.Gap;
                    for (var y = gap.Y; y < gap.Y + gap.Height; y++)
                    for (var x = gap.X; x < gap.X + gap.Width; x++)
                        _gateCells.Add(new GridPosition(x, y, _level.Arena.Floor));
                }
            }

            _horde.Killed += OnKilled;
            if (_abilities != null) _abilities.PhaseChanged += OnPhaseChanged;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _horde.Killed -= OnKilled;
            if (_abilities != null) _abilities.PhaseChanged -= OnPhaseChanged;
            _subscribed = false;
        }

        /// <summary>
        /// The boss in <paramref name="slot"/> came (a boss wave, the sandbox): it waits in the arena while the player is
        /// outside it (the closing on), its bar shows.
        /// </summary>
        public void Begin(int slot)
        {
            if (!_horde.IsActive || (uint)slot >= (uint)_horde.HighWater) return;
            if (IsBossAlive && Boss != slot) _horde.SetHeld(Boss, false);
            Boss = slot;
            _generation = _horde.Generations[slot];
            Definition = _horde.DefinitionOf(slot);
            CollectThresholds();
            if (WaitsInArena && !PlayerInArena) _horde.SetHeld(slot, true);
            GameLog.Info(LogChannel.Gameplay, $"Boss '{(Definition != null ? Definition.Id : "?")}' appeared (slot {slot}).");
            Appeared?.Invoke(slot);
        }

        /// <summary>
        /// Sandbox: a boss of <paramref name="kind"/> climbs out near <paramref name="center"/> where its body fits. Its
        /// slot, or -1.
        /// </summary>
        public int Summon(int kind, Vector2 center)
        {
            var slot = _horde.SpawnBoss(center, kind);
            if (slot >= 0) Begin(slot);
            return slot;
        }

        public void Tick(float deltaTime)
        {
            if (!IsBossAlive) return;
            if (_horde.Generations[Boss] != _generation || !HordeSystem.IsAlive(_horde.States[Boss]))
            {
                // Gone without a kill (the benchmark's reset, a level reset).
                End();
                return;
            }

            if (!WaitsInArena || !PlayerInArena || !PlayerClearOfEntries) return;
            _horde.SetHeld(Boss, false);
            SetGates(true);
        }

        private bool WaitsInArena => HasArena && _closeArena;

        /// <summary>
        /// The player's whole footprint is on the arena's cells: a gate coming down on a cell the body still overlaps
        /// would lock the player in place (every step would overlap a blocked cell).
        /// </summary>
        private bool PlayerClearOfEntries
        {
            get
            {
                var rect = _level.Arena.Rect;
                var half = new Vector2(1f, 1f) * (_player.Definition.BodyHalfSize - 0.001f);
                return rect.Contains(PlayerMovement.CellOf(_player.Position - half)) &&
                       rect.Contains(PlayerMovement.CellOf(_player.Position + half));
            }
        }

        private void OnKilled(int slot, Vector2 position)
        {
            if (slot != Boss || _horde.Generations[slot] != _generation) return;
            Kills++;
            _clock?.SlowDown(DeathSlowFactor, DeathSlowTime);
            GameLog.Info(LogChannel.Gameplay, $"Boss '{(Definition != null ? Definition.Id : "?")}' defeated.");
            End();
            Defeated?.Invoke(slot, position);
        }

        private void OnPhaseChanged(int slot, int phase)
        {
            if (slot == Boss) PhaseChanged?.Invoke(phase);
        }

        private void End()
        {
            Boss = -1;
            SetGates(false);
        }

        private void SetGates(bool closed)
        {
            if (GatesClosed == closed || _gateCells.Count == 0) return;
            GatesClosed = closed;
            if (_blockers != null)
                foreach (var cell in _gateCells)
                {
                    if (closed) _blockers.Add(cell);
                    else _blockers.Remove(cell);
                }

            if (closed && _blockers != null)
                foreach (var entry in _entries)
                    _horde.PushOutOf(entry.Gap, entry.Side, _level.Arena.Floor);

            GameLog.Info(LogChannel.Gameplay, closed ? "Arena closed." : "Arena opened.");
            GatesChanged?.Invoke(closed);
        }

        private void CollectThresholds()
        {
            _thresholds.Clear();
            if (Definition == null) return;
            foreach (var ability in Definition.Abilities)
                if ((ability is PhaseAbility || ability is RageAbility) && ability.HealthBelow < 1f)
                    _thresholds.Add(ability.HealthBelow);
        }
    }
}
