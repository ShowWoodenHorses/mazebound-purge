using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Zones on cells (<see cref="ZoneSystem"/>) of the player's floor: per zone kind a mask texture W×H (R8, bilinear:
    /// neighbouring cells merge into one patch, a fading zone thins out) and one dynamic mesh of quads over its visible
    /// cells, shader <c>Purge/FireCells</c> (<see cref="CombatVisualDefinition.ZoneCells"/>) with the kind's colours.
    /// No object per cell, allocation-free per frame.
    /// </summary>
    public sealed class ZoneView : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        private const int MaxQuads = 512;
        private const float Height = 0.04f;
        private const float Half = 0.5f;

        private static readonly int MaskId = Shader.PropertyToID("_PurgeFireMask");
        private static readonly int MaskSizeId = Shader.PropertyToID("_PurgeFireMaskSize");
        private static readonly int CoreId = Shader.PropertyToID("_Core");
        private static readonly int EdgeId = Shader.PropertyToID("_Edge");

        private readonly LevelData _level;
        private readonly ZoneSystem _zones;
        private readonly PlayerSystem _player;
        private readonly VisibilitySystem _visibility;
        private readonly CombatVisualDefinition _visual;
        private readonly List<Layer> _layers = new List<Layer>();
        private Bounds _bounds;
        private int _width;
        private int _height;

        public ZoneView(LevelData level, ZoneSystem zones, PlayerSystem player, VisibilitySystem visibility,
            CombatVisualDefinition visual = null)
        {
            _level = level;
            _zones = zones;
            _player = player;
            _visibility = visibility;
            _visual = visual;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _width = _level.Geometry.Width;
            _height = _level.Geometry.Height;
            _bounds = new Bounds(new Vector3(_width * 0.5f, 1f, _height * 0.5f), new Vector3(_width + 4f, 40f, _height + 4f));
            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            if (_visual == null || _visual.ZoneCells == null || _width == 0) return;
            var kinds = _zones.Kinds;
            while (_layers.Count < kinds.Count) _layers.Add(CreateLayer(kinds[_layers.Count].Core, kinds[_layers.Count].Edge));
            if (_layers.Count == 0) return;

            foreach (var layer in _layers)
            {
                for (var i = 0; i < layer.WrittenCount; i++) layer.Pixels[layer.Written[i]] = 0;
                layer.WrittenCount = 0;
                layer.Quads = 0;
            }

            var grid = _zones.Grid;
            var floorSize = grid.FloorSize;
            var floor = _player.Floor;
            foreach (var index in _zones.Cells)
            {
                if (index / floorSize != floor) continue;
                var kind = _zones.KindAt(index);
                if (kind < 0 || kind >= _layers.Count) continue;
                var layer = _layers[kind];
                var flat = index % floorSize;
                layer.Pixels[flat] = (byte)(Mathf.Clamp01(0.6f + _zones.Strength01(index) * 0.6f) * 255f);
                if (layer.WrittenCount < layer.Written.Length) layer.Written[layer.WrittenCount++] = flat;

                var x = flat % _width;
                var y = flat / _width;
                if (layer.Quads >= MaxQuads || !_visibility.IsVisible(new GridPosition(x, y, floor))) continue;
                var v = layer.Quads++ * 4;
                var h = Height + floor * GridPosition.FloorHeight;
                layer.Vertices[v] = new Vector3(x - Half, h, y - Half);
                layer.Vertices[v + 1] = new Vector3(x - Half, h, y + Half);
                layer.Vertices[v + 2] = new Vector3(x + Half, h, y + Half);
                layer.Vertices[v + 3] = new Vector3(x + Half, h, y - Half);
            }

            foreach (var layer in _layers)
            {
                if (layer.WrittenCount == 0 && !layer.Dirty) continue;
                layer.Dirty = layer.WrittenCount > 0; // cleared next frame once more
                layer.Mask.SetPixelData(layer.Pixels, 0);
                layer.Mask.Apply(false);
                if (layer.Quads == 0) continue;
                layer.Mesh.SetVertices(layer.Vertices, 0, layer.Vertices.Length,
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                layer.Mesh.SetIndices(layer.Indices, 0, layer.Quads * 6, MeshTopology.Triangles, 0, false);
                Graphics.RenderMesh(new RenderParams(_visual.ZoneCells)
                {
                    worldBounds = _bounds,
                    matProps = layer.Block,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                }, layer.Mesh, 0, Matrix4x4.identity);
            }
        }

        public void Dispose()
        {
            foreach (var layer in _layers)
            {
                UnityObjects.Destroy(layer.Mesh);
                UnityObjects.Destroy(layer.Mask);
            }

            _layers.Clear();
        }

        private Layer CreateLayer(Color core, Color edge)
        {
            var layer = new Layer
            {
                Pixels = new byte[_width * _height],
                Written = new int[ZoneSystem.MaxCells],
                Vertices = new Vector3[MaxQuads * 4],
                Indices = new int[MaxQuads * 6],
                Block = new MaterialPropertyBlock(),
            };
            layer.Mask = new Texture2D(_width, _height, TextureFormat.R8, false, true)
            {
                name = "Purge Zone Mask", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            layer.Mask.SetPixelData(layer.Pixels, 0);
            layer.Mask.Apply(false);
            for (var q = 0; q < MaxQuads; q++)
            {
                int v = q * 4, i = q * 6;
                layer.Indices[i] = v;
                layer.Indices[i + 1] = v + 1;
                layer.Indices[i + 2] = v + 2;
                layer.Indices[i + 3] = v;
                layer.Indices[i + 4] = v + 2;
                layer.Indices[i + 5] = v + 3;
            }

            layer.Mesh = new Mesh { name = "Zone Cells", hideFlags = HideFlags.DontSave };
            layer.Mesh.MarkDynamic();
            layer.Mesh.vertices = layer.Vertices;
            layer.Mesh.SetIndices(layer.Indices, MeshTopology.Triangles, 0, false);
            layer.Mesh.bounds = _bounds;
            layer.Block.SetTexture(MaskId, layer.Mask);
            layer.Block.SetVector(MaskSizeId, new Vector4(1f / _width, 1f / _height, 0f, 0f));
            layer.Block.SetColor(CoreId, core);
            layer.Block.SetColor(EdgeId, edge);
            return layer;
        }

        private sealed class Layer
        {
            public Texture2D Mask;
            public byte[] Pixels;
            public int[] Written;
            public int WrittenCount;
            public bool Dirty;
            public Mesh Mesh;
            public Vector3[] Vertices;
            public int[] Indices;
            public int Quads;
            public MaterialPropertyBlock Block;
        }
    }
}
