using System;
using UnityEngine;

namespace Purge.Core.Horde
{
    /// <summary>Stats of a mass monster (stage 2 draft; stage 5 turns them into monster definitions).</summary>
    [Serializable]
    public sealed class HordeMonsterStats
    {
        [Min(1f)] public float MaxHp = 30f;
        [Tooltip("Cells per second.")]
        [Min(0.1f)] public float Speed = 2.2f;
        [Tooltip("Body radius in cells (crowd spacing, hits).")]
        [Range(0.1f, 0.5f)] public float Radius = 0.3f;
        [Tooltip("Attacks when the distance between centres is at most this.")]
        [Min(0.3f)] public float AttackReach = 0.8f;
        [Min(0f)] public int Damage = 5;
        [Min(0.1f)] public float AttackInterval = 1.2f;
        [Tooltip("Degrees per second.")]
        [Min(30f)] public float TurnSpeed = 540f;
        [Tooltip("Share of the overlap pushed apart per tick (0 = no crowd spacing).")]
        [Range(0f, 1f)] public float Separation = 0.5f;
        [Tooltip("Seconds from death until the corpse disappears (the death clip plays first, then holds).")]
        [Min(0f)] public float CorpseTime = 3f;
    }

    /// <summary>Weapons of the stress test (stub; stage 4 rebuilds weapons from modules).</summary>
    [Serializable]
    public sealed class StressWeaponSettings
    {
        [Header("Machine gun")]
        [Min(0.1f)] public float GunRate = 12f;
        [Min(1f)] public float BulletSpeed = 28f;
        [Min(0f)] public float BulletDamage = 12f;
        [Min(1f)] public float BulletRange = 14f;
        [Range(0f, 20f)] public float GunSpread = 3f;

        [Header("Shotgun")]
        [Min(0.1f)] public float ShotgunRate = 1.5f;
        [Min(1)] public int Pellets = 8;
        [Range(1f, 60f)] public float ShotgunSpread = 24f;
        [Min(0f)] public float PelletDamage = 10f;

        [Header("Flamethrower")]
        [Min(0.5f)] public float FlameRange = 3.5f;
        [Range(5f, 90f)] public float FlameAngle = 30f;
        [Tooltip("Damage per second to every monster in the cone.")]
        [Min(0f)] public float FlameDps = 45f;

        [Header("Fire cells")]
        [Min(0.1f)] public float FireLifetime = 4f;
        [Min(0f)] public float FireDps = 20f;
        [Tooltip("Burning cells kept around the player by the benchmark (ТЗ: 30).")]
        [Min(0)] public int FireCellsTarget = 30;
    }

    /// <summary>
    /// Stage 2 stress test (dev levels only): referenced by <c>LevelData.StressTest</c>. Turns on the horde, the
    /// stress weapons, the autopilot and the measuring overlay; the numbers here are the starting point, the overlay
    /// changes them at run time.
    /// <para>With <see cref="IsSandbox"/> it is the combat sandbox of stage 4 instead: the horde fights the player's own
    /// weapons (picked up on the level), the player gets <see cref="SandboxAmmo"/> rounds of every ammo type; no stress
    /// weapons, autopilot or benchmark (the overlay keeps the counts, god mode and the metrics).</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Purge/Stress Test", fileName = "StressTest")]
    public sealed class StressTestDefinition : ScriptableObject
    {
        [SerializeField] private HordeVisualDefinition _visual;
        [SerializeField] private HordeMonsterStats _monster = new HordeMonsterStats();
        [SerializeField] private StressWeaponSettings _weapons = new StressWeaponSettings();

        [Header("Combat sandbox")]
        [Tooltip("The combat sandbox (stage 4): the player's own weapons against the horde.")]
        [SerializeField] private bool _sandbox;
        [Tooltip("Rounds of every ammo type the player starts with in the sandbox.")]
        [SerializeField, Min(0)] private int _sandboxAmmo = 500;
        [Tooltip("Weapons Build Stress Test Levels lays out around the start of the sandbox level.")]
        [SerializeField] private Definitions.WeaponDefinition[] _sandboxWeapons = new Definitions.WeaponDefinition[0];
        [Tooltip("Throwables of the sandbox: the drop pool's and the player's at the start (a full stack of the first).")]
        [SerializeField] private Definitions.ThrowableDefinition[] _sandboxThrowables = new Definitions.ThrowableDefinition[0];
        [Tooltip("What the horde's monsters drop (empty = nothing). Weapons and throwables — from the sandbox's lists.")]
        [SerializeField] private Drops.DropTable _drop;

        [Header("Run")]
        [SerializeField] private int[] _monsterCounts = { 50, 100, 150, 200, 300, 500 };
        [SerializeField, Min(0)] private int _startCount = 150;
        [Tooltip("Half-size (cells) of the square around the player that fits the screen.")]
        [SerializeField] private float[] _cameraHalfExtents = { 4f, 6f, 8f, 10f };
        [SerializeField, Min(1f)] private float _startCameraHalfExtent = 8f;
        [Tooltip("Player view radius in cells (ТЗ: about 21×21 → 10).")]
        [SerializeField, Min(1)] private int _visibilityRadius = 10;
        [Tooltip("New monsters appear at least this far from the player (cells).")]
        [SerializeField, Min(1f)] private float _spawnMinDistance = 12f;
        [SerializeField, Min(1f)] private float _spawnMaxDistance = 22f;
        [Tooltip("Radius of the autopilot's circle around the level centre (cells).")]
        [SerializeField, Min(1f)] private float _autopilotRadius = 10f;

        [Header("Effects")]
        [Tooltip("Tracers (Purge/Particle, additive, texture along U).")]
        [SerializeField] private Material _tracerMaterial;
        [Tooltip("Sparks, flame, embers (Purge/Particle, additive, soft dot).")]
        [SerializeField] private Material _particleMaterial;
        [Tooltip("Burning cells (Purge/FireCells).")]
        [SerializeField] private Material _fireMaterial;

        [Header("Benchmark")]
        [SerializeField, Min(0.5f)] private float _benchmarkWarmup = 3f;
        [SerializeField, Min(1f)] private float _benchmarkSample = 12f;

        public HordeVisualDefinition Visual => _visual;
        public bool IsSandbox => _sandbox;
        public int SandboxAmmo => _sandboxAmmo;
        public Definitions.WeaponDefinition[] SandboxWeapons => _sandboxWeapons;
        public Definitions.ThrowableDefinition[] SandboxThrowables => _sandboxThrowables ??= new Definitions.ThrowableDefinition[0];
        public Drops.DropTable Drop => _drop;
        public HordeMonsterStats Monster => _monster;
        public StressWeaponSettings Weapons => _weapons;
        public int[] MonsterCounts => _monsterCounts;
        public int StartCount => _startCount;
        public float[] CameraHalfExtents => _cameraHalfExtents;
        public float StartCameraHalfExtent => _startCameraHalfExtent;
        public int VisibilityRadius => _visibilityRadius;
        public float SpawnMinDistance => _spawnMinDistance;
        public float SpawnMaxDistance => _spawnMaxDistance;
        public float AutopilotRadius => _autopilotRadius;
        public Material TracerMaterial => _tracerMaterial;
        public Material ParticleMaterial => _particleMaterial;
        public Material FireMaterial => _fireMaterial;
        public float BenchmarkWarmup => _benchmarkWarmup;
        public float BenchmarkSample => _benchmarkSample;

        /// <summary>Largest monster count the run can ask for (array capacity).</summary>
        public int MaxCount
        {
            get
            {
                var max = _startCount;
                foreach (var count in _monsterCounts)
                    max = Math.Max(max, count);
                return Math.Max(1, max);
            }
        }

        internal void ConfigureSandbox(bool sandbox, int ammo, int startCount)
        {
            _sandbox = sandbox;
            _sandboxAmmo = ammo;
            _startCount = startCount;
        }

        internal void ConfigureDrops(Drops.DropTable drop, params Definitions.ThrowableDefinition[] throwables)
        {
            _drop = drop;
            _sandboxThrowables = throwables;
        }

        internal void Configure(HordeVisualDefinition visual, Material tracer, Material particles, Material fire)
        {
            _visual = visual;
            _tracerMaterial = tracer;
            _particleMaterial = particles;
            _fireMaterial = fire;
        }
    }
}
