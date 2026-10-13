using Maze.Presentation.UI.Shapes;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI.Style
{
    /// <summary>
    /// Button in the UI style: a <see cref="UiShape"/> body (dark fill, thin outline) and content (label, icons) in the
    /// line colour, or the accent for the main action (<see cref="Accent"/>). States: hover / gamepad selection — full
    /// strength outline; pressed — the light fill and slightly smaller; disabled — faint. Clicks on a disabled button
    /// still reach <see cref="UiSound"/> (the denied sound).
    /// </summary>
    public class UiButton : Button
    {
        private const float PressedScale = 0.96f;

        /// <summary>Scale change per second: the press takes about 0.07 s.</summary>
        private const float ScaleSpeed = 0.6f;

        [SerializeField] private UiStyle _style;
        [SerializeField] private UiShape _body;

        [Tooltip("Labels and icons tinted with the button (line colour, or the accent).")]
        [SerializeField] private Graphic[] _content;

        [SerializeField] private bool _accent;

        private SelectionState _state;
        private float _scale = 1f;
        private float _targetScale = 1f;

        public UiShape Body => _body;

        /// <summary>The main action of its screen (Play, Next): outline and content in the accent.</summary>
        public bool Accent
        {
            get => _accent;
            set
            {
                if (_accent == value) return;
                _accent = value;
                Paint(_state);
            }
        }

        protected override void Awake()
        {
            base.Awake();
            transition = Transition.None; // The look is painted here.
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _scale = _targetScale = 1f;
            transform.localScale = Vector3.one;
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            _state = state;
            Paint(state);
            _targetScale = state == SelectionState.Pressed ? PressedScale : 1f;
            if (instant || !UnityEngine.Application.isPlaying)
            {
                _scale = _targetScale;
                transform.localScale = new Vector3(_scale, _scale, 1f);
            }
        }

        private void Update()
        {
            if (Mathf.Approximately(_scale, _targetScale)) return;
            _scale = Mathf.MoveTowards(_scale, _targetScale, ScaleSpeed * Time.unscaledDeltaTime);
            transform.localScale = new Vector3(_scale, _scale, 1f);
        }

        /// <summary>Repaints with the current state (after the style or content changed).</summary>
        public void Repaint() => Paint(_state);

        private void Paint(SelectionState state)
        {
            if (_style == null) return;
            var line = _accent ? _style.Accent : _style.Line;
            var fill = _style.Fill;
            var content = _accent ? _style.Accent : _style.Text;
            switch (state)
            {
                case SelectionState.Highlighted:
                case SelectionState.Selected:
                    line = _accent ? _style.Accent : _style.Text;
                    break;
                case SelectionState.Pressed:
                    line = _accent ? _style.Accent : _style.Text;
                    fill = _style.PressedFill;
                    break;
                case SelectionState.Disabled:
                    line.a *= _style.DisabledAlpha;
                    fill.a *= _style.DisabledAlpha;
                    content.a *= _style.DisabledAlpha;
                    break;
            }

            if (_body != null)
            {
                _body.Stroke = line;
                _body.Fill = fill;
            }

            if (_content == null) return;
            foreach (var graphic in _content)
                if (graphic != null) graphic.color = content;
        }
    }
}
