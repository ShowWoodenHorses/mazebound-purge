using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Vehicles;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Weapons;
using UnityEngine;

namespace Purge.Gameplay.Vehicles
{
    /// <summary>A vehicle stands (nobody in it) or the player drives it.</summary>
    public enum VehicleState
    {
        Parked = 0,
        Driven = 1,
    }

    /// <summary>
    /// A vehicle of the level (stage 9a): its body on the grid, its speed and HP. Parked, it is a wall for everyone and
    /// stops shots (decision 2026-10-28); driven, it carries the player.
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
        public VehicleState State { get; internal set; }

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
    /// level puts it. A parked one blocks the cells under its body (<see cref="CellBlockers"/>, monsters there are put
    /// out) and stops shots (<see cref="SpatialQueryService.AddShotBlocker"/>). Use next to its body gets the player in
    /// (<see cref="Enter"/>; no shooting, throwing or weapon switching inside), Use again gets them out beside it.
    /// <para>Driving: throttle and brake/reverse (<see cref="PlayerAction.Gas"/>, <see cref="PlayerAction.Brake"/>), the
    /// wheel — the stick's x. The body is a rectangle moved along its heading without physics: a wall stops it, or, met
    /// at under 45°, it slides along; a hit faster than the hull's safe speed damages it. It turns only while moving.</para>
    /// Must be registered right after <see cref="PlayerSystem"/>: it carries the player (<see cref="PlayerSystem.Ride"/>).
    /// </summary>
    public sealed class VehicleSystem : ILevelLoadStep, ILevelTickable, IPlayerBlockers, IDisposable
    {
        /// <summary>Cells from the player to the body within which Use gets in.</summary>
        public const float EnterReach = 1f;

        /// <summary>A parked body blocks the cells it overlaps deeper than this (a corner touching a cell leaves it free).</summary>
        public const float BlockMargin = 0.2f;

        /// <summary>A wall met at under this angle is slid along; head-on it stops the vehicle.</summary>
        public const float SlideAngle = 45f;

        private const float SteerDeadZone = 0.15f;
        private const float MaxSubstep = 0.2f;

        /// <summary>The turn widens with speed: cells of radius added per cell per second.</summary>
        private const float TurnWidening = 0.25f;

        private static readonly float SlideSine = Mathf.Sin(SlideAngle * Mathf.Deg2Rad);

        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly IPlayerInput _input;
        private readonly LevelPassability _passability;
        private readonly CellBlockers _blockers;
        private readonly SpatialQueryService _spatial;
        private readonly WeaponSystem _weapons;
        private readonly HordeSystem _horde;
        private readonly List<Vehicle> _vehicles = new List<Vehicle>();
        private int _floor;

        public VehicleSystem(LevelData level, PlayerSystem player, IPlayerInput input, LevelPassability passability,
            CellBlockers blockers, SpatialQueryService spatial, WeaponSystem weapons = null, HordeSystem horde = null)
        {
            _level = level;
            _player = player;
            _input = input;
            _passability = passability;
            _blockers = blockers;
            _spatial = spatial;
            _weapons = weapons;
            _horde = horde;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        public IReadOnlyList<Vehicle> Vehicles => _vehicles;

        /// <summary>The vehicle the player drives; null on foot.</summary>
        public Vehicle Current { get; private set; }

        /// <summary>(vehicle) the player got in.</summary>
        public event Action<Vehicle> Entered;

        /// <summary>(vehicle) the player got out; it stands where it stopped.</summary>
        public event Action<Vehicle> Exited;

        /// <summary>(vehicle, speed into the wall, cells per second) it hit a wall (once per contact).</summary>
        public event Action<Vehicle, float> WallHit;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            foreach (var data in _level.Vehicles)
            {
                if (data.Definition == null)
                {
                    GameLog.Warning(LogChannel.Gameplay, $"Vehicle '{data.Id}' has no definition: skipped.");
                    continue;
                }

                var vehicle = new Vehicle(data, data.Definition);
                _vehicles.Add(vehicle);
                Park(vehicle);
            }

            if (_vehicles.Count > 0) GameLog.Info(LogChannel.Gameplay, $"Vehicles: {_vehicles.Count}.");
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            foreach (var vehicle in _vehicles)
                _spatial.RemoveShotBlocker(vehicle);
            _vehicles.Clear();
            Current = null;
        }

        public void Tick(float deltaTime)
        {
            var vehicle = Current;
            if (vehicle == null || deltaTime <= 0f) return;
            Drive(vehicle, deltaTime);
            _player.Ride(vehicle.Position, vehicle.Forward, deltaTime);
        }

        /// <summary>A parked vehicle within <see cref="EnterReach"/> of the player (the nearest); null when none or driving.</summary>
        public Vehicle FindEnterable()
        {
            if (!_player.IsSpawned || _player.InVehicle || _player.Down) return null;
            Vehicle best = null;
            var bestDistance = EnterReach;
            foreach (var vehicle in _vehicles)
            {
                if (vehicle.State != VehicleState.Parked || vehicle.Floor != _player.Floor) continue;
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
            if (vehicle == null || vehicle.State != VehicleState.Parked || Current != null || _player.InVehicle) return false;
            Unpark(vehicle);
            vehicle.State = VehicleState.Driven;
            vehicle.Speed = 0f;
            Current = vehicle;
            _floor = vehicle.Floor;
            _player.EnterVehicle(vehicle.Position, vehicle.Forward);
            if (_weapons != null) _weapons.InputLocked = true;
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
            Current = null;
            _player.ExitVehicle(spot);
            if (_weapons != null) _weapons.InputLocked = false;
            Exited?.Invoke(vehicle);
            return true;
        }

        bool IPlayerBlockers.IsWalkable(GridPosition cell) => _passability.IsWalkable(cell);

        bool IPlayerBlockers.CanEnter(GridPosition cell) => true;

        int IPlayerBlockers.Floor => _floor;

        // ------------------------------------------------------------------ Driving

        private void Drive(Vehicle vehicle, float deltaTime)
        {
            var engine = vehicle.Definition.Engine;
            var active = !_player.Down;
            var gas = active && _input.IsHeld(PlayerAction.Gas);
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
        }

        /// <summary>One tick of motion: turn, then move along the heading in small steps; walls stop or deflect it.</summary>
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
                var target = vehicle.Position + delta;
                if (!Overlaps(target, vehicle.Heading))
                {
                    vehicle.Position = target;
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
            var damage = Mathf.Max(0f, impact - hull.WallSafeSpeed) * hull.WallDamage;
            if (damage > 0f) vehicle.Health = Mathf.Max(0f, vehicle.Health - damage);
            if (impact > 0.5f) WallHit?.Invoke(vehicle, impact);
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
            return VehicleBody.Overlaps(center, heading, vehicle.HalfLength, vehicle.HalfWidth, this);
        }

        // ------------------------------------------------------------------ Parking

        /// <summary>The parked body: a wall for the player and the monsters (monsters in its cells are put out), a shot stop.</summary>
        private void Park(Vehicle vehicle)
        {
            VehicleBody.Cells(vehicle.Position, vehicle.Heading, vehicle.HalfLength, vehicle.HalfWidth, vehicle.Floor,
                BlockMargin, vehicle.BlockedCells);
            foreach (var cell in vehicle.BlockedCells)
                _blockers.Add(cell);
            _spatial.AddShotBlocker(vehicle);
            _horde?.PushOutOfBlocked(vehicle.Position, vehicle.HalfLength + 1f, vehicle.Floor);
        }

        private void Unpark(Vehicle vehicle)
        {
            foreach (var cell in vehicle.BlockedCells)
                _blockers.Remove(cell);
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
                // The parked body blocks whole cells: step further out until the footprint clears them.
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
