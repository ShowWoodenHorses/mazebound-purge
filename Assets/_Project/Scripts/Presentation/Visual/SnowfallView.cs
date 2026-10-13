using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Falling snow of the theme's weather (<see cref="ThemeWeather"/>, display only): flakes are emitted from code into
    /// one world-space particle system over a square around the player — only where the camera looks, no matter how
    /// big the level is. One draw call, a fixed particle budget, no allocations. Emitted in the tick (none on pause),
    /// the system itself is paused while the level does not tick.
    /// </summary>
    public sealed class SnowfallView : ILevelLoadStep, ILevelTickable, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly LevelViewRoot _root;

        private ThemeWeather _weather;
        private ParticleSystem _snow;
        private float _pending;
        private bool _filled;
        private bool _ticked;

        public SnowfallView(LevelData level, PlayerSystem player, LevelViewRoot root)
        {
            _level = level;
            _player = player;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>The flakes (tests); null without a snow material.</summary>
        public ParticleSystem Particles => _snow;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _weather = _level.VisualTheme != null ? _level.VisualTheme.Weather : null;
            if (_weather == null || _weather.SnowMaterial == null || _weather.SnowRate <= 0f)
                return UniTask.CompletedTask;

            var budget = Mathf.CeilToInt(_weather.SnowRate * Lifetime * 1.25f) + 8;
            _snow = WeatherParticles.Create("Snowfall", _root.transform, _weather.SnowMaterial, budget,
                fadeIn: 0.12f, fadeOut: 0.9f);
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            if (_snow == null || !_player.IsSpawned) return;
            _ticked = true;
            if (!_snow.isPlaying) _snow.Play(); // the warmup stops every particle system

            if (!_filled)
            {
                // The first frame already looks like snowing: a full column at random heights.
                _filled = true;
                var count = Mathf.RoundToInt(_weather.SnowRate * Lifetime);
                for (var i = 0; i < count; i++)
                    Emit(UnityEngine.Random.Range(0.05f, 1f));
                return;
            }

            _pending += _weather.SnowRate * deltaTime;
            while (_pending >= 1f)
            {
                _pending -= 1f;
                Emit(1f);
            }
        }

        public void LateTick(float deltaTime)
        {
            if (_snow == null) return;

            // Paused (map, pause menu): the flakes hang in the air.
            if (!_ticked && _snow.isPlaying) _snow.Pause();
            _ticked = false;
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (_snow != null) objects.Add(_snow.gameObject);
        }

        public void Dispose()
        {
            UnityObjects.DestroyObjectOf(_snow);
            _snow = null;
            _pending = 0f;
            _filled = false;
        }

        private float Lifetime => _weather.SnowHeight / Mathf.Max(_weather.SnowFallSpeed, 0.01f);

        /// <param name="heightShare">Share of the full height the flake starts from (1 = the top).</param>
        private void Emit(float heightShare)
        {
            var half = _weather.SnowArea * 0.5f;
            var player = _player.Position;
            var height = _weather.SnowHeight * heightShare;
            var flutter = UnityEngine.Random.insideUnitCircle * _weather.SnowFlutter;
            var size = _weather.SnowSize * UnityEngine.Random.Range(0.7f, 1.3f);
            var position = new Vector3(
                player.x + UnityEngine.Random.Range(-half, half), height, player.y + UnityEngine.Random.Range(-half, half));

            _snow.Emit(new ParticleSystem.EmitParams
            {
                position = _root.transform.TransformPoint(position),
                velocity = new Vector3(_weather.SnowWind.x + flutter.x, -_weather.SnowFallSpeed, _weather.SnowWind.y + flutter.y),
                startSize = size,
                startLifetime = height / Mathf.Max(_weather.SnowFallSpeed, 0.01f),
                rotation = UnityEngine.Random.Range(0f, 360f),
                startColor = _weather.SnowColor,
                applyShapeToPosition = false,
            }, 1);
        }
    }

    /// <summary>A code-driven particle system of the weather: world space, emitted only with <c>Emit</c>, no shadows.</summary>
    internal static class WeatherParticles
    {
        /// <param name="fadeIn">Share of the lifetime the particle fades in over.</param>
        /// <param name="fadeOut">Share of the lifetime from which it fades out.</param>
        /// <param name="grow">Size at the end of the lifetime (1 = no growth).</param>
        public static ParticleSystem Create(string name, Transform parent, Material material, int maxParticles,
            float fadeIn, float fadeOut, float grow = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = Mathf.Max(maxParticles, 1);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;

            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var collision = particles.collision;
            collision.enabled = false; // no physics in the project

            if (!Mathf.Approximately(grow, 1f))
            {
                var size = particles.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, grow));
            }

            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, Mathf.Clamp(fadeIn, 0.01f, 0.98f)),
                    new GradientAlphaKey(1f, Mathf.Clamp(fadeOut, 0.02f, 0.99f)), new GradientAlphaKey(0f, 1f),
                });
            color.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            particles.Play();
            return particles;
        }
    }
}
