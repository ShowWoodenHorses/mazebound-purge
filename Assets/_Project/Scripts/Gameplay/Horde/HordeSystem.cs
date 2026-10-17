using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Navigation;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Horde
{
    public enum HordeAgentState : byte
    {
        Inactive = 0,
        Moving = 1,
        Attacking = 2,
        /// <summary>Killed: the corpse stays for <see cref="MonsterStats.CorpseTime"/>, then the slot is free.</summary>
        Dying = 3,
        /// <summary>Climbs out of the ground (<see cref="MonsterStats.EmergeTime"/>): can be hit, neither walks nor attacks.</summary>
        Emerging = 4,
    }

    /// <summary>A kind of monster the horde has: its definition (null = the stress test's built-in one) and stats.</summary>
    public sealed class HordeKind
    {
        public HordeKind(MonsterDefinition definition, MonsterStats stats, float weight)
        {
            Definition = definition;
            Stats = stats;
            Weight = weight;
        }

        public MonsterDefinition Definition { get; }
        public MonsterStats Stats { get; }
        public float Weight { get; }
    }

    /// <summary>
    /// Mass monsters in flat arrays (ТЗ: rank-and-file are not MonoBehaviours): positions, facing, HP, state, timers
    /// and the kind (<see cref="HordeKind"/>: definition and stats — <see cref="StressTestDefinition.Monsters"/> by
    /// weight, or the built-in monster) per slot. The stress stand lives on the player's floor (walkability, flow field and buckets of that floor);
    /// monsters of stage 5 keep a floor per slot. Everyone walks the shared <see cref="FlowField"/> to the player (rebuilt only when the player changes
    /// cell or a door changes), keeps spacing through <see cref="CellBuckets"/> (no physics), slides along walls and
    /// attacks in reach. Views read the arrays; nothing here knows about rendering. Allocation-free per tick.
    /// Active only on levels with a <see cref="StressTestDefinition"/> (stage 2); stage 5 builds monsters on it.
    /// </summary>
    public sealed class HordeSystem : ILevelLoadStep, ILevelTickable, IDisposable
    {
        private const float PlayerRadius = 0.3f;
        private const float DirectChaseDistance = 1.6f;
        private const int SpawnsPerTick = 8;

        private readonly LevelData _level;
        private readonly LevelGrid _grid;
        private readonly LevelPassability _passability;
        private readonly DoorSystem _doors;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _playerHealth;
        private readonly HordeKind[] _kinds;
        private readonly float _maxRadius;
        private readonly float _totalWeight;

        private Vector2[] _positions;
        private float[] _facing; // radians, 0 = North (+Y), clockwise
        private float[] _health;
        private HordeAgentState[] _states;
        private float[] _stateStart;
        private float[] _attackTimer;
        private float[] _attackStart;
        private float[] _hitTime;
        private byte[] _variant;
        private byte[] _kind;
        private float[] _speed;
        private int[] _generation; // + 1 on every spawn into the slot: states of the previous monster are not carried over
        private float[] _speedFactor; // states (StatusSystem): slow
        private bool[] _stunned; // states: neither moves nor attacks
        private Vector2[] _push; // knockback (and a corpse's fling), cells per second, fading
        private float[] _staggerUntil; // a hit stops the monster's step and attack until then

        private bool[] _walkable;
        private int[] _walkableCells;
        private int _walkableCount;
        private FlowField _flow;
        private CellBuckets _buckets;
        private DeterministicRandom _random;
        private GridPosition _flowTarget;
        private bool _flowDirty = true;
        private bool _subscribed;
        private int _highWater;

        public HordeSystem(LevelData level, LevelGrid grid, LevelPassability passability, DoorSystem doors,
            PlayerSystem player, PlayerHealth playerHealth)
        {
            _level = level;
            _grid = grid;
            _passability = passability;
            _doors = doors;
            _player = player;
            _playerHealth = playerHealth;
            Settings = level.StressTest;
            _kinds = CreateKinds(Settings);
            foreach (var kind in _kinds)
            {
                _maxRadius = Mathf.Max(_maxRadius, kind.Stats.Radius);
                _totalWeight += kind.Weight;
            }
        }

        private static HordeKind[] CreateKinds(StressTestDefinition settings)
        {
            if (settings == null)
                return new[] { new HordeKind(null, new MonsterStats(), 1f) };

            var kinds = new System.Collections.Generic.List<HordeKind>();
            foreach (var spawn in settings.Monsters)
                if (spawn != null && spawn.Monster != null && spawn.Weight > 0f && kinds.Count < byte.MaxValue)
                    kinds.Add(new HordeKind(spawn.Monster, spawn.Monster.Stats, spawn.Weight));
            if (kinds.Count == 0)
                kinds.Add(new HordeKind(null, settings.Monster ?? new MonsterStats(), 1f));
            return kinds.ToArray();
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public StressTestDefinition Settings { get; }
        public bool IsActive => Settings != null && _positions != null;

        /// <summary>Kinds of monsters this horde has (slots refer to them by <see cref="Kinds"/>).</summary>
        public System.Collections.Generic.IReadOnlyList<HordeKind> KindList => _kinds;

        public HordeKind KindOf(int agent) => _kinds[_kind != null && (uint)agent < (uint)_kind.Length ? _kind[agent] : 0];

        public MonsterStats StatsOf(int agent) => KindOf(agent).Stats;

        /// <summary>The monster's definition (null = the stress test's built-in monster).</summary>
        public MonsterDefinition DefinitionOf(int agent) => KindOf(agent).Definition;

        public float RadiusOf(int agent) => StatsOf(agent).Radius;

        /// <summary>The widest body of all kinds (queries look this far around).</summary>
        public float MaxRadius => _maxRadius;

        /// <summary>Game time of the horde (seconds of ticking; stops on pause).</summary>
        public float Time { get; private set; }

        public int Capacity => _positions?.Length ?? 0;

        /// <summary>Slots 0 .. HighWater-1 may be in use; views iterate only these.</summary>
        public int HighWater => _highWater;

        /// <summary>Monsters the horde keeps alive (killed ones are replaced away from the player).</summary>
        public int TargetCount { get; private set; }

        public int AliveCount { get; private set; }
        public int Kills { get; private set; }

        /// <summary>Monster hits on the player (counted also when <see cref="GodMode"/> ignores them).</summary>
        public int PlayerHits { get; private set; }

        /// <summary>Monsters do not damage the player (stress test default).</summary>
        public bool GodMode { get; set; } = true;

        /// <summary>Killed monsters are replaced to keep <see cref="TargetCount"/>.</summary>
        public bool Respawn { get; set; } = true;

        /// <summary>Monsters hold still (measure rendering without simulation).</summary>
        public bool Frozen { get; set; }

        public int FlowBuilds { get; private set; }

        /// <summary>Last tick's cost, milliseconds: the whole simulation and the flow field rebuild within it.</summary>
        public float SimulationMs { get; private set; }
        public float FlowMs { get; private set; }

        public FlowField Flow => _flow;

        public ReadOnlySpan<Vector2> Positions => _positions;
        public ReadOnlySpan<float> Facing => _facing;
        public ReadOnlySpan<HordeAgentState> States => _states;
        public ReadOnlySpan<float> StateStart => _stateStart;
        public ReadOnlySpan<float> AttackStart => _attackStart;
        public ReadOnlySpan<float> HitTime => _hitTime;
        public ReadOnlySpan<byte> Variants => _variant;

        /// <summary>Index into <see cref="KindList"/> per slot.</summary>
        public ReadOnlySpan<byte> Kinds => _kind;

        /// <summary>Walking speed per slot, cells per second (before states).</summary>
        public ReadOnlySpan<float> Speeds => _speed;

        /// <summary>× speed of the states per slot (1 = none).</summary>
        public ReadOnlySpan<float> SpeedFactors => _speedFactor;
        public ReadOnlySpan<float> Health => _health;

        /// <summary>Changes every time a new monster takes the slot.</summary>
        public ReadOnlySpan<int> Generations => _generation;

        public ReadOnlySpan<bool> Stunned => _stunned;

        /// <summary>(slot, where) a monster was killed (by anything: weapons, zones, states).</summary>
        public event Action<int, Vector2> Killed;

        /// <summary>States of a monster for this tick on (<c>StatusSystem</c>): speed × factor; stunned stands still.</summary>
        public void SetModifiers(int agent, float speedFactor, bool stunned)
        {
            if ((uint)agent >= (uint)_highWater) return;
            _speedFactor[agent] = speedFactor;
            _stunned[agent] = stunned;
        }

        /// <summary>1/s: how fast a knockback fades (a push of 4 cells/s carries about 0.35 cells).</summary>
        public const float PushDamping = 11f;

        /// <summary>Strongest knockback, cells per second.</summary>
        public const float MaxPush = 12f;

        /// <summary>
        /// Knocks a monster (living, or a fresh corpse — its fling) by <paramref name="velocity"/> cells per second; pushes
        /// add up to <see cref="MaxPush"/> and fade (<see cref="PushDamping"/>). Walls stop it as a step.
        /// </summary>
        public void Push(int agent, Vector2 velocity)
        {
            if ((uint)agent >= (uint)_highWater || _states[agent] == HordeAgentState.Inactive) return;
            var push = _push[agent] + velocity;
            _push[agent] = push.sqrMagnitude > MaxPush * MaxPush ? push.normalized * MaxPush : push;
        }

        /// <summary>The hit monster neither steps nor attacks for <paramref name="seconds"/> (its attack waits).</summary>
        public void Stagger(int agent, float seconds)
        {
            if ((uint)agent >= (uint)_highWater || !IsAlive(_states[agent]) || seconds <= 0f) return;
            _staggerUntil[agent] = Mathf.Max(_staggerUntil[agent], Time + seconds);
        }

        public ReadOnlySpan<Vector2> PushVelocities => _push;

        public bool IsStaggered(int agent) => (uint)agent < (uint)_highWater && Time < _staggerUntil[agent];

        /// <summary>Takes hits and counts as present: walking, attacking or climbing out of the ground.</summary>
        public static bool IsAlive(HordeAgentState state) =>
            state == HordeAgentState.Moving || state == HordeAgentState.Attacking || state == HordeAgentState.Emerging;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (Settings == null)
                return UniTask.CompletedTask;

            var capacity = Settings.MaxCount;
            _positions = new Vector2[capacity];
            _facing = new float[capacity];
            _health = new float[capacity];
            _states = new HordeAgentState[capacity];
            _stateStart = new float[capacity];
            _attackTimer = new float[capacity];
            _attackStart = new float[capacity];
            _hitTime = new float[capacity];
            _variant = new byte[capacity];
            _kind = new byte[capacity];
            _speed = new float[capacity];
            _generation = new int[capacity];
            _speedFactor = new float[capacity];
            _stunned = new bool[capacity];
            _push = new Vector2[capacity];
            _staggerUntil = new float[capacity];
            for (var i = 0; i < capacity; i++)
            {
                _hitTime[i] = -100f;
                _speedFactor[i] = 1f;
            }

            _walkable = new bool[_grid.Width * _grid.Height];
            _walkableCells = new int[_walkable.Length];
            RefreshWalkable();
            _flow = new FlowField(_grid.Width, _grid.Height);
            _buckets = new CellBuckets(_grid.Width, _grid.Height, capacity);
            _random = new DeterministicRandom((int)StableHash.Of(_level.name + "/horde"));

            if (!_subscribed)
            {
                _doors.DoorChanged += OnDoorChanged;
                _subscribed = true;
            }

            RebuildFlow();
            SetTargetCount(Settings.StartCount);
            // The first wave appears at once, spread over the level.
            while (CountPresent() < TargetCount && SpawnOne(Settings.SpawnMinDistance * 0.5f, float.MaxValue, false)) { }
            GameLog.Info(LogChannel.Gameplay, $"Horde: {AliveCount} monster(s), capacity {capacity}.");
            return UniTask.CompletedTask;
        }

        public void SetTargetCount(int count)
        {
            if (_positions == null)
                return;
            TargetCount = Mathf.Clamp(count, 0, _positions.Length);
            // Fewer: the farthest living monsters leave at once.
            while (AliveCount > TargetCount)
            {
                var farthest = -1;
                var best = -1f;
                for (var i = 0; i < _highWater; i++)
                {
                    if (!IsAlive(_states[i])) continue;
                    var distance = (_positions[i] - _player.Position).sqrMagnitude;
                    if (distance > best)
                    {
                        best = distance;
                        farthest = i;
                    }
                }

                if (farthest < 0) break;
                _states[farthest] = HordeAgentState.Inactive;
                AliveCount--;
            }
        }

        /// <summary>Kills every monster (corpses stay) — the benchmark restarts from a fresh wave.</summary>
        public void Reset()
        {
            if (_positions == null) return;
            for (var i = 0; i < _highWater; i++)
                _states[i] = HordeAgentState.Inactive;
            AliveCount = 0;
            _highWater = 0;
            while (CountPresent() < TargetCount && SpawnOne(Settings.SpawnMinDistance * 0.5f, float.MaxValue, false)) { }
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive || deltaTime <= 0f)
                return;

            var started = Stopwatch.GetTimestamp();
            Time += deltaTime;

            if (_flowDirty || _player.Cell != _flowTarget)
            {
                var flowStarted = Stopwatch.GetTimestamp();
                RebuildFlow();
                FlowMs = Milliseconds(flowStarted);
            }

            if (Respawn)
            {
                var present = CountPresent();
                for (var n = 0; n < SpawnsPerTick && present < TargetCount; n++, present++)
                    if (!SpawnOne(Settings.SpawnMinDistance, Settings.SpawnMaxDistance, true))
                        break;
            }

            _buckets.Clear();
            for (var i = 0; i < _highWater; i++)
                if (IsAlive(_states[i]))
                    _buckets.Insert(i, CellIndex(_positions[i]));

            var playerPosition = _player.Position;
            for (var i = 0; i < _highWater; i++)
            {
                switch (_states[i])
                {
                    case HordeAgentState.Moving:
                    case HordeAgentState.Attacking:
                        if (!Frozen)
                            TickAlive(i, playerPosition, deltaTime);
                        break;
                    case HordeAgentState.Emerging:
                        if (!Frozen && Time - _stateStart[i] >= StatsOf(i).EmergeTime)
                        {
                            _states[i] = HordeAgentState.Moving;
                            _stateStart[i] = Time;
                        }

                        break;
                    case HordeAgentState.Dying:
                        if (Time - _stateStart[i] >= StatsOf(i).CorpseTime)
                            _states[i] = HordeAgentState.Inactive;
                        else
                            _positions[i] = ApplyPush(i, _positions[i], deltaTime);
                        break;
                }
            }

            while (_highWater > 0 && _states[_highWater - 1] == HordeAgentState.Inactive)
                _highWater--;

            SimulationMs = Milliseconds(started);
        }

        /// <summary>Damages a living monster; true when this hit killed it.</summary>
        public bool Damage(int agent, float amount)
        {
            if ((uint)agent >= (uint)_highWater || !IsAlive(_states[agent]) || amount <= 0f)
                return false;

            _hitTime[agent] = Time;
            _health[agent] -= amount;
            if (_health[agent] > 0f)
                return false;

            _states[agent] = HordeAgentState.Dying;
            _stateStart[agent] = Time;
            _stunned[agent] = false;
            AliveCount--;
            Kills++;
            Killed?.Invoke(agent, _positions[agent]);
            return true;
        }

        /// <summary>
        /// Nearest living monster the segment touches (bullets: previous → current position), -1 when none;
        /// <paramref name="t"/> — fraction 0..1 along the segment; <paramref name="ignore"/> is skipped (a piercing bullet
        /// inside the monster it has just hit).
        /// </summary>
        public int QuerySegment(Vector2 from, Vector2 to, out float t, int ignore = -1)
        {
            t = 1f;
            if (!IsActive) return -1;

            var reach = _maxRadius + 0.5f; // buckets are a tick old: monsters may have left their cell a little
            var minX = Mathf.FloorToInt(Mathf.Min(from.x, to.x) - reach + 0.5f);
            var maxX = Mathf.FloorToInt(Mathf.Max(from.x, to.x) + reach + 0.5f);
            var minY = Mathf.FloorToInt(Mathf.Min(from.y, to.y) - reach + 0.5f);
            var maxY = Mathf.FloorToInt(Mathf.Max(from.y, to.y) + reach + 0.5f);
            var delta = to - from;
            var best = -1;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            for (var agent = _buckets.First(x, y); agent >= 0; agent = _buckets.Next(agent))
            {
                if (agent == ignore || !IsAlive(_states[agent])) continue;
                if (SegmentCircle(from, delta, _positions[agent], _kinds[_kind[agent]].Stats.Radius, out var hit) && hit < t)
                {
                    t = hit;
                    best = agent;
                }
            }

            return best;
        }

        /// <summary>Living monsters in the cone (centres within <paramref name="range"/>, angle by cosine); count written.</summary>
        public int QueryCone(Vector2 origin, Vector2 direction, float range, float cosHalfAngle, int[] results)
        {
            if (!IsActive) return 0;
            var count = 0;
            var reach = range + 0.5f;
            var minX = Mathf.FloorToInt(origin.x - reach + 0.5f);
            var maxX = Mathf.FloorToInt(origin.x + reach + 0.5f);
            var minY = Mathf.FloorToInt(origin.y - reach + 0.5f);
            var maxY = Mathf.FloorToInt(origin.y + reach + 0.5f);
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            for (var agent = _buckets.First(x, y); agent >= 0 && count < results.Length; agent = _buckets.Next(agent))
            {
                if (!IsAlive(_states[agent])) continue;
                var offset = _positions[agent] - origin;
                var distance = offset.magnitude;
                if (distance > range + _kinds[_kind[agent]].Stats.Radius) continue;
                if (distance > 0.5f && Vector2.Dot(offset / distance, direction) < cosHalfAngle) continue;
                results[count++] = agent;
            }

            return count;
        }

        /// <summary>First living-or-not monster bucketed in the cell this tick (-1 = none); iterate with <see cref="NextInCell"/>.</summary>
        public int FirstInCell(int x, int y) => IsActive ? _buckets.First(x, y) : -1;

        public int NextInCell(int agent) => _buckets.Next(agent);

        /// <summary>Nearest living monster within <paramref name="maxDistance"/>, -1 when none.</summary>
        public int FindNearest(Vector2 position, float maxDistance)
        {
            var best = -1;
            var bestDistance = maxDistance * maxDistance;
            for (var i = 0; i < _highWater; i++)
            {
                if (!IsAlive(_states[i])) continue;
                var distance = (_positions[i] - position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        public bool IsWalkable(int x, int y) =>
            x >= 0 && x < _grid.Width && y >= 0 && y < _grid.Height && _walkable[y * _grid.Width + x];

        public void Dispose()
        {
            if (_subscribed)
            {
                _doors.DoorChanged -= OnDoorChanged;
                _subscribed = false;
            }
        }

        private void TickAlive(int i, Vector2 playerPosition, float deltaTime)
        {
            var stats = _kinds[_kind[i]].Stats;
            var position = ApplyPush(i, _positions[i], deltaTime);
            if (_stunned[i] || Time < _staggerUntil[i])
            {
                // Neither walks nor attacks (its attack waits); only the crowd and hits push it.
                var push = Separation(i, position, playerPosition, stats);
                if (push.sqrMagnitude > 1e-10f) position = MoveWithWalls(position, push, stats.Radius);
                _positions[i] = position;
                return;
            }

            var toPlayer = playerPosition - position;
            var distance = toPlayer.magnitude;
            var toPlayerDirection = distance > 1e-4f ? toPlayer / distance : Vector2.up;

            if (_states[i] == HordeAgentState.Attacking)
            {
                if (distance > stats.AttackReach * 1.25f)
                {
                    _states[i] = HordeAgentState.Moving;
                    _stateStart[i] = Time;
                }
                else
                {
                    _attackTimer[i] -= deltaTime;
                    if (_attackTimer[i] <= 0f)
                    {
                        _attackTimer[i] += stats.AttackInterval;
                        _attackStart[i] = Time;
                        PlayerHits++;
                        if (!GodMode && stats.Damage > 0)
                            _playerHealth.Damage(stats.Damage);
                    }
                }
            }
            else if (distance <= stats.AttackReach)
            {
                _states[i] = HordeAgentState.Attacking;
                _stateStart[i] = Time;
                _attackTimer[i] = stats.AttackInterval * 0.5f;
                _attackStart[i] = Time - stats.AttackInterval * 0.5f;
            }

            var desired = toPlayerDirection;
            if (_states[i] == HordeAgentState.Moving && distance > DirectChaseDistance)
            {
                var next = _flow.NextAt(CellIndex(position));
                if (next >= 0)
                {
                    var target = new Vector2(next % _grid.Width, next / _grid.Width);
                    var toNext = target - position;
                    var length = toNext.magnitude;
                    if (length > 1e-4f) desired = toNext / length;
                }
            }

            var step = _states[i] == HordeAgentState.Moving ? desired * (_speed[i] * _speedFactor[i] * deltaTime) : Vector2.zero;
            step += Separation(i, position, playerPosition, stats);
            if (step.sqrMagnitude > 1e-10f)
                position = MoveWithWalls(position, step, stats.Radius);
            _positions[i] = position;

            // Turn towards where it is going (attackers face the player).
            var targetAngle = Mathf.Atan2(desired.x, desired.y);
            var maxTurn = stats.TurnSpeed * Mathf.Deg2Rad * deltaTime;
            var difference = Mathf.DeltaAngle(_facing[i] * Mathf.Rad2Deg, targetAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            _facing[i] += Mathf.Clamp(difference, -maxTurn, maxTurn);
        }

        private Vector2 ApplyPush(int i, Vector2 position, float deltaTime)
        {
            var push = _push[i];
            if (push.sqrMagnitude < 1e-4f)
            {
                _push[i] = Vector2.zero;
                return position;
            }

            _push[i] = push * Mathf.Exp(-PushDamping * deltaTime);
            return MoveWithWalls(position, push * deltaTime, _kinds[_kind[i]].Stats.Radius);
        }

        private Vector2 Separation(int i, Vector2 position, Vector2 playerPosition, MonsterStats stats)
        {
            var push = Vector2.zero;
            var radius = stats.Radius;
            if (stats.Separation > 0f)
            {
                var cx = Mathf.FloorToInt(position.x + 0.5f);
                var cy = Mathf.FloorToInt(position.y + 0.5f);
                for (var y = cy - 1; y <= cy + 1; y++)
                for (var x = cx - 1; x <= cx + 1; x++)
                for (var other = _buckets.First(x, y); other >= 0; other = _buckets.Next(other))
                {
                    if (other == i) continue;
                    var minDistance = radius + _kinds[_kind[other]].Stats.Radius;
                    var minSquared = minDistance * minDistance;
                    var offset = position - _positions[other];
                    var squared = offset.sqrMagnitude;
                    if (squared >= minSquared) continue;
                    if (squared < 1e-8f)
                    {
                        // Exactly on top of each other: split by slot order.
                        var angle = i * 2.39996f;
                        push += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (minDistance * 0.25f);
                        continue;
                    }

                    var length = Mathf.Sqrt(squared);
                    push += offset / length * ((minDistance - length) * 0.5f * stats.Separation);
                }
            }

            // Never inside the player.
            var fromPlayer = position - playerPosition;
            var playerReach = radius + PlayerRadius;
            var playerDistance = fromPlayer.magnitude;
            if (playerDistance < playerReach && playerDistance > 1e-4f)
                push += fromPlayer / playerDistance * (playerReach - playerDistance);

            var maxPush = radius;
            return push.sqrMagnitude > maxPush * maxPush ? push.normalized * maxPush : push;
        }

        /// <summary>Moves by axis so the square footprint never enters an unwalkable cell (slides along walls).</summary>
        private Vector2 MoveWithWalls(Vector2 position, Vector2 step, float radius)
        {
            var x = new Vector2(position.x + step.x, position.y);
            if (FootprintFree(x, radius)) position = x;
            var y = new Vector2(position.x, position.y + step.y);
            if (FootprintFree(y, radius)) position = y;
            return position;
        }

        private bool FootprintFree(Vector2 center, float radius)
        {
            var r = radius * 0.9f;
            return IsWalkableAt(center.x - r, center.y - r) && IsWalkableAt(center.x + r, center.y - r) &&
                   IsWalkableAt(center.x - r, center.y + r) && IsWalkableAt(center.x + r, center.y + r);
        }

        private bool IsWalkableAt(float x, float y) => IsWalkable(Mathf.FloorToInt(x + 0.5f), Mathf.FloorToInt(y + 0.5f));

        private int CellIndex(Vector2 position)
        {
            var x = Mathf.FloorToInt(position.x + 0.5f);
            var y = Mathf.FloorToInt(position.y + 0.5f);
            return x >= 0 && x < _grid.Width && y >= 0 && y < _grid.Height ? y * _grid.Width + x : -1;
        }

        private int CountPresent()
        {
            var count = 0;
            for (var i = 0; i < _highWater; i++)
                if (IsAlive(_states[i]))
                    count++;
            return count;
        }

        /// <param name="emerge">A replacement climbs out of the ground (its kind's emerge time); the first wave stands.</param>
        private bool SpawnOne(float minDistance, float maxDistance, bool emerge)
        {
            var slot = -1;
            for (var i = 0; i < _positions.Length; i++)
            {
                if (_states[i] != HordeAgentState.Inactive) continue;
                slot = i;
                break;
            }

            if (slot < 0 || _walkableCount == 0)
                return false;

            var player = _player.Position;
            var cell = -1;
            for (var attempt = 0; attempt < 24 && cell < 0; attempt++)
            {
                var candidate = _walkableCells[_random.NextInt(_walkableCount)];
                if (_flow.DistanceAt(candidate) == FlowField.Unreachable) continue;
                var distance = (new Vector2(candidate % _grid.Width, candidate / _grid.Width) - player).magnitude;
                if (distance >= minDistance && distance <= maxDistance)
                    cell = candidate;
            }

            if (cell < 0)
            {
                // Small level: any reachable cell out of reach of the player.
                for (var attempt = 0; attempt < 24 && cell < 0; attempt++)
                {
                    var candidate = _walkableCells[_random.NextInt(_walkableCount)];
                    var distance = (new Vector2(candidate % _grid.Width, candidate / _grid.Width) - player).magnitude;
                    if (distance >= 3f) cell = candidate;
                }
            }

            if (cell < 0)
                return false;

            var kind = PickKind();
            var stats = _kinds[kind].Stats;
            var jitter = new Vector2((float)_random.NextDouble() - 0.5f, (float)_random.NextDouble() - 0.5f) * 0.6f;
            _positions[slot] = new Vector2(cell % _grid.Width, cell / _grid.Width) + jitter;
            _facing[slot] = (float)(_random.NextDouble() * Math.PI * 2.0);
            _kind[slot] = (byte)kind;
            _health[slot] = stats.MaxHp;
            _states[slot] = emerge && stats.EmergeTime > 0f ? HordeAgentState.Emerging : HordeAgentState.Moving;
            _stateStart[slot] = Time;
            _attackTimer[slot] = 0f;
            _attackStart[slot] = -100f;
            _hitTime[slot] = -100f;
            _variant[slot] = (byte)_random.NextInt(256);
            _generation[slot]++;
            _speedFactor[slot] = 1f;
            _stunned[slot] = false;
            _push[slot] = Vector2.zero;
            _staggerUntil[slot] = 0f;
            // ±10 % speed: the crowd spreads out instead of marching in step.
            _speed[slot] = stats.Speed * (0.9f + 0.2f * (float)_random.NextDouble());
            _highWater = Math.Max(_highWater, slot + 1);
            AliveCount++;
            return true;
        }

        private int PickKind()
        {
            if (_kinds.Length == 1) return 0;
            var roll = (float)_random.NextDouble() * _totalWeight;
            for (var k = 0; k < _kinds.Length; k++)
            {
                roll -= _kinds[k].Weight;
                if (roll < 0f) return k;
            }

            return _kinds.Length - 1;
        }

        private void RefreshWalkable()
        {
            _walkableCount = 0;
            for (var y = 0; y < _grid.Height; y++)
            for (var x = 0; x < _grid.Width; x++)
            {
                var index = y * _grid.Width + x;
                var walkable = _passability.IsWalkable(new GridPosition(x, y, _player.Floor));
                _walkable[index] = walkable;
                if (walkable) _walkableCells[_walkableCount++] = index;
            }
        }

        private void RebuildFlow()
        {
            _flowTarget = _player.Cell;
            _flow.Build(_flowTarget, new FloorMask(_grid.Width, _walkable, _flowTarget.Floor));
            _flowDirty = false;
            FlowBuilds++;
        }

        private void OnDoorChanged(DoorData door, bool open)
        {
            RefreshWalkable();
            _flowDirty = true;
        }

        /// <summary>Walkability of one floor (the mask is that floor's, y · width + x).</summary>
        private readonly struct FloorMask : IGridPassability
        {
            private readonly int _width;
            private readonly bool[] _mask;
            private readonly int _floor;

            public FloorMask(int width, bool[] mask, int floor)
            {
                _width = width;
                _mask = mask;
                _floor = floor;
            }

            public bool IsPassable(GridPosition position) =>
                position.Floor == _floor && position.X >= 0 && position.X < _width && position.Y >= 0 &&
                position.Y * _width + position.X < _mask.Length && _mask[position.Y * _width + position.X];
        }

        private static float Milliseconds(long started) =>
            (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);

        /// <summary>Smallest t in [0, 1] where the segment from + delta·t touches the circle; inside counts as t = 0.</summary>
        private static bool SegmentCircle(Vector2 from, Vector2 delta, Vector2 center, float radius, out float t)
        {
            var offset = from - center;
            var c = offset.sqrMagnitude - radius * radius;
            t = 0f;
            if (c <= 0f)
                return true;

            var a = delta.sqrMagnitude;
            if (a <= 1e-8f)
                return false;

            var b = Vector2.Dot(offset, delta);
            var discriminant = b * b - a * c;
            if (b > 0f || discriminant < 0f)
                return false;

            t = (-b - Mathf.Sqrt(discriminant)) / a;
            return t <= 1f;
        }
    }
}
