using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Level;
using Maze.Gameplay.Visibility;
using Maze.Gameplay.Zombies;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Vision zones of zombies that see (display only; theme settings <see cref="ThemeAwareness"/>): exactly what
    /// gameplay checks — the vision cone (VisionAngle, VisionRange) or, for vision + hearing, the circle of
    /// DetectionRadius — cut by walls and closed doors (<see cref="VisionZoneShape"/>). A faint fill with a bright edge,
    /// calm colour in Idle / Patrol / Return, alert colour while roaring; hidden while chasing or attacking, and for
    /// zombies the player does not see (a zone would give them away through the fog). A shown zone is drawn whole,
    /// over the fog. Each zone is one small mesh rebuilt only when its zombie moved or turned, or a door changed, at
    /// most every <see cref="RebuildInterval"/> seconds; colour and fade go through a property block. No allocations
    /// after loading. Must be registered after <see cref="ZombieSystem"/> (same load stage).
    /// </summary>
    public sealed class VisionZonesView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        public const float RebuildInterval = 0.1f;

        private const float Height = 0.03f;
        private const float MoveThreshold = 0.03f;
        private const float TurnThreshold = 0.9998f; // cos of about 1°
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly LevelData _level;
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;
        private readonly ZombieSystem _zombies;
        private readonly VisibilitySystem _visibility;
        private readonly LevelViewRoot _root;
        private readonly List<Zone> _zones = new List<Zone>();

        private ThemeAwareness _settings;
        private MaterialPropertyBlock _block;
        private bool _subscribed;

        public VisionZonesView(LevelData level, LevelGrid grid, DoorSystem doors, ZombieSystem zombies,
            VisibilitySystem visibility, LevelViewRoot root)
        {
            _level = level;
            _grid = grid;
            _doors = doors;
            _zombies = zombies;
            _visibility = visibility;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>Zombies with a zone (tests).</summary>
        public int ZoneCount => _zones.Count;

        /// <summary>Whether a zombie's zone is drawn now (tests).</summary>
        public bool IsShown(ZombieRuntime zombie)
        {
            foreach (var zone in _zones)
                if (zone.Zombie == zombie)
                    return zone.Renderer.enabled;
            return false;
        }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _settings = _level.VisualTheme != null ? _level.VisualTheme.Awareness : null;
            if (_settings == null || _settings.ZoneMaterial == null)
                return UniTask.CompletedTask;

            _block = new MaterialPropertyBlock();
            foreach (var zombie in _zombies.Zombies)
                if (zombie.Definition.DetectionType != ZombieDetectionType.HearingOnly)
                    _zones.Add(CreateZone(zombie));

            if (!_subscribed)
            {
                _doors.DoorChanged += OnDoorChanged;
                _subscribed = true;
            }

            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            foreach (var zone in _zones)
            {
                var zombie = zone.Zombie;
                var wanted = WantsZone(zombie);
                var fadeSpeed = deltaTime / Mathf.Max(_settings.ZoneFade, 0.01f);
                zone.Fade = Mathf.MoveTowards(zone.Fade, wanted ? 1f : 0f, fadeSpeed);
                zone.RebuildTimer -= deltaTime;

                if (zone.Fade <= 0f)
                {
                    if (zone.Renderer.enabled) zone.Renderer.enabled = false;
                    zone.Dirty = true; // shape may be stale when it comes back
                    continue;
                }

                if (wanted && zone.RebuildTimer <= 0f && (zone.Dirty || HasMoved(zone)))
                    Rebuild(zone);

                var color = zombie.State == ZombieState.Alert ? _settings.AlertColor : _settings.CalmColor;
                color.a *= zone.Fade;
                _block.SetColor(ColorId, color);
                zone.Renderer.SetPropertyBlock(_block);
                if (!zone.Renderer.enabled) zone.Renderer.enabled = true;
            }
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            foreach (var zone in _zones)
                objects.Add(zone.GameObject);
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _doors.DoorChanged -= OnDoorChanged;
                _subscribed = false;
            }

            foreach (var zone in _zones)
            {
                UnityObjects.Destroy(zone.GameObject);
                UnityObjects.Destroy(zone.Mesh);
            }
            _zones.Clear();
        }

        private bool WantsZone(ZombieRuntime zombie)
        {
            switch (zombie.State)
            {
                case ZombieState.Idle:
                case ZombieState.Patrol:
                case ZombieState.Return:
                case ZombieState.Alert:
                    return _visibility.IsVisible(zombie.Cell);
                default:
                    return false;
            }
        }

        private static bool HasMoved(Zone zone)
        {
            var zombie = zone.Zombie;
            return (zombie.Position - zone.BuiltPosition).sqrMagnitude > MoveThreshold * MoveThreshold ||
                   Vector2.Dot(zombie.Facing, zone.BuiltFacing) < TurnThreshold;
        }

        private void OnDoorChanged(DoorData door, bool open)
        {
            foreach (var zone in _zones)
                zone.Dirty = true;
        }

        // ---- Mesh ----

        /// <summary>
        /// Vertices: [0] origin, [1..n] ray ends (the fill fan), then two per outline vertex — on the outline and moved
        /// inside by the edge width (the edge ribbon). Fill alpha is FillAlpha, the ribbon fades from EdgeAlpha to 0
        /// inwards. Indices and colours never change; only positions are rewritten.
        /// </summary>
        private Zone CreateZone(ZombieRuntime zombie)
        {
            var definition = zombie.Definition;
            var circle = definition.DetectionType == ZombieDetectionType.VisionAndHearing;
            var angle = circle ? 360f : definition.VisionAngle;
            var range = circle ? definition.DetectionRadius : definition.VisionRange;
            var rays = VisionZoneShape.RayCount(angle, _settings.RayStep);
            var isCircle = VisionZoneShape.IsCircle(angle);
            var outline = isCircle ? rays : rays + 1;

            var zone = new Zone
            {
                Zombie = zombie,
                Angle = angle,
                Range = range,
                IsCircle = isCircle,
                Points = new Vector2[rays],
                Outline = new Vector2[outline],
                Vertices = new Vector3[rays + 1 + outline * 2],
                Dirty = true,
            };

            var colors = new Color32[zone.Vertices.Length];
            var fill = new Color(1f, 1f, 1f, _settings.FillAlpha);
            var edge = new Color(1f, 1f, 1f, _settings.EdgeAlpha);
            for (var i = 0; i <= rays; i++) colors[i] = fill;
            for (var k = 0; k < outline; k++)
            {
                colors[rays + 1 + k * 2] = edge;
                colors[rays + 2 + k * 2] = new Color(1f, 1f, 1f, 0f);
            }

            var triangles = new List<int>();
            var fans = isCircle ? rays : rays - 1;
            for (var i = 0; i < fans; i++)
                triangles.AddRange(new[] { 0, 1 + i, 1 + (i + 1) % rays });
            for (var k = 0; k < outline; k++)
            {
                var a = rays + 1 + k * 2;
                var b = rays + 1 + (k + 1) % outline * 2;
                triangles.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
            }

            zone.Mesh = new Mesh { name = "VisionZone " + zombie.Id };
            zone.Mesh.MarkDynamic();
            zone.Mesh.vertices = zone.Vertices;
            zone.Mesh.colors32 = colors;
            zone.Mesh.SetTriangles(triangles, 0, false);
            var geometry = _level.Geometry;
            zone.Mesh.bounds = new Bounds(
                new Vector3((geometry.Width - 1) * 0.5f, Height, (geometry.Height - 1) * 0.5f),
                new Vector3(geometry.Width + 1f, 1f, geometry.Height + 1f));

            zone.GameObject = new GameObject("VisionZone " + zombie.Id);
            zone.GameObject.transform.SetParent(_root.transform, false);
            zone.GameObject.AddComponent<MeshFilter>().sharedMesh = zone.Mesh;
            zone.Renderer = zone.GameObject.AddComponent<MeshRenderer>();
            zone.Renderer.sharedMaterial = _settings.ZoneMaterial;
            zone.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            zone.Renderer.receiveShadows = false;
            zone.Renderer.lightProbeUsage = LightProbeUsage.Off;
            zone.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            zone.Renderer.enabled = false;
            return zone;
        }

        private void Rebuild(Zone zone)
        {
            var zombie = zone.Zombie;
            zone.BuiltPosition = zombie.Position;
            zone.BuiltFacing = zombie.Facing;
            zone.Dirty = false;
            zone.RebuildTimer = RebuildInterval;

            var opacity = new LevelOpacity(_grid, _doors);
            var rays = VisionZoneShape.Cast(zombie.Position, zombie.Facing, zone.Angle, zone.Range, _settings.RayStep,
                opacity, zone.Points);

            var vertices = zone.Vertices;
            vertices[0] = World(zombie.Position);
            for (var i = 0; i < rays; i++)
                vertices[1 + i] = World(zone.Points[i]);

            var outline = zone.Outline;
            var count = 0;
            if (!zone.IsCircle) outline[count++] = zombie.Position;
            for (var i = 0; i < rays; i++) outline[count++] = zone.Points[i];

            var width = _settings.EdgeWidth;
            for (var k = 0; k < count; k++)
            {
                var point = outline[k];
                var inner = point + InwardOffset(outline[(k + count - 1) % count], point, outline[(k + 1) % count], width);
                vertices[rays + 1 + k * 2] = World(point);
                vertices[rays + 2 + k * 2] = World(inner);
            }

            zone.Mesh.vertices = vertices;
        }

        /// <summary>
        /// Moves an outline vertex inside a counter-clockwise outline by <paramref name="width"/> (mitred, limited at
        /// sharp corners). Zero-length edges (rays stopped at the same wall point) borrow the other edge's direction.
        /// </summary>
        private static Vector2 InwardOffset(Vector2 previous, Vector2 point, Vector2 next, float width)
        {
            var incoming = point - previous;
            var outgoing = next - point;
            if (incoming.sqrMagnitude < 1e-8f) incoming = outgoing;
            if (outgoing.sqrMagnitude < 1e-8f) outgoing = incoming;
            if (incoming.sqrMagnitude < 1e-8f) return Vector2.zero;

            var left1 = Left(incoming.normalized);
            var left2 = Left(outgoing.normalized);
            var direction = left1 + left2;
            if (direction.sqrMagnitude < 1e-6f) direction = left1;
            direction.Normalize();
            var miter = Mathf.Max(Vector2.Dot(direction, left1), 0.35f);
            return direction * (width / miter);
        }

        private static Vector2 Left(Vector2 direction) => new Vector2(-direction.y, direction.x);

        private static Vector3 World(Vector2 point) => new Vector3(point.x, Height, point.y);

        private sealed class Zone
        {
            public ZombieRuntime Zombie;
            public float Angle;
            public float Range;
            public bool IsCircle;
            public Vector2[] Points;
            public Vector2[] Outline;
            public Vector3[] Vertices;
            public Mesh Mesh;
            public GameObject GameObject;
            public MeshRenderer Renderer;
            public Vector2 BuiltPosition;
            public Vector2 BuiltFacing;
            public float RebuildTimer;
            public float Fade;
            public bool Dirty;
        }
    }
}
