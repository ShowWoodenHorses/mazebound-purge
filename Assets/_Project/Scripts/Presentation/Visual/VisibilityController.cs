using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Grid;
using Maze.Gameplay.Level;
using Maze.Gameplay.Visibility;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Applies VisibleCells to the already built views (ТЗ §46–48, §106): the per-cell geometry mask and chunks,
    /// and entity views by their cell. Only switches Visible/Hidden — never creates, destroys or picks visuals.
    /// Runs on <see cref="VisibilitySystem.Changed"/>, not per frame; views added or moved later get their state
    /// immediately. Must be registered after <see cref="LevelVisualSystem"/> (same load stage, registration order).
    /// </summary>
    public sealed class VisibilityController : ILevelLoadStep, IDisposable
    {
        private readonly VisibilitySystem _visibility;
        private readonly LevelVisualSystem _visuals;
        private readonly EntityViewRegistry _entities;
        private readonly FogOfWarView _fog;
        private readonly List<GridPosition> _shown = new List<GridPosition>();

        private LevelGeometryView _geometry;

        public VisibilityController(VisibilitySystem visibility, LevelVisualSystem visuals, EntityViewRegistry entities,
            FogOfWarView fog = null)
        {
            _fog = fog;
            _visibility = visibility;
            _visuals = visuals;
            _entities = entities;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>How many times the result was applied (diagnostics and tests).</summary>
        public int ApplyCount { get; private set; }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _geometry = _visuals.Geometry ??
                throw new InvalidOperationException($"{nameof(VisibilityController)} must run after {nameof(LevelVisualSystem)} built the level.");

            // Nothing is visible until the player appears.
            _geometry.SetAllVisible(false);
            _shown.Clear();

            _visibility.Changed += Apply;
            _entities.Added += ApplyTo;
            _entities.Moved += ApplyTo;
            Apply();
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            _visibility.Changed -= Apply;
            _entities.Added -= ApplyTo;
            _entities.Moved -= ApplyTo;
            _geometry = null;
        }

        private void Apply()
        {
            if (_geometry == null) return;

            // Only cells whose state changed are touched; the mask uploads once in ApplyVisibility.
            // Geometry follows revealed cells (no one-cell holes), objects follow strict visibility.
            // With fog, cells leaving the view stay until the fog covers them (FogOfWarView hides them then).
            var fogHides = _fog != null && _fog.HidesGeometry && _visibility.HasResult;
            if (!fogHides)
                foreach (var cell in _shown)
                    if (!_visibility.IsRevealed(cell))
                        _geometry.SetCellVisible(cell, false);

            _shown.Clear();
            var visible = _visibility.RevealedCells;
            for (var i = 0; i < visible.Count; i++)
            {
                _geometry.SetCellVisible(visible[i], true);
                _shown.Add(visible[i]);
            }

            _geometry.ApplyVisibility();

            var views = _entities.All;
            for (var i = 0; i < views.Count; i++)
                ApplyTo(views[i]);

            ApplyCount++;
        }

        private void ApplyTo(EntityView view) => view.SetVisible(_visibility.IsVisible(view.Cell));
    }
}
