using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// Frames per second in a corner of the screen (switched on in the settings). Averages over
    /// <see cref="Interval"/> seconds of real time, so pauses and slow frames show; the text changes only when the
    /// number does and comes from strings made once, so the counter itself allocates nothing per frame.
    /// </summary>
    public sealed class FpsCounter : MonoBehaviour
    {
        private const float Interval = 0.5f;
        private const int MaxShown = 240;

        private static string[] _texts;

        [SerializeField] private TMP_Text _text;

        private float _elapsed;
        private int _frames;
        private int _shown = -1;

        /// <summary>The last measured value (tests, diagnostics).</summary>
        public int Fps { get; private set; }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf == visible) return;
            gameObject.SetActive(visible);
        }

        private void Awake()
        {
            if (_texts != null) return;
            _texts = new string[MaxShown + 1];
            for (var i = 0; i < MaxShown; i++)
                _texts[i] = "FPS " + i;
            _texts[MaxShown] = "FPS " + MaxShown + "+";
        }

        private void OnEnable()
        {
            _elapsed = 0f;
            _frames = 0;
            _shown = -1;
            if (_text != null) _text.text = "FPS -";
        }

        private void Update()
        {
            _frames++;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < Interval) return;

            Fps = Mathf.RoundToInt(_frames / _elapsed);
            _frames = 0;
            _elapsed = 0f;

            var shown = Mathf.Clamp(Fps, 0, MaxShown);
            if (shown == _shown || _text == null) return;
            _shown = shown;
            _text.text = _texts[shown];
        }
    }
}
