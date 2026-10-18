using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Visibility;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual.Horde
{
    /// <summary>
    /// Draws the horde (<see cref="HordeSystem"/>) with GPU instancing: no GameObject per monster. Every kind of
    /// monster has its look (<see cref="HordeVisualDefinition"/> by monster id, <see cref="StressTestDefinition.VisualOf"/>);
    /// every late tick the monsters on screen and in the player's view (hidden ones keep simulating, ТЗ §57) are packed
    /// per look (kind × material variant) into batches of <see cref="BatchSize"/>: a matrix and the animation frame per
    /// instance (shader Purge/Horde reads the baked animation). The clip follows the gameplay state: run at the monster's
    /// speed, walk (wanderers, <see cref="HordePace.Walk"/>), idle (standing, roaring), attack, hit reaction while staggered, stun, climbing out of the ground, death by the side the
    /// killing push came from. Submeshes past the body (glued equipment) — one more draw per batch. Blob shadows — the
    /// same batches with a quad. Allocation-free per frame.
    /// </summary>
    public sealed class HordeRenderer : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        public const int BatchSize = 128;
        private const string VatKeyword = "PURGE_HORDE_VAT";
        private const string LerpKeyword = "PURGE_HORDE_LERP";
        private const float FlashTime = 0.12f;
        private const float ShadowHeight = 0.02f;
        private const float CullMargin = 1.5f;
        private const float MinPlayback = 0.5f;
        private const float MaxPlayback = 2f;

        /// <summary>A killing push weaker than this (cells/s) picks any death clip; stronger — by its side.</summary>
        private const float SidePush = 1.5f;

        /// <summary>A new hit restarts the hit reaction once this much of it has played (seconds).</summary>
        private const float ReactRestart = 0.3f;

        private static readonly int FrameProperty = Shader.PropertyToID("_HordeFrame");
        private static readonly int GlowProperty = Shader.PropertyToID("_HordeGlow");

        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly VisibilitySystem _visibility;
        private readonly TopDownCamera _camera;
        private readonly List<GameObject> _warmupObjects = new List<GameObject>();

        private Look[] _looks;
        private Clips[] _kindClips;    // per kind (null = not drawn)
        private Color[] _kindSplash;   // per kind: colour of its splashes (baked or Animator look)
        private int[] _kindLookStart;  // per kind: its first look
        private int[] _kindLookCount;  // per kind: its material variants
        private Material _shadowMaterial;
        private float _shadowSize;
        private Mesh _shadowMesh;
        private Batch[] _batches;
        private int[] _batchFill; // per look: instances in its current batch
        private int[] _batchOf;   // per look: index of its current batch in _batches
        private float[] _flash;   // per slot: strength of the hit flash (the last weapon's feel; 1 until set)
        private float[] _dent;    // per slot: squash of the last hit, share of the size
        private float[] _phase;   // per slot: seconds into the locomotion clip at its playback
        private float[] _reactStart; // per slot: when the hit reaction began (-1 = not reacting)
        private int[] _deathClip; // per slot: the chosen death clip
        private int[] _seenGeneration; // per slot: generation the per-slot look state belongs to
        private bool[] _seenDying;
        private Vector4[] _affixGlow; // per affix code (0 = not a champion): colour and strength of the glow
        private float _championScale = 1f;
        private float _lastTime;

        /// <summary>Seconds a hit's dent lasts (squash, then back).</summary>
        public const float DentTime = 0.16f;

        /// <summary>
        /// How the next flashes and dents of the monster in <paramref name="agent"/> look (the weapon's feel); damage
        /// without a weapon (zones, states) keeps the last ones.
        /// </summary>
        public void SetHitLook(int agent, float flash, float dent)
        {
            if (_flash == null || (uint)agent >= (uint)_flash.Length) return;
            _flash[agent] = Mathf.Clamp01(flash);
            _dent[agent] = Mathf.Clamp(dent, 0f, 0.5f);
        }

        private int _batchesUsed;
        private Bounds _worldBounds;
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

        /// <summary>The first kind's look (null until loaded or without a horde): the stress overlay's numbers.</summary>
        public HordeVisualDefinition Visual { get; private set; }

        /// <summary>Draw the monsters at all (off = measure the simulation alone).</summary>
        public bool Enabled { get; set; } = true;

        public bool Shadows { get; set; } = true;

        /// <summary>Skip monsters outside the player's view (as the game does); off = draw everyone on screen.</summary>
        public bool CullByVisibility { get; set; } = true;

        /// <summary>Vertex animation texture instead of bone skinning in the shader (stress test; looks without one break).</summary>
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

        /// <summary>Playback of the walk/run clip of looks without a measured ground speed.</summary>
        public float RunPlayback { get; set; } = 1f;

        public int DrawnCount { get; private set; }
        public int DrawCalls { get; private set; }
        public float RenderMs { get; private set; }

        /// <summary>Vertices of one monster as drawn (welded mesh) and in the source model (first kind).</summary>
        public int MeshVertexCount => Visual != null && Visual.Mesh != null ? Visual.Mesh.vertexCount : 0;
        public int SourceVertexCount => Visual != null ? Visual.SourceVertexCount : 0;

        /// <summary>Colour of the splashes of the monster in <paramref name="agent"/> (its look's).</summary>
        public Color SplashColorOf(int agent)
        {
            if (_kindSplash != null && _horde.IsActive && (uint)agent < (uint)_horde.Capacity)
                return _kindSplash[_horde.Kinds[agent]];

            return Visual != null ? Visual.SplashColor : new Color(0.55f, 0.85f, 0.25f, 1f);
        }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var settings = _level.StressTest;
            if (!_horde.IsActive || settings == null)
                return UniTask.CompletedTask;

            var kinds = _horde.KindList;
            var looks = new List<Look>();
            var byVisual = new Dictionary<HordeVisualDefinition, Clips>();
            _kindClips = new Clips[kinds.Count];
            _kindLookStart = new int[kinds.Count];
            _kindLookCount = new int[kinds.Count];
            _kindSplash = new Color[kinds.Count];
            for (var k = 0; k < kinds.Count; k++)
            {
                _kindSplash[k] = new Color(0.55f, 0.85f, 0.25f, 1f);
                // Officers, elite, bosses: Animator (MonsterViewPresenter).
                var view = settings.ViewOf(kinds[k].Definition);
                if (view != null)
                {
                    _kindSplash[k] = view.SplashColor;
                    continue;
                }

                var visual = settings.VisualOf(kinds[k].Definition);
                if (visual == null || visual.Mesh == null || visual.Variants.Length == 0)
                {
                    GameLog.Warning(LogChannel.Gameplay,
                        $"Horde: no look for monster '{kinds[k].Definition?.Id ?? "built-in"}' (Build Monsters) — not drawn.");
                    continue;
                }

                if (!byVisual.TryGetValue(visual, out var clips))
                {
                    clips = new Clips(visual);
                    byVisual.Add(visual, clips);
                }

                Visual ??= visual;
                _kindSplash[k] = visual.SplashColor;
                _kindClips[k] = clips;
                _kindLookStart[k] = looks.Count;
                _kindLookCount[k] = visual.Variants.Length;
                foreach (var material in visual.Variants)
                    looks.Add(new Look(visual, material));
            }

            if (looks.Count == 0)
                return UniTask.CompletedTask;
            _looks = looks.ToArray();

            var lighting = _level.VisualTheme != null ? _level.VisualTheme.Lighting : null;
            if (lighting != null && lighting.BlobShadowMaterial != null)
            {
                _shadowMaterial = new Material(lighting.BlobShadowMaterial) { enableInstancing = true, name = "Horde Blob Shadow" };
                _shadowSize = lighting.BlobShadowSize;
                _shadowMesh = CreateQuad();
            }

            var capacity = _horde.Capacity;
            var perLook = (capacity + BatchSize - 1) / BatchSize;
            _batches = new Batch[_looks.Length * perLook];
            for (var i = 0; i < _batches.Length; i++)
                _batches[i] = new Batch();
            _batchFill = new int[_looks.Length];
            _batchOf = new int[_looks.Length];
            _flash = new float[capacity];
            _dent = new float[capacity];
            _phase = new float[capacity];
            _reactStart = new float[capacity];
            _deathClip = new int[capacity];
            _seenGeneration = new int[capacity];
            _seenDying = new bool[capacity];
            for (var i = 0; i < capacity; i++)
            {
                _flash[i] = 1f;
                _phase[i] = i * 0.377f; // the crowd does not step in sync
                _reactStart[i] = -1f;
                _seenGeneration[i] = -1;
            }

            // Champions (stage 5e2): the affix's glow, a bigger look.
            var champions = _horde.Champions;
            var affixes = champions != null ? champions.Affixes : Array.Empty<Core.Monsters.ChampionAffixDefinition>();
            _affixGlow = new Vector4[affixes.Length + 1];
            for (var a = 0; a < affixes.Length; a++)
                if (affixes[a] != null)
                {
                    // A vector array is not converted like a colour: into the project's colour space here.
                    var glow = affixes[a].Glow;
                    var rgb = QualitySettings.activeColorSpace == ColorSpace.Linear ? glow.linear : glow;
                    _affixGlow[a + 1] = new Vector4(rgb.r, rgb.g, rgb.b, glow.a);
                }
            _championScale = champions != null ? champions.Scale : 1f;

            var size = new Vector3(_level.Geometry.Width + 4f, 6f, _level.Geometry.Height + 4f);
            _worldBounds = new Bounds(new Vector3(_level.Geometry.Width * 0.5f, 1f, _level.Geometry.Height * 0.5f), size);
            UseVat = false;
            Interpolate = false;
            return UniTask.CompletedTask;
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (!IsActive) return;
            var center = new Vector3(_level.Geometry.Width * 0.5f, 0f, _level.Geometry.Height * 0.5f);
            foreach (var look in _looks)
            {
                var go = new GameObject("Horde Warmup");
                go.transform.position = center;
                go.AddComponent<MeshFilter>().sharedMesh = look.Visual.Mesh;
                var materials = new Material[look.Visual.Mesh.subMeshCount];
                for (var s = 0; s < materials.Length; s++)
                    materials[s] = s == 0 ? look.Material : look.Extra(s);
                go.AddComponent<MeshRenderer>().sharedMaterials = materials;
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
            var time = _horde.Time;
            var step = Mathf.Max(0f, time - _lastTime);
            _lastTime = time;
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
            var stunned = _horde.Stunned;
            var paces = _horde.Paces;
            var variants = _horde.Variants;
            var kinds = _horde.Kinds;
            var generations = _horde.Generations;
            var speeds = _horde.Speeds;
            var speedFactors = _horde.SpeedFactors;
            var pushes = _horde.PushVelocities;
            var affixCodes = _horde.Affixes;
            var count = _horde.HighWater;

            _batchesUsed = 0;
            for (var l = 0; l < _looks.Length; l++)
                _batchOf[l] = -1;

            for (var i = 0; i < count; i++)
            {
                var state = states[i];
                if (state == HordeAgentState.Inactive) continue;
                var kind = kinds[i];
                var clips = _kindClips[kind];
                if (clips == null) continue;

                if (_seenGeneration[i] != generations[i])
                {
                    // A new monster in the slot: its own look state.
                    _seenGeneration[i] = generations[i];
                    _seenDying[i] = false;
                    _reactStart[i] = -1f;
                }

                var stats = _horde.KindList[kind].Stats;
                var pace = state == HordeAgentState.Moving && !stunned[i] ? paces[i] : HordePace.Still;
                var walking = pace == HordePace.Walk && clips.Walk.Length > 0;
                if (pace == HordePace.Run)
                {
                    // The phase runs at the monster's speed: the feet do not slide.
                    var playback = clips.LocomotionSpeed > 0f
                        ? Mathf.Clamp(speeds[i] * speedFactors[i] / clips.LocomotionSpeed, MinPlayback, MaxPlayback)
                        : RunPlayback;
                    _phase[i] += step * playback;
                }
                else if (walking)
                {
                    _phase[i] += step * Mathf.Clamp(stats.WanderSpeed * speedFactors[i] / MonsterStats.WalkClipSpeed,
                        MinPlayback, MaxPlayback);
                }

                var p = positions[i];
                if (p.x < rect.x || p.x > rect.z || p.y < rect.y || p.y > rect.w) continue;
                if (CullByVisibility && _visibility.HasResult &&
                    !_visibility.IsVisible(new GridPosition(Mathf.FloorToInt(p.x + 0.5f), Mathf.FloorToInt(p.y + 0.5f))))
                    continue;

                var reacting = state != HordeAgentState.Dying && !stunned[i] && clips.HitReact.Length > 0 &&
                               _horde.IsStaggered(i);
                if (!reacting) _reactStart[i] = -1f;
                else if (_reactStart[i] < 0f || (time - _reactStart[i] >= ReactRestart && hitTime[i] > _reactStart[i]))
                    _reactStart[i] = time;

                int clip;
                float clipTime;
                if (state == HordeAgentState.Dying)
                {
                    if (!_seenDying[i])
                    {
                        _seenDying[i] = true;
                        _deathClip[i] = clips.DeathFor(pushes[i], facing[i], i + generations[i]);
                    }

                    clip = _deathClip[i];
                    clipTime = time - stateStart[i];
                }
                else if (state == HordeAgentState.Emerging && clips.Emerge >= 0)
                {
                    // Fits the clip into the emerge time (the kind's, or a raise's).
                    clip = clips.Emerge;
                    var length = clips.Visual.Clips[clip].Length;
                    var emergeTime = _horde.EmergeTimeOf(i);
                    clipTime = (time - stateStart[i]) * (emergeTime > 0f ? length / emergeTime : 1f);
                }
                else if (stunned[i] && (clips.Stun >= 0 || clips.Idle >= 0))
                {
                    clip = clips.Stun >= 0 ? clips.Stun : clips.Idle;
                    clipTime = time + i * 0.377f;
                }
                else if (reacting)
                {
                    clip = clips.HitReact[(i + generations[i] + (int)(_reactStart[i] * 7f)) % clips.HitReact.Length];
                    clipTime = time - _reactStart[i];
                }
                else if (state == HordeAgentState.Attacking && clips.Attack.Length > 0)
                {
                    clip = clips.Attack[(i + generations[i]) % clips.Attack.Length];
                    clipTime = (time - attackStart[i]) / _horde.AttackIntervalOf(i) * clips.Visual.Clips[clip].Length;
                }
                else if (walking)
                {
                    clip = clips.Walk[(i + generations[i]) % clips.Walk.Length];
                    clipTime = _phase[i];
                }
                else if (clips.Locomotion.Length > 0 && (pace != HordePace.Still || clips.Idle < 0))
                {
                    clip = clips.Locomotion[(i + generations[i]) % clips.Locomotion.Length];
                    clipTime = _phase[i];
                }
                else
                {
                    clip = clips.Idle;
                    clipTime = time + i * 0.377f;
                }

                var frame = FrameOf(clips.Visual, clip, clipTime);
                var sinceHit = time - hitTime[i];
                frame.w = Mathf.Clamp01(1f - sinceHit / FlashTime) * _flash[i];
                var scale = clips.Visual.Scale;
                var glow = Vector4.zero;
                var affix = affixCodes[i];
                if (affix != 0 && affix < _affixGlow.Length)
                {
                    scale *= _championScale;
                    // A slow pulse, out of step between champions.
                    if (state != HordeAgentState.Dying)
                    {
                        glow = _affixGlow[affix];
                        glow.w *= 0.8f + 0.2f * Mathf.Sin(time * 5f + i);
                    }
                }

                var size = new Vector3(scale, scale, scale);
                if (state == HordeAgentState.Dying) frame.w = 0f;
                else if (sinceHit < DentTime && _dent[i] > 0f)
                {
                    // Squashed at once, back in a smooth bulge: lower and wider, as if punched.
                    var dent = _dent[i] * Mathf.Sin((1f - sinceHit / DentTime) * Mathf.PI * 0.5f);
                    size = new Vector3(scale * (1f + dent * 0.5f), scale * (1f - dent), scale * (1f + dent * 0.5f));
                }

                var look = _kindLookStart[kind] + variants[i] % _kindLookCount[kind];
                var batch = CurrentBatch(look);
                var slot = _batchFill[look]++;
                var world = new Vector3(p.x, 0f, p.y);
                batch.Matrices[slot] = Matrix4x4.TRS(world, Quaternion.Euler(0f, facing[i] * Mathf.Rad2Deg, 0f), size);
                batch.Frames[slot] = frame;
                batch.Glows[slot] = glow;
                if (_shadowMaterial != null && state != HordeAgentState.Dying)
                {
                    batch.Shadows[batch.ShadowCount++] = Matrix4x4.TRS(new Vector3(p.x, ShadowHeight, p.y),
                        Quaternion.identity, Vector3.one * (_shadowSize * stats.Radius / 0.3f * (affix != 0 ? _championScale : 1f)));
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

        private Batch CurrentBatch(int look)
        {
            var index = _batchOf[look];
            if (index >= 0 && _batchFill[look] < BatchSize)
                return _batches[index];

            index = _batchesUsed++;
            _batchOf[look] = index;
            _batchFill[look] = 0;
            var batch = _batches[index];
            batch.Look = look;
            batch.Count = 0;
            batch.ShadowCount = 0;
            return batch;
        }

        private void Draw(Batch batch)
        {
            if (batch.Count == 0) return;
            var look = _looks[batch.Look];
            var mesh = look.Visual.Mesh;
            batch.Properties.SetVectorArray(FrameProperty, batch.Frames);
            batch.Properties.SetVectorArray(GlowProperty, batch.Glows);
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var material = s == 0 ? look.Material : look.Extra(s);
                if (material == null) continue;
                var renderParams = new RenderParams(material)
                {
                    worldBounds = _worldBounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                    matProps = batch.Properties,
                };
                Graphics.RenderMeshInstanced(renderParams, mesh, s, batch.Matrices, batch.Count);
                DrawCalls++;
            }

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
        private Vector4 FrameOf(HordeVisualDefinition visual, int clipIndex, float time)
        {
            if (clipIndex < 0) return Vector4.zero;
            var clip = visual.Clips[clipIndex];
            var position = time * (clip.FrameRate > 0f ? clip.FrameRate : visual.FrameRate);
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

        /// <summary>A material variant of a look: one instanced draw per batch (plus the equipment's submeshes).</summary>
        private readonly struct Look
        {
            public readonly HordeVisualDefinition Visual;
            public readonly Material Material;

            public Look(HordeVisualDefinition visual, Material material)
            {
                Visual = visual;
                Material = material;
            }

            /// <summary>Material of submesh <paramref name="submesh"/> ≥ 1 (null = not drawn).</summary>
            public Material Extra(int submesh)
            {
                var extra = Visual.ExtraMaterials;
                return submesh - 1 < extra.Length ? extra[submesh - 1] : null;
            }
        }

        /// <summary>A look's clips by what they show.</summary>
        private sealed class Clips
        {
            public readonly HordeVisualDefinition Visual;
            public readonly int[] Locomotion;
            public readonly int[] Walk;
            public readonly int[] Attack;
            public readonly int[] HitReact;
            public readonly int Idle;
            public readonly int Stun;
            public readonly int Emerge;
            public readonly float LocomotionSpeed;
            private readonly int[] _deaths;
            private readonly int[][] _deathsBySide;

            public Clips(HordeVisualDefinition visual)
            {
                Visual = visual;
                Locomotion = visual.ClipsOf(HordeClipKind.Run);
                Walk = visual.ClipsOf(HordeClipKind.Walk);
                if (Locomotion.Length == 0) Locomotion = Walk;
                Attack = visual.ClipsOf(HordeClipKind.Attack);
                HitReact = visual.ClipsOf(HordeClipKind.HitReact);
                Idle = visual.FindClip(HordeClipKind.Idle);
                Stun = visual.FindClip(HordeClipKind.Stun);
                Emerge = visual.FindClip(HordeClipKind.Emerge);
                LocomotionSpeed = visual.LocomotionSpeed;
                _deaths = visual.ClipsOf(HordeClipKind.Death);
                _deathsBySide = new int[5][];
                var sides = new List<int>();
                for (var side = 0; side < _deathsBySide.Length; side++)
                {
                    sides.Clear();
                    foreach (var death in _deaths)
                        if ((int)visual.Clips[death].Side == side)
                            sides.Add(death);
                    _deathsBySide[side] = sides.ToArray();
                }
            }

            /// <summary>
            /// The death clip: a strong killing push picks the side it came from (pushed backwards = hit in the front);
            /// otherwise any, by slot.
            /// </summary>
            public int DeathFor(Vector2 push, float facing, int seed)
            {
                if (_deaths.Length == 0) return -1;
                if (push.sqrMagnitude >= SidePush * SidePush)
                {
                    var from = -push; // the hit came from the opposite side of the push
                    var forward = new Vector2(Mathf.Sin(facing), Mathf.Cos(facing));
                    var right = new Vector2(forward.y, -forward.x);
                    var f = Vector2.Dot(from, forward);
                    var r = Vector2.Dot(from, right);
                    var side = Mathf.Abs(f) >= Mathf.Abs(r)
                        ? f >= 0f ? HordeClipSide.Front : HordeClipSide.Back
                        : r >= 0f ? HordeClipSide.Right : HordeClipSide.Left;
                    var matching = _deathsBySide[(int)side];
                    if (matching.Length > 0) return matching[seed % matching.Length];
                }

                return _deaths[(seed & int.MaxValue) % _deaths.Length];
            }
        }

        private sealed class Batch
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[BatchSize];
            public readonly Vector4[] Frames = new Vector4[BatchSize];
            public readonly Vector4[] Glows = new Vector4[BatchSize];
            public readonly Matrix4x4[] Shadows = new Matrix4x4[BatchSize];
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
            public int Look;
            public int Count;
            public int ShadowCount;
        }
    }
}
