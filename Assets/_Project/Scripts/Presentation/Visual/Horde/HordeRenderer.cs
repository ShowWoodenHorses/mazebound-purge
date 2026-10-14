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
using Purge.Gameplay.Visibility;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual.Horde
{
    /// <summary>
    /// Draws the horde (<see cref="HordeSystem"/>) with GPU instancing: no GameObject per monster. Every late tick
    /// the monsters on screen and in the player's view (hidden ones keep simulating, ТЗ §57) are packed per look
    /// variant into batches of <see cref="BatchSize"/>: a matrix and the animation frame per instance
    /// (shader Purge/Horde reads the baked animation). Blob shadows — the same batches with a quad.
    /// Allocation-free per frame.
    /// </summary>
    public sealed class HordeRenderer : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        public const int BatchSize = 128;
        private const string VatKeyword = "PURGE_HORDE_VAT";
        private const string LerpKeyword = "PURGE_HORDE_LERP";
        private const float FlashTime = 0.12f;
        private const float ShadowHeight = 0.02f;
        private const float CullMargin = 1.5f;

        private static readonly int FrameProperty = Shader.PropertyToID("_HordeFrame");

        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly VisibilitySystem _visibility;
        private readonly TopDownCamera _camera;
        private readonly List<GameObject> _warmupObjects = new List<GameObject>();

        private HordeVisualDefinition _visual;
        private Material[] _materials;
        private Material _shadowMaterial;
        private float _shadowSize;
        private Mesh _shadowMesh;
        private Batch[] _batches;
        private int[] _batchFill; // per variant: instances in its current batch
        private int[] _batchOf;   // per variant: index of its current batch in _batches
        private int _batchesUsed;
        private Bounds _worldBounds;
        private int _runClip, _walkClip, _deathClip, _firstAttack, _attackCount;
        private bool _useVat, _interpolate;

        public HordeRenderer(LevelData level, HordeSystem horde, VisibilitySystem visibility, TopDownCamera camera)
        {
            _level = level;
            _horde = horde;
            _visibility = visibility;
            _camera = camera;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool IsActive => _batches != null;

        /// <summary>Draw the monsters at all (off = measure the simulation alone).</summary>
        public bool Enabled { get; set; } = true;

        public bool Shadows { get; set; } = true;

        /// <summary>Skip monsters outside the player's view (as the game does); off = draw everyone on screen.</summary>
        public bool CullByVisibility { get; set; } = true;

        /// <summary>Vertex animation texture instead of bone skinning in the shader.</summary>
        public bool UseVat
        {
            get => _useVat;
            set
            {
                _useVat = value;
                SetKeyword(VatKeyword, value);
            }
        }

        /// <summary>Blend two frames (smoother, twice the texture reads).</summary>
        public bool Interpolate
        {
            get => _interpolate;
            set
            {
                _interpolate = value;
                SetKeyword(LerpKeyword, value);
            }
        }

        /// <summary>Animation playback speed of the run clip.</summary>
        public float RunPlayback { get; set; } = 1f;

        public int DrawnCount { get; private set; }
        public int DrawCalls { get; private set; }
        public float RenderMs { get; private set; }

        /// <summary>Vertices of one monster as drawn (welded mesh) and in the source model.</summary>
        public int MeshVertexCount => _visual != null && _visual.Mesh != null ? _visual.Mesh.vertexCount : 0;
        public int SourceVertexCount => _visual != null ? _visual.SourceVertexCount : 0;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var settings = _level.StressTest;
            _visual = settings != null ? settings.Visual : null;
            if (!_horde.IsActive || _visual == null || _visual.Mesh == null || _visual.Variants.Length == 0)
                return UniTask.CompletedTask;

            _materials = _visual.Variants;
            _runClip = _visual.FindClip(HordeClipKind.Run);
            _walkClip = _visual.FindClip(HordeClipKind.Walk);
            if (_runClip < 0) _runClip = _walkClip;
            _deathClip = _visual.FindClip(HordeClipKind.Death);
            _firstAttack = _visual.FindClip(HordeClipKind.Attack);
            _attackCount = _visual.CountClips(HordeClipKind.Attack);

            var lighting = _level.VisualTheme != null ? _level.VisualTheme.Lighting : null;
            if (lighting != null && lighting.BlobShadowMaterial != null)
            {
                _shadowMaterial = new Material(lighting.BlobShadowMaterial) { enableInstancing = true, name = "Horde Blob Shadow" };
                _shadowSize = lighting.BlobShadowSize;
                _shadowMesh = CreateQuad();
            }

            var variants = _materials.Length;
            var perVariant = (_horde.Capacity + BatchSize - 1) / BatchSize;
            _batches = new Batch[variants * perVariant];
            for (var i = 0; i < _batches.Length; i++)
                _batches[i] = new Batch();
            _batchFill = new int[variants];
            _batchOf = new int[variants];

            var size = new Vector3(_level.Geometry.Width + 4f, 6f, _level.Geometry.Height + 4f);
            _worldBounds = new Bounds(new Vector3(_level.Geometry.Width * 0.5f, 1f, _level.Geometry.Height * 0.5f), size);
            UseVat = false;
            Interpolate = false;
            return UniTask.CompletedTask;
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (!IsActive) return;
            foreach (var material in _materials)
            {
                var go = new GameObject("Horde Warmup");
                go.transform.position = new Vector3(_level.Geometry.Width * 0.5f, 0f, _level.Geometry.Height * 0.5f);
                go.AddComponent<MeshFilter>().sharedMesh = _visual.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                go.SetActive(false);
                _warmupObjects.Add(go);
                objects.Add(go);
            }
        }

        public void LateTick(float deltaTime)
        {
            if (!IsActive)
                return;

            var started = Stopwatch.GetTimestamp();
            DrawnCount = 0;
            DrawCalls = 0;
            if (!Enabled)
            {
                RenderMs = 0f;
                return;
            }

            var rect = GroundRect();
            var positions = _horde.Positions;
            var facing = _horde.Facing;
            var states = _horde.States;
            var stateStart = _horde.StateStart;
            var attackStart = _horde.AttackStart;
            var hitTime = _horde.HitTime;
            var variants = _horde.Variants;
            var time = _horde.Time;
            var scale = Vector3.one * _visual.Scale;
            var interval = _horde.Stats.AttackInterval;
            var count = _horde.HighWater;
            var variantCount = _materials.Length;

            _batchesUsed = 0;
            for (var v = 0; v < variantCount; v++)
                _batchOf[v] = -1;

            for (var i = 0; i < count; i++)
            {
                var state = states[i];
                if (state == HordeAgentState.Inactive) continue;
                var p = positions[i];
                if (p.x < rect.x || p.x > rect.z || p.y < rect.y || p.y > rect.w) continue;
                if (CullByVisibility && _visibility.HasResult &&
                    !_visibility.IsVisible(new GridPosition(Mathf.FloorToInt(p.x + 0.5f), Mathf.FloorToInt(p.y + 0.5f))))
                    continue;

                int clip;
                float clipTime;
                switch (state)
                {
                    case HordeAgentState.Attacking when _attackCount > 0:
                        clip = _firstAttack + i % _attackCount;
                        clipTime = (time - attackStart[i]) / interval * _visual.Clips[clip].Length;
                        break;
                    case HordeAgentState.Dying when _deathClip >= 0:
                        clip = _deathClip;
                        clipTime = time - stateStart[i];
                        break;
                    default:
                        clip = _runClip;
                        // Phase per slot: the crowd does not step in sync.
                        clipTime = (time - stateStart[i]) * RunPlayback + i * 0.377f;
                        break;
                }

                var frame = FrameOf(clip, clipTime);
                frame.w = Mathf.Clamp01(1f - (time - hitTime[i]) / FlashTime);
                if (state == HordeAgentState.Dying) frame.w = 0f;

                var variant = variants[i] % variantCount;
                var batch = CurrentBatch(variant);
                var slot = _batchFill[variant]++;
                var world = new Vector3(p.x, 0f, p.y);
                batch.Matrices[slot] = Matrix4x4.TRS(world, Quaternion.Euler(0f, facing[i] * Mathf.Rad2Deg, 0f), scale);
                batch.Frames[slot] = frame;
                if (_shadowMaterial != null && state != HordeAgentState.Dying)
                {
                    batch.Shadows[batch.ShadowCount++] = Matrix4x4.TRS(new Vector3(p.x, ShadowHeight, p.y),
                        Quaternion.identity, Vector3.one * _shadowSize);
                }

                batch.Count = slot + 1;
                DrawnCount++;
            }

            for (var b = 0; b < _batchesUsed; b++)
                Draw(_batches[b]);

            RenderMs = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        }

        public void Dispose()
        {
            SetKeyword(VatKeyword, false);
            SetKeyword(LerpKeyword, false);
            UnityObjects.Destroy(_shadowMaterial);
            UnityObjects.Destroy(_shadowMesh);
            foreach (var go in _warmupObjects)
                UnityObjects.Destroy(go);
            _warmupObjects.Clear();
            _shadowMaterial = null;
            _shadowMesh = null;
        }

        private Batch CurrentBatch(int variant)
        {
            var index = _batchOf[variant];
            if (index >= 0 && _batchFill[variant] < BatchSize)
                return _batches[index];

            index = _batchesUsed++;
            _batchOf[variant] = index;
            _batchFill[variant] = 0;
            var batch = _batches[index];
            batch.Variant = variant;
            batch.Count = 0;
            batch.ShadowCount = 0;
            return batch;
        }

        private void Draw(Batch batch)
        {
            if (batch.Count == 0) return;
            batch.Properties.SetVectorArray(FrameProperty, batch.Frames);
            var renderParams = new RenderParams(_materials[batch.Variant])
            {
                worldBounds = _worldBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                matProps = batch.Properties,
            };
            Graphics.RenderMeshInstanced(renderParams, _visual.Mesh, 0, batch.Matrices, batch.Count);
            DrawCalls++;

            if (!Shadows || _shadowMaterial == null || batch.ShadowCount == 0) return;
            var shadowParams = new RenderParams(_shadowMaterial)
            {
                worldBounds = _worldBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
            Graphics.RenderMeshInstanced(shadowParams, _shadowMesh, 0, batch.Shadows, batch.ShadowCount);
            DrawCalls++;
        }

        /// <summary>(frame A, frame B, blend, 0) of the clip at <paramref name="time"/> seconds.</summary>
        private Vector4 FrameOf(int clipIndex, float time)
        {
            if (clipIndex < 0) return Vector4.zero;
            var clip = _visual.Clips[clipIndex];
            var position = time * _visual.FrameRate;
            int a, b;
            float blend;
            if (clip.Loop)
            {
                var length = clip.FrameCount;
                position %= length;
                if (position < 0f) position += length;
                a = (int)position;
                blend = position - a;
                b = (a + 1) % length;
            }
            else
            {
                var last = clip.FrameCount - 1;
                position = Mathf.Clamp(position, 0f, last);
                a = (int)position;
                blend = position - a;
                b = Mathf.Min(a + 1, last);
            }

            if (!_interpolate && blend >= 0.5f)
                a = b;
            return new Vector4(clip.FirstFrame + a, clip.FirstFrame + b, blend, 0f);
        }

        /// <summary>Ground area (grid units: minX, minY, maxX, maxY) seen by the camera, with a margin.</summary>
        private Vector4 GroundRect()
        {
            var camera = _camera != null ? _camera.Camera : null;
            if (camera == null)
                return new Vector4(float.MinValue, float.MinValue, float.MaxValue, float.MaxValue);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (var c = 0; c < 4; c++)
            {
                var ray = camera.ViewportPointToRay(new Vector3(c & 1, c >> 1, 0f));
                if (ray.direction.y > -1e-4f)
                    return new Vector4(float.MinValue, float.MinValue, float.MaxValue, float.MaxValue);
                var t = -ray.origin.y / ray.direction.y;
                var hit = ray.origin + ray.direction * t;
                minX = Mathf.Min(minX, hit.x);
                maxX = Mathf.Max(maxX, hit.x);
                minY = Mathf.Min(minY, hit.z);
                maxY = Mathf.Max(maxY, hit.z);
            }

            return new Vector4(minX - CullMargin, minY - CullMargin, maxX + CullMargin, maxY + CullMargin);
        }

        private static void SetKeyword(string keyword, bool enabled)
        {
            if (enabled) Shader.EnableKeyword(keyword);
            else Shader.DisableKeyword(keyword);
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Horde Blob Shadow", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class Batch
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[BatchSize];
            public readonly Vector4[] Frames = new Vector4[BatchSize];
            public readonly Matrix4x4[] Shadows = new Matrix4x4[BatchSize];
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
            public int Variant;
            public int Count;
            public int ShadowCount;
        }
    }
}
