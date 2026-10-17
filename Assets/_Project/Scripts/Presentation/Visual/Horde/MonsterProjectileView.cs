using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual.Horde
{
    /// <summary>
    /// Projectiles of the monsters (<see cref="MonsterAbilities"/>): each drawn as its shooter's
    /// <see cref="MonsterViewDefinition.ProjectileModel"/> (its first mesh and material, pointing along the flight) or,
    /// without one, a glowing blob of <see cref="MonsterViewDefinition.ProjectileColor"/>
    /// (<see cref="CombatVisualDefinition.EffectParticles"/>) with a short trail. Arcs rise and fall. No GameObject per
    /// projectile; allocation-free per frame.
    /// </summary>
    public sealed class MonsterProjectileView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private const float FlightHeight = 1f;
        private const float TrailDots = 3;

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly MonsterAbilities _abilities;
        private readonly PlayerSystem _player;
        private readonly CombatVisualDefinition _visual;
        private readonly List<GameObject> _warmup = new List<GameObject>();
        private Look[] _looks; // per horde kind
        private Mesh _quad;
        private MaterialPropertyBlock _block;
        private Bounds _bounds;

        public MonsterProjectileView(LevelData level, HordeSystem horde, MonsterAbilities abilities, PlayerSystem player,
            CombatVisualDefinition visual = null)
        {
            _level = level;
            _horde = horde;
            _abilities = abilities;
            _player = player;
            _visual = visual;
        }

        // After MonsterAbilities (stage SpawnZombies, registered before the views): it knows its casters then.
        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var settings = _level.StressTest;
            if (!_abilities.IsActive || settings == null) return UniTask.CompletedTask;
            var kinds = _horde.KindList;
            _looks = new Look[kinds.Count];
            for (var k = 0; k < kinds.Count; k++)
            {
                var view = settings.ViewOf(kinds[k].Definition);
                var look = new Look
                {
                    Color = view != null ? view.ProjectileColor : new Color(0.6f, 1f, 0.3f, 1f),
                    Size = view != null ? view.ProjectileSize : 0.35f,
                };
                var model = view != null ? view.ProjectileModel : null;
                var filter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
                var renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
                if (filter != null && filter.sharedMesh != null && renderer != null && renderer.sharedMaterial != null)
                {
                    look.Mesh = filter.sharedMesh;
                    look.Material = renderer.sharedMaterial;
                    look.Offset = model.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    var warm = UnityEngine.Object.Instantiate(model);
                    warm.SetActive(false);
                    _warmup.Add(warm);
                }

                _looks[k] = look;
            }

            var geometry = _level.Geometry;
            _bounds = new Bounds(new Vector3(geometry.Width * 0.5f, 2f, geometry.Height * 0.5f),
                new Vector3(geometry.Width + 20f, 20f, geometry.Height + 20f));
            _quad = new Mesh { name = "Monster Projectile", hideFlags = HideFlags.DontSave };
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateBounds();
            _block = new MaterialPropertyBlock();
            return UniTask.CompletedTask;
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_warmup);

        public void LateTick(float deltaTime)
        {
            if (_looks == null || _abilities.ProjectileCount == 0) return;
            var floorHeight = _player.Floor * GridPosition.FloorHeight;
            for (var p = 0; p < _abilities.ProjectileCount; p++)
            {
                var kind = _abilities.ProjectileKind(p);
                if ((uint)kind >= (uint)_looks.Length) continue;
                var look = _looks[kind];
                var position = _abilities.ProjectilePosition(p);
                var direction = _abilities.ProjectileDirection(p);
                var world = new Vector3(position.x, floorHeight + FlightHeight + _abilities.ProjectileHeight(p), position.y);
                var forward = new Vector3(direction.x, 0f, direction.y);
                if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
                if (look.Mesh != null)
                {
                    var matrix = Matrix4x4.TRS(world, Quaternion.LookRotation(forward), Vector3.one) * look.Offset;
                    Graphics.RenderMesh(Params(look.Material, null), look.Mesh, 0, matrix);
                    continue;
                }

                if (_visual == null || _visual.EffectParticles == null) continue;
                _block.SetColor(ColorId, look.Color);
                for (var d = 0; d < TrailDots; d++)
                {
                    // The blob and a fading trail behind it.
                    var size = look.Size * (1f - d * 0.25f);
                    var at = world - forward * (d * look.Size * 0.6f);
                    Graphics.RenderMesh(Params(_visual.EffectParticles, _block), _quad, 0,
                        Matrix4x4.TRS(at, Quaternion.identity, new Vector3(size, 1f, size)));
                }
            }
        }

        public void Dispose()
        {
            UnityObjects.Destroy(_quad);
            _quad = null;
            foreach (var go in _warmup) UnityObjects.Destroy(go);
            _warmup.Clear();
        }

        private RenderParams Params(Material material, MaterialPropertyBlock block) => new RenderParams(material)
        {
            worldBounds = _bounds,
            matProps = block,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
        };

        private struct Look
        {
            public Mesh Mesh;
            public Material Material;
            public Matrix4x4 Offset;
            public Color Color;
            public float Size;
        }
    }
}
