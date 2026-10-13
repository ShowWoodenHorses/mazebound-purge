using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Level;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Fixtures of the level's light sources (torches on walls; load stage BuildVisuals, after
    /// <see cref="LevelVisualSystem"/>): the saved variant (<see cref="LightFixtures.Resolve"/>) on the wall face
    /// (<see cref="LightFixtures.TryGetPose"/>), as entity views — hidden with their cell, warmed up with the level.
    /// The flame takes the light's colour. The load warm-up clears particle systems, so the flames of the views that
    /// stayed active are restarted on the first late tick (hidden ones restart themselves when shown: play on awake).
    /// </summary>
    public sealed class LightFixturesView : ILevelLoadStep, ILevelLateTickable
    {
        private readonly LevelData _level;
        private readonly LevelVisualSystem _visuals;
        private readonly LevelViewRoot _root;
        private readonly EntityViewRegistry _entities;
        private readonly List<TorchFlame> _flames = new List<TorchFlame>();
        private bool _restartPending;

        public LightFixturesView(LevelData level, LevelVisualSystem visuals, LevelViewRoot root, EntityViewRegistry entities)
        {
            _level = level;
            _visuals = visuals;
            _root = root;
            _entities = entities;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>Number of fixtures created (for tests).</summary>
        public int Count { get; private set; }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var factory = _visuals.EntityViews;
            var missing = 0;
            foreach (var light in _level.Lights)
            {
                var choice = LightFixtures.Resolve(_level, light);
                if (choice.IsEmpty || !LightFixtures.TryGetPose(_level.Geometry, light, out var offset, out var yaw))
                    continue;

                var view = factory?.Create(light.Id, VisualKind.Light, choice, light.Cell, _root.transform, offset, yaw);
                if (view == null)
                {
                    missing++;
                    continue;
                }

                _entities.Add(view);
                Count++;
                var flame = view.GameObject.GetComponent<TorchFlame>();
                if (flame == null) continue;
                flame.SetColor(light.Color);
                _flames.Add(flame);
            }

            if (missing > 0)
                GameLog.Warning(LogChannel.Visual, $"Level '{_level.name}': {missing} light fixture(s) have no prefab.");
            _restartPending = _flames.Count > 0;
            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            if (!_restartPending)
                return;

            _restartPending = false;
            foreach (var flame in _flames)
                if (flame != null)
                    flame.Play();
        }
    }
}
