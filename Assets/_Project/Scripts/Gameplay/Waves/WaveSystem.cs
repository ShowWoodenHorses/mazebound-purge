using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Level;
using Purge.Core.Navigation;
using Purge.Core.Waves;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Run;
using UnityEngine;

namespace Purge.Gameplay.Waves
{
    public enum WavePhase
    {
        /// <summary>The level has no waves.</summary>
        Inactive = 0,
        /// <summary>Before the first wave.</summary>
        Delay = 1,
        Running = 2,
        /// <summary>Between waves (ТЗ: отсчёт, «Пропустить +N»): the wave's drop lies until the next wave.</summary>
        Pause = 3,
        /// <summary>The last wave is cleared: the level is completed.</summary>
        Completed = 4,
    }

    /// <summary>
    /// The run's waves (stage 6a): on a level whose horde has a <see cref="WaveScenario"/> (and is not a dev level),
    /// generates each wave (<see cref="WaveGenerator"/>, × the run's difficulty, seed of the run) and spawns it at the
    /// level's spawn points (<see cref="LevelData.MonsterSpawns"/>): a pack climbs out within <see cref="SpawnRadius"/>
    /// of one point; points closer than <see cref="MinPlayerDistance"/> to the player or cut off from them are skipped
    /// (all of them — the farthest reachable one). A wave is cleared when all of it has appeared and no monster is
    /// alive; after the last one <see cref="Completed"/> (<see cref="WavesClearedRule"/> completes the level). Ticks after
    /// <see cref="HordeSystem"/>.
    /// <para>Stage 6b: the pause between waves counts down <see cref="WaveScenario.PauseTime"/>; <see cref="Skip"/> starts
    /// the next wave at once and pays <see cref="SkipBonus"/> into the <see cref="RunWallet"/>. When the next wave starts
    /// the monsters' drop still lying disappears (<see cref="PickupSystem.ClearDropped"/>).</para>
    /// <para>Stage 6d: a cleared wave pays <see cref="WaveScenario.MoneyBonusOf"/> into the wallet.</para>
    /// </summary>
    public sealed class WaveSystem : ILevelLoadStep, ILevelTickable
    {
        /// <summary>Cells around the spawn point a monster may climb out at.</summary>
        public const float SpawnRadius = 2f;

        /// <summary>Spawn points this close to the player (cells, straight line) wait.</summary>
        public const float MinPlayerDistance = 8f;

        private const int SpawnsPerTick = 8;

        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly PlayerSystem _player;
        private readonly PickupSystem _pickups;
        private readonly RunWallet _wallet;
        private readonly BossSystem _bosses;
        private readonly List<WaveSpawn> _spawns = new List<WaveSpawn>();
        private readonly Dictionary<int, int> _packPoints = new Dictionary<int, int>();
        private readonly List<int> _allowed = new List<int>();
        private int[] _kinds = Array.Empty<int>();
        private DeterministicRandom _random;
        private int _next;
        private float _waveTime;

        // A boss wave (stage 8a): the boss, its escort, and the budget's clock that runs only once the boss is dead.
        private readonly List<WaveSpawn> _escort = new List<WaveSpawn>();
        private readonly Dictionary<int, int> _escortPoints = new Dictionary<int, int>();
        private int _escortNext;
        private int _bossKind = -1;
        private bool _bossPending;
        private int _bossSlot = -1;
        private int _bossGeneration;
        private float _budgetTime;

        public WaveSystem(LevelData level, HordeSystem horde, PlayerSystem player, LevelLaunchOptions options = null,
            PickupSystem pickups = null, RunWallet wallet = null, BossSystem bosses = null)
        {
            _level = level;
            _horde = horde;
            _player = player;
            _pickups = pickups;
            _wallet = wallet;
            _bosses = bosses;
            Seed = options?.Seed ?? Environment.TickCount;
        }

        /// <summary>The current wave is a boss wave and its boss has not come or still lives: the budget waits.</summary>
        public bool BossHolds => _bossPending || BossAlive;

        private bool BossAlive =>
            _bossSlot >= 0 && _horde.Generations[_bossSlot] == _bossGeneration && HordeSystem.IsAlive(_horde.States[_bossSlot]);

        /// <summary>The wave being fought is a boss wave.</summary>
        public bool IsBossWave => Phase == WavePhase.Running && Scenario.Waves[WaveIndex].IsBossWave;

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public WaveScenario Scenario { get; private set; }
        public WavePhase Phase { get; private set; }
        public bool IsActive => Phase != WavePhase.Inactive;

        /// <summary>The run's seed: wave N is generated from it (the run save keeps it, stage 6e).</summary>
        public int Seed { get; }

        /// <summary>Current (or last) wave, from 0; -1 before the first.</summary>
        public int WaveIndex { get; private set; } = -1;

        public int WaveCount => Scenario != null ? Scenario.Count : 0;

        /// <summary>Seconds left of <see cref="WavePhase.Delay"/> or <see cref="WavePhase.Pause"/>.</summary>
        public float TimeLeft { get; private set; }

        /// <summary>Monsters of the current wave still to appear (a boss wave's boss and escort too).</summary>
        public int PendingCount =>
            Phase == WavePhase.Running ? _spawns.Count - _next + _escort.Count - _escortNext + (_bossPending ? 1 : 0) : 0;

        /// <summary>Monsters of the current wave in total.</summary>
        public int WaveSize => _spawns.Count;

        /// <summary>Enemies of the current wave left: alive (raised by a Shaman too) and still to appear; 0 between waves.</summary>
        public int RemainingCount => Phase == WavePhase.Running ? _horde.WaveAliveCount + PendingCount : 0;

        /// <summary>Seconds the current pause has lasted (0 outside one).</summary>
        public float PauseElapsed => Phase == WavePhase.Pause ? Mathf.Max(0f, Scenario.PauseTime - TimeLeft) : 0f;

        /// <summary>
        /// What <see cref="Skip"/> pays now: the whole seconds left of the pause × the next wave's
        /// <see cref="WaveScenario.SkipRateOf"/> × the difficulty's money factor; 0 outside the pause between waves.
        /// </summary>
        public int SkipBonus
        {
            get
            {
                if (Phase != WavePhase.Pause) return 0;
                var factor = _horde.Difficulty != null ? _horde.Difficulty.Money : 1f;
                return Mathf.FloorToInt(Mathf.Ceil(TimeLeft) * Scenario.SkipRateOf(WaveIndex + 1) * factor + 1e-4f);
            }
        }

        /// <summary>A wave starts (its index).</summary>
        public event Action<int> WaveStarted;

        /// <summary>The pause was skipped (the bonus paid), right before <see cref="WaveStarted"/>.</summary>
        public event Action<int> Skipped;

        /// <summary>A wave is cleared (its index); the last one is followed by <see cref="Completed"/>.</summary>
        public event Action<int> WaveCleared;

        public event Action Completed;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_horde.UsesWaves || !_horde.IsActive || _horde.Horde.Waves.Count == 0)
                return UniTask.CompletedTask;

            Scenario = _horde.Horde.Waves;
            _random = new DeterministicRandom((int)StableHash.Of($"{_level.name}/spawns/{Seed}"));
            if (_level.MonsterSpawns.Count == 0)
                GameLog.Warning(LogChannel.Gameplay, $"Level '{_level.name}' has waves but no monster spawn points: monsters appear anywhere far from the player.");
            Phase = WavePhase.Delay;
            TimeLeft = Scenario.FirstDelay;
            GameLog.Info(LogChannel.Gameplay, $"Waves: {Scenario.Count}, seed {Seed}, {_level.MonsterSpawns.Count} spawn point(s).");
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            switch (Phase)
            {
                case WavePhase.Delay:
                case WavePhase.Pause:
                    TimeLeft -= deltaTime;
                    if (TimeLeft <= 0f) StartWave(WaveIndex + 1);
                    break;
                case WavePhase.Running:
                    _waveTime += deltaTime;
                    if (_bossPending) SpawnBoss();
                    SpawnEscort();
                    // ТЗ: обычное появление ждёт до смерти босса.
                    if (!BossHolds) _budgetTime += deltaTime;
                    if (!BossHolds) SpawnDue();
                    if (_next >= _spawns.Count && _escortNext >= _escort.Count && !BossHolds && _horde.WaveAliveCount == 0)
                        ClearWave();
                    break;
            }
        }

        /// <summary>
        /// A continued run (stage 6e): instead of the first delay, the full pause before wave <paramref name="index"/>
        /// (0 — the first delay as usual). Call after the load step, before the first tick.
        /// </summary>
        public void ResumeAt(int index)
        {
            if (!IsActive || index <= 0) return;
            index = Mathf.Min(index, Scenario.Count - 1);
            WaveIndex = index - 1;
            Phase = WavePhase.Pause;
            TimeLeft = Scenario.PauseTime;
            GameLog.Info(LogChannel.Gameplay, $"Waves continued: wave {index + 1}/{Scenario.Count} after the pause.");
        }

        /// <summary>Starts the next wave at once, without the bonus (tests, dev). False when not waiting.</summary>
        public bool StartNextWave()
        {
            if (Phase != WavePhase.Delay && Phase != WavePhase.Pause) return false;
            StartWave(WaveIndex + 1);
            return true;
        }

        /// <summary>
        /// «Пропустить +N» of the pause between waves: pays <see cref="SkipBonus"/> into the run's wallet and starts the
        /// next wave. False outside the pause (the short delay before the first wave is not skipped).
        /// </summary>
        public bool Skip()
        {
            if (Phase != WavePhase.Pause) return false;
            var bonus = SkipBonus;
            _wallet?.Add(bonus);
            GameLog.Info(LogChannel.Gameplay, $"Pause skipped with {TimeLeft:0.#} s left: +{bonus}.");
            Skipped?.Invoke(bonus);
            StartWave(WaveIndex + 1);
            return true;
        }

        private void StartWave(int index)
        {
            // ТЗ: дроп волны лежит до начала следующей волны, потом исчезает.
            if (index > 0) _pickups?.ClearDropped();
            WaveIndex = index;
            var wave = Scenario.Waves[index];
            _spawns.Clear();
            _spawns.AddRange(WaveGenerator.Generate(wave, _horde.Difficulty, WaveGenerator.SeedOf(Seed, index)));
            if (_kinds.Length < _spawns.Count) _kinds = new int[_spawns.Count];
            for (var i = 0; i < _spawns.Count; i++)
                _kinds[i] = _horde.KindIndexOf(_spawns[i].Monster);
            _packPoints.Clear();
            _next = 0;
            _waveTime = 0f;
            _budgetTime = 0f;
            // A boss wave (stage 8a): the boss and its escort first.
            _escort.Clear();
            _escortPoints.Clear();
            _escortNext = 0;
            _bossSlot = -1;
            var boss = wave.Boss;
            _bossKind = boss != null ? _horde.KindIndexOf(boss.Monster) : -1;
            _bossPending = _bossKind >= 0;
            if (boss != null && _bossKind < 0)
                GameLog.Warning(LogChannel.Gameplay, $"Wave {index + 1}: boss '{boss.Monster.Id}' is not one of the horde's kinds (run Build Monsters).");
            if (boss != null) _escort.AddRange(WaveGenerator.Escort(boss, _horde.Difficulty));
            TimeLeft = 0f;
            Phase = WavePhase.Running;
            GameLog.Info(LogChannel.Gameplay, $"Wave {index + 1}/{Scenario.Count}: {_spawns.Count} monster(s)" +
                                              (boss != null ? $", boss '{boss.Monster.Id}' with {_escort.Count}." : "."));
            WaveStarted?.Invoke(index);
        }

        private void SpawnDue()
        {
            var wave = Scenario.Waves[WaveIndex];
            var limit = Mathf.Min(wave.MaxAlive, _horde.MaxCount);
            for (var n = 0; n < SpawnsPerTick && _next < _spawns.Count; n++)
            {
                var spawn = _spawns[_next];
                if (spawn.Time > _budgetTime || _horde.WaveAliveCount >= limit) return;

                var kind = _kinds[_next];
                if (kind < 0)
                {
                    // Not one of the horde's kinds (a scenario changed after Build Monsters): skipped, not stuck.
                    _next++;
                    continue;
                }

                if (!_packPoints.TryGetValue(spawn.Pack, out var point))
                {
                    point = PickPoint(spawn.Point);
                    _packPoints[spawn.Pack] = point;
                }

                var center = point >= 0 ? CenterOf(point) : FallbackCenter();
                if (_horde.SpawnEmerging(center, kind, SpawnRadius) < 0)
                    return; // no free slot or cell this tick: tries again
                _next++;
            }
        }

        /// <summary>The boss climbs out in the arena's centre (no arena on this floor — at a far spawn point); tries every tick.</summary>
        private void SpawnBoss()
        {
            var arena = _level.Arena;
            Vector2 center;
            if (!arena.IsEmpty && arena.Floor == _player.Floor) center = new Vector2(arena.Center.X, arena.Center.Y);
            else
            {
                var point = PickPoint(-1);
                center = point >= 0 ? CenterOf(point) : FallbackCenter();
            }

            var slot = _horde.SpawnBoss(center, _bossKind);
            if (slot < 0) return; // no free slot or place this tick
            _bossPending = false;
            _bossSlot = slot;
            _bossGeneration = _horde.Generations[slot];
            _bosses?.Begin(slot);
        }

        /// <summary>The escort's packs, each from one of the spawn points nearest the arena.</summary>
        private void SpawnEscort()
        {
            for (var n = 0; n < SpawnsPerTick && _escortNext < _escort.Count; n++)
            {
                var spawn = _escort[_escortNext];
                if (spawn.Time > _waveTime) return;
                var kind = _horde.KindIndexOf(spawn.Monster);
                if (kind < 0)
                {
                    _escortNext++;
                    continue;
                }

                if (!_escortPoints.TryGetValue(spawn.Pack, out var point))
                {
                    point = spawn.Point >= 0 ? PickPoint(spawn.Point) : PickNearArena(spawn.Pack);
                    _escortPoints[spawn.Pack] = point;
                }

                var center = point >= 0 ? CenterOf(point) : FallbackCenter();
                if (_horde.SpawnEmerging(center, kind, SpawnRadius) < 0) return;
                _escortNext++;
            }
        }

        /// <summary>
        /// An allowed spawn point (far enough from the player, reachable) among the nearest to the arena: the packs of
        /// the escort take the nearest ones in turn; no arena — any allowed one.
        /// </summary>
        private int PickNearArena(int pack)
        {
            var arena = _level.Arena;
            if (arena.IsEmpty) return PickPoint(-1);
            PickPoint(-1); // fills _allowed
            if (_allowed.Count == 0) return PickPoint(-1);
            var center = new Vector2(arena.Center.X, arena.Center.Y);
            _allowed.Sort((a, b) => (CenterOf(a) - center).sqrMagnitude.CompareTo((CenterOf(b) - center).sqrMagnitude));
            // The two nearest share the packs.
            return _allowed[pack % Mathf.Min(2, _allowed.Count)];
        }

        private void ClearWave()
        {
            GameLog.Info(LogChannel.Gameplay, $"Wave {WaveIndex + 1}/{Scenario.Count} cleared.");
            // The wave's money (stage 6d); its score — RunScore.
            if (_wallet != null) _wallet.Add(Scenario.MoneyBonusOf(WaveIndex) * _wallet.Factor);
            WaveCleared?.Invoke(WaveIndex);
            if (WaveIndex + 1 >= Scenario.Count)
            {
                Phase = WavePhase.Completed;
                Completed?.Invoke();
                return;
            }

            Phase = WavePhase.Pause;
            TimeLeft = Scenario.PauseTime;
        }

        /// <summary>
        /// A spawn point on the player's floor: <paramref name="wanted"/> when allowed, else a random allowed one (far
        /// enough, reachable), else the farthest reachable; -1 = none on this floor.
        /// </summary>
        private int PickPoint(int wanted)
        {
            var points = _level.MonsterSpawns;
            var player = _player.Position;
            var floor = _player.Floor;
            _allowed.Clear();
            var farthest = -1;
            var farthestDistance = -1f;
            for (var i = 0; i < points.Count; i++)
            {
                var cell = points[i].Position;
                if (cell.Floor != floor || _horde.FlowDistance(cell.X, cell.Y) == FlowField.Unreachable) continue;
                var distance = (new Vector2(cell.X, cell.Y) - player).magnitude;
                if (distance >= MinPlayerDistance) _allowed.Add(i);
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthest = i;
                }
            }

            if (wanted >= 0 && _allowed.Contains(wanted)) return wanted;
            return _allowed.Count > 0 ? _allowed[_random.NextInt(_allowed.Count)] : farthest;
        }

        private Vector2 CenterOf(int point)
        {
            var cell = _level.MonsterSpawns[point].Position;
            return new Vector2(cell.X, cell.Y);
        }

        /// <summary>No spawn point: a random reachable cell far enough from the player (or any reachable one).</summary>
        private Vector2 FallbackCenter()
        {
            var width = _level.Geometry.Width;
            var any = -1;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var cell = _horde.RandomReachableCell(_random);
                if (cell < 0) break;
                any = cell;
                var position = new Vector2(cell % width, cell / width);
                if ((position - _player.Position).magnitude >= MinPlayerDistance) return position;
            }

            return any >= 0 ? new Vector2(any % width, any / width) : _player.Position;
        }
    }
}
