using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Noise waves (display only; theme settings <see cref="ThemeAwareness"/>): every sound of the player
    /// (<see cref="SoundEventBus"/>) shows how far it carries — a ring growing to the sound's radius and fading, not cut by
    /// walls (sound passes them) and drawn over everything but the UI. Repeating sounds do not flicker: steps and
    /// automatic fire show one ring when walking / firing starts — it grows out of the player to the step / weapon
    /// radius (<see cref="ThemeAwareness.RingGrow"/>) following them and fades (<see cref="ThemeAwareness.RingFade"/>)
    /// while they go on; the next ring only after a quiet pause (<see cref="ThemeAwareness.RingRearm"/>). Single events (a single shot, a melee hit, a door, a pickup) each send a wave. Flat quads with
    /// the <c>Maze/Ring</c> shader from a small pool made while loading; no allocations during play.
    /// </summary>
    public sealed class NoiseWavesView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        public const int WaveCount = 6;

        private const float Height = 0.05f;
        private const float MovingThreshold = 0.05f;
        private const float AutomaticHold = 0.15f; // seconds the ring stays after the last shot, beyond the cooldown

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");

        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly WeaponSystem _weapons;
        private readonly SoundEventBus _sounds;
        private readonly LevelViewRoot _root;
        private readonly Wave[] _waves = new Wave[WaveCount];

        private ThemeAwareness _settings;
        private MaterialPropertyBlock _block;
        private Mesh _quad;
        private Ring _steps;
        private Ring _automatic;
        private float _automaticLeft;
        private float _automaticRadius;
        private int _nextWave;
        private bool _subscribed;

        public NoiseWavesView(LevelData level, PlayerSystem player, WeaponSystem weapons, SoundEventBus sounds,
            LevelViewRoot root)
        {
            _level = level;
            _player = player;
            _weapons = weapons;
            _sounds = sounds;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>Waves being shown now (tests).</summary>
        public int ActiveWaves
        {
            get
            {
                var count = 0;
                foreach (var wave in _waves)
                    if (wave != null && wave.Renderer.enabled) count++;
                return count;
            }
        }

        /// <summary>Whether the start-of-walking / start-of-firing rings are shown now (tests).</summary>
        public bool StepRingShown => _steps != null && _steps.Renderer.enabled;
        public bool AutomaticRingShown => _automatic != null && _automatic.Renderer.enabled;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _settings = _level.VisualTheme != null ? _level.VisualTheme.Awareness : null;
            if (_settings == null || _settings.NoiseMaterial == null)
                return UniTask.CompletedTask;

            _block = new MaterialPropertyBlock();
            _quad = CreateQuad();
            for (var i = 0; i < _waves.Length; i++)
                _waves[i] = new Wave { Renderer = CreateRenderer("NoiseWave") };
            _steps = new Ring { Renderer = CreateRenderer("NoiseSteps") };
            _automatic = new Ring { Renderer = CreateRenderer("NoiseAutomatic") };

            if (!_subscribed)
            {
                _sounds.Emitted += OnSound;
                _subscribed = true;
            }

            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            if (_settings == null || _quad == null) return;

            for (var i = 0; i < _waves.Length; i++)
            {
                var wave = _waves[i];
                if (!wave.Renderer.enabled) continue;

                wave.Elapsed += deltaTime;
                var t = wave.Elapsed / Mathf.Max(_settings.WaveTime, 0.05f);
                if (t >= 1f)
                {
                    wave.Renderer.enabled = false;
                    continue;
                }

                var grow = 1f - (1f - t) * (1f - t); // fast start, slows at the radius
                Draw(wave.Renderer, wave.Position, Mathf.Max(wave.Radius * grow, 0.05f), 1f - t);
            }

            var moving = _player.IsSpawned && _player.SpeedFactor > MovingThreshold;
            UpdateRing(_steps, moving, _player.Definition.StepSoundRadius, deltaTime);

            _automaticLeft -= deltaTime;
            UpdateRing(_automatic, _automaticLeft > 0f, _automaticRadius, deltaTime);
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (_steps != null) objects.Add(_steps.Renderer.gameObject);
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _sounds.Emitted -= OnSound;
                _subscribed = false;
            }

            for (var i = 0; i < _waves.Length; i++)
            {
                if (_waves[i] != null) UnityObjects.DestroyObjectOf(_waves[i].Renderer);
                _waves[i] = null;
            }
            if (_steps != null) UnityObjects.DestroyObjectOf(_steps.Renderer);
            if (_automatic != null) UnityObjects.DestroyObjectOf(_automatic.Renderer);
            UnityObjects.Destroy(_quad);
            _steps = _automatic = null;
            _quad = null;
        }

        private void OnSound(SoundEvent sound)
        {
            switch (sound.Type)
            {
                case SoundType.Step:
                    return; // the steady step ring
                case SoundType.Ranged:
                    var weapon = _weapons.Active;
                    if (weapon != null && weapon.Definition.FireMode == FireMode.Automatic)
                    {
                        _automaticRadius = sound.Radius;
                        _automaticLeft = weapon.Definition.Cooldown + AutomaticHold;
                        return;
                    }
                    break;
            }

            // The oldest wave is reused when all are busy.
            var wave = _waves[_nextWave];
            _nextWave = (_nextWave + 1) % _waves.Length;
            wave.Position = sound.Position;
            wave.Radius = sound.Radius;
            wave.Elapsed = 0f;
            wave.Renderer.enabled = true;
            Draw(wave.Renderer, wave.Position, 0.05f, 1f);
        }

        /// <summary>
        /// A start-of-activity ring: when <paramref name="active"/> begins after a quiet pause, the ring grows from the
        /// player to <paramref name="radius"/> and fades, following the player, whether the activity goes on or not.
        /// </summary>
        private void UpdateRing(Ring ring, bool active, float radius, float deltaTime)
        {
            if (active)
            {
                ring.Quiet = 0f;
                if (ring.Armed)
                {
                    ring.Armed = false;
                    ring.Shown = true;
                    ring.Elapsed = 0f;
                }
            }
            else
            {
                ring.Quiet += deltaTime;
                if (ring.Quiet >= _settings.RingRearm) ring.Armed = true;
            }

            if (ring.Shown)
            {
                ring.Elapsed += deltaTime;
                var growTime = Mathf.Max(_settings.RingGrow, 0.01f);
                var grow = Mathf.Clamp01(ring.Elapsed / growTime);
                var fade = 1f - Mathf.Clamp01((ring.Elapsed - growTime) / Mathf.Max(_settings.RingFade, 0.01f));
                if (fade <= 0f || radius <= 0f || !_player.IsSpawned)
                {
                    ring.Shown = false;
                }
                else
                {
                    if (!ring.Renderer.enabled) ring.Renderer.enabled = true;
                    grow = 1f - (1f - grow) * (1f - grow); // fast start, slows at the radius
                    Draw(ring.Renderer, _player.Position, Mathf.Max(radius * grow, 0.05f), fade);
                    return;
                }
            }

            if (ring.Renderer.enabled) ring.Renderer.enabled = false;
        }

        private void Draw(MeshRenderer renderer, Vector2 position, float radius, float strength)
        {
            var transform = renderer.transform;
            transform.localPosition = new Vector3(position.x, Height, position.y);
            transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);

            var color = _settings.NoiseColor;
            color.a *= strength;
            _block.SetColor(ColorId, color);
            _block.SetFloat(ThicknessId, Mathf.Clamp(_settings.RingWidth / radius, 0.005f, 1f));
            renderer.SetPropertyBlock(_block);
        }

        private MeshRenderer CreateRenderer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _settings.NoiseMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
            return renderer;
        }

        /// <summary>A flat 1×1 quad on XZ, uv 0..1, facing up.</summary>
        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "NoiseQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class Wave
        {
            public MeshRenderer Renderer;
            public Vector2 Position;
            public float Radius;
            public float Elapsed;
        }

        private sealed class Ring
        {
            public MeshRenderer Renderer;

            /// <summary>The next start of the activity shows the ring (quiet long enough).</summary>
            public bool Armed = true;

            public bool Shown;
            public float Elapsed;

            /// <summary>Seconds the activity has been off.</summary>
            public float Quiet;
        }
    }
}
