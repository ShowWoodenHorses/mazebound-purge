using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Throwing;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// The throw aim (ТЗ: от игрока до клетки рисуется дуга): dots along the arc from the hand to
    /// <see cref="PlayerThrowing.Target"/> and a ring on the ground as wide as the throwable's effect. One dynamic mesh of
    /// quads and one quad for the ring, drawn over everything while <see cref="PlayerThrowing.IsAiming"/>; the dots run
    /// towards the target. Materials — <see cref="CombatVisualDefinition.ThrowArc"/> and
    /// <see cref="CombatVisualDefinition.ThrowTarget"/>; without them nothing is drawn. Allocation-free per frame.
    /// </summary>
    public sealed class ThrowAimView : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        private const int Dots = 18;
        private const float DotSize = 0.16f;
        private const float HandHeight = 1.1f;
        private const float RingWidth = 0.08f;
        private const float RunSpeed = 1.2f;
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");

        private readonly LevelData _level;
        private readonly PlayerThrowing _throwing;
        private readonly PlayerSystem _player;
        private readonly CombatVisualDefinition _visual;
        private Mesh _dots;
        private Mesh _quad;
        private Vector3[] _vertices;
        private MaterialPropertyBlock _block;
        private Bounds _bounds;
        private float _time;

        public ThrowAimView(LevelData level, PlayerThrowing throwing, PlayerSystem player, CombatVisualDefinition visual = null)
        {
            _level = level;
            _throwing = throwing;
            _player = player;
            _visual = visual;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_visual == null || _visual.ThrowArc == null) return UniTask.CompletedTask;
            var geometry = _level.Geometry;
            _bounds = new Bounds(new Vector3(geometry.Width * 0.5f, 2f, geometry.Height * 0.5f),
                new Vector3(geometry.Width + 20f, 40f, geometry.Height + 20f));

            _vertices = new Vector3[Dots * 4];
            var uvs = new Vector2[Dots * 4];
            var indices = new int[Dots * 6];
            for (var q = 0; q < Dots; q++)
            {
                int v = q * 4, i = q * 6;
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, 1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(1f, 0f);
                indices[i] = v;
                indices[i + 1] = v + 1;
                indices[i + 2] = v + 2;
                indices[i + 3] = v;
                indices[i + 4] = v + 2;
                indices[i + 5] = v + 3;
            }

            _dots = new Mesh { name = "Throw Arc", hideFlags = HideFlags.DontSave };
            _dots.MarkDynamic();
            _dots.vertices = _vertices;
            _dots.uv = uvs;
            var colors = new Color32[_vertices.Length];
            for (var c = 0; c < colors.Length; c++) colors[c] = new Color32(255, 255, 255, 255);
            _dots.colors32 = colors;
            _dots.SetIndices(indices, MeshTopology.Triangles, 0, false);
            _dots.bounds = _bounds;

            _quad = new Mesh { name = "Throw Target", hideFlags = HideFlags.DontSave };
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

        public void LateTick(float deltaTime)
        {
            if (_dots == null || !_throwing.IsAiming || !_player.IsSpawned || _throwing.Slot.Definition == null) return;
            _time += deltaTime;

            var definition = _throwing.Slot.Definition;
            var from = _player.Position;
            var to = _throwing.Target;
            var floorHeight = _player.Floor * GridPosition.FloorHeight;
            var distance = (to - from).magnitude;
            var arc = definition.ArcHeight * distance;
            var shift = Mathf.Repeat(_time * RunSpeed, 1f);
            for (var d = 0; d < Dots; d++)
            {
                var p = Mathf.Clamp01((d + shift) / Dots);
                var flat = Vector2.Lerp(from, to, p);
                var height = Mathf.Lerp(HandHeight, 0.05f, p) + arc * 4f * p * (1f - p) + floorHeight;
                var size = DotSize * (d == 0 ? shift : 1f) * 0.5f;
                var v = d * 4;
                _vertices[v] = new Vector3(flat.x - size, height, flat.y - size);
                _vertices[v + 1] = new Vector3(flat.x - size, height, flat.y + size);
                _vertices[v + 2] = new Vector3(flat.x + size, height, flat.y + size);
                _vertices[v + 3] = new Vector3(flat.x + size, height, flat.y - size);
            }

            _dots.SetVertices(_vertices, 0, _vertices.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            Graphics.RenderMesh(Params(_visual.ThrowArc, null), _dots, 0, Matrix4x4.identity);

            if (_visual.ThrowTarget == null) return;
            var radius = Mathf.Max(0.5f, definition.EffectRadius);
            _block.SetFloat(ThicknessId, Mathf.Clamp(RingWidth / radius, 0.005f, 1f));
            var matrix = Matrix4x4.TRS(new Vector3(to.x, floorHeight + 0.03f, to.y), Quaternion.identity,
                new Vector3(radius * 2f, 1f, radius * 2f));
            Graphics.RenderMesh(Params(_visual.ThrowTarget, _block), _quad, 0, matrix);
        }

        public void Dispose()
        {
            UnityObjects.Destroy(_dots);
            UnityObjects.Destroy(_quad);
            _dots = null;
            _quad = null;
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
    }
}
