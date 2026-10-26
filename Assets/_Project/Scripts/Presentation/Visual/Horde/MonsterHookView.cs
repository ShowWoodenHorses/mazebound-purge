using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual.Horde
{
    /// <summary>
    /// Hooks of the monsters (stage 8b, <see cref="MonsterAbilities.HookOf"/>): a chain or a vine from the monster's right
    /// hand (<see cref="MonsterViewPresenter.HandOf"/>; without a shown view — about its chest) to the head — a
    /// <see cref="LineRenderer"/> with the kind's <see cref="MonsterViewDefinition.Tether"/> (the texture tiles along it),
    /// and the head drawn as the kind's <see cref="MonsterViewDefinition.ProjectileModel"/> pointing along the throw. The
    /// lines are pooled at load (<see cref="MaxHooks"/>); drawn while the monster or the head is in view.
    /// Allocation-free per frame.
    /// </summary>
    public sealed class MonsterHookView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        /// <summary>Hooks drawn at once.</summary>
        public const int MaxHooks = 8;

        private const float HeadHeight = 1f;
        private const float ChestHeight = 1.1f;

        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly MonsterAbilities _abilities;
        private readonly PlayerSystem _player;
        private readonly MonsterViewPresenter _views;
        private readonly VisibilitySystem _visibility;
        private readonly LevelViewRoot _root;
        private readonly List<GameObject> _warmup = new List<GameObject>();
        private Look[] _looks; // per horde kind (Material null = no hook)
        private LineRenderer[] _lines;
        private Bounds _bounds;

        public MonsterHookView(LevelData level, HordeSystem horde, MonsterAbilities abilities, PlayerSystem player,
            MonsterViewPresenter views, VisibilitySystem visibility, LevelViewRoot root)
        {
            _level = level;
            _horde = horde;
            _abilities = abilities;
            _player = player;
            _views = views;
            _visibility = visibility;
            _root = root;
        }

        // After MonsterAbilities and MonsterViewPresenter (stage SpawnZombies).
        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var settings = _level.Horde;
            if (!_abilities.IsActive || settings == null) return UniTask.CompletedTask;
            var kinds = _horde.KindList;
            var looks = new Look[kinds.Count];
            var any = false;
            for (var k = 0; k < kinds.Count; k++)
            {
                var definition = kinds[k].Definition;
                if (definition == null || !HasHook(definition)) continue;
                var view = settings.ViewOf(definition);
                if (view == null || view.Tether == null) continue;
                var look = new Look { Material = view.Tether, Width = view.TetherWidth };
                var model = view.ProjectileModel;
                var filter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
                var renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
                if (filter != null && filter.sharedMesh != null && renderer != null && renderer.sharedMaterial != null)
                {
                    look.Mesh = filter.sharedMesh;
                    look.HeadMaterial = renderer.sharedMaterial;
                    look.Offset = Matrix4x4.Scale(Vector3.one * view.ProjectileSize) *
                                  (model.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix);
                }

                looks[k] = look;
                any = true;
            }

            if (!any) return UniTask.CompletedTask;
            _looks = looks;
            var parent = _root != null ? _root.transform : null;
            _lines = new LineRenderer[MaxHooks];
            for (var n = 0; n < MaxHooks; n++)
            {
                var go = new GameObject($"Hook Line #{n + 1}");
                go.transform.SetParent(parent, false);
                var line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.textureMode = LineTextureMode.Tile;
                line.alignment = LineAlignment.View;
                line.numCapVertices = 0;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = LightProbeUsage.Off;
                line.reflectionProbeUsage = ReflectionProbeUsage.Off;
                line.enabled = false;
                _lines[n] = line;
                if (n == 0) _warmup.Add(go);
            }

            var geometry = _level.Geometry;
            _bounds = new Bounds(new Vector3(geometry.Width * 0.5f, 2f, geometry.Height * 0.5f),
                new Vector3(geometry.Width + 20f, 20f, geometry.Height + 20f));
            return UniTask.CompletedTask;
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_warmup);

        public void LateTick(float deltaTime)
        {
            if (_looks == null) return;
            var used = 0;
            var floorHeight = _player.Floor * GridPosition.FloorHeight;
            var kinds = _horde.Kinds;
            var positions = _horde.Positions;
            for (var i = 0; i < _horde.HighWater && used < MaxHooks; i++)
            {
                var stage = _abilities.HookOf(i);
                if (stage == HookStage.None) continue;
                var look = _looks[kinds[i]];
                if (look.Material == null) continue;
                var body = positions[i];
                var head = _abilities.HookHead(i);
                if (!Visible(body) && !Visible(head)) continue;

                var hand = _views != null ? _views.HandOf(i) : null;
                var from = hand != null ? hand.position : new Vector3(body.x, floorHeight + ChestHeight, body.y);
                var to = new Vector3(head.x, floorHeight + HeadHeight, head.y);
                var line = _lines[used++];
                if (line.sharedMaterial != look.Material) line.sharedMaterial = look.Material;
                line.widthMultiplier = look.Width;
                // One texture repeat per chain width × 2 of length: the links keep their shape at any length.
                line.textureScale = new Vector2(1f / Mathf.Max(0.02f, look.Width * 2f), 1f);
                line.SetPosition(0, from);
                line.SetPosition(1, to);
                if (!line.enabled) line.enabled = true;

                if (look.Mesh == null) continue;
                var forward = to - from;
                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
                Graphics.RenderMesh(new RenderParams(look.HeadMaterial)
                {
                    worldBounds = _bounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                }, look.Mesh, 0, Matrix4x4.TRS(to, Quaternion.LookRotation(forward), Vector3.one) * look.Offset);
            }

            for (var n = used; n < MaxHooks; n++)
                if (_lines[n].enabled) _lines[n].enabled = false;
        }

        public void Dispose()
        {
            if (_lines != null)
                foreach (var line in _lines)
                    if (line != null) UnityObjects.Destroy(line.gameObject);
            _lines = null;
            _looks = null;
            _warmup.Clear();
        }

        private bool Visible(Vector2 point) =>
            _visibility == null || !_visibility.HasResult ||
            _visibility.IsVisible(new GridPosition(Mathf.FloorToInt(point.x + 0.5f), Mathf.FloorToInt(point.y + 0.5f), _player.Floor));

        private static bool HasHook(MonsterDefinition definition)
        {
            foreach (var ability in definition.Abilities)
                if (ability is PullAbility)
                    return true;
            return false;
        }

        private struct Look
        {
            public Material Material;
            public float Width;
            public Mesh Mesh;
            public Material HeadMaterial;
            public Matrix4x4 Offset;
        }
    }
}
