using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Navigation;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Visibility;
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
        /// <summary>
        /// Climbs out of the ground (<see cref="MonsterStats.EmergeTime"/>, or a raise's time — <see cref="HordeSystem.EmergeTimeOf"/>):
        /// can be hit, neither walks nor attacks.
        /// </summary>
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
            Doors = definition != null ? definition.DoorUse : MonsterDoorUse.Never;
            Mass = MonsterDefinition.MassOf(definition != null ? definition.Rank : MonsterRank.Mass, stats);
            InverseMass = float.IsPositiveInfinity(Mass) ? 0f : 1f / Mass;
        }

        /// <summary>Body mass (rank resolved; infinity = immovable).</summary>
        public float Mass { get; }

        /// <summary>1 / <see cref="Mass"/>; 0 = immovable.</summary>
        public float InverseMass { get; }

        /// <summary>× knockbacks, blasts and shoves: 1 up to mass 1, less for the heavy, 0 for the immovable.</summary>
        public float KnockScale => Mathf.Min(1f, InverseMass);

        public MonsterDefinition Definition { get; }
        public MonsterStats Stats { get; }
        public float Weight { get; }

        /// <summary>What it does with a closed door (rank resolved).</summary>
        public MonsterDoorUse Doors { get; }
    }

    /// <summary>
    /// Mass monsters in flat arrays (ТЗ: rank-and-file are not MonoBehaviours): positions, facing, HP, state, timers
    /// and the kind (<see cref="HordeKind"/>: definition and stats — <see cref="StressTestDefinition.Monsters"/> by
    /// weight, or the built-in monster) per slot. The stress stand lives on the player's floor (walkability, flow field and buckets of that floor);
    /// monsters of stage 5 keep a floor per slot. Everyone walks the shared <see cref="FlowField"/> to the player (rebuilt only when the player changes
    /// cell or a door changes), keeps spacing through <see cref="CellBuckets"/> (no physics), slides along walls and
    /// attacks in reach. Who chases and who wanders or follows a sound — <see cref="Perception"/> (stage 5b): unaware monsters
    /// walk their group's or the sound's field instead. Views read the arrays; nothing here knows about rendering.
    /// <para><b>Doors</b> (stage 5d, <see cref="HordeKind.Doors"/>): a closed door is a wall for the rank-and-file; those
    /// that open or break doors chase along a second field (<see cref="DoorFlow"/>, built only while such kinds and closed
    /// doors they may pass exist) and, at the door, open it at once or beat it down blow by blow
    /// (<see cref="DoorSystem.HitDoor"/>). Locked and frozen doors stop everyone.</para>
    /// <para><b>Dash</b> (<see cref="BeginDash"/>, the charge of <c>MonsterAbilities</c>): rushes straight on, shoving the
    /// crowd aside (<see cref="CrowdImpulse"/>), until its distance is run, a wall or a closed door stops it, or a stun.</para>
    /// Allocation-free per tick.
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
        private readonly SoundEventBus _sounds;
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
        private HordePace[] _pace;
        private bool[] _casting; // an ability is being cast (MonsterAbilities): stands, faces _castFace
        private Vector2[] _castFace;
        private bool[] _sees; // nothing that stops bullets between it and the player (MonsterAbilities, a few times a second)
        private float[] _cornerUntil; // a retreating monster with its back to a wall fights in melee until then
        private float[] _retreatStuck;
        private Vector2[] _dashDirection; // unit
        private float[] _dashSpeed;
        private float[] _dashLeft; // cells still to rush; 0 = not dashing
        private float[] _dashPush;
        private int[] _doorAt; // cell of the door it is beating down, -1
        private float[] _doorTimer;
        private float[] _emergeTime; // how long this one climbs out (its kind's, or a raise's)
        private float[] _damageFactor; // × its damage (rage)
        private bool[] _enraged;
        private byte[] _affix; // champion's affix: index in Champions.Affixes + 1; 0 = not a champion
        private float[] _maxHealth;
        private float[] _attackRate; // × how often it attacks (champions)

        // The fallen (stage 5e, the Shaman's raise): a ring of recent deaths of the rank-and-file and officers — where,
        // which kind, when. Bodies are not kept for it: a corpse still disappears after its CorpseTime.
        private readonly Vector2[] _fallenAt = new Vector2[FallenMemory];
        private readonly byte[] _fallenKind = new byte[FallenMemory];
        private readonly float[] _fallenTime = new float[FallenMemory];
        private readonly bool[] _fallenUsed = new bool[FallenMemory];
        private int _fallenNext;

        private bool[] _walkable;
        private bool[] _doorWalkable; // + closed doors monsters may get through (only with door users)
        private int[] _walkableCells;
        private int _walkableCount;
        private bool _doorUsers;
        private int _passableDoors; // closed doors in _doorWalkable
        private FlowField _flow;
        private FlowField _doorFlow;
        private CellBuckets _buckets;
        private DeterministicRandom _random;
        private GridPosition _flowTarget;
        private bool _flowDirty = true;
        private bool _subscribed;
        private int _highWater;

        public HordeSystem(LevelData level, LevelGrid grid, LevelPassability passability, DoorSystem doors,
            PlayerSystem player, PlayerHealth playerHealth, SoundEventBus sounds = null, VisibilitySystem visibility = null)
        {
            _level = level;
            _grid = grid;
            _passability = passability;
            _doors = doors;
            _player = player;
            _playerHealth = playerHealth;
            _sounds = sounds;
            Settings = level.StressTest;
            _kinds = CreateKinds(Settings);
            Perception = new HordePerception(this, grid, doors, sounds, visibility, player);
            foreach (var kind in _kinds)
            {
                _maxRadius = Mathf.Max(_maxRadius, kind.Stats.Radius);
                _totalWeight += kind.Weight;
                if (kind.Doors == MonsterDoorUse.Open || kind.Doors == MonsterDoorUse.Break) _doorUsers = true;
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
            // Weight 0: never in the waves, only when asked (SpawnKind — the sandbox's officers).
            foreach (var monster in settings.OnDemandMonsters)
                if (monster != null && kinds.Count < byte.MaxValue && kinds.FindIndex(kind => kind.Definition == monster) < 0)
                    kinds.Add(new HordeKind(monster, monster.Stats, 0f));
            return kinds.ToArray();
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public StressTestDefinition Settings { get; }

        /// <summary>Who knows about the player (stage 5b).</summary>
        public HordePerception Perception { get; }

        internal string LevelName => _level.name;
        public bool IsActive => Settings != null && _positions != null;

        /// <summary>Kinds of monsters this horde has (slots refer to them by <see cref="Kinds"/>).</summary>
        public System.Collections.Generic.IReadOnlyList<HordeKind> KindList => _kinds;

        public HordeKind KindOf(int agent) => _kinds[_kind != null && (uint)agent < (uint)_kind.Length ? _kind[agent] : 0];

        public MonsterStats StatsOf(int agent) => KindOf(agent).Stats;

        /// <summary>The monster's definition (null = the stress test's built-in monster).</summary>
        public MonsterDefinition DefinitionOf(int agent) => KindOf(agent).Definition;

        public float RadiusOf(int agent) => StatsOf(agent).Radius;

        /// <summary>Index of <paramref name="monster"/> in <see cref="KindList"/>, -1 when the horde has no such kind.</summary>
        public int KindIndexOf(MonsterDefinition monster)
        {
            for (var k = 0; k < _kinds.Length; k++)
                if (_kinds[k].Definition == monster)
                    return k;
            return -1;
        }

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

        /// <summary>The field of those that open or break doors (closed doors they may pass count as floor); null when not needed.</summary>
        public FlowField DoorFlow => UsesDoorFlow ? _doorFlow : null;

        private bool UsesDoorFlow => _doorUsers && _passableDoors > 0;

        /// <summary>Doors opened by monsters (tests, metrics).</summary>
        public int DoorsOpened { get; private set; }

        /// <summary>Blows monsters dealt to doors (tests, metrics).</summary>
        public int DoorBlows { get; private set; }

        /// <summary>Gameplay noise of a monster opening a door / of a blow on a door, cells.</summary>
        public const float DoorOpenNoise = 5f;
        public const float DoorBlowNoise = 9f;

        /// <summary>A monster acts on a closed door this close to its body, cells.</summary>
        private const float DoorReach = 0.75f;

        /// <summary>How many recent deaths the horde remembers for raising (the oldest are forgotten first).</summary>
        public const int FallenMemory = 128;

        /// <summary>Monsters raised from the fallen (tests, metrics).</summary>
        public int Raised { get; private set; }

        /// <summary>
        /// Up to <paramref name="max"/> (and <c>results.Length</c>) of the fallen within <paramref name="radius"/> of
        /// <paramref name="center"/> that died at most <paramref name="maxAge"/> s ago and were not raised yet, the nearest
        /// first; their indices written, the count returned. Only the rank-and-file and officers are remembered.
        /// </summary>
        public int FindFallen(Vector2 center, float radius, float maxAge, int[] results, int max = int.MaxValue)
        {
            if (!IsActive || results == null) return 0;
            var limit = Mathf.Min(max, results.Length);
            if (limit <= 0) return 0;
            var count = 0;
            var radiusSquared = radius * radius;
            for (var f = 0; f < FallenMemory; f++)
            {
                if (!IsFallen(f, maxAge)) continue;
                var distance = (_fallenAt[f] - center).sqrMagnitude;
                if (distance > radiusSquared) continue;
                // Insertion by distance into the short list.
                var at = count < limit ? count++ : limit;
                while (at > 0 && (_fallenAt[results[at - 1]] - center).sqrMagnitude > distance)
                {
                    if (at < limit) results[at] = results[at - 1];
                    at--;
                }

                if (at < limit) results[at] = f;
            }

            return count;
        }

        /// <summary>Where the fallen <paramref name="index"/> (of <see cref="FindFallen"/>) lies.</summary>
        public Vector2 FallenPosition(int index) => _fallenAt[(uint)index % FallenMemory];

        /// <summary>The fallen <paramref name="index"/> is still there to be raised (not raised, not forgotten, not too old).</summary>
        public bool IsFallen(int index, float maxAge) =>
            (uint)index < FallenMemory && _fallenUsed[index] && Time - _fallenTime[index] <= maxAge;

        /// <summary>
        /// Raises the fallen <paramref name="index"/>: the same kind gets up where it fell, climbing out for
        /// <paramref name="riseTime"/> s, and chases the player at once (on top of the target count). Its slot, or -1 (no
        /// free slot, the spot is not walkable any more, already raised).
        /// </summary>
        public int Raise(int index, float riseTime)
        {
            if (!IsActive || (uint)index >= FallenMemory || !_fallenUsed[index]) return -1;
            _fallenUsed[index] = false;
            var position = _fallenAt[index];
            var cell = CellIndex(position);
            if (cell < 0 || !_walkable[cell]) return -1;
            var slot = Place(cell, true, MonsterPerception.Hunter, position, _fallenKind[index], riseTime);
            if (slot >= 0) Raised++;
            return slot;
        }

        private void RememberFallen(int agent)
        {
            var definition = _kinds[_kind[agent]].Definition;
            if (definition != null && definition.Rank != MonsterRank.Mass && definition.Rank != MonsterRank.Officer) return;
            var f = _fallenNext;
            _fallenNext = (_fallenNext + 1) % FallenMemory;
            _fallenAt[f] = _positions[agent];
            _fallenKind[f] = _kind[agent];
            _fallenTime[f] = Time;
            _fallenUsed[f] = true;
        }

        /// <summary>
        /// Rage (<c>RageAbility</c>): from now on the monster deals × <paramref name="damageFactor"/> damage and moves
        /// × <paramref name="speedFactor"/>, until it dies. Once per life.
        /// </summary>
        public void Enrage(int agent, float damageFactor, float speedFactor)
        {
            if ((uint)agent >= (uint)_highWater || !IsAlive(_states[agent]) || _enraged[agent]) return;
            _enraged[agent] = true;
            _damageFactor[agent] = Mathf.Max(0f, damageFactor);
            _speed[agent] *= Mathf.Max(0.1f, speedFactor);
        }

        public bool IsEnraged(int agent) => (uint)agent < (uint)_highWater && _enraged[agent];

        /// <summary>× the damage the monster deals (rage; 1 = as its data says).</summary>
        public float DamageFactorOf(int agent) => (uint)agent < (uint)_highWater ? _damageFactor[agent] : 1f;

        /// <summary>Seconds the monster in <paramref name="agent"/> climbs out of the ground (its kind's, or a raise's).</summary>
        public float EmergeTimeOf(int agent) => (uint)agent < (uint)_highWater ? _emergeTime[agent] : 0f;

        /// <summary>Champions of this horde (stage 5e2; null without settings).</summary>
        public ChampionRules Champions => Settings?.Champions;

        /// <summary>Champions made so far (tests, metrics).</summary>
        public int ChampionsMade { get; private set; }

        /// <summary>Index into <see cref="ChampionRules.Affixes"/> + 1 per slot; 0 = not a champion.</summary>
        public ReadOnlySpan<byte> Affixes => _affix;

        /// <summary>The champion's affix of the monster in <paramref name="agent"/> (null = not a champion).</summary>
        public ChampionAffixDefinition AffixOf(int agent)
        {
            if (_affix == null || (uint)agent >= (uint)_affix.Length || _affix[agent] == 0) return null;
            var affixes = Champions.Affixes;
            var index = _affix[agent] - 1;
            return index < affixes.Length ? affixes[index] : null;
        }

        public bool IsChampion(int agent) => AffixOf(agent) != null;

        /// <summary>Full HP of the monster in <paramref name="agent"/> (its kind's; × <see cref="ChampionRules.HealthFactor"/> for a champion).</summary>
        public float MaxHealthOf(int agent) =>
            _maxHealth != null && (uint)agent < (uint)_maxHealth.Length ? _maxHealth[agent] : StatsOf(agent).MaxHp;

        /// <summary>Seconds between its attacks (its kind's; a quick champion's shorter).</summary>
        public float AttackIntervalOf(int agent)
        {
            var interval = StatsOf(agent).AttackInterval;
            return _attackRate != null && (uint)agent < (uint)_attackRate.Length ? interval / _attackRate[agent] : interval;
        }

        /// <summary>
        /// The new monster in <paramref name="agent"/> becomes a champion with affix <paramref name="affix"/> (index into
        /// <see cref="ChampionRules.Affixes"/>): × HP, the affix's speed and attack rate. Only the rank-and-file and
        /// officers, once. False when it cannot be one.
        /// </summary>
        public bool MakeChampion(int agent, int affix)
        {
            var rules = Champions;
            if (rules == null || (uint)agent >= (uint)_highWater || !IsAlive(_states[agent]) || _affix[agent] != 0) return false;
            if ((uint)affix >= (uint)rules.Affixes.Length || rules.Affixes[affix] == null || affix >= byte.MaxValue) return false;
            var definition = _kinds[_kind[agent]].Definition;
            if (definition != null && !ChampionRules.CanBe(definition.Rank)) return false;
            var data = rules.Affixes[affix];
            _affix[agent] = (byte)(affix + 1);
            _maxHealth[agent] *= rules.HealthFactor;
            _health[agent] = _maxHealth[agent];
            _speed[agent] *= data.SpeedFactor;
            _attackRate[agent] = Mathf.Max(0.1f, data.AttackRateFactor);
            ChampionsMade++;
            return true;
        }

        /// <summary>A monster of the waves is a champion now and then (<see cref="ChampionRules.Chance"/>), its affix evenly.</summary>
        private void RollChampion(int slot)
        {
            var rules = Champions;
            if (rules == null || rules.Chance <= 0f || rules.Affixes.Length == 0) return;
            if (_random.NextDouble() >= rules.Chance) return;
            MakeChampion(slot, _random.NextInt(rules.Affixes.Length));
        }

        /// <summary>Heals a living monster by <paramref name="amount"/>, up to its full HP (a vampire champion).</summary>
        public void Heal(int agent, float amount)
        {
            if ((uint)agent >= (uint)_highWater || !IsAlive(_states[agent]) || amount <= 0f) return;
            _health[agent] = Mathf.Min(_maxHealth[agent], _health[agent] + amount);
        }

        /// <summary>The monster's affix spares it this zone (its own trail).</summary>
        public bool IgnoresZone(int agent, ZoneDefinition zone)
        {
            var trail = AffixOf(agent)?.Trail;
            return trail != null && zone != null && trail.Zone == zone;
        }

        /// <summary>(slot, damage) a monster's attack or ability hurt the player (vampire, a hit's state).</summary>
        public event Action<int, int> PlayerHurt;

        /// <summary>
        /// The monster in <paramref name="agent"/> hurt the player by <paramref name="amount"/> — reported only while that
        /// monster (<paramref name="generation"/>) still lives there (a projectile of a dead shooter heals no one).
        /// </summary>
        public void ReportPlayerHit(int agent, int generation, int amount)
        {
            if ((uint)agent >= (uint)_highWater || _generation[agent] != generation || !IsAlive(_states[agent])) return;
            PlayerHurt?.Invoke(agent, amount);
        }

        /// <summary>
        /// × damage of a hit travelling along <paramref name="hitDirection"/> (zero = no direction) — the monster's front
        /// armor (<see cref="MonsterStats.FrontArmor"/>, stage 5e).
        /// </summary>
        public float ArmorFactor(int agent, Vector2 hitDirection)
        {
            if ((uint)agent >= (uint)_highWater) return 1f;
            var factor = _kinds[_kind[agent]].Stats.ArmorFactor(_facing[agent], hitDirection);
            // A champion's armor all round (stage 5e2): hits with a direction only.
            var affix = AffixOf(agent);
            if (affix != null && hitDirection.sqrMagnitude > 1e-8f) factor *= affix.DamageTaken;
            return factor;
        }

        /// <summary>
        /// The monster rushes along <paramref name="direction"/> at <paramref name="speed"/> cells per second for up to
        /// <paramref name="distance"/> cells (stage 5d, the charge): it does nothing else meanwhile, shoves the crowd aside
        /// (<paramref name="crowdPush"/> cells per second, <see cref="CrowdImpulse"/>) and stops at a wall or a closed door,
        /// when stunned or by <see cref="EndDash"/>.
        /// </summary>
        public void BeginDash(int agent, Vector2 direction, float speed, float distance, float crowdPush)
        {
            if ((uint)agent >= (uint)_highWater || !IsAlive(_states[agent]) || direction.sqrMagnitude < 1e-8f) return;
            _dashDirection[agent] = direction.normalized;
            _dashSpeed[agent] = speed;
            _dashLeft[agent] = distance;
            _dashPush[agent] = crowdPush;
            _facing[agent] = Mathf.Atan2(direction.x, direction.y);
        }

        public void EndDash(int agent)
        {
            if ((uint)agent < (uint)_highWater) _dashLeft[agent] = 0f;
        }

        public bool IsDashing(int agent) => (uint)agent < (uint)_highWater && _dashLeft[agent] > 0f;

        /// <summary>
        /// How far a body of <paramref name="radius"/> can rush from <paramref name="origin"/> along
        /// <paramref name="direction"/> before a wall or a closed door, at most <paramref name="distance"/> (the charge's
        /// warning line ends there).
        /// </summary>
        public float DashReach(Vector2 origin, Vector2 direction, float distance, float radius)
        {
            if (!IsActive || direction.sqrMagnitude < 1e-8f) return 0f;
            const float step = 0.2f;
            direction = direction.normalized;
            var reach = 0f;
            while (reach < distance)
            {
                var next = Mathf.Min(distance, reach + step);
                if (!FootprintFree(origin + direction * next, radius)) break;
                reach = next;
            }

            return reach;
        }

        /// <summary>A living monster's body overlaps <paramref name="cell"/> (a door is not closed on it).</summary>
        public bool AnyBodyInCell(GridPosition cell)
        {
            if (!IsActive) return false;
            for (var y = cell.Y - 1; y <= cell.Y + 1; y++)
            for (var x = cell.X - 1; x <= cell.X + 1; x++)
            for (var agent = _buckets.First(x, y); agent >= 0; agent = _buckets.Next(agent))
            {
                if (!IsAlive(_states[agent])) continue;
                var reach = 0.5f + _kinds[_kind[agent]].Stats.Radius * 0.9f;
                var position = _positions[agent];
                if (Mathf.Abs(position.x - cell.X) < reach && Mathf.Abs(position.y - cell.Y) < reach) return true;
            }

            return false;
        }

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

        /// <summary>How each monster moved this tick (idle / walk / run look).</summary>
        public ReadOnlySpan<HordePace> Paces => _pace;

        /// <summary>An ability is being cast (the monster stands and faces its aim).</summary>
        public ReadOnlySpan<bool> Casting => _casting;

        /// <summary>
        /// The monster casts an ability (<c>MonsterAbilities</c>): it stands still (only the crowd and hits push it) and
        /// turns to <paramref name="face"/> (unit) until released.
        /// </summary>
        public void SetCasting(int agent, bool casting, Vector2 face = default)
        {
            if ((uint)agent >= (uint)_highWater) return;
            _casting[agent] = casting;
            if (face.sqrMagnitude > 1e-8f) _castFace[agent] = face.normalized;
        }

        /// <summary>Whether the monster sees the player (nothing that stops bullets between them), kept by <c>MonsterAbilities</c>.</summary>
        public void SetSeesPlayer(int agent, bool sees)
        {
            if ((uint)agent < (uint)_highWater) _sees[agent] = sees;
        }

        public bool SeesPlayer(int agent) => (uint)agent < (uint)_highWater && _sees[agent];

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
        /// Knocks a monster (living, or a fresh corpse — its fling) by <paramref name="velocity"/> cells per second, × its
        /// <see cref="HordeKind.KnockScale"/> (the heavy fly less, the immovable not at all; stage 5d) — weapons, blasts,
        /// <see cref="CrowdImpulse"/>; pushes add up to <see cref="MaxPush"/> and fade (<see cref="PushDamping"/>). Walls
        /// stop it as a step.
        /// </summary>
        public void Push(int agent, Vector2 velocity)
        {
            if ((uint)agent >= (uint)_highWater || _states[agent] == HordeAgentState.Inactive) return;
            var scale = _kinds[_kind[agent]].KnockScale;
            if (scale <= 0f) return;
            var push = _push[agent] + velocity * scale;
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

            var capacity = Settings.Capacity;
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
            _pace = new HordePace[capacity];
            _casting = new bool[capacity];
            _castFace = new Vector2[capacity];
            _sees = new bool[capacity];
            _cornerUntil = new float[capacity];
            _retreatStuck = new float[capacity];
            _dashDirection = new Vector2[capacity];
            _dashSpeed = new float[capacity];
            _dashLeft = new float[capacity];
            _dashPush = new float[capacity];
            _doorAt = new int[capacity];
            _doorTimer = new float[capacity];
            _emergeTime = new float[capacity];
            _damageFactor = new float[capacity];
            _enraged = new bool[capacity];
            _affix = new byte[capacity];
            _maxHealth = new float[capacity];
            _attackRate = new float[capacity];
            for (var i = 0; i < capacity; i++)
            {
                _hitTime[i] = -100f;
                _speedFactor[i] = 1f;
                _doorAt[i] = -1;
                _damageFactor[i] = 1f;
                _attackRate[i] = 1f;
            }

            _walkable = new bool[_grid.Width * _grid.Height];
            _walkableCells = new int[_walkable.Length];
            if (_doorUsers)
            {
                _doorWalkable = new bool[_walkable.Length];
                _doorFlow = new FlowField(_grid.Width, _grid.Height);
            }

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
            Perception.Initialize(capacity, Settings);
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
            TargetCount = Mathf.Clamp(count, 0, Mathf.Min(Settings.MaxCount, _positions.Length));
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
            Array.Clear(_fallenUsed, 0, FallenMemory);
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

            if (!Frozen) Perception.Tick(Time);

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
                        if (!Frozen && Time - _stateStart[i] >= _emergeTime[i])
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

            if (!Frozen) ResolvePlayerContacts();

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
            {
                Perception.OnHit(agent, Time);
                return false;
            }

            _states[agent] = HordeAgentState.Dying;
            _stateStart[agent] = Time;
            _stunned[agent] = false;
            _dashLeft[agent] = 0f;
            AliveCount--;
            Kills++;
            RememberFallen(agent);
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

            Perception.Dispose();
        }

        private void TickAlive(int i, Vector2 playerPosition, float deltaTime)
        {
            var stats = _kinds[_kind[i]].Stats;
            var position = ApplyPush(i, _positions[i], deltaTime);
            if (_stunned[i]) _dashLeft[i] = 0f;
            if (_dashLeft[i] > 0f)
            {
                TickDash(i, position, stats, deltaTime);
                return;
            }

            if (_stunned[i] || Time < _staggerUntil[i])
            {
                // Neither walks nor attacks (its attack waits); only the crowd and hits push it.
                var push = Separation(i, position, playerPosition, stats);
                if (push.sqrMagnitude > 1e-10f) position = MoveWithWalls(position, push, stats.Radius);
                _positions[i] = position;
                _pace[i] = HordePace.Still;
                return;
            }

            if (_casting[i])
            {
                // Casts: stands and faces its aim; the plain attack waits.
                if (_states[i] == HordeAgentState.Attacking)
                {
                    _states[i] = HordeAgentState.Moving;
                    _stateStart[i] = Time;
                }

                var push = Separation(i, position, playerPosition, stats);
                if (push.sqrMagnitude > 1e-10f) position = MoveWithWalls(position, push, stats.Radius);
                _positions[i] = position;
                _pace[i] = HordePace.Still;
                Turn(i, _castFace[i], stats, deltaTime);
                return;
            }

            var awareness = Perception.AwarenessOf(i);
            if (awareness != HordeAwareness.Chase)
            {
                TickUnaware(i, position, playerPosition, stats, awareness, deltaTime);
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
                        _attackTimer[i] += AttackIntervalOf(i);
                        _attackStart[i] = Time;
                        PlayerHits++;
                        if (!GodMode && stats.Damage > 0 && !_playerHealth.IsDead)
                        {
                            var amount = Mathf.RoundToInt(stats.Damage * _damageFactor[i]);
                            _playerHealth.Damage(amount);
                            ReportPlayerHit(i, _generation[i], amount);
                        }
                    }
                }
            }
            else if (distance <= stats.AttackReach)
            {
                _states[i] = HordeAgentState.Attacking;
                _stateStart[i] = Time;
                _attackTimer[i] = AttackIntervalOf(i) * 0.5f;
                _attackStart[i] = Time - AttackIntervalOf(i) * 0.5f;
            }

            var doors = _kinds[_kind[i]].Doors;
            var flow = UsesDoorFlow && (doors == MonsterDoorUse.Open || doors == MonsterDoorUse.Break) ? _doorFlow : _flow;
            if (flow == _doorFlow && _states[i] == HordeAgentState.Moving &&
                TickDoor(i, position, playerPosition, stats, doors, deltaTime))
                return;
            _doorAt[i] = -1;

            if (_states[i] == HordeAgentState.Moving && distance > stats.AttackReach &&
                (stats.HoldRange > 0f || stats.RetreatRange > 0f) &&
                TickRanged(i, position, playerPosition, toPlayerDirection, distance, stats, deltaTime))
                return;

            var desired = toPlayerDirection;
            if (_states[i] == HordeAgentState.Moving && distance > DirectChaseDistance)
            {
                var next = flow.NextAt(CellIndex(position));
                if (next >= 0)
                {
                    var target = new Vector2(next % _grid.Width, next / _grid.Width);
                    var toNext = target - position;
                    var length = toNext.magnitude;
                    if (length > 1e-4f) desired = toNext / length;
                }
            }

            var moving = _states[i] == HordeAgentState.Moving;
            _pace[i] = moving ? HordePace.Run : HordePace.Still;
            var step = moving ? desired * (_speed[i] * _speedFactor[i] * deltaTime) : Vector2.zero;
            step += Separation(i, position, playerPosition, stats);
            if (step.sqrMagnitude > 1e-10f)
                position = MoveWithWalls(position, step, stats.Radius);
            _positions[i] = position;

            // Turn towards where it is going (attackers face the player).
            Turn(i, desired, stats, deltaTime);
        }

        /// <summary>
        /// A ranged monster in the chase (stats <see cref="MonsterStats.HoldRange"/> / <see cref="MonsterStats.RetreatRange"/>):
        /// steps back from a player that is too close (a back to the wall — <see cref="CornerTime"/> s of plain chase and
        /// melee), stands where it sees the player within the hold range. False — chases as usual.
        /// </summary>
        private bool TickRanged(int i, Vector2 position, Vector2 playerPosition, Vector2 toPlayer, float distance, MonsterStats stats,
            float deltaTime)
        {
            if (Time < _cornerUntil[i]) return false;
            if (stats.RetreatRange > 0f && distance < stats.RetreatRange)
            {
                var away = -toPlayer;
                var want = _speed[i] * _speedFactor[i] * deltaTime;
                var moved = MoveWithWalls(position, away * want, stats.Radius);
                // Barely moves away: cornered.
                _retreatStuck[i] = (moved - position).magnitude < want * 0.3f ? _retreatStuck[i] + deltaTime : 0f;
                if (_retreatStuck[i] >= StuckTime)
                {
                    _retreatStuck[i] = 0f;
                    _cornerUntil[i] = Time + CornerTime;
                    return false;
                }

                var push = Separation(i, moved, playerPosition, stats);
                if (push.sqrMagnitude > 1e-10f) moved = MoveWithWalls(moved, push, stats.Radius);
                _positions[i] = moved;
                _pace[i] = HordePace.Run;
                Turn(i, away, stats, deltaTime);
                return true;
            }

            _retreatStuck[i] = 0f;
            if (stats.HoldRange <= 0f || distance > stats.HoldRange || !_sees[i]) return false;
            // In range and in sight: stands and aims.
            var separation = Separation(i, position, playerPosition, stats);
            if (separation.sqrMagnitude > 1e-10f) position = MoveWithWalls(position, separation, stats.Radius);
            _positions[i] = position;
            _pace[i] = HordePace.Still;
            Turn(i, toPlayer, stats, deltaTime);
            return true;
        }

        /// <summary>Rushes on (the charge): shoves the crowd aside, a wall or a closed door ends it.</summary>
        private void TickDash(int i, Vector2 position, MonsterStats stats, float deltaTime)
        {
            var direction = _dashDirection[i];
            var want = Mathf.Min(_dashSpeed[i] * _speedFactor[i] * deltaTime, _dashLeft[i]);
            var moved = MoveWithWalls(position, direction * want, stats.Radius);
            CrowdImpulse.Shove(this, position, moved, stats.Radius, _dashPush[i], i);
            _dashLeft[i] -= want;
            // Barely moved on: something that stops a step (a wall, a closed door) is in the way.
            if (Vector2.Dot(moved - position, direction) < want * 0.5f) _dashLeft[i] = 0f;
            _positions[i] = moved;
            _pace[i] = HordePace.Run;
            _facing[i] = Mathf.Atan2(direction.x, direction.y);
        }

        /// <summary>
        /// A monster that opens or breaks doors, next to the closed door its field leads through: opens it at once (and
        /// walks on — false), or stands and beats it down, a blow per attack interval (true: this tick is spent).
        /// </summary>
        private bool TickDoor(int i, Vector2 position, Vector2 playerPosition, MonsterStats stats, MonsterDoorUse use,
            float deltaTime)
        {
            var cell = CellIndex(position);
            var next = cell >= 0 ? _doorFlow.NextAt(cell) : -1;
            if (next < 0 || _walkable[next]) return false; // the way on is floor: not at a closed door
            var at = new Vector2(next % _grid.Width, next / _grid.Width);
            var toDoor = at - position;
            if (toDoor.magnitude > stats.Radius + DoorReach) return false;
            var doorCell = new GridPosition(next % _grid.Width, next / _grid.Width, _player.Floor);
            if (!_doors.TryGetDoor(doorCell, out var door) || _doors.IsOpen(door.Id)) return false;

            if (use == MonsterDoorUse.Open)
            {
                DoorsOpened++;
                _doors.SetOpen(door.Id, true);
                _sounds?.Emit(SoundType.Door, at, DoorOpenNoise, doorCell.Floor);
                return false;
            }

            if (_doorAt[i] != next)
            {
                // Came up to it: the first blow after half an interval (as a plain attack).
                _doorAt[i] = next;
                _doorTimer[i] = AttackIntervalOf(i) * 0.5f;
            }

            _doorTimer[i] -= deltaTime;
            if (_doorTimer[i] <= 0f)
            {
                _doorTimer[i] += AttackIntervalOf(i);
                _attackStart[i] = Time;
                DoorBlows++;
                _sounds?.Emit(SoundType.Door, at, DoorBlowNoise, doorCell.Floor);
                if (_doors.HitDoor(door.Id, stats.DoorDamage, position)) _doorAt[i] = -1;
            }

            var push = Separation(i, position, playerPosition, stats);
            if (push.sqrMagnitude > 1e-10f) position = MoveWithWalls(position, push, stats.Radius);
            _positions[i] = position;
            _pace[i] = HordePace.Still;
            if (toDoor.sqrMagnitude > 1e-8f) Turn(i, toDoor.normalized, stats, deltaTime);
            return true;
        }

        /// <summary>Seconds a retreat may barely move before the monster counts as cornered.</summary>
        private const float StuckTime = 0.4f;

        /// <summary>Seconds a cornered ranged monster chases and fights in melee before it tries to step back again.</summary>
        public const float CornerTime = 2f;

        /// <summary>Does not know about the player (or roars, having just noticed them): never attacks.</summary>
        private void TickUnaware(int i, Vector2 position, Vector2 playerPosition, MonsterStats stats, HordeAwareness awareness,
            float deltaTime)
        {
            if (_states[i] == HordeAgentState.Attacking)
            {
                _states[i] = HordeAgentState.Moving;
                _stateStart[i] = Time;
            }

            Vector2 desired;
            HordePace pace;
            if (awareness == HordeAwareness.Alert)
            {
                // Stands and roars at the player.
                var toPlayer = playerPosition - position;
                desired = toPlayer.sqrMagnitude > 1e-8f ? toPlayer.normalized : Vector2.zero;
                pace = HordePace.Still;
            }
            else
            {
                Perception.Steer(i, position, Time, out desired, out pace);
            }

            var speed = pace == HordePace.Walk ? stats.WanderSpeed : pace == HordePace.Run ? _speed[i] : 0f;
            _pace[i] = pace;
            var step = desired * (speed * _speedFactor[i] * deltaTime) + Separation(i, position, playerPosition, stats);
            if (step.sqrMagnitude > 1e-10f)
                position = MoveWithWalls(position, step, stats.Radius);
            _positions[i] = position;
            if (desired.sqrMagnitude > 0f) Turn(i, desired, stats, deltaTime);
        }

        private void Turn(int i, Vector2 desired, MonsterStats stats, float deltaTime)
        {
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
            var ownInverse = _kinds[_kind[i]].InverseMass;
            if (stats.Separation > 0f && ownInverse > 0f)
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

                    // The lighter gives way more (equal masses — half each, as before; the immovable never).
                    var inverse = _kinds[_kind[other]].InverseMass;
                    var sum = ownInverse + inverse;
                    var share = sum > 0f ? ownInverse / sum : 0f;
                    var length = Mathf.Sqrt(squared);
                    push += offset / length * ((minDistance - length) * share * stats.Separation);
                }
            }

            // The player's body — ResolvePlayerContacts, after everyone has moved.
            var maxPush = radius;
            return push.sqrMagnitude > maxPush * maxPush ? push.normalized * maxPush : push;
        }

        /// <summary>
        /// Bodies of living monsters overlapping the player's (stage 5d): the overlap is shared by mass — the monster steps
        /// back by its share (walls may stop it), the player is moved by the rest (<see cref="PlayerSystem.Displace"/>).
        /// Rank-and-file give way almost entirely, a Brute hardly, the immovable not at all (the player slides round it).
        /// A crowd does not stack: the player moves at most as far as the deepest single overlap demands.
        /// </summary>
        private void ResolvePlayerContacts()
        {
            if (!_player.IsSpawned) return;
            var center = _player.Position;
            var playerInverse = 1f / _player.Definition.Mass;
            var reach = PlayerRadius + _maxRadius;
            var minX = Mathf.FloorToInt(center.x - reach + 0.5f);
            var maxX = Mathf.FloorToInt(center.x + reach + 0.5f);
            var minY = Mathf.FloorToInt(center.y - reach + 0.5f);
            var maxY = Mathf.FloorToInt(center.y + reach + 0.5f);
            var playerMove = Vector2.zero;
            var deepest = 0f;
            for (var y = minY - 1; y <= maxY + 1; y++)
            for (var x = minX - 1; x <= maxX + 1; x++)
            for (var agent = _buckets.First(x, y); agent >= 0; agent = _buckets.Next(agent))
            {
                if (!IsAlive(_states[agent])) continue;
                var kind = _kinds[_kind[agent]];
                var position = _positions[agent];
                var offset = position - center;
                var touch = kind.Stats.Radius + PlayerRadius;
                var squared = offset.sqrMagnitude;
                if (squared >= touch * touch) continue;
                var distance = Mathf.Sqrt(squared);
                // Dead centre: apart along the monster's facing.
                var away = distance > 1e-4f ? offset / distance : new Vector2(Mathf.Sin(_facing[agent]), Mathf.Cos(_facing[agent]));
                var overlap = touch - distance;
                var share = kind.InverseMass / (kind.InverseMass + playerInverse);
                var stepped = 0f;
                if (share > 0f)
                {
                    var moved = MoveWithWalls(position, away * (overlap * share), kind.Stats.Radius);
                    stepped = Mathf.Max(0f, Vector2.Dot(moved - position, away));
                    _positions[agent] = moved;
                }

                var rest = overlap - stepped;
                if (rest <= 1e-5f) continue;
                playerMove -= away * rest;
                deepest = Mathf.Max(deepest, rest);
            }

            if (deepest <= 0f) return;
            if (playerMove.sqrMagnitude > deepest * deepest) playerMove = playerMove.normalized * deepest;
            _player.Displace(playerMove);
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

        /// <summary>
        /// Sandbox: <paramref name="count"/> monsters around <paramref name="center"/> (within 3 cells, walkable and
        /// reachable), all with <paramref name="perception"/> whatever their kind says — the first of them
        /// <paramref name="leader"/> instead when given (a caller among wanderers). They come on top of the target count
        /// (replacements wait until it is short again). Returns how many appeared.
        /// </summary>
        public int SpawnGroup(Vector2 center, int count, MonsterPerception perception, MonsterPerception? leader = null)
        {
            if (!IsActive) return 0;
            var spawned = 0;
            for (var attempt = 0; attempt < count * 12 && spawned < count; attempt++)
            {
                var offset = new Vector2((float)_random.NextDouble() - 0.5f, (float)_random.NextDouble() - 0.5f) * 6f;
                var cell = CellIndex(center + offset);
                if (cell < 0 || !_walkable[cell] || _flow.DistanceAt(cell) == FlowField.Unreachable) continue;
                if (Place(cell, false, spawned == 0 && leader.HasValue ? leader.Value : perception) < 0) break;
                spawned++;
            }

            return spawned;
        }

        /// <summary>
        /// Sandbox and tests: one monster exactly at <paramref name="position"/> (of <paramref name="kind"/>, -1 = by
        /// weight); its slot, or -1 (no free slot, not walkable).
        /// </summary>
        public int SpawnAt(Vector2 position, MonsterPerception perception, int kind = -1)
        {
            if (!IsActive) return -1;
            var cell = CellIndex(position);
            return cell >= 0 && _walkable[cell] ? Place(cell, false, perception, position, kind) : -1;
        }

        /// <summary>
        /// Sandbox: a monster of <paramref name="kind"/> (<see cref="KindList"/>) on a reachable cell 2–3 cells around
        /// <paramref name="center"/>, with its own perception, on top of the target count. Its slot, or -1.
        /// </summary>
        public int SpawnKind(Vector2 center, int kind)
        {
            if (!IsActive || (uint)kind >= (uint)_kinds.Length) return -1;
            var definition = _kinds[kind].Definition;
            var perception = definition != null ? definition.Perception : MonsterPerception.Hunter;
            for (var attempt = 0; attempt < 24; attempt++)
            {
                var angle = (float)(_random.NextDouble() * Math.PI * 2.0);
                var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (2f + (float)_random.NextDouble());
                var cell = CellIndex(center + offset);
                if (cell < 0 || !_walkable[cell] || _flow.DistanceAt(cell) == FlowField.Unreachable) continue;
                return Place(cell, false, perception, null, kind);
            }

            return -1;
        }

        /// <summary>A random walkable cell the player can be reached from (index y · width + x), -1 when none found.</summary>
        public int RandomReachableCell(DeterministicRandom random)
        {
            for (var attempt = 0; attempt < 32 && _walkableCount > 0; attempt++)
            {
                var candidate = _walkableCells[random.NextInt(_walkableCount)];
                if (_flow.DistanceAt(candidate) != FlowField.Unreachable) return candidate;
            }

            return -1;
        }

        /// <summary>Builds <paramref name="field"/> towards <paramref name="target"/> over the horde's walkability.</summary>
        internal void BuildFlow(FlowField field, GridPosition target) =>
            field.Build(target, new FloorMask(_grid.Width, _walkable, target.Floor));

        /// <param name="emerge">A replacement climbs out of the ground (its kind's emerge time); the first wave stands.</param>
        private bool SpawnOne(float minDistance, float maxDistance, bool emerge)
        {
            if (FreeSlot() < 0 || _walkableCount == 0)
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

            var slot = Place(cell, emerge, null);
            if (slot < 0) return false;
            RollChampion(slot);
            return true;
        }

        private int FreeSlot()
        {
            for (var i = 0; i < _positions.Length; i++)
                if (_states[i] == HordeAgentState.Inactive)
                    return i;
            return -1;
        }

        /// <param name="perception">Overrides the kind's (sandbox groups).</param>
        /// <param name="emergeTime">How long it climbs out when <paramref name="emerge"/> (negative = its kind's).</param>
        private int Place(int cell, bool emerge, MonsterPerception? perception, Vector2? exact = null, int forcedKind = -1,
            float emergeTime = -1f)
        {
            var slot = FreeSlot();
            if (slot < 0) return -1;

            var kind = forcedKind >= 0 && forcedKind < _kinds.Length ? forcedKind : PickKind();
            var stats = _kinds[kind].Stats;
            var jitter = new Vector2((float)_random.NextDouble() - 0.5f, (float)_random.NextDouble() - 0.5f) * 0.6f;
            _positions[slot] = exact ?? new Vector2(cell % _grid.Width, cell / _grid.Width) + jitter;
            _facing[slot] = (float)(_random.NextDouble() * Math.PI * 2.0);
            _kind[slot] = (byte)kind;
            _health[slot] = stats.MaxHp;
            _maxHealth[slot] = stats.MaxHp;
            _affix[slot] = 0;
            _attackRate[slot] = 1f;
            _emergeTime[slot] = emerge ? emergeTime >= 0f ? emergeTime : stats.EmergeTime : 0f;
            _states[slot] = _emergeTime[slot] > 0f ? HordeAgentState.Emerging : HordeAgentState.Moving;
            _damageFactor[slot] = 1f;
            _enraged[slot] = false;
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
            _pace[slot] = HordePace.Still;
            _casting[slot] = false;
            _sees[slot] = false;
            _cornerUntil[slot] = 0f;
            _retreatStuck[slot] = 0f;
            _dashLeft[slot] = 0f;
            _doorAt[slot] = -1;
            _highWater = Math.Max(_highWater, slot + 1);
            AliveCount++;
            var definition = _kinds[kind].Definition;
            Perception.OnSpawn(slot, perception ?? (definition != null ? definition.Perception : MonsterPerception.Hunter), Time);
            return slot;
        }

        private int PickKind()
        {
            if (_kinds.Length == 1) return 0;
            var roll = (float)_random.NextDouble() * _totalWeight;
            var last = 0;
            for (var k = 0; k < _kinds.Length; k++)
            {
                if (_kinds[k].Weight <= 0f) continue; // only on demand
                last = k;
                roll -= _kinds[k].Weight;
                if (roll < 0f) return k;
            }

            return last;
        }

        private void RefreshWalkable()
        {
            _walkableCount = 0;
            _passableDoors = 0;
            for (var y = 0; y < _grid.Height; y++)
            for (var x = 0; x < _grid.Width; x++)
            {
                var index = y * _grid.Width + x;
                var cell = new GridPosition(x, y, _player.Floor);
                var walkable = _passability.IsWalkable(cell);
                _walkable[index] = walkable;
                if (walkable) _walkableCells[_walkableCount++] = index;
                if (_doorWalkable == null) continue;
                // A closed door those that open or break doors may get through.
                var door = !walkable && _grid.GetCell(cell) == CellType.Door && _doors.TryGetDoor(cell, out var data) &&
                           _doors.IsPassableForMonsters(data.Id);
                _doorWalkable[index] = walkable || door;
                if (door) _passableDoors++;
            }
        }

        private void RebuildFlow()
        {
            _flowTarget = _player.Cell;
            _flow.Build(_flowTarget, new FloorMask(_grid.Width, _walkable, _flowTarget.Floor));
            if (UsesDoorFlow) _doorFlow.Build(_flowTarget, new FloorMask(_grid.Width, _doorWalkable, _flowTarget.Floor));
            _flowDirty = false;
            FlowBuilds++;
        }

        private void OnDoorChanged(DoorData door, bool open)
        {
            RefreshWalkable();
            _flowDirty = true;
            Perception.OnDoorChanged();
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
