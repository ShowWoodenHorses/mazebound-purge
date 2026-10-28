using System;
using System.Collections.Generic;
using Purge.Core.Audio;
using Purge.Core.Content;
using Purge.Core.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Core.Vehicles
{
    /// <summary>
    /// A module of a vehicle (<see cref="VehicleDefinition.Modules"/>, ТЗ 9а): the vehicle is assembled from them in the
    /// inspector. Engine, steering and hull are required; ram and look are optional.
    /// </summary>
    [Serializable]
    public abstract class VehicleModule : ContentModule
    {
    }

    /// <summary>How fast it goes and how fast it gets there.</summary>
    [Serializable, ModuleInfo("Engine")]
    public sealed class EngineModule : VehicleModule
    {
        [Tooltip("Top speed forward, cells per second.")]
        [SerializeField, Min(1f)] private float _maxSpeed = 10f;
        [Tooltip("Cells per second gained each second with the throttle held.")]
        [SerializeField, Min(0.1f)] private float _acceleration = 6f;
        [Tooltip("Cells per second lost each second while braking.")]
        [SerializeField, Min(0.1f)] private float _braking = 14f;
        [Tooltip("Cells per second lost each second with no pedal (rolling).")]
        [SerializeField, Min(0f)] private float _rolling = 3f;
        [Tooltip("Top speed backwards, cells per second (brake held at a stop).")]
        [SerializeField, Min(0.5f)] private float _reverseSpeed = 3.5f;
        [Tooltip("Pushing force through the crowd (stage 9b): how much of the speed it keeps per monster run over.")]
        [SerializeField, Min(0f)] private float _power = 1f;

        public float MaxSpeed { get => _maxSpeed; internal set => _maxSpeed = value; }
        public float Acceleration { get => _acceleration; internal set => _acceleration = value; }
        public float Braking { get => _braking; internal set => _braking = value; }
        public float Rolling { get => _rolling; internal set => _rolling = value; }
        public float ReverseSpeed { get => _reverseSpeed; internal set => _reverseSpeed = value; }
        public float Power { get => _power; internal set => _power = value; }
    }

    /// <summary>How sharp it turns: it turns only while it moves (no turning on the spot).</summary>
    [Serializable, ModuleInfo("Steering")]
    public sealed class SteeringModule : VehicleModule
    {
        [Tooltip("Radius of the tightest turn, cells (at full lock).")]
        [SerializeField, Min(0.5f)] private float _turnRadius = 3.5f;

        public float TurnRadius { get => _turnRadius; internal set => _turnRadius = value; }
    }

    /// <summary>The body: its rectangle on the grid and its HP.</summary>
    [Serializable, ModuleInfo("Hull")]
    public sealed class HullModule : VehicleModule
    {
        [Tooltip("Damage it takes before it breaks down (stage 9b).")]
        [SerializeField, Min(1f)] private float _health = 300f;
        [Tooltip("Length of the body rectangle, cells (front to back).")]
        [SerializeField, Min(0.5f)] private float _length = 4.6f;
        [Tooltip("Width of the body rectangle, cells.")]
        [SerializeField, Min(0.5f)] private float _width = 2f;
        [Tooltip("A wall hit faster than this (cells per second, straight into the wall) damages it.")]
        [SerializeField, Min(0f)] private float _wallSafeSpeed = 3f;
        [Tooltip("Damage per cell per second of a wall hit above the safe speed.")]
        [SerializeField, Min(0f)] private float _wallDamage = 4f;

        public float Health { get => _health; internal set => _health = value; }
        public float Length { get => _length; internal set => _length = value; }
        public float Width { get => _width; internal set => _width = value; }
        public float WallSafeSpeed { get => _wallSafeSpeed; internal set => _wallSafeSpeed = value; }
        public float WallDamage { get => _wallDamage; internal set => _wallDamage = value; }
    }

    /// <summary>The weight it rams the horde with (stage 9b: damage = mass × speed).</summary>
    [Serializable, ModuleInfo("Ram")]
    public sealed class RamModule : VehicleModule
    {
        [Tooltip("Mass (the player is 1, a common monster about 1).")]
        [SerializeField, Min(0.1f)] private float _mass = 10f;

        public float Mass { get => _mass; internal set => _mass = value; }
    }

    /// <summary>Display only: the model, the camera while driving, the engine's sounds.</summary>
    [Serializable, ModuleInfo("Look")]
    public sealed class VehicleLookModule : VehicleModule
    {
        [Tooltip("The model; its forward is +Z, its origin on the ground at the centre of the body.")]
        [SerializeField] private AssetReferenceGameObject _prefab;
        [Tooltip("Cells the camera pulls back at full speed (added to the player's camera distance).")]
        [SerializeField, Min(0f)] private float _cameraZoom = 3f;
        [Tooltip("Engine idling (loop).")]
        [SerializeField, VehicleSound] private string _idle;
        [Tooltip("Engine driving (loop): its pitch follows the speed.")]
        [SerializeField, VehicleSound] private string _drive;
        [Tooltip("Engine starts (getting in).")]
        [SerializeField, VehicleSound] private string _start;
        [Tooltip("Engine roars (throttle from a standstill).")]
        [SerializeField, VehicleSound] private string _rev;

        public AssetReferenceGameObject Prefab => _prefab;
        public float CameraZoom { get => _cameraZoom; internal set => _cameraZoom = value; }
        public string Idle => _idle;
        public string Drive => _drive;
        public string Start => _start;
        public string Rev => _rev;

        internal void SetPrefab(AssetReferenceGameObject prefab) => _prefab = prefab;

        internal void SetSounds(string idle, string drive, string start, string rev)
        {
            _idle = idle;
            _drive = drive;
            _start = start;
            _rev = rev;
        }
    }

    /// <summary>
    /// A drivable vehicle (ТЗ 9, «Машина-таран»): assembled from <see cref="VehicleModule"/>s — engine, steering and hull
    /// are required. One asset per vehicle (<c>Data/Vehicles</c>, Build Vehicles); a level's <c>VehicleData</c> refers to
    /// it. The model is display only.
    /// </summary>
    [CreateAssetMenu(fileName = "Vehicle", menuName = "Purge/Definitions/Vehicle")]
    public sealed class VehicleDefinition : ContentDefinition
    {
        private static readonly EngineModule DefaultEngine = new EngineModule();
        private static readonly SteeringModule DefaultSteering = new SteeringModule();
        private static readonly HullModule DefaultHull = new HullModule();
        private static readonly RamModule DefaultRam = new RamModule();

        [SerializeReference, ModuleList] private List<VehicleModule> _modules = new List<VehicleModule>();

        public IReadOnlyList<VehicleModule> Modules => _modules;

        public EngineModule Engine => TryGet(out EngineModule module) ? module : DefaultEngine;
        public SteeringModule Steering => TryGet(out SteeringModule module) ? module : DefaultSteering;
        public HullModule Hull => TryGet(out HullModule module) ? module : DefaultHull;
        public RamModule Ram => TryGet(out RamModule module) ? module : DefaultRam;

        /// <summary>Null without a look module.</summary>
        public VehicleLookModule Look => TryGet(out VehicleLookModule module) ? module : null;

        public bool TryGet<T>(out T module) where T : VehicleModule => Purge.Core.Modules.Modules.TryGet(_modules, out module);

        public void Validate(List<string> problems)
        {
            var owner = $"Vehicle '{name}'";
            Purge.Core.Modules.Modules.Validate(_modules, owner, problems);
            if (!TryGet(out EngineModule _)) problems.Add($"{owner}: no Engine module.");
            if (!TryGet(out SteeringModule _)) problems.Add($"{owner}: no Steering module.");
            if (!TryGet(out HullModule _)) problems.Add($"{owner}: no Hull module.");
        }

        internal List<VehicleModule> MutableModules => _modules ??= new List<VehicleModule>();

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, params VehicleModule[] modules)
        {
            SetId(id);
            _modules = new List<VehicleModule>(modules);
        }
    }
}
