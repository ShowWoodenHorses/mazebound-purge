using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Level;
using Maze.Gameplay.Visibility;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Animated fog of war (visual only): one plane at floor level over the whole level with the theme's fog material
    /// (shader "Maze/Fog"). Its mask follows <see cref="VisibilitySystem.RevealedCells"/> — the same cells as the
    /// geometry — and fades in and out over the material's Fade Time. The mask texture is uploaded only on a
    /// visibility change and while cells are fading. No fog material in the theme = no fog.
    /// With fog, geometry of a cell leaving the view is hidden only once the fog has fully covered it
    /// (<see cref="VisibilityController"/> leaves that to this class), so nothing pops out before the fog arrives.
    /// </summary>
    public sealed class FogOfWarView : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        /// <summary>The plane reaches this far past the level: the camera sees beyond the border near the edges.</summary>
        private const float Margin = 20f;

        private static readonly int MaskId = Shader.PropertyToID("_MazeFog");
        private static readonly int MaskSizeId = Shader.PropertyToID("_MazeFogSize");
        private static readonly int FadeSecondsId = Shader.PropertyToID("_FadeSeconds");
        private static readonly int PlaneHeightId = Shader.PropertyToID("_PlaneHeight");

        private readonly LevelData _level;
        private readonly LevelGrid _grid;
        private readonly VisibilitySystem _visibility;
        private readonly LevelViewRoot _root;
        private readonly LevelVisualSystem _visuals;

        private Material _material;
        private FogMask _mask;
        private Texture2D _texture;
        private Mesh _mesh;
        private GameObject _plane;
        private bool _revealedOnce;
        private bool _subscribed;

        public FogOfWarView(LevelData level, LevelGrid grid, VisibilitySystem visibility, LevelViewRoot root,
            LevelVisualSystem visuals)
        {
            _visuals = visuals;
            _level = level;
            _grid = grid;
            _visibility = visibility;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public bool IsActive => _plane != null;

        /// <summary>True when the theme has a fog material: then the fog decides when hidden geometry disappears.</summary>
        public bool HidesGeometry => _level.VisualTheme != null && _level.VisualTheme.FogMaterial != null;

        /// <summary>Fog values per cell (tests and diagnostics); null without fog.</summary>
        public FogMask Mask => _mask;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _material = _level.VisualTheme != null ? _level.VisualTheme.FogMaterial : null;
            if (_material == null)
            {
                GameLog.Info(LogChannel.Visual, "The theme has no fog material: hidden cells are simply not drawn.");
                return UniTask.CompletedTask;
            }

            _mask = new FogMask(_grid.Width, _grid.Height);
            _texture = new Texture2D(_mask.TextureWidth, _mask.TextureHeight, TextureFormat.R8, false, true)
            {
                name = "Maze Fog Mask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            Upload();
            Shader.SetGlobalTexture(MaskId, _texture);
            Shader.SetGlobalVector(MaskSizeId,
                new Vector4(1f / _mask.TextureWidth, 1f / _mask.TextureHeight, _mask.TextureWidth, _mask.TextureHeight));

            CreatePlane();

            if (!_subscribed)
            {
                _visibility.Changed += OnVisibilityChanged;
                _subscribed = true;
            }

            OnVisibilityChanged();
            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            if (_mask == null || !_mask.IsFading) return;
            var fade = _material.HasProperty(FadeSecondsId) ? _material.GetFloat(FadeSecondsId) : 0.3f;
            if (!_mask.Step(deltaTime, fade)) return;
            Upload();

            var covered = _mask.JustCovered;
            var geometry = _visuals.Geometry;
            if (covered.Count == 0 || geometry == null) return;
            for (var i = 0; i < covered.Count; i++)
                if (!_visibility.IsRevealed(covered[i]))
                    geometry.SetCellVisible(covered[i], false);
            geometry.ApplyVisibility();
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _visibility.Changed -= OnVisibilityChanged;
                _subscribed = false;
            }

            if (_plane != null) UnityObjects.Destroy(_plane);
            UnityObjects.Destroy(_mesh);
            UnityObjects.Destroy(_texture);
            _plane = null;
            _mesh = null;
            _texture = null;
            _mask = null;
        }

        private void OnVisibilityChanged()
        {
            var revealed = _visibility.RevealedCells;
            // The first reveal happens behind the loading screen: no fade.
            var instant = !_revealedOnce;
            if (revealed.Count > 0) _revealedOnce = true;
            if (_mask.SetRevealed(revealed, instant))
                Upload();
        }

        private void Upload()
        {
            _texture.SetPixelData(_mask.Pixels, 0);
            _texture.Apply(false, false);
        }

        private void CreatePlane()
        {
            // Cell (x, y) is centred at world (x, 0, y): the grid spans [-0.5, Width - 0.5].
            var minX = -0.5f - Margin;
            var minZ = -0.5f - Margin;
            var maxX = _grid.Width - 0.5f + Margin;
            var maxZ = _grid.Height - 0.5f + Margin;
            _mesh = new Mesh
            {
                name = "Maze Fog Plane",
                hideFlags = HideFlags.DontSave,
                vertices = new[]
                {
                    new Vector3(minX, 0f, minZ), new Vector3(minX, 0f, maxZ),
                    new Vector3(maxX, 0f, maxZ), new Vector3(maxX, 0f, minZ),
                },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            _mesh.RecalculateBounds();

            var height = _material.HasProperty(PlaneHeightId) ? _material.GetFloat(PlaneHeightId) : 0.01f;
            _plane = new GameObject("Fog Of War");
            _plane.transform.SetParent(_root.transform, false);
            _plane.transform.localPosition = new Vector3(0f, height, 0f);
            _plane.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _plane.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
