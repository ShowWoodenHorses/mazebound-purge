using Purge.Application.Save;
using Purge.Core.Definitions;
using Purge.Presentation.UI.Shapes;
using Purge.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The panel over a weapon on the ground (stage 6e2, part of the HUD): the frame and name in the weapon's rarity
    /// colour, rows "stat — value" compared with the weapon in hands — an arrow up/down (<see cref="PickupCompare.Arrows"/>)
    /// or the value coloured (<see cref="PickupCompare.Color"/>). The presenter fills it and places it over the weapon.
    /// </summary>
    public sealed class PickupComparePanel : MonoBehaviour
    {
        /// <summary>Rows in order: damage per second, magazine, range, mobility.</summary>
        public const int RowCount = 4;

        [SerializeField] private UiStyle _style;
        [SerializeField] private UiShape _frame;
        [SerializeField] private TMP_Text _name;
        [Tooltip("Stat captions, RowCount in order.")]
        [SerializeField] private TMP_Text[] _captions;
        [Tooltip("Stat values, RowCount in order.")]
        [SerializeField] private TMP_Text[] _values;
        [Tooltip("Arrows (a chevron pointing right, turned up or down), RowCount in order.")]
        [SerializeField] private Image[] _arrows;

        private RectTransform _rect;

        public bool IsShown => gameObject.activeSelf;

        public void SetVisible(bool shown)
        {
            if (gameObject.activeSelf != shown) gameObject.SetActive(shown);
        }

        public void SetCaption(int row, string caption)
        {
            if (row < _captions.Length) _captions[row].text = caption;
        }

        public void SetHeader(string name, Core.Content.Rarity rarityOf)
        {
            var rarity = _style.RarityColor(rarityOf);
            _name.text = name;
            _name.color = rarity;
            _frame.Stroke = rarity;
        }

        /// <summary>One row: its value and how it compares, shown as the mode says.</summary>
        public void SetRow(int row, string value, StatDelta delta, PickupCompare mode)
        {
            if (row >= _values.Length) return;
            var color = delta == StatDelta.Better ? _style.Better : delta == StatDelta.Worse ? _style.Danger : _style.Text;
            _values[row].text = value;
            _values[row].color = mode == PickupCompare.Color ? color : _style.Text;
            var arrow = _arrows[row];
            var shown = mode == PickupCompare.Arrows && delta != StatDelta.Same;
            if (arrow.enabled != shown) arrow.enabled = shown;
            if (!shown) return;
            arrow.color = color;
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, delta == StatDelta.Better ? 90f : -90f);
        }

        /// <summary>Puts the panel's bottom centre at <paramref name="screen"/> (pixels), kept inside the HUD.</summary>
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
