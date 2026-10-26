using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The boss's HP bar at the top centre of the HUD (stage 8a): its name, the fill with a trail that catches up, the
    /// notches of its phases (HP shares). Laid out by <c>RuntimeScenesBuilder</c>; driven by <c>BossHudPresenter</c>.
    /// </summary>
    public sealed class BossBarView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _name;
        [SerializeField] private RectTransform _fill;
        [SerializeField] private RectTransform _trail;
        [Tooltip("Hidden template of a phase notch (a thin mark across the bar); copies are made on Show.")]
        [SerializeField] private RectTransform _notchTemplate;

        [Tooltip("Share of the bar per second the trail catches up with.")]
        [SerializeField, Min(0.05f)] private float _trailSpeed = 0.35f;

        private readonly List<RectTransform> _notches = new List<RectTransform>();
        private float _shown = 1f;
        private float _trailShown = 1f;

        public bool IsShown => gameObject.activeSelf;

        /// <summary>Shows the bar full with <paramref name="title"/> and a notch per threshold (HP shares 0..1).</summary>
        public void Show(string title, IReadOnlyList<float> thresholds)
        {
            if (_name != null) _name.text = title;
            while (_notches.Count < thresholds.Count && _notchTemplate != null)
            {
                var notch = Instantiate(_notchTemplate, _notchTemplate.parent);
                notch.name = "Notch" + _notches.Count;
                _notches.Add(notch);
            }

            for (var i = 0; i < _notches.Count; i++)
            {
                var notch = _notches[i];
                var used = i < thresholds.Count;
                notch.gameObject.SetActive(used);
                if (!used) continue;
                var x = Mathf.Clamp01(thresholds[i]);
                notch.anchorMin = new Vector2(x, 0f);
                notch.anchorMax = new Vector2(x, 1f);
                notch.anchoredPosition = Vector2.zero;
            }

            _shown = _trailShown = 1f;
            Apply();
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        /// <summary>Its HP share now; the trail follows at <see cref="_trailSpeed"/>.</summary>
        public void SetHealth(float share, float deltaTime)
        {
            _shown = Mathf.Clamp01(share);
            _trailShown = _trailShown > _shown ? Mathf.Max(_shown, _trailShown - _trailSpeed * deltaTime) : _shown;
            Apply();
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private void Apply()
        {
            if (_fill != null) _fill.anchorMax = new Vector2(_shown, 1f);
            if (_trail != null) _trail.anchorMax = new Vector2(_trailShown, 1f);
        }
    }
}
