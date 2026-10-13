using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Level;
using Maze.Gameplay.Navigation;
using Maze.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// The route hint on the floor (display only; theme settings <see cref="ThemeRoute"/>): a ribbon from the player
    /// through the path of <see cref="RouteHintSystem"/> to its target, drawn over the fog (the hint may lead through
    /// unknown places). Starts at the player, so nothing stays behind; rebuilt only when the path changes or the player
    /// moves. One dynamic mesh and renderer made while loading, lists reused: no allocations during play.
    /// </summary>
    public sealed class RouteLineView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int HalfWidthId = Shader.PropertyToID("_HalfWidth");
        private static readonly int OutlineId = Shader.PropertyToID("_Outline");
        private static readonly int StartFadeId = Shader.PropertyToID("_StartFade");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int PulseSpacingId = Shader.PropertyToID("_PulseSpacing");
        private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");

        private readonly LevelData _level;
        private readonly RouteHintSystem _route;
        private readonly PlayerSystem _player;
        private readonly LevelViewRoot _root;
        private readonly List<Vector2> _points = new List<Vector2>(256);
        private readonly List<Vector2> _scratch = new List<Vector2>(128);
        private readonly List<Vector3> _vertices = new List<Vector3>(512);
        private readonly List<Vector2> _uv = new List<Vector2>(512);
        private readonly List<Vector2> _uv2 = new List<Vector2>(512);
        private readonly List<int> _triangles = new List<int>(1536);

        private ThemeRoute _settings;
        private MaterialPropertyBlock _block;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private int _builtVersion = -1;
        private Vector2 _builtFrom;

        public RouteLineView(LevelData level, RouteHintSystem route, PlayerSystem player, LevelViewRoot root)
        {
            _level = level;
            _route = route;
            _player = player;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>Whether the line is drawn now (tests).</summary>
        public bool IsShown => _renderer != null && _renderer.enabled;

        /// <summary>Points of the drawn line, grid units (tests).</summary>
        public IReadOnlyList<Vector2> Points => _points;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _settings = _level.VisualTheme != null ? _level.VisualTheme.Route : null;
            if (_settings == null || _settings.LineMaterial == null || _renderer != null)
                return UniTask.CompletedTask;

            _block = new MaterialPropertyBlock();
            _mesh = new Mesh { name = "RouteLine" };
            _mesh.MarkDynamic();
            var go = new GameObject("RouteLine");
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _settings.LineMaterial;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // A short piece for the warm-up draw; hidden until a hint.
            _points.Clear();
            _points.Add(Vector2.zero);
            _points.Add(Vector2.right);
            UploadRibbon();
            _renderer.enabled = false;
            return UniTask.CompletedTask;
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (_renderer != null) objects.Add(_renderer.gameObject);
        }

        public void LateTick(float deltaTime)
        {
            if (_renderer == null) return;
            if (!_route.IsActive || !_player.IsSpawned || _route.Path.Count < 2)
            {
                if (_renderer.enabled) _renderer.enabled = false;
                _builtVersion = -1;
                return;
            }

            var from = _player.Position;
            if (_builtVersion == _route.Version && (from - _builtFrom).sqrMagnitude < 1e-6f && _renderer.enabled) return;

            _builtVersion = _route.Version;
            _builtFrom = from;
            RouteLineMesh.BuildPoints(from, _route.Path, _settings.CornerRadius, _points, _scratch);
            UploadRibbon();
            _renderer.enabled = _points.Count >= 2;
        }

        public void Dispose()
        {
            if (_renderer != null) UnityObjects.DestroyObjectOf(_renderer);
            UnityObjects.Destroy(_mesh);
            _renderer = null;
            _mesh = null;
        }

        private void UploadRibbon()
        {
            RouteLineMesh.BuildRibbon(_points, _settings.Width * 0.5f, _settings.Height, _vertices, _uv, _uv2, _triangles);
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uv);
            _mesh.SetUVs(1, _uv2);
            _mesh.SetTriangles(_triangles, 0, false);
            _mesh.RecalculateBounds();

            _block.SetColor(ColorId, _settings.Color);
            _block.SetColor(OutlineColorId, _settings.OutlineColor);
            _block.SetFloat(BrightnessId, _settings.Brightness);
            _block.SetFloat(OpacityId, _settings.Opacity);
            _block.SetFloat(HalfWidthId, _settings.Width * 0.5f);
            _block.SetFloat(OutlineId, Mathf.Min(_settings.OutlineWidth, _settings.Width * 0.5f));
            _block.SetFloat(StartFadeId, _settings.StartFade);
            _block.SetFloat(PulseId, _settings.Pulse);
            _block.SetFloat(PulseSpacingId, _settings.PulseSpacing);
            _block.SetFloat(PulseSpeedId, _settings.PulseSpeed);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
