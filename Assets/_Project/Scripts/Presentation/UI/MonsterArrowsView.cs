using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// Small arrows at the screen edge towards off-screen monsters (decision 2026-10-26; placed by
    /// <c>MonsterArrowsPresenter</c>). A full-screen HUD layer with its own Canvas (arrows move every frame without
    /// rebuilding the rest of the HUD); its hidden template points along +X. Copies are made at loading and reused.
    /// </summary>
    public sealed class MonsterArrowsView : MonoBehaviour
    {
        [SerializeField] private Image _template;

        [Tooltip("Arrow centres keep this far from the layer's edges, canvas units.")]
        [SerializeField, Min(0f)] private float _edgeInset = 24f;

        private readonly List<Image> _arrows = new List<Image>();
        private readonly List<bool> _shown = new List<bool>();
        private Color _color;

        public RectTransform Layer => (RectTransform)transform;

        public float EdgeInset => _edgeInset;

        /// <summary>Makes at least <paramref name="count"/> arrows (hidden).</summary>
        public void Prepare(int count)
        {
            if (_template == null) return;
            _color = _template.color;
            while (_arrows.Count < count)
            {
                var arrow = Instantiate(_template, transform);
                arrow.name = "Arrow" + _arrows.Count;
                arrow.enabled = false;
                arrow.gameObject.SetActive(true);
                _arrows.Add(arrow);
                _shown.Add(false);
            }
        }

        /// <summary>Arrow <paramref name="index"/> at <paramref name="local"/> (layer space), turned to <paramref name="degrees"/>.</summary>
        public void Show(int index, Vector2 local, float degrees, float alpha)
        {
            if (index >= _arrows.Count) return;
            var arrow = _arrows[index];
            var rect = arrow.rectTransform;
            rect.anchoredPosition = local;
            rect.localRotation = Quaternion.Euler(0f, 0f, degrees);
            var color = _color;
            color.a *= alpha;
            arrow.color = color;
            if (_shown[index]) return;
            _shown[index] = true;
            arrow.enabled = true;
        }

        public void Hide(int index)
        {
            if (index >= _arrows.Count || !_shown[index]) return;
            _shown[index] = false;
            _arrows[index].enabled = false;
        }

        public void HideAll()
        {
            for (var i = 0; i < _arrows.Count; i++) Hide(i);
        }

        /// <summary>Removes the copies (the level is unloaded).</summary>
        public void Clear()
        {
            foreach (var arrow in _arrows)
                if (arrow != null) Destroy(arrow.gameObject);
            _arrows.Clear();
            _shown.Clear();
        }
    }
}
