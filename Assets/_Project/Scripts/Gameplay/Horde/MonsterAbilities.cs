using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Effects;
using Purge.Core.Monsters;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Spatial;
using UnityEngine;

namespace Purge.Gameplay.Horde
{
    /// <summary>Where a monster is in casting an ability.</summary>
    public enum AbilityPhase : byte
    {
        None = 0,
        /// <summary>Stands, the warning shows (the aim is fixed: where the player was when it began).</summary>
        Warning = 1,
        /// <summary>It went off; the monster still stands for the ability's recovery.</summary>
        Recovery = 2,
        /// <summary>It goes on after it went off: a charge rushes (<see cref="HordeSystem.IsDashing"/>); then the recovery.</summary>
        Active = 3,
    }

    /// <summary>
    /// Abilities of the horde's monsters (stage 5c, ТЗ: «способности — общая система для монстров и боссов»): the
    /// <see cref="MonsterDefinition.Abilities"/> of each slot's kind, in flat arrays. A chasing monster casts the first
    /// ability in its list whose cooldown is over and whose conditions hold (distance, line of sight, HP; never in the
    /// first <see cref="AbilityModule.FirstDelay"/> seconds of the chase): it stands (<see cref="HordeSystem.SetCasting"/>)
    /// and the warning shows for <see cref="AbilityModule.Warning"/> s, aimed where the player was when it began; then it
    /// goes off. A stun breaks the cast (a hit does not: a machine gun must not lock a caster for good).
    /// <list type="bullet">
    /// <item><b>Projectiles</b> (<see cref="ProjectileAbility"/>) are kept here, in flat arrays (at most
    /// <see cref="MaxProjectiles"/>): straight ones fly over monsters and end at what stops bullets or at the player;
    /// arcs land in their cell. The player hit takes the damage (with its direction — the HUD's arc) and the state; the
    /// impact effects happen where it ended (<see cref="EffectSystem"/>, hostile: its zones hurt the player).</item>
    /// <item><b>Strike</b> (<see cref="StrikeAbility"/>): the player in the sector and not behind a wall.</item>
    /// <item><b>Charge</b> (<see cref="ChargeAbility"/>, stage 5d): the warning line runs as far as the body can rush
    /// (<see cref="HordeSystem.DashReach"/>); then <see cref="AbilityPhase.Active"/> — <see cref="HordeSystem.BeginDash"/>
    /// (the crowd shoved aside unhurt); the player it runs into takes the damage and is knocked along the charge (no stun),
    /// and the rush ends there.</item>
    /// <item><b>Push</b> (<see cref="PushAbility"/>, stage 5d): the player in the circle around the monster and not
    /// behind a wall takes the damage and the state and is knocked away from it.</item>
    /// <item><b>Area strike</b> (<see cref="AreaStrikeAbility"/>, stage 5e): the circle under the player; when it comes down
    /// the player still in it takes the damage and the state, the effects happen at its centre (hostile).</item>
    /// <item><b>Raise</b> (<see cref="RaiseAbility"/>, stage 5e): only with the fallen near (<see cref="HordeSystem.FindFallen"/>,
    /// chosen when the cast begins — the warning marks them); they get up where they fell (<see cref="HordeSystem.Raise"/>).</item>
    /// <item><b>Rage</b> (<see cref="RageAbility"/>, stage 5e): roars through the warning, then <see cref="HordeSystem.Enrage"/>;
    /// once per life. The damage of every attack and ability is × <see cref="HordeSystem.DamageFactorOf"/>.</item>
    /// </list>
    /// Also keeps <see cref="HordeSystem.SetSeesPlayer"/> (line of sight, <see cref="SightInterval"/>) for the ranged
    /// monsters' tactics. In <see cref="HordeSystem.GodMode"/> the player takes neither damage nor states — knocks still
    /// happen (the stand must feel a charge). Registered after
    /// <see cref="HordeSystem"/>; allocation-free per tick.
    /// </summary>
    public sealed class MonsterAbilities : ILevelLoadStep, ILevelTickable
    {
        public const int MaxAbilities = 8;
        public const int MaxProjectiles = 96;
        public const float SightInterval = 0.2f;
        public const float PlayerRadius = 0.35f;

        /// <summary>Seconds before a monster whose cast was broken may try again.</summary>
        public const float BrokenCooldown = 1f;

        private readonly HordeSystem _horde;
        private readonly CombatTargets _targets;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _health;
        private readonly PlayerStatusSystem _statuses;
        private readonly EffectSystem _effects;

        private AbilityModule[][] _kindAbilities; // per kind (null = none)
        private int[] _generation;
        private sbyte[] _cast; // ability index being cast, -1
        private AbilityPhase[] _phase;
        private float[] _phaseStart;
        private Vector2[] _origin;
        private Vector2[] _aim; // unit direction (line, fan, sector) or the landing point (arc)
        private float[] _ready; // agent · MaxAbilities + ability: game time it may be cast again
        private float[] _chaseSince; // -1 = not chasing
        private float[] _nextSight;
        private float[] _length; // a charge: how far it rushes (the warning line)
        private Vector2[] _dashFrom; // a charge: where the body was last tick
        private int[] _raise; // agent · RaiseAbility.MaxCount + n: the fallen a raise lifts (HordeSystem.FindFallen)
        private byte[] _raiseCount;
        private readonly int[] _found = new int[RaiseAbility.MaxCount];
        private byte[][] _kindPhaseNumber; // per kind and ability: the phase a PhaseAbility switches to (1, 2…), else 0
        private byte[] _healthPhase; // the phase by HP the monster is in (stage 8a)
        private float[] _tempo; // × its cooldowns (its phases)

        private int _projectileCount;
        private readonly Vector2[] _from = new Vector2[MaxProjectiles];
        private readonly Vector2[] _to = new Vector2[MaxProjectiles];
        private readonly Vector2[] _position = new Vector2[MaxProjectiles];
        private readonly Vector2[] _direction = new Vector2[MaxProjectiles];
        private readonly float[] _left = new float[MaxProjectiles];
        private readonly float[] _age = new float[MaxProjectiles];
        private readonly int[] _projectileKind = new int[MaxProjectiles];
        private readonly ProjectileAbility[] _projectile = new ProjectileAbility[MaxProjectiles];
        private readonly float[] _projectileFactor = new float[MaxProjectiles]; // × damage (the shooter's rage)
        private readonly int[] _projectileOwner = new int[MaxProjectiles]; // the shooter's slot and generation
        private readonly int[] _projectileGeneration = new int[MaxProjectiles];
        private readonly int[] _projectileSerial = new int[MaxProjectiles]; // stable while it flies (views follow it)
        private int _nextSerial;

        public MonsterAbilities(HordeSystem horde, CombatTargets targets, PlayerSystem player, PlayerHealth health,
            PlayerStatusSystem statuses = null, EffectSystem effects = null)
        {
            _horde = horde;
            _targets = targets;
            _player = player;
            _health = health;
            _statuses = statuses;
            _effects = effects;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool IsActive => _cast != null;

        /// <summary>(slot, ability index) a cast began: the warning shows.</summary>
        public event Action<int, int> CastStarted;

        /// <summary>(slot, ability index) the ability went off.</summary>
        public event Action<int, int> Released;

        /// <summary>(slot, phase) the monster went into its next phase by HP (stage 8a).</summary>
        public event Action<int, int> PhaseChanged;

        /// <summary>The phase by HP of the monster in <paramref name="agent"/> (0 — the first; stage 8a).</summary>
        public int HealthPhaseOf(int agent) => IsActive && (uint)agent < (uint)_healthPhase.Length ? _healthPhase[agent] : 0;

        /// <summary>(where, the horde kind that shot it, hit the player) a projectile ended.</summary>
        public event Action<Vector2, int, bool> ProjectileEnded;

        /// <summary>Casts that went off (tests, metrics).</summary>
        public int Casts { get; private set; }

        /// <summary>Charges that ran into the player (tests, metrics).</summary>
        public int ChargeHits { get; private set; }

        /// <summary>How far the charge of the monster in <paramref name="agent"/> rushes (its warning line), cells.</summary>
        public float ChargeLength(int agent) => IsActive && (uint)agent < (uint)_length.Length ? _length[agent] : 0f;

        /// <summary>How many of the fallen the raise of the monster in <paramref name="agent"/> lifts (its warning marks them).</summary>
        public int RaiseCount(int agent) => IsActive && (uint)agent < (uint)_raiseCount.Length ? _raiseCount[agent] : 0;

        /// <summary>Where the fallen <paramref name="n"/> of <see cref="RaiseCount"/> lies.</summary>
        public Vector2 RaisePoint(int agent, int n) => _horde.FallenPosition(_raise[agent * RaiseAbility.MaxCount + n]);

        public ReadOnlySpan<sbyte> CastIndices => _cast;
        public ReadOnlySpan<AbilityPhase> Phases => _phase;
        public ReadOnlySpan<float> PhaseStart => _phaseStart;
        public ReadOnlySpan<Vector2> Origins => _origin;
        public ReadOnlySpan<Vector2> Aims => _aim;

        /// <summary>The ability <paramref name="index"/> of the monster in <paramref name="agent"/> (null = none).</summary>
        public AbilityModule AbilityOf(int agent, int index)
        {
            if (!IsActive || (uint)agent >= (uint)_horde.Capacity || index < 0) return null;
            var abilities = _kindAbilities[_horde.Kinds[agent]];
            return abilities != null && index < abilities.Length ? abilities[index] : null;
        }

        /// <summary>The ability being cast by the monster in <paramref name="agent"/> (null = none).</summary>
        public AbilityModule CastOf(int agent) =>
            IsActive && (uint)agent < (uint)_cast.Length && _cast[agent] >= 0 ? AbilityOf(agent, _cast[agent]) : null;

        public int ProjectileCount => _projectileCount;
        public Vector2 ProjectilePosition(int index) => _position[index];
        public Vector2 ProjectileDirection(int index) => _direction[index];
        public int ProjectileKind(int index) => _projectileKind[index];

        /// <summary>Number of projectile <paramref name="index"/> that stays the same while it flies (indices shift as others end).</summary>
        public int ProjectileSerial(int index) => _projectileSerial[index];

        /// <summary>Height above the floor of an arc projectile (0 for straight ones).</summary>
        public float ProjectileHeight(int index)
        {
            var ability = _projectile[index];
            if (ability.Form != ProjectileForm.Arc) return 0f;
            var travel = (_to[index] - _from[index]).magnitude;
            var p = travel > 1e-3f ? Mathf.Clamp01(_age[index] * ability.Speed / travel) : 1f;
            return ability.ArcHeight * travel * 4f * p * (1f - p);
        }

        /// <summary>
        /// How far a straight shot from <paramref name="origin"/> flies before what stops bullets, at most
        /// <paramref name="range"/> (the warning line ends there).
        /// </summary>
        public float ShotReach(Vector2 origin, Vector2 direction, float range)
        {
            var hit = _targets.Spatial.QuerySegment(origin, origin + direction * range, _player.Floor);
            return hit.Kind == SegmentHitKind.Blocker ? range * hit.T : range;
        }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_horde.IsActive) return UniTask.CompletedTask;

            var kinds = _horde.KindList;
            _kindAbilities = new AbilityModule[kinds.Count][];
            _kindPhaseNumber = new byte[kinds.Count][];
            for (var k = 0; k < kinds.Count; k++)
            {
                var list = kinds[k].Definition != null ? kinds[k].Definition.Abilities : null;
                if (list == null || list.Count == 0) continue;
                var count = 0;
                var abilities = new AbilityModule[Mathf.Min(list.Count, MaxAbilities)];
                for (var a = 0; a < list.Count && count < abilities.Length; a++)
                    if (list[a] != null) abilities[count++] = list[a];
                if (count == 0) continue;
                if (count < abilities.Length) Array.Resize(ref abilities, count);
                _kindAbilities[k] = abilities;
                // Phases count in list order (stage 8a).
                var numbers = new byte[count];
                var phase = 0;
                for (var a = 0; a < count; a++)
                    if (abilities[a] is PhaseAbility && phase < AbilityModule.MaxPhase)
                        numbers[a] = (byte)++phase;
                _kindPhaseNumber[k] = numbers;
            }

            var capacity = _horde.Capacity;
            _generation = new int[capacity];
            _cast = new sbyte[capacity];
            _phase = new AbilityPhase[capacity];
            _phaseStart = new float[capacity];
            _origin = new Vector2[capacity];
            _aim = new Vector2[capacity];
            _ready = new float[capacity * MaxAbilities];
            _chaseSince = new float[capacity];
            _nextSight = new float[capacity];
            _length = new float[capacity];
            _dashFrom = new Vector2[capacity];
            _raise = new int[capacity * RaiseAbility.MaxCount];
            _raiseCount = new byte[capacity];
            _healthPhase = new byte[capacity];
            _tempo = new float[capacity];
            for (var i = 0; i < capacity; i++)
            {
                _generation[i] = -1;
                _cast[i] = -1;
                _chaseSince[i] = -1f;
                _tempo[i] = 1f;
            }

            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive || deltaTime <= 0f) return;
            var time = _horde.Time;
            var states = _horde.States;
            var kinds = _horde.Kinds;
            var generations = _horde.Generations;
            var stunned = _horde.Stunned;
            var high = _horde.HighWater;
            for (var i = 0; i < high; i++)
            {
                var abilities = _kindAbilities[kinds[i]];
                var ranged = abilities != null || _horde.StatsOf(i).HoldRange > 0f;
                if (!ranged) continue;

                if (_generation[i] != generations[i])
                {
                    // A new monster in the slot.
                    _generation[i] = generations[i];
                    _cast[i] = -1;
                    _phase[i] = AbilityPhase.None;
                    _chaseSince[i] = -1f;
                    _nextSight[i] = time + i % 8 * 0.025f; // spread the line-of-sight checks over ticks
                    for (var a = 0; a < MaxAbilities; a++) _ready[i * MaxAbilities + a] = 0f;
                    _healthPhase[i] = 0;
                    _tempo[i] = 1f;
                }

                var state = states[i];
                if (state != HordeAgentState.Moving && state != HordeAgentState.Attacking ||
                    _horde.Perception.AwarenessOf(i) != HordeAwareness.Chase)
                {
                    if (_cast[i] >= 0) Break(i, time);
                    _chaseSince[i] = -1f;
                    continue;
                }

                if (_chaseSince[i] < 0f) _chaseSince[i] = time;
                if (time >= _nextSight[i])
                {
                    _nextSight[i] = time + SightInterval;
                    _horde.SetSeesPlayer(i, _player.IsSpawned && _targets.IsClear(_horde.Positions[i], _player.Position, _player.Floor));
                }

                if (abilities == null) continue;
                switch (_phase[i])
                {
                    case AbilityPhase.Warning:
                        var casting = abilities[_cast[i]];
                        if (stunned[i]) Break(i, time);
                        else if (time - _phaseStart[i] >= casting.Warning) Release(i, casting, time);
                        break;
                    case AbilityPhase.Active:
                        TickCharge(i, abilities[_cast[i]] as ChargeAbility, time);
                        break;
                    case AbilityPhase.Recovery:
                        if (time - _phaseStart[i] >= abilities[_cast[i]].Recovery) Finish(i);
                        break;
                    default:
                        // In melee (attacking) only the rage may begin: a Brute at the player still roars at 30 %.
                        if (!stunned[i]) TryBegin(i, abilities, time, state == HordeAgentState.Attacking);
                        break;
                }
            }

            TickProjectiles(deltaTime);
        }

        /// <summary>Removes every projectile and breaks every cast (level stop, tests).</summary>
        public void Clear()
        {
            _projectileCount = 0;
            if (!IsActive) return;
            for (var i = 0; i < _cast.Length; i++)
                if (_cast[i] >= 0)
                    Break(i, _horde.Time);
        }

        private void TryBegin(int i, AbilityModule[] abilities, float time, bool inMelee)
        {
            if (!_player.IsSpawned) return;
            var position = _horde.Positions[i];
            var toPlayer = _player.Position - position;
            var distance = toPlayer.magnitude;
            var health = _horde.Health[i] / Mathf.Max(1f, _horde.MaxHealthOf(i));
            var phases = _kindPhaseNumber[_horde.Kinds[i]];
            var current = _healthPhase[i];
            for (var a = 0; a < abilities.Length; a++)
            {
                var ability = abilities[a];
                var phaseNumber = phases[a];
                // The roars (rage, a phase) may begin in melee: a monster at the player still turns at its threshold.
                if (inMelee && !(ability is RageAbility) && phaseNumber == 0) continue;
                if (phaseNumber > 0 ? phaseNumber != current + 1 : !ability.InPhase(current)) continue;
                if (time < _ready[i * MaxAbilities + a] || time - _chaseSince[i] < ability.FirstDelay) continue;
                if (distance < ability.MinRange || distance > ability.MaxRange) continue;
                if (health > ability.HealthBelow) continue;
                if (ability.NeedsSight && !_horde.SeesPlayer(i)) continue;
                if (ability is RageAbility && _horde.IsEnraged(i)) continue; // once per life
                var found = 0;
                if (ability is RaiseAbility raise &&
                    (found = _horde.FindFallen(position, raise.Radius, raise.MaxAge, _found, raise.Count)) == 0) continue;
                Begin(i, a, ability, position, toPlayer, distance, time);
                // A raise chooses its spots now: the warning marks them.
                for (var n = 0; n < found; n++) _raise[i * RaiseAbility.MaxCount + n] = _found[n];
                _raiseCount[i] = (byte)found;
                return;
            }
        }

        private void Begin(int i, int index, AbilityModule ability, Vector2 position, Vector2 toPlayer, float distance, float time)
        {
            var direction = distance > 1e-4f ? toPlayer / distance : Vector2.up;
            _cast[i] = (sbyte)index;
            _phase[i] = AbilityPhase.Warning;
            _phaseStart[i] = time;
            _origin[i] = position;
            _aim[i] = ability switch
            {
                ProjectileAbility { Form: ProjectileForm.Arc } => LandingPoint(position, _player.Position),
                AreaStrikeAbility => _player.Position,
                PushAbility => position,
                RaiseAbility => position,
                _ => direction,
            };
            if (ability is ChargeAbility charge)
                _length[i] = _horde.DashReach(position, direction, charge.Distance, _horde.RadiusOf(i));
            _horde.SetCasting(i, true, direction);
            CastStarted?.Invoke(i, index);
        }

        private void Release(int i, AbilityModule ability, float time)
        {
            var index = _cast[i];
            _phase[i] = AbilityPhase.Recovery;
            _phaseStart[i] = time;
            _ready[i * MaxAbilities + index] = time + ability.Cooldown * _tempo[i];
            Casts++;
            // Shoots from where it stands now (the crowd may have nudged it); the aim stays as warned.
            var position = _horde.Positions[i];
            switch (ability)
            {
                case ProjectileAbility projectile when projectile.Form == ProjectileForm.Arc:
                    Launch(projectile, i, position, Vector2.zero, _aim[i]);
                    break;
                case ProjectileAbility projectile:
                    var count = projectile.Count;
                    var radius = _horde.RadiusOf(i);
                    for (var n = 0; n < count; n++)
                    {
                        var angle = count > 1 ? Mathf.Lerp(-0.5f, 0.5f, n / (count - 1f)) * projectile.FanAngle : 0f;
                        var direction = Rotate(_aim[i], angle);
                        Launch(projectile, i, position + direction * radius, direction, Vector2.zero);
                    }

                    break;
                case StrikeAbility strike:
                    Strike(i, strike, position, _aim[i]);
                    break;
                case PushAbility push:
                    Shove(i, push, position);
                    break;
                case AreaStrikeAbility area:
                    AreaStrike(i, area, _aim[i]);
                    break;
                case RaiseAbility raise:
                    for (var n = 0; n < _raiseCount[i]; n++)
                        _horde.Raise(_raise[i * RaiseAbility.MaxCount + n], raise.RiseTime);
                    _raiseCount[i] = 0;
                    break;
                case RageAbility rage:
                    _horde.Enrage(i, rage.DamageFactor, rage.SpeedFactor);
                    break;
                case PhaseAbility phase:
                    _healthPhase[i] = _kindPhaseNumber[_horde.Kinds[i]][index];
                    _tempo[i] *= phase.CooldownFactor;
                    _horde.Strengthen(i, phase.DamageFactor, phase.SpeedFactor, phase.Rage);
                    PhaseChanged?.Invoke(i, _healthPhase[i]);
                    break;
                case ChargeAbility charge:
                    // Rushes along the warned line (from where it stands now); the recovery begins when it stops.
                    _phase[i] = AbilityPhase.Active;
                    _dashFrom[i] = position;
                    _horde.BeginDash(i, _aim[i], charge.Speed, _length[i], charge.CrowdPush);
                    break;
            }

            Released?.Invoke(i, index);
        }

        private void TickCharge(int i, ChargeAbility charge, float time)
        {
            var position = _horde.Positions[i];
            if (charge != null && _player.IsSpawned && _horde.IsDashing(i) &&
                SegmentCircle(_dashFrom[i], position - _dashFrom[i], _player.Position, _horde.RadiusOf(i) + PlayerRadius, out _))
            {
                // Ran into the player: hurt and knocked along the charge (no stun); the rush ends here.
                ChargeHits++;
                HurtPlayer(i, _horde.Generations[i], charge.Damage * _horde.DamageFactorOf(i), position, null, 0f);
                // God spares the damage, not the knock (the stand must feel it).
                if (!_health.IsDead) _player.Knock(_aim[i], charge.Knockback);
                _horde.EndDash(i);
            }

            _dashFrom[i] = position;
            if (_horde.IsDashing(i)) return;
            _phase[i] = AbilityPhase.Recovery;
            _phaseStart[i] = time;
        }

        private void Shove(int i, PushAbility push, Vector2 position)
        {
            if (!_player.IsSpawned) return;
            var offset = _player.Position - position;
            if (offset.magnitude > push.Radius + PlayerRadius) return;
            if (!_targets.IsClear(position, _player.Position, _player.Floor)) return;
            HurtPlayer(i, _horde.Generations[i], push.Damage * _horde.DamageFactorOf(i), position, push.Status, push.StatusDuration);
            if (!_health.IsDead) _player.Knock(offset.sqrMagnitude > 1e-6f ? offset : Vector2.up, push.Knockback);
        }

        private void Finish(int i)
        {
            _raiseCount[i] = 0;
            _cast[i] = -1;
            _phase[i] = AbilityPhase.None;
            _horde.SetCasting(i, false);
        }

        /// <summary>The cast is broken (stun, death, lost the player): it may try again a little later.</summary>
        private void Break(int i, float time)
        {
            var index = _cast[i];
            if (index >= 0 && _phase[i] == AbilityPhase.Warning)
            {
                var ability = AbilityOf(i, index);
                if (ability != null)
                    _ready[i * MaxAbilities + index] = time + Mathf.Min(BrokenCooldown, ability.Cooldown);
            }

            if (_phase[i] == AbilityPhase.Active) _horde.EndDash(i);
            Finish(i);
        }

        /// <summary>
        /// The blow comes down where the player stood when the cast began: the player inside and not behind a wall takes
        /// the damage and the state; the effects happen at the centre (hostile).
        /// </summary>
        private void AreaStrike(int i, AreaStrikeAbility area, Vector2 at)
        {
            var floor = _player.Floor;
            if (_targets.IsShielded(at, floor)) return; // the safe dome takes the blow
            if (_player.IsSpawned && (_player.Position - at).magnitude <= area.Radius + PlayerRadius &&
                _targets.IsClear(at, _player.Position, floor))
                HurtPlayer(i, _horde.Generations[i], area.Damage * _horde.DamageFactorOf(i), at, area.Status, area.StatusDuration);
            if (area.Impact.Count > 0) _effects?.Apply(area.Impact, at, floor, hostile: true);
        }

        private void Strike(int i, StrikeAbility strike, Vector2 position, Vector2 direction)
        {
            if (!_player.IsSpawned) return;
            var offset = _player.Position - position;
            var distance = offset.magnitude;
            if (distance > strike.Reach + PlayerRadius) return;
            if (strike.Angle < 360f && distance > PlayerRadius &&
                Vector2.Angle(direction, offset) > strike.Angle * 0.5f) return;
            if (!_targets.IsClear(position, _player.Position, _player.Floor)) return;
            HurtPlayer(i, _horde.Generations[i], strike.Damage * _horde.DamageFactorOf(i), position, strike.Status, strike.StatusDuration);
        }

        /// <summary>Where an arc aimed at <paramref name="target"/> lands: walls cut it short (as a throw).</summary>
        private Vector2 LandingPoint(Vector2 from, Vector2 target)
        {
            if (_targets.IsClear(from, target, _player.Floor)) return target;
            const int steps = 16;
            var last = from;
            for (var s = 1; s <= steps; s++)
            {
                var point = Vector2.Lerp(from, target, s / (float)steps);
                if (!_targets.IsClear(from, point, _player.Floor)) break;
                last = point;
            }

            return last;
        }

        private void Launch(ProjectileAbility ability, int agent, Vector2 origin, Vector2 direction, Vector2 target)
        {
            if (_projectileCount >= MaxProjectiles) return;
            var p = _projectileCount++;
            _projectile[p] = ability;
            _projectileKind[p] = _horde.Kinds[agent];
            _projectileFactor[p] = _horde.DamageFactorOf(agent);
            _projectileOwner[p] = agent;
            _projectileGeneration[p] = _horde.Generations[agent];
            _projectileSerial[p] = ++_nextSerial & int.MaxValue;
            _from[p] = origin;
            _position[p] = origin;
            _to[p] = target;
            _age[p] = 0f;
            if (ability.Form == ProjectileForm.Arc)
            {
                var offset = target - origin;
                _direction[p] = offset.sqrMagnitude > 1e-6f ? offset.normalized : Vector2.up;
                _left[p] = offset.magnitude;
            }
            else
            {
                _direction[p] = direction;
                _left[p] = ability.Range;
            }
        }

        private void TickProjectiles(float deltaTime)
        {
            var floor = _player.Floor;
            for (var p = _projectileCount - 1; p >= 0; p--)
            {
                var ability = _projectile[p];
                _age[p] += deltaTime;
                if (ability.Form == ProjectileForm.Arc)
                {
                    var travel = (_to[p] - _from[p]).magnitude;
                    var progress = travel > 1e-3f ? _age[p] * ability.Speed / travel : 1f;
                    if (progress < 1f)
                    {
                        _position[p] = Vector2.Lerp(_from[p], _to[p], progress);
                        continue;
                    }

                    // Lands: the player near the spot takes the damage, the effects happen there.
                    var at = _to[p];
                    _position[p] = at;
                    if (_targets.IsShielded(at, floor))
                    {
                        // Lands on the safe dome: nothing happens under it.
                        End(p, at, floor, false, impact: false);
                        continue;
                    }

                    var hit = _player.IsSpawned && (_player.Position - at).magnitude <= ability.ArcRadius + PlayerRadius;
                    if (hit) HurtPlayer(_projectileOwner[p], _projectileGeneration[p], ability.Damage * _projectileFactor[p], _from[p], ability.Status, ability.StatusDuration);
                    End(p, at, floor, hit);
                    continue;
                }

                var from = _position[p];
                var distance = Mathf.Min(ability.Speed * deltaTime, _left[p]);
                var next = from + _direction[p] * distance;
                var wall = 2f;
                var segment = _targets.Spatial.QuerySegment(from, next, floor);
                if (segment.Kind == SegmentHitKind.Blocker) wall = segment.T;
                var player = 2f;
                if (_player.IsSpawned && SegmentCircle(from, next - from, _player.Position, PlayerRadius, out var t)) player = t;

                if (player <= 1f && player <= wall)
                {
                    var at = Vector2.Lerp(from, next, player);
                    _position[p] = at;
                    HurtPlayer(_projectileOwner[p], _projectileGeneration[p], ability.Damage * _projectileFactor[p], _from[p], ability.Status, ability.StatusDuration);
                    End(p, at, floor, true);
                    continue;
                }

                if (wall <= 1f)
                {
                    // Just short of the wall: the impact is on this side.
                    var at = Vector2.Lerp(from, next, wall) - _direction[p] * 0.05f;
                    _position[p] = at;
                    End(p, at, floor, false);
                    continue;
                }

                _position[p] = next;
                _left[p] -= distance;
                if (_left[p] <= 1e-4f) End(p, next, floor, false);
            }
        }

        private void End(int p, Vector2 at, int floor, bool hitPlayer, bool impact = true)
        {
            var ability = _projectile[p];
            var kind = _projectileKind[p];
            var last = --_projectileCount;
            _from[p] = _from[last];
            _to[p] = _to[last];
            _position[p] = _position[last];
            _direction[p] = _direction[last];
            _left[p] = _left[last];
            _age[p] = _age[last];
            _projectileKind[p] = _projectileKind[last];
            _projectileFactor[p] = _projectileFactor[last];
            _projectileOwner[p] = _projectileOwner[last];
            _projectileGeneration[p] = _projectileGeneration[last];
            _projectileSerial[p] = _projectileSerial[last];
            _projectile[p] = _projectile[last];
            _projectile[last] = null;
            if (impact && ability.Impact.Count > 0) _effects?.Apply(ability.Impact, at, floor, hostile: true);
            ProjectileEnded?.Invoke(at, kind, hitPlayer);
        }

        /// <summary>
        /// False when nothing happens to the player (<see cref="HordeSystem.GodMode"/>, dead). The hit is reported as the
        /// monster's in <paramref name="agent"/> (<paramref name="generation"/>: a dead shooter's projectile is no one's).
        /// </summary>
        private bool HurtPlayer(int agent, int generation, float damage, Vector2 source, StatusDefinition status, float duration)
        {
            if (_horde.GodMode || _health.IsDead) return false;
            var amount = Mathf.RoundToInt(damage);
            if (amount > 0) _health.Damage(amount, source);
            if (status != null) _statuses?.Apply(status, duration);
            if (amount > 0) _horde.ReportPlayerHit(agent, generation, amount);
            return true;
        }

        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            var radians = -degrees * Mathf.Deg2Rad; // clockwise from above, as the horde's facing
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            return new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);
        }

        /// <summary>Smallest t in [0, 1] where the segment from + delta·t touches the circle; inside counts as t = 0.</summary>
        private static bool SegmentCircle(Vector2 from, Vector2 delta, Vector2 center, float radius, out float t)
        {
            var offset = from - center;
            var c = offset.sqrMagnitude - radius * radius;
            t = 0f;
            if (c <= 0f) return true;
            var a = delta.sqrMagnitude;
            if (a <= 1e-8f) return false;
            var b = Vector2.Dot(offset, delta);
            var discriminant = b * b - a * c;
            if (b > 0f || discriminant < 0f) return false;
            t = (-b - Mathf.Sqrt(discriminant)) / a;
            return t <= 1f;
        }
    }
}
