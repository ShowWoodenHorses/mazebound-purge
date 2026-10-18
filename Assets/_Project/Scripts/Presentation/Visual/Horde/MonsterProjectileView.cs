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
    /// projectile; allocation-free per frame. With <see cref="MonsterViewDefinition.ProjectileTrail"/> (stage Э3) a pack
    /// effect follows each projectile in view (<see cref="PackEffectsPlayer.Follow"/>) and fades where it ended.
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
        private readonly PackEffectsPlayer _packEffects;
        // Followed trails by projectile serial (MonsterAbilities.ProjectileSerial): serial → handle.
        private readonly Dictionary<int, int> _trails = new Dictionary<int, int>(MonsterAbilities.MaxProjectiles);
        private readonly List<int> _seen = new List<int>(MonsterAbilities.MaxProjectiles);
        private readonly List<int> _ended = new List<int>(MonsterAbilities.MaxProjectiles);
        private readonly List<GameObject> _warmup = new List<GameObject>();
        private Look[] _looks; // per horde kind
        private Mesh _quad;
        private MaterialPropertyBlock _block;
        private Bounds _bounds;

        public MonsterProjectileView(LevelData level, HordeSystem horde, MonsterAbilities abilities, PlayerSystem player,
            CombatVisualDefinition visual = null, PackEffectsPlayer packEffects = null)
        {
            _packEffects = packEffects;
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
                    Trail = view != null && CombatVisualDefinition.IsSet(view.ProjectileTrail) ? view.ProjectileTrail : null,
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
            if (_looks == null) return;
            _seen.Clear();
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
                if (look.Trail != null && _packEffects != null) FollowTrail(p, look.Trail, world, forward);
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

            if (_packEffects != null) EndTrails();
        }

        /// <summary>Trails of projectiles that ended fade where they were.</summary>
        private void EndTrails()
        {
            if (_trails.Count == 0) return;
            _ended.Clear();
            foreach (var pair in _trails)
                if (!_seen.Contains(pair.Key)) _ended.Add(pair.Key);
            foreach (var serial in _ended)
            {
                _packEffects.Unfollow(_trails[serial]);
                _trails.Remove(serial);
            }
        }

        private void FollowTrail(int p, UnityEngine.AddressableAssets.AssetReferenceGameObject trail, Vector3 world, Vector3 forward)
        {
            var serial = _abilities.ProjectileSerial(p);
            _seen.Add(serial);
            var rotation = Quaternion.LookRotation(forward);
            if (_trails.TryGetValue(serial, out var handle))
            {
                _packEffects.Move(handle, world, rotation);
                return;
            }

            handle = _packEffects.Follow(trail, world, rotation);
            if (handle >= 0) _trails[serial] = handle;
        }

        public void Dispose()
        {
            _trails.Clear();
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
            public UnityEngine.AddressableAssets.AssetReferenceGameObject Trail;
        }
    }
}
