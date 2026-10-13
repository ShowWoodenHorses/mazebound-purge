using UnityEngine;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// Moves a few laid-out elements left from where the scene builder put them (a site's bar over the screen edge,
    /// <see cref="Maze.Application.Platform.PlatformTraits.TopRightButtonsShift"/>). Repeated calls do not add up.
    /// </summary>
    public sealed class HorizontalShift
    {
        private readonly RectTransform[] _targets;
        private Vector2[] _laidOut;

        public HorizontalShift(params Component[] targets)
        {
            _targets = new RectTransform[targets.Length];
            for (var i = 0; i < targets.Length; i++)
                _targets[i] = targets[i] != null ? (RectTransform)targets[i].transform : null;
        }

        /// <summary>Puts the elements <paramref name="left"/> UI units left of their laid-out places.</summary>
        public void Apply(float left)
        {
            if (_laidOut == null)
            {
                if (left == 0f) return;
                _laidOut = new Vector2[_targets.Length];
                for (var i = 0; i < _targets.Length; i++)
                    if (_targets[i] != null) _laidOut[i] = _targets[i].anchoredPosition;
            }

            for (var i = 0; i < _targets.Length; i++)
                if (_targets[i] != null) _targets[i].anchoredPosition = _laidOut[i] + new Vector2(-left, 0f);
        }
    }
}
