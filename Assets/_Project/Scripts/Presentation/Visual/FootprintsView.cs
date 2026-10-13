using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;
using Maze.Gameplay.Visibility;
using Maze.Gameplay.Zombies;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Footprints and step dust (display only; theme settings <see cref="ThemeFootprints"/>). The player and every living
    /// zombie leave prints from their gameplay positions (<see cref="FootprintField"/>) — zombies also where the player
    /// does not see, so their fresh trail can be found later. All prints are one dynamic mesh (one draw call, rebuilt
    /// only while prints exist, no allocations), drawn under the fog: a print is as visible as its cell (the fog value
    /// with fog, revealed cells without it). Every print in a visible cell raises a short dust puff, two when running —
    /// emitted into one shared particle system.
    /// </summary>
    public sealed class FootprintsView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        /// <summary>Above the floor (and under the blob shadows).</summary>
        private const float PrintHeight = 0.012f;

        private const float DustHeight = 0.06f;
        private const int MaxDust = 64;

        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly ZombieSystem _zombies;
        private readonly VisibilitySystem _visibility;
        private readonly FogOfWarView _fog;
        private readonly LevelViewRoot _root;

        private readonly List<int> _zombieWalkers = new List<int>();
        private readonly List<Vector2> _lastPositions = new List<Vector2>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Color32> _colors = new List<Color32>();

        private ThemeFootprints _settings;
        private FootprintField _field;
        private int _playerWalker = -1;
        private Mesh _mesh;
        private GameObject _printsObject;
        private ParticleSystem _dust;
        private bool _meshEmpty = true;

        public FootprintsView(LevelData level, PlayerSystem player, ZombieSystem zombies, VisibilitySystem visibility,
            FogOfWarView fog, LevelViewRoot root)
        {
            _level = level;
            _player = player;
            _zombies = zombies;
            _visibility = visibility;
            _fog = fog;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>The prints and step rhythm (tests); null without print and dust materials.</summary>
        public FootprintField Field => _field;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _settings = _level.VisualTheme != null ? _level.VisualTheme.Footprints : null;
            if (_settings == null)
                return UniTask.CompletedTask;

            if (_settings.PrintMaterial == null && _settings.DustMaterial == null)
                return UniTask.CompletedTask;

            // The field gives the step rhythm for the dust too; without a print material it is just not drawn.
            _field = new FootprintField(Mathf.Max(_settings.MaxPrints, 1));
            if (_settings.PrintMaterial != null)
                CreatePrints();
            if (_settings.DustMaterial != null)
                CreateDust();
            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            if (_field == null) return;

            if (_player.IsSpawned)
            {
                if (_playerWalker < 0)
                    _playerWalker = AddWalker(_settings.Player, _player.Position);
                Walk(_playerWalker, _player.Position, deltaTime);
            }

            var zombies = _zombies.Zombies;
            for (var i = 0; i < zombies.Count; i++)
            {
                var zombie = zombies[i];
                if (i == _zombieWalkers.Count)
                    _zombieWalkers.Add(AddWalker(_settings.Zombie, zombie.Position));
                if (zombie.IsAlive)
                    Walk(_zombieWalkers[i], zombie.Position, deltaTime);
            }

            _field.Update(deltaTime);
            if (_mesh != null)
                RebuildMesh();
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (_printsObject != null) objects.Add(_printsObject);
            if (_dust != null) objects.Add(_dust.gameObject);
        }

        public void Dispose()
        {
            UnityObjects.Destroy(_printsObject);
            UnityObjects.Destroy(_mesh);
            UnityObjects.DestroyObjectOf(_dust);
            _printsObject = null;
            _mesh = null;
            _dust = null;
            _field?.Clear();
            _field = null;
            _zombieWalkers.Clear();
            _lastPositions.Clear();
            _playerWalker = -1;
        }

        // ---- Walkers ----

        /// <summary>Walker ids are the same in the field and in <see cref="_lastPositions"/>.</summary>
        private int AddWalker(FootprintKind kind, Vector2 position)
        {
            _lastPositions.Add(position);
            var id = _field.AddWalker(kind);
            _field.Move(id, position);
            return id;
        }

        private void Walk(int walker, Vector2 position, float deltaTime)
        {
            var last = _lastPositions[walker];
            _lastPositions[walker] = position;
            var slot = _field.Move(walker, position);
            if (slot >= 0)
                Puff(_field.Prints[slot].Position, deltaTime > 0f ? (position - last).magnitude / deltaTime : 0f);
        }

        // ---- Prints ----

        private void CreatePrints()
        {
            var capacity = _field.Capacity;

            var triangles = new int[capacity * 6];
            for (var i = 0; i < capacity; i++)
            {
                _vertices.Add(Vector3.zero); _vertices.Add(Vector3.zero); _vertices.Add(Vector3.zero); _vertices.Add(Vector3.zero);
                _uvs.Add(Vector2.zero); _uvs.Add(Vector2.zero); _uvs.Add(Vector2.zero); _uvs.Add(Vector2.zero);
                _colors.Add(default); _colors.Add(default); _colors.Add(default); _colors.Add(default);
                var v = i * 4;
                triangles[i * 6] = v;
                triangles[i * 6 + 1] = v + 2;
                triangles[i * 6 + 2] = v + 1;
                triangles[i * 6 + 3] = v + 1;
                triangles[i * 6 + 4] = v + 2;
                triangles[i * 6 + 5] = v + 3;
            }

            _mesh = new Mesh { name = "Footprints" };
            _mesh.MarkDynamic();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(triangles, 0, false);
            var geometry = _level.Geometry;
            _mesh.bounds = new Bounds(
                new Vector3((geometry.Width - 1) * 0.5f, PrintHeight, (geometry.Height - 1) * 0.5f),
                new Vector3(geometry.Width + 1f, 1f, geometry.Height + 1f));

            _printsObject = new GameObject("Footprints");
            _printsObject.transform.SetParent(_root.transform, false);
            _printsObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _printsObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _settings.PrintMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void RebuildMesh()
        {
            if (_field.AliveCount == 0 && _meshEmpty) return;

            var prints = _field.Prints;
            var fogMask = _fog != null && _fog.IsActive ? _fog.Mask : null;
            for (var i = 0; i < prints.Length; i++)
            {
                var print = prints[i];
                var alpha = print.Alive ? print.Alpha * CellShown(PlayerMovement.CellOf(print.Position), fogMask) : 0f;
                var v = i * 4;
                if (alpha <= 0.004f)
                {
                    _vertices[v] = _vertices[v + 1] = _vertices[v + 2] = _vertices[v + 3] = Vector3.zero;
                    continue;
                }

                var kind = _field.KindOf(print.Walker);
                var zombie = kind == _settings.Zombie;
                var forward = new Vector3(print.Direction.x, 0f, print.Direction.y) * (kind.Length * 0.5f);
                var right = new Vector3(print.Direction.y, 0f, -print.Direction.x) * (kind.Width * 0.5f);
                var centre = new Vector3(print.Position.x, PrintHeight, print.Position.y);
                _vertices[v] = centre - forward - right;
                _vertices[v + 1] = centre - forward + right;
                _vertices[v + 2] = centre + forward - right;
                _vertices[v + 3] = centre + forward + right;

                // The texture shows right feet: a left print is mirrored. Atlas: player left half, zombie right half.
                var u0 = zombie ? 0.5f : 0f;
                var u1 = u0 + 0.5f;
                if (print.LeftFoot) (u0, u1) = (u1, u0);
                _uvs[v] = new Vector2(u0, 0f);
                _uvs[v + 1] = new Vector2(u1, 0f);
                _uvs[v + 2] = new Vector2(u0, 1f);
                _uvs[v + 3] = new Vector2(u1, 1f);

                var color = kind.Color;
                color.a *= alpha;
                Color32 color32 = color;
                _colors[v] = _colors[v + 1] = _colors[v + 2] = _colors[v + 3] = color32;
            }

            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            _mesh.SetVertices(_vertices, 0, _vertices.Count, flags);
            _mesh.SetUVs(0, _uvs, 0, _uvs.Count, flags);
            _mesh.SetColors(_colors, 0, _colors.Count, flags);
            _meshEmpty = _field.AliveCount == 0;
        }

        /// <summary>How much of a cell is shown: the fog value with fog, revealed or not without it.</summary>
        private float CellShown(GridPosition cell, FogMask fogMask)
        {
            if (fogMask != null) return fogMask.ValueOf(cell);
            return _visibility.IsRevealed(cell) ? 1f : 0f;
        }

        // ---- Dust ----

        private void CreateDust()
        {
            var go = new GameObject("FootstepDust");
            go.transform.SetParent(_root.transform, false);
            _dust = go.AddComponent<ParticleSystem>();
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _dust.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = MaxDust;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = _settings.DustLifetime;
            main.startSize = _settings.DustSize;
            main.startColor = _settings.DustColor;
            main.startSpeed = 0f;

            var emission = _dust.emission;
            emission.enabled = false;
            var shape = _dust.shape;
            shape.enabled = false;
            var collision = _dust.collision;
            collision.enabled = false; // no physics in the project

            var size = _dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.3f));

            var color = _dust.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _settings.DustMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _dust.Play();
        }

        private void Puff(Vector2 position, float speed)
        {
            if (_dust == null || !_visibility.IsVisible(PlayerMovement.CellOf(position)))
                return;

            var running = speed >= _settings.DustRunSpeed;
            var count = running ? 2 : 1;
            for (var i = 0; i < count; i++)
            {
                var spread = UnityEngine.Random.insideUnitCircle * 0.25f;
                _dust.Emit(new ParticleSystem.EmitParams
                {
                    position = _root.transform.TransformPoint(new Vector3(position.x, DustHeight, position.y)),
                    velocity = new Vector3(spread.x, 0.2f, spread.y),
                    startSize = _settings.DustSize * (running ? 1.25f : 0.8f) * UnityEngine.Random.Range(0.8f, 1.2f),
                    startLifetime = _settings.DustLifetime * UnityEngine.Random.Range(0.8f, 1.2f),
                    rotation = UnityEngine.Random.Range(0f, 360f),
                    startColor = _settings.DustColor,
                    applyShapeToPosition = false,
                }, 1);
            }
        }
    }
}
