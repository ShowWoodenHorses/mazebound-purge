using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Level;
using Maze.Gameplay.Level;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>A level view with objects that are not shown right away (pooled effects, weapons not in hand yet).</summary>
    public interface IViewWarmup
    {
        /// <summary>Adds objects to draw once while loading. They may be inactive; their state is restored after.</summary>
        void CollectWarmup(List<GameObject> objects);
    }

    /// <summary>
    /// Pays the first-draw costs while the loading screen is still shown (ТЗ §103), instead of on the frame the player
    /// first meets a zombie or picks up a weapon: shader programs, mesh and texture uploads, skinning buffers, animator
    /// graphs. Every entity view (hidden ones too, with inactive children and disabled renderers — e.g. a door's other
    /// state) and every object from <see cref="IViewWarmup"/> is switched on for one frame of a helper camera that sees
    /// the whole level and draws into a tiny texture; then all of it is switched back exactly as it was. The helper
    /// copies the level camera, so the same shader variants are used. Particle systems are simulated for
    /// <see cref="ParticleTime"/> so they have something to draw, then cleared. Ends with a garbage collection, so the load's
    /// garbage is not collected in the middle of play.
    /// </summary>
    public sealed class LevelWarmup : ILevelLoadStep
    {
        private const int TextureSize = 32;
        private const float CameraHeight = 20f;

        /// <summary>Seconds particle systems are simulated before the draw: bursts are out, short flashes still alive.</summary>
        private const float ParticleTime = 0.02f;

        private readonly LevelData _level;
        private readonly EntityViewRegistry _views;
        private readonly TopDownCamera _camera;
        private readonly IReadOnlyList<IViewWarmup> _sources;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<GameObject> _activated = new List<GameObject>();
        private readonly List<Renderer> _enabled = new List<Renderer>();
        private readonly List<Transform> _transforms = new List<Transform>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Animator> _animators = new List<Animator>();
        private readonly List<ParticleSystem> _particles = new List<ParticleSystem>();
        private readonly List<ParticleSystem> _foundParticles = new List<ParticleSystem>();
        private readonly List<Animator> _foundAnimators = new List<Animator>();

        public LevelWarmup(LevelData level, EntityViewRegistry views, TopDownCamera camera, IReadOnlyList<IViewWarmup> sources)
        {
            _level = level;
            _views = views;
            _camera = camera;
            _sources = sources ?? Array.Empty<IViewWarmup>();
        }

        public LevelLoadStage Stage => LevelLoadStage.Warmup;

        /// <summary>Objects drawn by the last warm-up (for tests).</summary>
        public int WarmedCount { get; private set; }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _objects.Clear();
            foreach (var view in _views.All)
                if (view.GameObject != null)
                    _objects.Add(view.GameObject);
            foreach (var source in _sources)
                source.CollectWarmup(_objects);

            try
            {
                foreach (var target in _objects)
                    if (target != null)
                        SwitchOn(target);

                foreach (var animator in _animators)
                    if (animator != null && animator.isActiveAndEnabled)
                        animator.Update(0f);
                foreach (var particles in _particles)
                    if (particles != null)
                        particles.Simulate(ParticleTime, false, true);

                Render();
                WarmedCount = _objects.Count;
            }
            finally
            {
                foreach (var particles in _particles)
                    if (particles != null)
                        particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

                // Reverse order: children were switched on after their parents.
                for (var i = _enabled.Count - 1; i >= 0; i--)
                    if (_enabled[i] != null) _enabled[i].enabled = false;
                for (var i = _activated.Count - 1; i >= 0; i--)
                    if (_activated[i] != null) _activated[i].SetActive(false);

                _objects.Clear();
                _activated.Clear();
                _enabled.Clear();
                _animators.Clear();
                _particles.Clear();
            }

            GC.Collect();
            return UniTask.CompletedTask;
        }

        private void SwitchOn(GameObject target)
        {
            target.GetComponentsInChildren(true, _transforms);
            foreach (var transform in _transforms)
            {
                var gameObject = transform.gameObject;
                if (gameObject.activeSelf) continue;
                gameObject.SetActive(true);
                _activated.Add(gameObject);
            }

            target.GetComponentsInChildren(true, _renderers);
            foreach (var renderer in _renderers)
            {
                if (renderer.enabled) continue;
                renderer.enabled = true;
                _enabled.Add(renderer);
            }

            // GetComponentsInChildren clears the list it fills: collect per target, then add.
            target.GetComponentsInChildren(true, _foundAnimators);
            _animators.AddRange(_foundAnimators);
            target.GetComponentsInChildren(true, _foundParticles);
            _particles.AddRange(_foundParticles);
            _foundAnimators.Clear();
            _foundParticles.Clear();
            _transforms.Clear();
            _renderers.Clear();
        }

        private void Render()
        {
            var source = _camera != null ? _camera.Camera : null;
            if (source == null) return;

            var geometry = _level.Geometry;
            var holder = new GameObject("Warmup Camera");
            var texture = RenderTexture.GetTemporary(TextureSize, TextureSize, 24);
            try
            {
                var camera = holder.AddComponent<Camera>();
                camera.CopyFrom(source);
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(geometry.Width, geometry.Height) * 0.5f + 1f;
                camera.aspect = 1f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = CameraHeight * 2f;
                camera.targetTexture = texture;
                holder.transform.SetPositionAndRotation(
                    new Vector3((geometry.Width - 1) * 0.5f, CameraHeight, (geometry.Height - 1) * 0.5f),
                    Quaternion.Euler(90f, 0f, 0f));
                camera.Render();
                camera.targetTexture = null;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(texture);
                UnityObjects.Destroy(holder);
            }
        }
    }
}
