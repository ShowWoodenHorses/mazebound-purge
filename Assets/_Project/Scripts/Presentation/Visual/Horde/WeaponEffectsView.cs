using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using Purge.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual.Horde
{
    /// <summary>
    /// Draft looks of the player's attacks that have no effect prefabs yet (stage 4а; the feel comes with 4в): the flame
    /// of a stream, the bolts of chain lightning (one dynamic mesh of jagged quads), explosions and sparks where the horde
    /// is hit, the blasts of thrown items (<see cref="EffectSystem.Exploded"/>), flames on burning monsters and stars over
    /// stunned ones (<see cref="StatusSystem"/>, in the states' colours). Particles are fed by <c>Emit</c>, only in visible
    /// cells; nothing is created per effect. Materials — <see cref="CombatVisualDefinition.BoltMaterial"/> and
    /// <see cref="CombatVisualDefinition.EffectParticles"/> (Build Combat Effects; on every level since stage 4в), else
    /// those of the sandbox settings; off without either.
    /// </summary>
    public sealed class WeaponEffectsView : ILevelLoadStep, ILevelTickable, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private const int MaxBoltQuads = 256;
        private const int SegmentsPerJump = 5;
        private const float BoltTime = 0.12f;
        private const float BoltWidth = 0.09f;
        private const float GunHeight = 0.75f;
        private const float StatusInterval = 0.12f;

        private readonly LevelData _level;
        private readonly PlayerCombat _combat;
        private readonly PlayerSystem _player;
        private readonly BulletSystem _bullets;
        private readonly CombatTargets _targets;
        private readonly VisibilitySystem _visibility;
        private readonly LevelViewRoot _root;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly Vector2[] _bolt = new Vector2[PlayerCombat.MaxChainPoints];
        private readonly EffectSystem _effects;
        private readonly StatusSystem _statuses;
        private readonly HordeSystem _horde;
        private readonly CombatVisualDefinition _visual;
        private readonly PackEffectsPlayer _packEffects;
        private float _statusTimer;

        private Material _boltMaterial;
        private Mesh _boltMesh;
        private Vector3[] _boltVertices;
        private int[] _boltIndices;
        private ParticleSystem _sparks;
        private ParticleSystem _flame;
        private ParticleSystem _blast;
        private Bounds _bounds;
        private bool _bound;
        private int _boltCount;
        private float _boltLeft;

        public WeaponEffectsView(LevelData level, PlayerCombat combat, PlayerSystem player, BulletSystem bullets,
            CombatTargets targets, VisibilitySystem visibility, LevelViewRoot root, EffectSystem effects = null,
            StatusSystem statuses = null, HordeSystem horde = null, CombatVisualDefinition visual = null,
            PackEffectsPlayer packEffects = null)
        {
            _visual = visual;
            _packEffects = packEffects;
            _effects = effects;
            _statuses = statuses;
            _horde = horde;
            _level = level;
            _combat = combat;
            _player = player;
            _bullets = bullets;
            _targets = targets;
            _visibility = visibility;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var settings = _level.StressTest;
            var sandbox = settings != null && settings.IsSandbox ? settings : null;
            var boltMaterial = _visual != null && _visual.BoltMaterial != null ? _visual.BoltMaterial : sandbox?.TracerMaterial;
            var particles = _visual != null && _visual.EffectParticles != null ? _visual.EffectParticles : sandbox?.ParticleMaterial;
            if (boltMaterial == null && particles == null)
                return UniTask.CompletedTask;

            var width = _level.Geometry.Width;
            var height = _level.Geometry.Height;
            _bounds = new Bounds(new Vector3(width * 0.5f, 1f, height * 0.5f), new Vector3(width + 4f, 4f, height + 4f));

            _boltMaterial = boltMaterial;
            if (_boltMaterial != null)
            {
                _boltVertices = new Vector3[MaxBoltQuads * 4];
                _boltIndices = new int[MaxBoltQuads * 6];
                for (var q = 0; q < MaxBoltQuads; q++)
                {
                    int v = q * 4, i = q * 6;
                    _boltIndices[i] = v;
                    _boltIndices[i + 1] = v + 1;
                    _boltIndices[i + 2] = v + 2;
                    _boltIndices[i + 3] = v;
                    _boltIndices[i + 4] = v + 2;
                    _boltIndices[i + 5] = v + 3;
                }

                var uvs = new Vector2[_boltVertices.Length];
                for (var q = 0; q < MaxBoltQuads; q++)
                {
                    uvs[q * 4] = new Vector2(0f, 0f);
                    uvs[q * 4 + 1] = new Vector2(0f, 1f);
                    uvs[q * 4 + 2] = new Vector2(1f, 1f);
                    uvs[q * 4 + 3] = new Vector2(1f, 0f);
                }

                _boltMesh = new Mesh { name = "Chain Bolts", hideFlags = HideFlags.DontSave };
                _boltMesh.MarkDynamic();
                _boltMesh.vertices = _boltVertices;
                _boltMesh.uv = uvs;
                _boltMesh.SetIndices(_boltIndices, MeshTopology.Triangles, 0, false);
                _boltMesh.bounds = _bounds;
            }

            if (particles != null)
            {
                _sparks = CreateParticles("Hit Sparks", particles, 600, new Color(1f, 0.85f, 0.4f), 0.25f, 0.12f);
                _flame = CreateParticles("Stream Flame", particles, 500, new Color(1f, 0.55f, 0.15f), 0.4f, 0.5f);
                _blast = CreateParticles("Explosions", particles, 600, new Color(1f, 0.7f, 0.25f), 0.5f, 0.6f);
            }

            _combat.Attacked += OnAttacked;
            _bullets.Exploded += OnExploded;
            if (_effects != null) _effects.Exploded += OnExploded;
            _targets.Damaged += OnDamaged;
            _bound = true;
            return UniTask.CompletedTask;
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_objects);

        public void Tick(float deltaTime)
        {
            // The level warmup stops every particle system once it has drawn them.
            if (_sparks != null && !_sparks.isPlaying)
            {
                _sparks.Play();
                _flame.Play();
                _blast.Play();
            }

            if (_boltLeft > 0f) _boltLeft -= deltaTime;
            _statusTimer -= deltaTime;
            if (_statusTimer <= 0f)
            {
                _statusTimer += StatusInterval;
                EmitStatuses();
            }
        }

        /// <summary>A flame over every visible monster with damage over time, a spark circling a stunned one.</summary>
        private void EmitStatuses()
        {
            if (_statuses == null || _horde == null || !_statuses.IsActive || _sparks == null) return;
            var kinds = _statuses.Kinds;
            var positions = _horde.Positions;
            var states = _horde.States;
            var emit = new ParticleSystem.EmitParams();
            for (var agent = 0; agent < _horde.HighWater; agent++)
            {
                if (!HordeSystem.IsAlive(states[agent])) continue;
                for (var kind = 0; kind < kinds.Count; kind++)
                {
                    if (_statuses.Left(agent, kind) <= 0f) continue;
                    var at = positions[agent];
                    if (!Visible(at)) break;
                    var status = kinds[kind];
                    emit.startColor = status.Color;
                    if (status.Stuns)
                    {
                        var angle = Time.time * 8f + agent;
                        emit.position = new Vector3(at.x + Mathf.Cos(angle) * 0.3f, 1.5f, at.y + Mathf.Sin(angle) * 0.3f);
                        emit.velocity = Vector3.zero;
                        emit.startLifetime = 0.2f;
                        emit.startSize = 0.14f;
                        _sparks.Emit(emit, 1);
                    }
                    else if (_flame != null && status.DamagePerSecond > 0f)
                    {
                        emit.position = new Vector3(at.x + UnityEngine.Random.Range(-0.2f, 0.2f), 0.6f,
                            at.y + UnityEngine.Random.Range(-0.2f, 0.2f));
                        emit.velocity = Vector3.up * 1.6f;
                        emit.startLifetime = 0.35f;
                        emit.startSize = 0.35f;
                        _flame.Emit(emit, 1);
                    }
                }
            }
        }

        public void LateTick(float deltaTime)
        {
            if (_boltMesh == null || _boltLeft <= 0f || _boltCount < 2) return;

            var quads = 0;
            for (var p = 1; p < _boltCount; p++)
            {
                var from = _bolt[p - 1];
                var to = _bolt[p];
                var previous = from;
                for (var s = 1; s <= SegmentsPerJump && quads < MaxBoltQuads; s++)
                {
                    var t = (float)s / SegmentsPerJump;
                    var along = Vector2.Lerp(from, to, t);
                    var direction = (to - from).normalized;
                    // Jagged: every inner point jumps aside, new shape each frame.
                    var next = s < SegmentsPerJump
                        ? along + new Vector2(-direction.y, direction.x) * UnityEngine.Random.Range(-0.18f, 0.18f)
                        : to;
                    AddQuad(quads++, previous, next);
                    previous = next;
                }
            }

            _boltMesh.SetVertices(_boltVertices, 0, _boltVertices.Length,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _boltMesh.SetIndices(_boltIndices, 0, quads * 6, MeshTopology.Triangles, 0, false);
            Graphics.RenderMesh(new RenderParams(_boltMaterial)
            {
                worldBounds = _bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            }, _boltMesh, 0, Matrix4x4.identity);
        }

        public void Dispose()
        {
            if (_bound)
            {
                _combat.Attacked -= OnAttacked;
                _bullets.Exploded -= OnExploded;
                if (_effects != null) _effects.Exploded -= OnExploded;
                _targets.Damaged -= OnDamaged;
                _bound = false;
            }

            UnityObjects.Destroy(_boltMesh);
            foreach (var go in _objects)
                UnityObjects.Destroy(go);
            _objects.Clear();
        }

        private void OnAttacked(WeaponRuntime weapon, Vector2 direction)
        {
            switch (weapon.Definition.Attack)
            {
                case StreamAttack stream when _flame != null:
                    EmitFlame(stream, direction);
                    break;
                case ChainAttack _:
                    var points = _combat.ChainPoints;
                    _boltCount = points.Length;
                    for (var i = 0; i < _boltCount; i++) _bolt[i] = points[i];
                    if (_boltCount > 0) _bolt[0] += direction * 0.5f; // from the gun, not the chest
                    _boltLeft = BoltTime;
                    break;
            }
        }

        private void EmitFlame(StreamAttack stream, Vector2 aim)
        {
            var player = _player.Position;
            var emit = new ParticleSystem.EmitParams();
            var count = Mathf.CeilToInt(stream.Interval * 70f);
            for (var i = 0; i < count; i++)
            {
                var spread = (UnityEngine.Random.value - 0.5f) * stream.Angle * Mathf.Deg2Rad;
                var dir = new Vector2(aim.x * Mathf.Cos(spread) - aim.y * Mathf.Sin(spread),
                    aim.x * Mathf.Sin(spread) + aim.y * Mathf.Cos(spread));
                emit.position = new Vector3(player.x + aim.x * 0.5f, GunHeight * 0.8f, player.y + aim.y * 0.5f);
                emit.velocity = new Vector3(dir.x, 0.15f, dir.y) * (stream.Range / 0.4f);
                emit.startLifetime = 0.4f;
                emit.startSize = 0.35f;
                _flame.Emit(emit, 1);
            }
        }

        private void OnExploded(Vector2 at, float radius, IExplosionLook source)
        {
            // The pack effect of the source, else the default by size; the draft particles only without both.
            var look = source != null && CombatVisualDefinition.IsSet(source.Look) ? source.Look : _visual?.ExplosionOf(radius);
            if (look != null && _packEffects != null &&
                _packEffects.Play(look, new Vector3(at.x, 0f, at.y), Quaternion.identity, radius))
                return;
            if (_blast == null || !Visible(at)) return;
            var emit = new ParticleSystem.EmitParams { position = new Vector3(at.x, 0.4f, at.y) };
            var count = Mathf.Clamp(Mathf.RoundToInt(radius * 25f), 20, 120);
            for (var i = 0; i < count; i++)
            {
                var flat = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(0.4f, 1f);
                emit.velocity = new Vector3(flat.x, UnityEngine.Random.Range(0.1f, 0.6f), flat.y) * (radius / 0.45f);
                emit.startLifetime = 0.45f;
                emit.startSize = UnityEngine.Random.Range(0.4f, 0.9f);
                _blast.Emit(emit, 1);
            }
        }

        private void OnDamaged(CombatTarget target, Vector2 at, float damage, Vector2 direction, bool killed)
        {
            if (_sparks == null || target.Agent < 0 || !Visible(at)) return;
            var emit = new ParticleSystem.EmitParams
            {
                position = new Vector3(at.x - direction.x * 0.25f, GunHeight, at.y - direction.y * 0.25f),
                startLifetime = 0.25f,
            };
            var count = killed ? 8 : 3;
            for (var i = 0; i < count; i++)
            {
                emit.velocity = UnityEngine.Random.insideUnitSphere * 3f + Vector3.up;
                _sparks.Emit(emit, 1);
            }
        }

        private void AddQuad(int quad, Vector2 from, Vector2 to)
        {
            var direction = (to - from).normalized;
            var side = new Vector2(-direction.y, direction.x) * (BoltWidth * 0.5f);
            var v = quad * 4;
            _boltVertices[v] = new Vector3(from.x - side.x, GunHeight, from.y - side.y);
            _boltVertices[v + 1] = new Vector3(from.x + side.x, GunHeight, from.y + side.y);
            _boltVertices[v + 2] = new Vector3(to.x + side.x, GunHeight, to.y + side.y);
            _boltVertices[v + 3] = new Vector3(to.x - side.x, GunHeight, to.y - side.y);
        }

        private bool Visible(Vector2 point) =>
            _visibility.IsVisible(new GridPosition(Mathf.FloorToInt(point.x + 0.5f), Mathf.FloorToInt(point.y + 0.5f)));

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
    }
}
