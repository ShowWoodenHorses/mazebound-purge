using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Level;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Breath puffs in the cold (<see cref="ThemeWeather"/>, display only): every <see cref="ThemeWeather.BreathInterval"/>
    /// the player and each living zombie whose view is shown puff a small cloud from the mouth (the head bone of the
    /// shared humanoid avatar), each with its own phase. One particle system, emitted from code, no allocations;
    /// time stands still on pause.
    /// </summary>
    public sealed class BreathView : ILevelLoadStep, ILevelTickable, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private readonly LevelData _level;
        private readonly PlayerViewPresenter _player;
        private readonly ZombieViewPresenter _zombies;
        private readonly LevelViewRoot _root;

        private ThemeWeather _weather;
        private ParticleSystem _breath;
        private float _time;
        private bool _ticked;

        public BreathView(LevelData level, PlayerViewPresenter player, ZombieViewPresenter zombies, LevelViewRoot root)
        {
            _level = level;
            _player = player;
            _zombies = zombies;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>The puffs (tests); null without a breath material.</summary>
        public ParticleSystem Particles => _breath;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _weather = _level.VisualTheme != null ? _level.VisualTheme.Weather : null;
            if (_weather == null || _weather.BreathMaterial == null)
                return UniTask.CompletedTask;

            _breath = WeatherParticles.Create("Breath", _root.transform, _weather.BreathMaterial, 48,
                fadeIn: 0.15f, fadeOut: 0.35f, grow: 2.2f);
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            if (_breath == null) return;
            _ticked = true;
            if (!_breath.isPlaying) _breath.Play(); // the warmup stops every particle system

            var interval = Mathf.Max(_weather.BreathInterval, 0.1f);
            var before = _time;
            _time += deltaTime;

            TryPuff(_player.Head, before, interval);
            var heads = _zombies.Heads;
            for (var i = 0; i < heads.Count; i++)
                TryPuff(heads[i], before, interval);
        }

        public void LateTick(float deltaTime)
        {
            if (_breath == null) return;
            if (!_ticked && _breath.isPlaying) _breath.Pause();
            _ticked = false;
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (_breath != null) objects.Add(_breath.gameObject);
        }

        public void Dispose()
        {
            UnityObjects.DestroyObjectOf(_breath);
            _breath = null;
            _time = 0f;
        }

        /// <summary>Puffs when the character's own clock (time + its phase) crosses a whole interval.</summary>
        private void TryPuff(CharacterHead head, float before, float interval)
        {
            if (!head.IsShown) return;

            var shift = head.Phase * interval;
            if (Mathf.Floor((before + shift) / interval) == Mathf.Floor((_time + shift) / interval))
                return;

            var mouth = head.Mouth(_weather.BreathMouthOffset);
            var forward = head.Root.forward;
            forward.y = 0f;
            _breath.Emit(new ParticleSystem.EmitParams
            {
                position = mouth,
                velocity = forward.normalized * _weather.BreathSpeed + Vector3.up * 0.08f,
                startSize = _weather.BreathSize * UnityEngine.Random.Range(0.85f, 1.15f),
                startLifetime = _weather.BreathLifetime * UnityEngine.Random.Range(0.85f, 1.15f),
                rotation = UnityEngine.Random.Range(0f, 360f),
                startColor = _weather.BreathColor,
                applyShapeToPosition = false,
            }, 1);
        }
    }
}
