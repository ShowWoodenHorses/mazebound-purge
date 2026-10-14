using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual.Horde
{
    /// <summary>
    /// Effects of <see cref="StressWeapons"/>: tracers (one dynamic mesh), sparks on hits, the flamethrower's flame,
    /// embers over burning cells (three particle systems fed by <c>Emit</c>, only in visible cells) and the burning
    /// cells (mask texture W×H + quads over the visible burning cells, shader Purge/FireCells). No GameObject per
    /// effect, allocation-free per frame. Every part can be switched off to measure its cost.
    /// </summary>
    public sealed class StressEffectsView : ILevelLoadStep, ILevelTickable, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private const int MaxTracers = 512;
        private const int MaxFireQuads = 512;
        private const float TracerLength = 0.9f;
        private const float TracerWidth = 0.07f;
        private const float GunHeight = 0.75f;
        private const float FireHeight = 0.04f;
        private const float FireQuadHalf = 0.5f; // exactly the cell: quads never overlap
        private const float EmberInterval = 0.2f;

        private static readonly int FireMaskProperty = Shader.PropertyToID("_PurgeFireMask");
        private static readonly int FireMaskSizeProperty = Shader.PropertyToID("_PurgeFireMaskSize");

        private readonly LevelData _level;
        private readonly StressWeapons _weapons;
        private readonly HordeSystem _horde;
        private readonly PlayerSystem _player;
        private readonly VisibilitySystem _visibility;
        private readonly LevelViewRoot _root;
        private readonly List<GameObject> _objects = new List<GameObject>();

        private Material _tracerMaterial;
        private Material _fireMaterial;
        private Mesh _tracerMesh;
        private Vector3[] _tracerVertices;
        private Vector2[] _tracerUvs;
        private Color32[] _tracerColors;
        private int[] _tracerIndices;
        private Mesh _fireMesh;
        private Vector3[] _fireVertices;
        private int[] _fireIndices;
        private Texture2D _fireMask;
        private byte[] _fireMaskPixels;
        private ParticleSystem _sparks;
        private ParticleSystem _flame;
        private ParticleSystem _embers;
        private Bounds _bounds;
        private int _shownHits;
        private float _emberTimer;

        public StressEffectsView(LevelData level, StressWeapons weapons, HordeSystem horde, PlayerSystem player,
            VisibilitySystem visibility, LevelViewRoot root)
        {
            _level = level;
            _weapons = weapons;
            _horde = horde;
            _player = player;
            _visibility = visibility;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool Tracers { get; set; } = true;
        public bool Particles { get; set; } = true;
        public bool FireCells { get; set; } = true;
        public float RenderMs { get; private set; }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var settings = _level.StressTest;
            if (settings == null || !_weapons.IsActive)
                return UniTask.CompletedTask;

            var width = _level.Geometry.Width;
            var height = _level.Geometry.Height;
            _bounds = new Bounds(new Vector3(width * 0.5f, 1f, height * 0.5f), new Vector3(width + 4f, 4f, height + 4f));

            _tracerMaterial = settings.TracerMaterial;
            if (_tracerMaterial != null)
            {
                _tracerVertices = new Vector3[MaxTracers * 4];
                _tracerUvs = new Vector2[MaxTracers * 4];
                _tracerColors = new Color32[MaxTracers * 4];
                _tracerIndices = new int[MaxTracers * 6];
                for (var i = 0; i < MaxTracers; i++)
                {
                    _tracerUvs[i * 4] = new Vector2(0f, 0f);
                    _tracerUvs[i * 4 + 1] = new Vector2(0f, 1f);
                    _tracerUvs[i * 4 + 2] = new Vector2(1f, 1f);
                    _tracerUvs[i * 4 + 3] = new Vector2(1f, 0f);
                    for (var c = 0; c < 4; c++) _tracerColors[i * 4 + c] = new Color32(255, 255, 255, 255);
                }

                _tracerMesh = CreateDynamicMesh("Stress Tracers", _tracerVertices, _tracerIndices);
                _tracerMesh.uv = _tracerUvs;
                _tracerMesh.colors32 = _tracerColors;
            }

            _fireMaterial = settings.FireMaterial;
            if (_fireMaterial != null)
            {
                _fireMask = new Texture2D(width, height, TextureFormat.R8, false, true)
                {
                    name = "Purge Fire Mask", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                _fireMaskPixels = new byte[width * height];
                _fireMask.SetPixelData(_fireMaskPixels, 0);
                _fireMask.Apply(false);
                Shader.SetGlobalTexture(FireMaskProperty, _fireMask);
                Shader.SetGlobalVector(FireMaskSizeProperty, new Vector4(1f / width, 1f / height, 0f, 0f));
                _fireVertices = new Vector3[MaxFireQuads * 4];
                _fireIndices = new int[MaxFireQuads * 6];
                _fireMesh = CreateDynamicMesh("Stress Fire", _fireVertices, _fireIndices);
            }

            var particles = settings.ParticleMaterial;
            if (particles != null)
            {
                _sparks = CreateParticles("Stress Sparks", particles, 600, new Color(1f, 0.85f, 0.4f), 0.25f, 0.12f);
                _flame = CreateParticles("Stress Flame", particles, 400, new Color(1f, 0.55f, 0.15f), 0.4f, 0.5f);
                _embers = CreateParticles("Stress Embers", particles, 400, new Color(1f, 0.45f, 0.1f), 0.8f, 0.18f);
            }

            return UniTask.CompletedTask;
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_objects);

        public void Tick(float deltaTime)
        {
            if (!_weapons.IsActive || !Particles || _sparks == null)
            {
                _shownHits = _weapons.HitCount;
                return;
            }

            // The level warmup stops every particle system once it has drawn them.
            if (!_sparks.isPlaying)
            {
                _sparks.Play();
                _flame.Play();
                _embers.Play();
            }

            // Sparks of the hits since the last tick (at most the ring buffer).
            var first = Mathf.Max(_shownHits, _weapons.HitCount - StressWeapons.HitHistory);
            for (var hit = first; hit < _weapons.HitCount; hit++)
            {
                var point = _weapons.HitAt(hit, out var monster);
                if (!Visible(point)) continue;
                Emit(_sparks, new Vector3(point.x, GunHeight, point.y), monster ? 4 : 3, 3f, 0.25f);
            }

            _shownHits = _weapons.HitCount;

            if (_weapons.Flaming)
            {
                var origin = _player.Position;
                var aim = _weapons.AimDirection;
                var range = _weapons.Settings.FlameRange;
                var emit = new ParticleSystem.EmitParams();
                var count = Mathf.CeilToInt(deltaTime * 70f);
                for (var i = 0; i < count; i++)
                {
                    var spread = (UnityEngine.Random.value - 0.5f) * _weapons.Settings.FlameAngle * Mathf.Deg2Rad;
                    var dir = new Vector2(aim.x * Mathf.Cos(spread) - aim.y * Mathf.Sin(spread),
                        aim.x * Mathf.Sin(spread) + aim.y * Mathf.Cos(spread));
                    emit.position = new Vector3(origin.x + aim.x * 0.4f, GunHeight * 0.8f, origin.y + aim.y * 0.4f);
                    emit.velocity = new Vector3(dir.x, 0.15f, dir.y) * (range / 0.4f);
                    emit.startLifetime = 0.4f;
                    emit.startSize = 0.35f;
                    _flame.Emit(emit, 1);
                }
            }

            _emberTimer -= deltaTime;
            if (_emberTimer <= 0f && FireCells && _weapons.Fire != null)
            {
                _emberTimer += EmberInterval;
                var fire = _weapons.Fire;
                foreach (var index in fire.Cells)
                {
                    var x = index % fire.Width;
                    var y = index / fire.Width;
                    if (!_visibility.IsVisible(new GridPosition(x, y))) continue;
                    var emit = new ParticleSystem.EmitParams
                    {
                        position = new Vector3(x + UnityEngine.Random.Range(-0.4f, 0.4f), 0.1f, y + UnityEngine.Random.Range(-0.4f, 0.4f)),
                        velocity = new Vector3(0f, 1.6f, 0f),
                        startLifetime = 0.8f,
                        startSize = 0.18f,
                    };
                    _embers.Emit(emit, 1);
                }
            }
        }

        public void LateTick(float deltaTime)
        {
            if (!_weapons.IsActive)
                return;

            var started = Stopwatch.GetTimestamp();
            if (Tracers && _tracerMesh != null)
                DrawTracers();
            if (FireCells && _fireMesh != null)
                DrawFire();
            RenderMs = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        }

        public void Dispose()
        {
            Shader.SetGlobalTexture(FireMaskProperty, Texture2D.blackTexture);
            UnityObjects.Destroy(_tracerMesh);
            UnityObjects.Destroy(_fireMesh);
            UnityObjects.Destroy(_fireMask);
            foreach (var go in _objects)
                UnityObjects.Destroy(go);
            _objects.Clear();
        }

        private void DrawTracers()
        {
            var positions = _weapons.BulletPositions;
            var directions = _weapons.BulletDirections;
            var quads = 0;
            for (var i = 0; i < positions.Length && quads < MaxTracers; i++)
            {
                var head = positions[i];
                if (!Visible(head)) continue;
                var dir = directions[i];
                var tail = head - dir * TracerLength;
                var side = new Vector2(-dir.y, dir.x) * (TracerWidth * 0.5f);
                var v = quads * 4;
                _tracerVertices[v] = new Vector3(tail.x - side.x, GunHeight, tail.y - side.y);
                _tracerVertices[v + 1] = new Vector3(tail.x + side.x, GunHeight, tail.y + side.y);
                _tracerVertices[v + 2] = new Vector3(head.x + side.x, GunHeight, head.y + side.y);
                _tracerVertices[v + 3] = new Vector3(head.x - side.x, GunHeight, head.y - side.y);
                quads++;
            }

            if (quads == 0) return;
            Upload(_tracerMesh, _tracerVertices, _tracerIndices, quads);
            Graphics.RenderMesh(Params(_tracerMaterial), _tracerMesh, 0, Matrix4x4.identity);
        }

        private void DrawFire()
        {
            var fire = _weapons.Fire;
            // Mask: clear what burned last frame, write current strengths.
            Array.Clear(_fireMaskPixels, 0, _fireMaskPixels.Length);
            var quads = 0;
            foreach (var index in fire.Cells)
            {
                _fireMaskPixels[index] = (byte)(Mathf.Clamp01(0.6f + fire.Strength(index) * 0.6f) * 255f);
                var x = index % fire.Width;
                var y = index / fire.Width;
                if (quads >= MaxFireQuads || !_visibility.IsVisible(new GridPosition(x, y))) continue;
                var v = quads * 4;
                _fireVertices[v] = new Vector3(x - FireQuadHalf, FireHeight, y - FireQuadHalf);
                _fireVertices[v + 1] = new Vector3(x - FireQuadHalf, FireHeight, y + FireQuadHalf);
                _fireVertices[v + 2] = new Vector3(x + FireQuadHalf, FireHeight, y + FireQuadHalf);
                _fireVertices[v + 3] = new Vector3(x + FireQuadHalf, FireHeight, y - FireQuadHalf);
                quads++;
            }

            _fireMask.SetPixelData(_fireMaskPixels, 0);
            _fireMask.Apply(false);
            if (quads == 0) return;
            Upload(_fireMesh, _fireVertices, _fireIndices, quads);
            Graphics.RenderMesh(Params(_fireMaterial), _fireMesh, 0, Matrix4x4.identity);
        }

        private RenderParams Params(Material material) => new RenderParams(material)
        {
            worldBounds = _bounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
        };

        private bool Visible(Vector2 point) =>
            _visibility.IsVisible(new GridPosition(Mathf.FloorToInt(point.x + 0.5f), Mathf.FloorToInt(point.y + 0.5f)));

        private static void Upload(Mesh mesh, Vector3[] vertices, int[] indices, int quads)
        {
            // The vertex buffer keeps its full size (unused quads are not indexed); only the index count changes.
            mesh.SetVertices(vertices, 0, vertices.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.SetIndices(indices, 0, quads * 6, MeshTopology.Triangles, 0, false);
        }

        private Mesh CreateDynamicMesh(string name, Vector3[] vertices, int[] indices)
        {
            for (var q = 0; q < indices.Length / 6; q++)
            {
                var v = q * 4;
                var i = q * 6;
                indices[i] = v;
                indices[i + 1] = v + 1;
                indices[i + 2] = v + 2;
                indices[i + 3] = v;
                indices[i + 4] = v + 2;
                indices[i + 5] = v + 3;
            }

            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = _bounds;
            return mesh;
        }

        private ParticleSystem CreateParticles(string name, Material material, int max, Color color, float lifetime, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root != null ? _root.transform : null, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startSpeed = 0f;
            main.startColor = color;
            main.gravityModifier = 0f;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;
            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
            _objects.Add(go);
            return system;
        }

        private static void Emit(ParticleSystem system, Vector3 position, int count, float speed, float lifetime)
        {
            var emit = new ParticleSystem.EmitParams { position = position, startLifetime = lifetime };
            for (var i = 0; i < count; i++)
            {
                emit.velocity = UnityEngine.Random.insideUnitSphere * speed + Vector3.up * 1f;
                system.Emit(emit, 1);
            }
        }
    }
}
