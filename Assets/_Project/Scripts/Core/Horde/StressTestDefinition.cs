using System;
using Purge.Core.Monsters;
using UnityEngine;

namespace Purge.Core.Horde
{
    /// <summary>A monster the horde spawns and how often (weights are relative).</summary>
    [Serializable]
    public sealed class HordeSpawn
    {
        [SerializeField] private MonsterDefinition _monster;
        [SerializeField, Min(0f)] private float _weight = 1f;

        public MonsterDefinition Monster => _monster;
        public float Weight => _weight;

        public HordeSpawn()
        {
        }

        public HordeSpawn(MonsterDefinition monster, float weight)
        {
            _monster = monster;
            _weight = weight;
        }
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
        [Tooltip("Look of the built-in monster (used when Monsters is empty: the stress test's zombie).")]
        [SerializeField] private HordeVisualDefinition _visual;
        [Tooltip("Stats of the built-in monster (used when Monsters is empty).")]
        [SerializeField] private MonsterStats _monster = new MonsterStats();
        [Tooltip("Monsters the horde spawns, by weight (stage 5); empty = the built-in monster above.")]
        [SerializeField] private HordeSpawn[] _monsters = Array.Empty<HordeSpawn>();
        [Tooltip("Looks of the monsters (by monster id; Build Monsters fills it).")]
        [SerializeField] private HordeVisualDefinition[] _monsterVisuals = Array.Empty<HordeVisualDefinition>();
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
        public MonsterStats Monster => _monster;
        public HordeSpawn[] Monsters => _monsters ??= Array.Empty<HordeSpawn>();
        public HordeVisualDefinition[] MonsterVisuals => _monsterVisuals ??= Array.Empty<HordeVisualDefinition>();

        /// <summary>Look of <paramref name="monster"/> among <see cref="MonsterVisuals"/> (null = none).</summary>
        public HordeVisualDefinition VisualOf(MonsterDefinition monster)
        {
            if (monster == null) return _visual;
            foreach (var visual in MonsterVisuals)
                if (visual != null && visual.MonsterId == monster.Id)
                    return visual;
            return null;
        }
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

        internal void ConfigureMonsters(HordeSpawn[] monsters) => _monsters = monsters ?? Array.Empty<HordeSpawn>();

        internal void SetMonsterVisuals(HordeVisualDefinition[] visuals) => _monsterVisuals = visuals ?? Array.Empty<HordeVisualDefinition>();

        internal void Configure(HordeVisualDefinition visual, Material tracer, Material particles, Material fire)
        {
            _visual = visual;
            _tracerMaterial = tracer;
            _particleMaterial = particles;
            _fireMaterial = fire;
        }
    }
}
