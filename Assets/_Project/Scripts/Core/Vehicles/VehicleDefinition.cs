using System;
using System.Collections.Generic;
using Purge.Core.Audio;
using Purge.Core.Content;
using Purge.Core.Effects;
using Purge.Core.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Core.Vehicles
{
    /// <summary>
    /// A module of a vehicle (<see cref="VehicleDefinition.Modules"/>, ТЗ 9а): the vehicle is assembled from them in the
    /// inspector. Engine, steering and hull are required; ram, wreck and look are optional.
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
        [Tooltip("Damage it takes before it breaks down (stage 9b): rams, monsters' blows, walls.")]
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

    /// <summary>
    /// How it rams the horde (stage 9b): rank-and-file and officers take mass × speed (rank-and-file die outright at
    /// <see cref="KillShare"/> of the top speed), each one hit brakes it by its mass and costs the hull
    /// <see cref="Wear"/>; elite and bosses throw it back (<see cref="Bounce"/>), hurt a little and stunned.
    /// </summary>
    [Serializable, ModuleInfo("Ram")]
    public sealed class RamModule : VehicleModule
    {
        [Tooltip("Mass (the player is 1, a rank-and-file monster 0.15): damage = mass × speed; the heavier, the less a hit brakes it.")]
        [SerializeField, Min(0.1f)] private float _mass = 10f;
        [Tooltip("Slower than this, cells per second, it only pushes monsters aside (no ram).")]
        [SerializeField, Min(0f)] private float _minSpeed = 2f;
        [Tooltip("From this share of the top speed a rank-and-file monster dies outright.")]
        [SerializeField, Range(0f, 1f)] private float _killShare = 0.5f;
        [Tooltip("Damage the hull takes per rank-and-file monster rammed.")]
        [SerializeField, Min(0f)] private float _wear = 4f;
        [Tooltip("Damage the hull takes per officer rammed.")]
        [SerializeField, Min(0f)] private float _officerWear = 12f;
        [Tooltip("Elite and bosses: share of the ram damage they take.")]
        [SerializeField, Range(0f, 1f)] private float _heavyShare = 0.15f;
        [Tooltip("Elite and bosses: share of the speed it is thrown back with.")]
        [SerializeField, Range(0f, 1f)] private float _bounce = 0.35f;
        [Tooltip("Elite and bosses: damage the hull takes per bounce.")]
        [SerializeField, Min(0f)] private float _bounceDamage = 25f;
        [Tooltip("Elite and bosses: the state put on them (a stun).")]
        [SerializeField] private StatusDefinition _stun;
        [SerializeField, Min(0f)] private float _stunTime = 1f;

        public float Mass { get => _mass; internal set => _mass = value; }
        public float MinSpeed { get => _minSpeed; internal set => _minSpeed = value; }
        public float KillShare { get => _killShare; internal set => _killShare = value; }
        public float Wear { get => _wear; internal set => _wear = value; }
        public float OfficerWear { get => _officerWear; internal set => _officerWear = value; }
        public float HeavyShare { get => _heavyShare; internal set => _heavyShare = value; }
        public float Bounce { get => _bounce; internal set => _bounce = value; }
        public float BounceDamage { get => _bounceDamage; internal set => _bounceDamage = value; }
        public StatusDefinition Stun { get => _stun; internal set => _stun = value; }
        public float StunTime { get => _stunTime; internal set => _stunTime = value; }
    }

    /// <summary>
    /// How it breaks down (stage 9b): at 0 HP it smokes for <see cref="BreakTime"/>, throws the player out beside it
    /// (monsters around pushed off, the player unhurt for <see cref="Invulnerability"/>), then blows up — its
    /// <see cref="Effects"/> are the player's (they hurt monsters, not the player) — and is gone.
    /// </summary>
    [Serializable, ModuleInfo("Wreck")]
    public sealed class WreckModule : VehicleModule
    {
        [Tooltip("Seconds it smokes from 0 HP to the blast.")]
        [SerializeField, Min(0f)] private float _breakTime = 1.5f;
        [Tooltip("Monsters this close to the player thrown out are pushed off, cells.")]
        [SerializeField, Min(0f)] private float _pushRadius = 3f;
        [Tooltip("Speed of that push, cells per second.")]
        [SerializeField, Min(0f)] private float _pushSpeed = 9f;
        [Tooltip("Seconds the player thrown out takes no damage.")]
        [SerializeField, Min(0f)] private float _invulnerability = 1f;
        [Tooltip("The blast (an explosion, a fire zone…): the player's effects — they do not hurt the player.")]
        [SerializeReference, ModuleList] private List<EffectModule> _effects = new List<EffectModule>();

        public float BreakTime { get => _breakTime; internal set => _breakTime = value; }
        public float PushRadius { get => _pushRadius; internal set => _pushRadius = value; }
        public float PushSpeed { get => _pushSpeed; internal set => _pushSpeed = value; }
        public float Invulnerability { get => _invulnerability; internal set => _invulnerability = value; }
        public IReadOnlyList<EffectModule> Effects => _effects;
        internal List<EffectModule> MutableEffects => _effects ??= new List<EffectModule>();
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
        [Tooltip("Smoke over the broken-down vehicle (a pack effect; it loops while it smokes).")]
        [SerializeField] private AssetReferenceGameObject _smoke;

        public AssetReferenceGameObject Prefab => _prefab;
        public AssetReferenceGameObject Smoke => _smoke;
        public float CameraZoom { get => _cameraZoom; internal set => _cameraZoom = value; }
        public string Idle => _idle;
        public string Drive => _drive;
        public string Start => _start;
        public string Rev => _rev;

        internal void SetPrefab(AssetReferenceGameObject prefab) => _prefab = prefab;
        internal void SetSmoke(AssetReferenceGameObject smoke) => _smoke = smoke;

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
        private static readonly WreckModule DefaultWreck = new WreckModule();

        [SerializeReference, ModuleList] private List<VehicleModule> _modules = new List<VehicleModule>();

        public IReadOnlyList<VehicleModule> Modules => _modules;

        public EngineModule Engine => TryGet(out EngineModule module) ? module : DefaultEngine;
        public SteeringModule Steering => TryGet(out SteeringModule module) ? module : DefaultSteering;
        public HullModule Hull => TryGet(out HullModule module) ? module : DefaultHull;
        public RamModule Ram => TryGet(out RamModule module) ? module : DefaultRam;
        public WreckModule Wreck => TryGet(out WreckModule module) ? module : DefaultWreck;

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
