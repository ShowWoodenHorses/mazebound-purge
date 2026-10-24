using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// A small bar of the HUD over a point of the world (stage 7в: the opening of a chest). The presenter places it
    /// (<see cref="Place"/>) and sets the fill; layout — <c>RuntimeScenesBuilder</c>.
    /// </summary>
    public sealed class HudWorldBar : MonoBehaviour
    {
        [SerializeField] private RectTransform _fill;

        private RectTransform _rect;

        public bool IsShown => gameObject.activeSelf;

        public void SetVisible(bool shown)
        {
            if (gameObject.activeSelf != shown) gameObject.SetActive(shown);
        }

        /// <summary>0..1 of the bar filled from the left.</summary>
        public void SetProgress(float progress)
        {
            if (_fill != null) _fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }

        /// <summary>Puts the bar's bottom centre at <paramref name="screen"/> (pixels), kept inside the HUD.</summary>
        public void Place(Vector2 screen)
        {
            if (_rect == null) _rect = (RectTransform)transform;
            var area = (RectTransform)_rect.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, null, out var local);
            var bounds = area.rect;
            var size = _rect.rect.size;
            local.x = Mathf.Clamp(local.x, bounds.xMin + size.x * 0.5f, bounds.xMax - size.x * 0.5f);
            local.y = Mathf.Clamp(local.y, bounds.yMin, bounds.yMax - size.y);
            _rect.localPosition = local;
        }
    }
}
