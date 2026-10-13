using Maze.Presentation.UI.Shapes;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI.Style
{
    /// <summary>
    /// Look of a <see cref="Toggle"/> as a thin pill switch: the knob on the right and both in the accent when on,
    /// on the left in the line colour when off; it slides. Follows <see cref="Toggle.isOn"/> itself (values set
    /// without notification change it too).
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public sealed class UiSwitch : MonoBehaviour
    {
        private const float SlideTime = 0.12f;

        [SerializeField] private UiStyle _style;
        [SerializeField] private UiShape _pill;
        [SerializeField] private UiShape _knob;

        [Tooltip("Knob x (anchored) when off and when on.")]
        [SerializeField] private Vector2 _knobX = new Vector2(-13f, 13f);

        private Toggle _toggle;
        private bool _shown;
        private bool _painted;
        private float _position;

        private void Awake() => _toggle = GetComponent<Toggle>();

        private void OnEnable()
        {
            _painted = false;
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (_toggle == null || _style == null) return;
            var on = _toggle.isOn;
            var target = on ? 1f : 0f;
            if (!_painted)
            {
                _position = target;
                _painted = true;
                _shown = !on; // Force the colours below.
            }

            if (_shown != on)
            {
                _shown = on;
                var color = on ? _style.Accent : _style.Line;
                _pill.Stroke = color;
                _pill.Fill = Color.clear;
                _knob.Fill = color;
                _knob.Stroke = color;
            }

            if (Mathf.Approximately(_position, target) && _knob.rectTransform.anchoredPosition.x == Mathf.Lerp(_knobX.x, _knobX.y, _position))
                return;
            _position = Mathf.MoveTowards(_position, target, Time.unscaledDeltaTime / SlideTime);
            var knob = _knob.rectTransform;
            knob.anchoredPosition = new Vector2(Mathf.Lerp(_knobX.x, _knobX.y, _position), knob.anchoredPosition.y);
        }
    }
}
