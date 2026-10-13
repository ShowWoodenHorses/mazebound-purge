using Maze.Gameplay.Combat;
using Maze.Presentation.UI.Shapes;
using Maze.Presentation.UI.Style;
using UnityEngine;

namespace Maze.Presentation.UI.Touch
{
    /// <summary>
    /// Diamond marks around the stick ring showing the aim mode: four (N, E, S, W) for <see cref="AimMode.Four"/>, eight
    /// for <see cref="AimMode.Eight"/>, none for <see cref="AimMode.Free"/>. The mark nearest to where the stick
    /// points is filled with the accent while it is held.
    /// </summary>
    public sealed class TouchStickMarks : MonoBehaviour
    {
        /// <summary>Deflection (0..1) from which a direction is shown.</summary>
        private const float ShowFrom = 0.35f;

        [SerializeField] private UiStyle _style;

        [Tooltip("Eight marks counter-clockwise from East: E, NE, N, NW, W, SW, S, SE.")]
        [SerializeField] private UiShape[] _marks;

        private AimMode _mode = AimMode.Four;
        private int _lit = -1;

        public void SetMode(AimMode mode)
        {
            _mode = mode;
            for (var i = 0; i < _marks.Length; i++)
                if (_marks[i] != null) _marks[i].gameObject.SetActive(IsShown(i));
            Light(-1);
        }

        /// <summary>Stick deflection (−1..1 per axis; zero when released).</summary>
        public void Point(Vector2 deflection)
        {
            if (_mode == AimMode.Free || deflection.sqrMagnitude < ShowFrom * ShowFrom)
            {
                Light(-1);
                return;
            }

            var step = _mode == AimMode.Four ? 90f : 45f;
            var angle = Mathf.Atan2(deflection.y, deflection.x) * Mathf.Rad2Deg;
            var index = Mathf.RoundToInt(Mathf.Repeat(Mathf.Round(angle / step) * step, 360f) / 45f) % 8;
            Light(index);
        }

        private bool IsShown(int index) => _mode == AimMode.Eight || (_mode == AimMode.Four && index % 2 == 0);

        private void Light(int index)
        {
            if (index == _lit || _style == null) return;
            if (_lit >= 0 && _marks[_lit] != null) Paint(_marks[_lit], false);
            _lit = index;
            if (_lit >= 0 && _marks[_lit] != null) Paint(_marks[_lit], true);
        }

        private void Paint(UiShape mark, bool lit)
        {
            mark.Stroke = lit ? _style.Accent : _style.Line;
            mark.Fill = lit ? _style.Accent : Color.clear;
        }
    }
}
