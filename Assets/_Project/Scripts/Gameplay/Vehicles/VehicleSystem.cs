using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Vehicles;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Weapons;
using UnityEngine;

namespace Purge.Gameplay.Vehicles
{
    /// <summary>A vehicle stands (nobody in it), the player drives it, or it has blown up (gone).</summary>
    public enum VehicleState
    {
        Parked = 0,
        Driven = 1,
        Destroyed = 2,
    }

    /// <summary>
    /// A vehicle of the level (stage 9a): its body on the grid, its speed and HP. Parked, it is a wall for everyone and
    /// stops shots (decision 2026-10-28); driven, it carries the player. At 0 HP it is broken down (stage 9b): it smokes,
    /// the engine is dead, and in <see cref="BreakLeft"/> seconds it blows up.
    /// </summary>
    public sealed class Vehicle : IShotBlocker
    {
        internal Vehicle(VehicleData data, VehicleDefinition definition)
        {
            Data = data;
            Definition = definition;
            Position = new Vector2(data.Position.X, data.Position.Y);
            Heading = data.Heading * Mathf.Deg2Rad;
            Health = definition.Hull.Health;
        }

        public VehicleData Data { get; }
        public VehicleDefinition Definition { get; }
        public string Id => Data.Id;
        public int Floor => Data.Position.Floor;

        /// <summary>Centre of the body in grid units.</summary>
        public Vector2 Position { get; internal set; }

        /// <summary>Radians clockwise from North (<see cref="VehicleBody.Forward"/>).</summary>
        public float Heading { get; internal set; }

        public Vector2 Forward => VehicleBody.Forward(Heading);

        /// <summary>Cells per second along the front; negative — backwards.</summary>
        public float Speed { get; internal set; }

        /// <summary>|speed| of the top speed, 0..1 (camera, engine sound).</summary>
        public float SpeedShare => Mathf.Clamp01(Mathf.Abs(Speed) / Mathf.Max(0.01f, Definition.Engine.MaxSpeed));

        /// <summary>The wheel this tick, -1 (left) … 1 (right).</summary>
        public float Steer { get; internal set; }

        /// <summary>The throttle is held this tick.</summary>
        public bool Throttle { get; internal set; }

        public float Health { get; internal set; }

        /// <summary>HP of the hull's full HP, 0..1 (the HUD's bar).</summary>
        public float HealthShare => Mathf.Clamp01(Health / Mathf.Max(1f, Definition.Hull.Health));

        public VehicleState State { get; internal set; }

        /// <summary>0 HP: it smokes, the engine is dead, it blows up in <see cref="BreakLeft"/> seconds.</summary>
        public bool Broken { get; internal set; }

        public float BreakLeft { get; internal set; }

        /// <summary>The body touches a wall this tick.</summary>
        public bool InContact { get; internal set; }

        /// <summary>It slides along a wall this tick (scrapes).</summary>
        public bool IsScraping { get; internal set; }

        public float HalfLength => Definition.Hull.Length * 0.5f;
        public float HalfWidth => Definition.Hull.Width * 0.5f;

        internal readonly List<GridPosition> BlockedCells = new List<GridPosition>();

        public bool Crosses(Vector2 from, Vector2 to, int floor, out float t)
        {
            t = 0f;
            return floor == Floor && State == VehicleState.Parked &&
                   VehicleBody.Segment(from, to, Position, Heading, HalfLength, HalfWidth, out t);
        }
    }

    /// <summary>
    /// The level's vehicles (ТЗ 9а «Езда»): each <see cref="VehicleData"/> is a <see cref="Vehicle"/> standing where the
    /// level puts it. A parked one blocks the cells under its body for the monsters (<see cref="CellBlockers"/>, monsters
    /// there are put out), the player and a driven vehicle meet its exact rectangle (<see cref="IPlayerBodies"/>), and it
    /// stops shots (<see cref="SpatialQueryService.AddShotBlocker"/>). Use next to its body gets the player in
    /// (<see cref="Enter"/>; no shooting, throwing or weapon switching inside), Use again gets them out beside it.
    /// <para>Driving: throttle and brake/reverse (<see cref="PlayerAction.Gas"/>, <see cref="PlayerAction.Brake"/>), the
    /// wheel — the stick's x. The body is a rectangle moved along its heading without physics: a wall stops it, or, met
    /// at under 45°, it slides along; a hit faster than the hull's safe speed damages it. It turns only while moving.</para>
    /// <para>Ramming (stage 9b, <see cref="RamModule"/>): monsters its body sweeps over at speed — rank-and-file and
    /// officers take mass × speed (rank-and-file die outright from half the top speed), fly aside and brake it by their
    /// mass; elite and bosses throw it back, a little hurt and stunned. Each one hit wears the hull; monsters reach for
    /// the body and hit it (<see cref="HordeSystem.Shell"/>), and the player's damage goes to it
    /// (<see cref="PlayerHealth.Shield"/>). At 0 HP (<see cref="WreckModule"/>) it smokes, then throws the player out
    /// (monsters around pushed off, a second unhurt), blows up as the player's own effects and is gone.</para>
    /// Must be registered right after <see cref="PlayerSystem"/>: it carries the player (<see cref="PlayerSystem.Ride"/>).
    /// </summary>
    public sealed class VehicleSystem : ILevelLoadStep, ILevelTickable, IPlayerBlockers, IPlayerBodies, IPlayerShield, IPlayerShell, IDisposable
    {
        /// <summary>Cells from the player to the body within which Use gets in.</summary>
        public const float EnterReach = 1f;

        /// <summary>A parked body blocks for the monsters the cells it overlaps deeper than this (a corner touching a cell leaves it free).</summary>
        public const float BlockMargin = VehicleFootprint.BlockMargin;

        /// <summary>A wall met at under this angle is slid along; head-on it stops the vehicle.</summary>
        public const float SlideAngle = 45f;

        private const float SteerDeadZone = 0.15f;
        private const float MaxSubstep = 0.2f;

        /// <summary>The turn widens with speed: cells of radius added per cell per second.</summary>
        private const float TurnWidening = 0.2f;

        /// <summary>Seconds a rammed monster is not rammed again (it is pushed aside meanwhile).</summary>
        public const float RamCooldown = 0.5f;

        private static readonly float SlideSine = Mathf.Sin(SlideAngle * Mathf.Deg2Rad);

        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly IPlayerInput _input;
        private readonly LevelPassability _passability;
        private readonly CellBlockers _blockers;
        private readonly SpatialQueryService _spatial;
        private readonly WeaponSystem _weapons;
        private readonly HordeSystem _horde;
        private readonly CombatTargets _targets;
        private readonly StatusSystem _statuses;
        private readonly EffectSystem _effects;
        private readonly PlayerHealth _health;
        private readonly LevelLaunchOptions _options;
        private readonly List<Vehicle> _vehicles = new List<Vehicle>();
        private readonly List<string> _gone = new List<string>();
        private int _floor;
        private float _time;
        private float _shieldLeft;
        private float[] _rammedAt;
        private int[] _rammedGeneration;
        private int _tickHits;
        private int _tickKills;

        public VehicleSystem(LevelData level, PlayerSystem player, IPlayerInput input, LevelPassability passability,
            CellBlockers blockers, SpatialQueryService spatial, WeaponSystem weapons = null, HordeSystem horde = null,
            CombatTargets targets = null, StatusSystem statuses = null, EffectSystem effects = null, PlayerHealth health = null,
            LevelLaunchOptions options = null)
        {
            _options = options;
            _level = level;
            _player = player;
            _input = input;
            _passability = passability;
            _blockers = blockers;
            _spatial = spatial;
            _weapons = weapons;
            _horde = horde;
            _targets = targets;
            _statuses = statuses;
            _effects = effects;
            _health = health;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        public IReadOnlyList<Vehicle> Vehicles => _vehicles;

        /// <summary>Ids of the vehicles a continued run left out (blown up before).</summary>
        public IReadOnlyList<string> GoneIds => _gone;

        /// <summary>The vehicle the player drives; null on foot.</summary>
        public Vehicle Current { get; private set; }

        /// <summary>(vehicle) the player got in.</summary>
        public event Action<Vehicle> Entered;

        /// <summary>(vehicle) the player got out; it stands where it stopped.</summary>
        public event Action<Vehicle> Exited;

        /// <summary>(vehicle, speed into the wall, cells per second) it hit a wall (once per contact).</summary>
        public event Action<Vehicle, float> WallHit;

        /// <summary>(vehicle, monsters rammed, of them killed) once per tick it rammed any.</summary>
        public event Action<Vehicle, int, int> Rammed;

        /// <summary>(vehicle, where the monster stands) it bounced off an elite monster or a boss.</summary>
        public event Action<Vehicle, Vector2> Bounced;

        /// <summary>(vehicle) its HP ran out: it smokes, the engine is dead.</summary>
        public event Action<Vehicle> BrokeDown;

        /// <summary>(vehicle) the broken-down vehicle threw the player out (just before it blows up).</summary>
        public event Action<Vehicle> Ejected;

        /// <summary>(vehicle) it blew up and is gone.</summary>
        public event Action<Vehicle> Exploded;

        /// <summary>Monsters rammed and killed by ramming (all vehicles, the level).</summary>
        public int RamHits { get; private set; }

        public int RamKills { get; private set; }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var resume = _options?.Resume;
            foreach (var data in _level.Vehicles)
            {
                if (data.Definition == null)
                {
                    GameLog.Warning(LogChannel.Gameplay, $"Vehicle '{data.Id}' has no definition: skipped.");
                    continue;
                }

                // A continued run (stage 9в): blown up — gone; damaged — at its place with the HP it had.
                if (resume != null && resume.DestroyedVehicles.Contains(data.Id))
                {
                    _gone.Add(data.Id);
                    continue;
                }

                var vehicle = new Vehicle(data, data.Definition);
                if (resume != null && resume.VehicleHealth.TryGetValue(data.Id, out var health))
                    vehicle.Health = Mathf.Clamp(health, 1f, data.Definition.Hull.Health);
                _vehicles.Add(vehicle);
                Park(vehicle);
            }

            if (_vehicles.Count > 0)
            {
                GameLog.Info(LogChannel.Gameplay, $"Vehicles: {_vehicles.Count}.");
                if (_health != null) _health.Shield = this;
                _player.Bodies = this;
            }

            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            foreach (var vehicle in _vehicles)
                _spatial.RemoveShotBlocker(vehicle);
            _vehicles.Clear();
            _gone.Clear();
            if (_health != null && _health.Shield == this) _health.Shield = null;
            if (_horde != null && _horde.Shell == this) _horde.Shell = null;
            if (_player.Bodies == this) _player.Bodies = null;
            Current = null;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || _vehicles.Count == 0) return;
            _time += deltaTime;
            if (_shieldLeft > 0f) _shieldLeft -= deltaTime;
            var vehicle = Current;
            if (vehicle != null)
            {
                Drive(vehicle, deltaTime);
                _player.Ride(vehicle.Position, vehicle.Forward, deltaTime);
            }

            for (var i = 0; i < _vehicles.Count; i++)
            {
                var wreck = _vehicles[i];
                if (!wreck.Broken || wreck.State == VehicleState.Destroyed) continue;
                wreck.BreakLeft -= deltaTime;
                if (wreck.BreakLeft <= 0f) Explode(wreck);
            }
        }

        /// <summary>A parked vehicle within <see cref="EnterReach"/> of the player (the nearest); null when none or driving.</summary>
        public Vehicle FindEnterable()
        {
            if (!_player.IsSpawned || _player.InVehicle || _player.Down) return null;
            Vehicle best = null;
            var bestDistance = EnterReach;
            foreach (var vehicle in _vehicles)
            {
                if (vehicle.State != VehicleState.Parked || vehicle.Broken || vehicle.Floor != _player.Floor) continue;
                var distance = VehicleBody.Distance(_player.Position, vehicle.Position, vehicle.Heading, vehicle.HalfLength,
                    vehicle.HalfWidth);
                if (distance > bestDistance) continue;
                best = vehicle;
                bestDistance = distance;
            }

            return best;
        }

        /// <summary>The player gets into <paramref name="vehicle"/> (parked, within reach — <see cref="FindEnterable"/>).</summary>
        public bool Enter(Vehicle vehicle)
        {
            if (vehicle == null || vehicle.State != VehicleState.Parked || vehicle.Broken || Current != null ||
                _player.InVehicle) return false;
            Unpark(vehicle);
            vehicle.State = VehicleState.Driven;
            vehicle.Speed = 0f;
            Current = vehicle;
            _floor = vehicle.Floor;
            _player.EnterVehicle(vehicle.Position, vehicle.Forward);
            if (_weapons != null) _weapons.InputLocked = true;
            if (_horde != null) _horde.Shell = this;
            Entered?.Invoke(vehicle);
            return true;
        }

        /// <summary>
        /// The player gets out beside the vehicle (the left side first, then the right, behind, in front); it stops and
        /// stands there. False when there is no room for the player around it.
        /// </summary>
        public bool TryExit()
        {
            var vehicle = Current;
            if (vehicle == null) return false;
            Park(vehicle);
            if (!TryFindExit(vehicle, out var spot))
            {
                Unpark(vehicle);
                return false;
            }

            vehicle.State = VehicleState.Parked;
            vehicle.Speed = 0f;
            vehicle.Steer = 0f;
            vehicle.Throttle = false;
            vehicle.InContact = vehicle.IsScraping = false;
            LeaveVehicle(spot);
            Exited?.Invoke(vehicle);
            return true;
        }

        bool IPlayerBlockers.IsWalkable(GridPosition cell) => _passability.IsWalkable(cell, bodies: false);

        bool IPlayerBlockers.CanEnter(GridPosition cell) => true;

        int IPlayerBlockers.Floor => _floor;

        bool IPlayerBodies.Overlaps(Vector2 center, float halfSize, int floor)
        {
            foreach (var vehicle in _vehicles)
                if (vehicle.State == VehicleState.Parked && vehicle.Floor == floor &&
                    VehicleBody.OverlapsSquare(vehicle.Position, vehicle.Forward, vehicle.HalfLength, vehicle.HalfWidth, center,
                        halfSize))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ Damage

        /// <summary>The player's damage: the driven vehicle takes it; the player thrown out of a wreck takes none for a while.</summary>
        bool IPlayerShield.Absorb(int amount)
        {
            if (_shieldLeft > 0f) return true;
            var vehicle = Current;
            if (vehicle == null) return false;
            Hurt(vehicle, amount);
            return true;
        }

        float IPlayerShell.Reach
        {
            get
            {
                var vehicle = Current;
                return vehicle == null ? 0f : Mathf.Sqrt(vehicle.HalfLength * vehicle.HalfLength + vehicle.HalfWidth * vehicle.HalfWidth);
            }
        }

        float IPlayerShell.DistanceTo(Vector2 point)
        {
            var vehicle = Current;
            return vehicle == null
                ? float.MaxValue
                : VehicleBody.Distance(point, vehicle.Position, vehicle.Heading, vehicle.HalfLength, vehicle.HalfWidth);
        }

        bool IPlayerShell.PushOut(Vector2 point, float radius, out Vector2 step)
        {
            var vehicle = Current;
            step = Vector2.zero;
            return vehicle != null &&
                   VehicleBody.PushOut(point, radius, vehicle.Position, vehicle.Heading, vehicle.HalfLength, vehicle.HalfWidth, out step);
        }

        /// <summary>Damage to the hull; at 0 HP it breaks down (a broken one takes no more).</summary>
        public void Hurt(Vehicle vehicle, float amount)
        {
            if (vehicle == null || vehicle.Broken || vehicle.State == VehicleState.Destroyed || amount <= 0f) return;
            vehicle.Health = Mathf.Max(0f, vehicle.Health - amount);
            if (vehicle.Health > 0f) return;
            vehicle.Broken = true;
            vehicle.BreakLeft = vehicle.Definition.Wreck.BreakTime;
            vehicle.Throttle = false;
            BrokeDown?.Invoke(vehicle); // it blows up in the tick (never in the middle of a move)
        }

        /// <summary>
        /// The wreck blows up: the player inside is thrown out beside it first (monsters around pushed off, a while
        /// unhurt); its effects are the player's (they hurt monsters, not the player); then it is gone.
        /// </summary>
        private void Explode(Vehicle vehicle)
        {
            var wreck = vehicle.Definition.Wreck;
            if (Current == vehicle)
            {
                // The parked body shows where the player fits beside it.
                Park(vehicle);
                if (!TryFindExit(vehicle, out var spot)) spot = vehicle.Position;
                Unpark(vehicle);
                LeaveVehicle(spot);
                _shieldLeft = wreck.Invulnerability;
                if (_horde != null && _horde.IsActive && wreck.PushRadius > 0f) PushAway(spot, wreck.PushRadius, wreck.PushSpeed);
                Ejected?.Invoke(vehicle);
            }
            else
            {
                Unpark(vehicle);
            }

            vehicle.State = VehicleState.Destroyed;
            vehicle.Speed = 0f;
            vehicle.Steer = 0f;
            vehicle.InContact = vehicle.IsScraping = false;
            _effects?.Apply(wreck.Effects, vehicle.Position, vehicle.Floor, hostile: false);
            Exploded?.Invoke(vehicle);
        }

        private void LeaveVehicle(Vector2 spot)
        {
            Current = null;
            if (_horde != null && _horde.Shell == this) _horde.Shell = null;
            _player.ExitVehicle(spot);
            if (_weapons != null) _weapons.InputLocked = false;
        }

        /// <summary>Living monsters within <paramref name="radius"/> of <paramref name="center"/> are pushed straight away.</summary>
        private void PushAway(Vector2 center, float radius, float speed)
        {
            var reach = radius + _horde.MaxRadius;
            var positions = _horde.Positions;
            var states = _horde.States;
            for (var y = Mathf.FloorToInt(center.y - reach + 0.5f); y <= Mathf.FloorToInt(center.y + reach + 0.5f); y++)
            for (var x = Mathf.FloorToInt(center.x - reach + 0.5f); x <= Mathf.FloorToInt(center.x + reach + 0.5f); x++)
            for (var agent = _horde.FirstInCell(x, y); agent >= 0; agent = _horde.NextInCell(agent))
            {
                if (!HordeSystem.IsAlive(states[agent])) continue;
                var offset = positions[agent] - center;
                var distance = offset.magnitude;
                if (distance > radius + _horde.RadiusOf(agent)) continue;
                var away = distance > 1e-4f ? offset / distance : new Vector2((agent & 1) == 0 ? 1f : -1f, 0f);
                _horde.Push(agent, away * speed);
            }
        }

        // ------------------------------------------------------------------ Driving

        private void Drive(Vehicle vehicle, float deltaTime)
        {
            var engine = vehicle.Definition.Engine;
            var active = !_player.Down;
            var gas = active && !vehicle.Broken && _input.IsHeld(PlayerAction.Gas); // a wreck's engine is dead
            var brake = active && _input.IsHeld(PlayerAction.Brake);
            var steer = active ? Mathf.Clamp(_input.Move.x, -1f, 1f) : 0f;
            if (Mathf.Abs(steer) < SteerDeadZone) steer = 0f;
            vehicle.Throttle = gas && !brake;
            vehicle.Steer = steer;

            var speed = vehicle.Speed;
            if (gas && !brake)
                speed = speed < 0f
                    ? Mathf.MoveTowards(speed, 0f, engine.Braking * deltaTime)
                    : Mathf.MoveTowards(speed, engine.MaxSpeed, engine.Acceleration * deltaTime);
            else if (brake && !gas)
                speed = speed > 0f
                    ? Mathf.MoveTowards(speed, 0f, engine.Braking * deltaTime)
                    : Mathf.MoveTowards(speed, -engine.ReverseSpeed, engine.Acceleration * deltaTime);
            else
                speed = Mathf.MoveTowards(speed, 0f, engine.Rolling * deltaTime);

            vehicle.Speed = speed;
            Move(vehicle, deltaTime);
            if (_tickHits == 0) return;
            RamHits += _tickHits;
            RamKills += _tickKills;
            Rammed?.Invoke(vehicle, _tickHits, _tickKills);
            _tickHits = _tickKills = 0;
        }

        /// <summary>
        /// One tick of motion: turn, then move along the heading in small steps; walls stop or deflect it, monsters in
        /// its way are rammed (<see cref="Ram"/>).
        /// </summary>
        private void Move(Vehicle vehicle, float deltaTime)
        {
            var speed = vehicle.Speed;
            var radius = vehicle.Definition.Steering.TurnRadius + Mathf.Abs(speed) * TurnWidening;
            var yaw = speed / radius * vehicle.Steer * deltaTime;
            var distance = Mathf.Abs(speed) * deltaTime;
            var steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(distance, Mathf.Abs(yaw) * vehicle.HalfLength) / MaxSubstep));
            var contact = false;
            var scraping = false;
            var impact = 0f;

            for (var i = 0; i < steps; i++)
            {
                var heading = vehicle.Heading + yaw / steps;
                if (!Overlaps(vehicle.Position, heading)) vehicle.Heading = heading;
                else contact = true; // the body cannot turn there: the wall holds its corner

                var delta = VehicleBody.Forward(vehicle.Heading) * (speed * deltaTime / steps);
                var length = delta.magnitude;
                if (length < 1e-6f) continue;
                var before = vehicle.Position;
                var target = before + delta;
                if (!Overlaps(target, vehicle.Heading))
                {
                    vehicle.Position = target;
                    if (Ram(vehicle, before, ref speed)) break;
                    continue;
                }

                contact = true;
                var alongX = new Vector2(delta.x, 0f);
                var alongY = new Vector2(0f, delta.y);
                var freeX = Mathf.Abs(delta.x) > 1e-6f && !Overlaps(vehicle.Position + alongX, vehicle.Heading);
                var freeY = Mathf.Abs(delta.y) > 1e-6f && !Overlaps(vehicle.Position + alongY, vehicle.Heading);
                Vector2 slide;
                float into;
                if (freeX && (!freeY || Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)))
                {
                    slide = alongX;
                    into = Mathf.Abs(delta.y);
                }
                else if (freeY)
                {
                    slide = alongY;
                    into = Mathf.Abs(delta.x);
                }
                else
                {
                    slide = Vector2.zero;
                    into = length;
                }

                var sine = into / length;
                impact = Mathf.Max(impact, Mathf.Abs(speed) * sine);
                if (slide != Vector2.zero && sine < SlideSine)
                {
                    vehicle.Position += slide;
                    scraping = true;
                    if (Ram(vehicle, before, ref speed)) break;
                    continue;
                }

                // Head-on: it stops right at the wall (no gap to creep over and hit again).
                vehicle.Position = Closest(vehicle.Position, delta, vehicle.Heading);
                speed = 0f;
                break;
            }

            vehicle.Speed = speed;
            if (contact && !vehicle.InContact) HitWall(vehicle, impact);
            vehicle.InContact = contact;
            vehicle.IsScraping = scraping && Mathf.Abs(speed) > 0.5f;
        }

        private void HitWall(Vehicle vehicle, float impact)
        {
            var hull = vehicle.Definition.Hull;
            Hurt(vehicle, Mathf.Max(0f, impact - hull.WallSafeSpeed) * hull.WallDamage);
            if (impact > 0.5f) WallHit?.Invoke(vehicle, impact);
        }

        // ------------------------------------------------------------------ Ramming

        /// <summary>
        /// Monsters the body overlaps after a step from <paramref name="before"/>: at ramming speed rank-and-file and
        /// officers are hit (damage mass × speed, rank-and-file outright from <see cref="RamModule.KillShare"/> of the
        /// top speed), fly aside (corpses — on along the way) and brake it by their mass; each wears the hull. Elite and
        /// bosses ahead: at ramming speed it bounces back (they take a little, stunned; the hull takes
        /// <see cref="RamModule.BounceDamage"/>), slower it just stops. True when it stopped or bounced (the move ends).
        /// </summary>
        private bool Ram(Vehicle vehicle, Vector2 before, ref float speed)
        {
            if (_horde == null || !_horde.IsActive || vehicle.Floor != _player.Floor) return false;
            var ram = vehicle.Definition.Ram;
            var magnitude = Mathf.Abs(speed);
            var ramming = magnitude >= ram.MinSpeed;
            var travel = vehicle.Forward * Mathf.Sign(speed);
            EnsureRamMemory();
            var center = vehicle.Position;
            var reach = Mathf.Sqrt(vehicle.HalfLength * vehicle.HalfLength + vehicle.HalfWidth * vehicle.HalfWidth) + _horde.MaxRadius;
            var minX = Mathf.FloorToInt(center.x - reach + 0.5f);
            var maxX = Mathf.FloorToInt(center.x + reach + 0.5f);
            var minY = Mathf.FloorToInt(center.y - reach + 0.5f);
            var maxY = Mathf.FloorToInt(center.y + reach + 0.5f);
            var positions = _horde.Positions;
            var states = _horde.States;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            for (var agent = _horde.FirstInCell(x, y); agent >= 0; agent = _horde.NextInCell(agent))
            {
                if (!HordeSystem.IsAlive(states[agent])) continue;
                var position = positions[agent];
                if (VehicleBody.Distance(position, center, vehicle.Heading, vehicle.HalfLength, vehicle.HalfWidth) >=
                    _horde.RadiusOf(agent)) continue;
                var kind = _horde.KindOf(agent);
                var rank = kind.Definition != null ? kind.Definition.Rank : MonsterRank.Mass;
                if (rank >= MonsterRank.Elite)
                {
                    // Only one ahead stops it: backing away from a boss is free.
                    if (Vector2.Dot(position - center, travel) <= 0f) continue;
                    vehicle.Position = before;
                    if (ramming && !RecentlyRammed(agent))
                    {
                        Remember(agent);
                        Damage(agent, ram.Mass * magnitude * ram.HeavyShare, travel);
                        if (ram.Stun != null && _statuses != null)
                            _statuses.Apply(CombatTarget.OfAgent(agent), ram.Stun, ram.StunTime);
                        speed = -speed * ram.Bounce;
                        Hurt(vehicle, ram.BounceDamage);
                        Bounced?.Invoke(vehicle, position);
                    }
                    else
                    {
                        speed = 0f;
                    }

                    return true;
                }

                // Slow: the horde puts them out of the body at the end of its tick.
                if (!ramming || RecentlyRammed(agent)) continue;
                Remember(agent);
                var damage = ram.Mass * magnitude;
                if (rank == MonsterRank.Mass && magnitude >= ram.KillShare * vehicle.Definition.Engine.MaxSpeed)
                    damage = Mathf.Max(damage, _horde.Health[agent] + 1f);
                var killed = Damage(agent, damage, travel);

                // Aside, to the side it stands on (dead centre: by slot); a corpse flies on along the way.
                var side = new Vector2(-travel.y, travel.x);
                var cross = Vector2.Dot(position - center, side);
                if (Mathf.Abs(cross) < 1e-3f ? (agent & 1) == 1 : cross < 0f) side = -side;
                _horde.Push(agent, killed
                    ? (travel + side * 0.5f) * magnitude
                    : (side + travel * 0.35f).normalized * magnitude);

                // The impulse: the heavier the monster and the weaker the engine, the more it brakes.
                var push = ram.Mass * Mathf.Max(0.1f, vehicle.Definition.Engine.Power);
                if (!float.IsInfinity(kind.Mass)) speed *= push / (push + kind.Mass);
                Hurt(vehicle, rank == MonsterRank.Officer ? ram.OfficerWear : ram.Wear);
                _tickHits++;
                if (killed) _tickKills++;
                magnitude = Mathf.Abs(speed);
                ramming = magnitude >= ram.MinSpeed;
            }

            return false;
        }

        private bool Damage(int agent, float amount, Vector2 direction) =>
            _targets != null ? _targets.Damage(CombatTarget.OfAgent(agent), amount, direction) : _horde.Damage(agent, amount);

        private void EnsureRamMemory()
        {
            if (_rammedAt != null && _rammedAt.Length == _horde.Capacity) return;
            _rammedAt = new float[_horde.Capacity];
            _rammedGeneration = new int[_horde.Capacity];
            for (var i = 0; i < _rammedAt.Length; i++) _rammedAt[i] = float.NegativeInfinity;
        }

        private bool RecentlyRammed(int agent) =>
            _rammedGeneration[agent] == _horde.Generations[agent] && _time - _rammedAt[agent] < RamCooldown;

        private void Remember(int agent)
        {
            _rammedAt[agent] = _time;
            _rammedGeneration[agent] = _horde.Generations[agent];
        }

        /// <summary>The farthest point along <paramref name="delta"/> the body fits (halving the step).</summary>
        private Vector2 Closest(Vector2 from, Vector2 delta, float heading)
        {
            float free = 0f, blocked = 1f;
            for (var i = 0; i < 8; i++)
            {
                var middle = (free + blocked) * 0.5f;
                if (Overlaps(from + delta * middle, heading)) blocked = middle;
                else free = middle;
            }

            return from + delta * free;
        }

        private bool Overlaps(Vector2 center, float heading)
        {
            var vehicle = Current;
            if (VehicleBody.Overlaps(center, heading, vehicle.HalfLength, vehicle.HalfWidth, this)) return true;
            // Parked ones — by their rectangles (their cells are left out of the walls above).
            var forward = VehicleBody.Forward(heading);
            foreach (var other in _vehicles)
                if (other.State == VehicleState.Parked && other.Floor == vehicle.Floor &&
                    VehicleBody.OverlapsBody(center, forward, vehicle.HalfLength - VehicleBody.Skin,
                        vehicle.HalfWidth - VehicleBody.Skin, other.Position, other.Forward, other.HalfLength, other.HalfWidth))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ Parking

        /// <summary>
        /// The parked body: its cells a wall for the monsters (those in them are put out), its rectangle — for the player
        /// and a driven vehicle (<see cref="IPlayerBodies"/>); a shot stop.
        /// </summary>
        private void Park(Vehicle vehicle)
        {
            VehicleBody.Cells(vehicle.Position, vehicle.Heading, vehicle.HalfLength, vehicle.HalfWidth, vehicle.Floor,
                BlockMargin, vehicle.BlockedCells);
            foreach (var cell in vehicle.BlockedCells)
                _blockers.Add(cell, body: true);
            _spatial.AddShotBlocker(vehicle);
            _horde?.PushOutOfBlocked(vehicle.Position, vehicle.HalfLength + 1f, vehicle.Floor);
        }

        private void Unpark(Vehicle vehicle)
        {
            foreach (var cell in vehicle.BlockedCells)
                _blockers.Remove(cell, body: true);
            vehicle.BlockedCells.Clear();
            _spatial.RemoveShotBlocker(vehicle);
        }

        /// <summary>A free spot for the player's footprint beside the body: sides first (the driver's — the left), then the ends.</summary>
        private bool TryFindExit(Vehicle vehicle, out Vector2 spot)
        {
            var half = _player.Definition.BodyHalfSize;
            var forward = vehicle.Forward;
            var right = VehicleBody.Right(forward);
            var along = vehicle.HalfLength * 0.5f;
            var blockers = (IPlayerBlockers)_player;
            // (outward, offset along the body, distance from the centre to the body's edge), by preference.
            var places = new[]
            {
                (-right, Vector2.zero, vehicle.HalfWidth), (-right, forward * along, vehicle.HalfWidth),
                (-right, -forward * along, vehicle.HalfWidth),
                (right, Vector2.zero, vehicle.HalfWidth), (right, forward * along, vehicle.HalfWidth),
                (right, -forward * along, vehicle.HalfWidth),
                (-forward, Vector2.zero, vehicle.HalfLength), (forward, Vector2.zero, vehicle.HalfLength),
            };

            foreach (var (outward, offset, edge) in places)
                // Step further out until the footprint clears walls and other bodies.
                for (var extra = 0.15f; extra < 1.5f; extra += 0.45f)
                {
                    var candidate = vehicle.Position + offset + outward * (edge + half + extra);
                    if (PlayerMovement.Overlaps(candidate, half, blockers)) continue;
                    spot = candidate;
                    return true;
                }

            spot = vehicle.Position;
            return false;
        }
    }
}
