using UnityEngine;

namespace Maze.Presentation.UI.Style
{
    /// <summary>
    /// A screen fades in when shown (unscaled time, so also over a paused level); its panel, if any, grows from slightly
    /// smaller at the same time. Only the look: the screen is active and takes clicks at once.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UiAppear : MonoBehaviour
    {
        private const float Duration = 0.16f;
        private const float StartScale = 0.96f;

        [Tooltip("Grows in with the fade (a dialog panel); optional.")]
        [SerializeField] private RectTransform _panel;

        private CanvasGroup _group;
        private float _time = -1f;

        private void Awake() => _group = GetComponent<CanvasGroup>();

        private void OnEnable()
        {
            _time = 0f;
            Apply(0f);
        }

        private void OnDisable()
        {
            _time = -1f;
            Apply(1f);
        }

        private void Update()
        {
            if (_time < 0f) return;
            _time += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(_time / Duration);
            Apply(t * (2f - t)); // Ease out.
            if (t >= 1f) _time = -1f;
        }

        private void Apply(float t)
        {
            if (_group != null) _group.alpha = t;
            if (_panel != null)
            {
                var scale = Mathf.Lerp(StartScale, 1f, t);
                _panel.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
