using Maze.Presentation.UI.Shapes;
using Maze.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// A weapon slot of the HUD where the on-screen controls are not shown (desktop): a frame (accent when active,
    /// faint when empty), the weapon silhouette, rounds in the magazine and a reload line along the bottom, the key
    /// hint in the corner; the ranged slot also shows the reload key while the magazine in hands is not full.
    /// </summary>
    public sealed class HudWeaponSlot : MonoBehaviour
    {
        [SerializeField] private UiStyle _style;
        [SerializeField] private UiShape _frame;
        [SerializeField] private Image _icon;
        [SerializeField] private Sprite _fallbackIcon;
        [SerializeField] private TMP_Text _ammo;
        [SerializeField] private TMP_Text _hint;

        [Tooltip("Optional: the reload key, shown while the active weapon's magazine is not full.")]
        [SerializeField] private TMP_Text _reloadHint;

        [Tooltip("Accent line along the bottom; its width (right anchor) is the reload done.")]
        [SerializeField] private RectTransform _reload;

        private WeaponSlotState _state;
        private bool _painted;
        private string _reloadText = "RELOAD";

        /// <summary>The ammo caption while reloading (localized).</summary>
        public void SetReloadText(string text)
        {
            _reloadText = text;
            _painted = false; // Repaint with the new caption.
        }

        public void Set(in WeaponSlotState state)
        {
            if (_painted && state.Same(_state)) return;
            var ammoChanged = !_painted || state.Ammo != _state.Ammo || state.Magazine != _state.Magazine ||
                              state.Reloading != _state.Reloading || state.Equipped != _state.Equipped;
            _state = state;
            _painted = true;
            if (_style == null) return;

            var line = state.Active ? _style.Accent : _style.Line;
            var fill = _style.Fill;
            if (!state.Equipped)
            {
                line.a *= _style.DisabledAlpha;
                fill.a *= _style.DisabledAlpha;
            }

            _frame.Stroke = line;
            _frame.Fill = fill;

            _icon.enabled = state.Equipped;
            if (state.Equipped)
            {
                _icon.sprite = state.Icon != null ? state.Icon : _fallbackIcon;
                var color = _style.Text;
                if (state.Reloading) color.a *= 0.5f;
                _icon.color = color;
            }

            if (_hint != null)
            {
                var hint = state.Active ? _style.Accent : _style.MutedText;
                if (!state.Equipped) hint.a *= _style.DisabledAlpha;
                _hint.color = hint;
            }

            if (_ammo != null)
            {
                var shown = state.Equipped && state.Magazine > 0;
                _ammo.enabled = shown;
                if (shown && ammoChanged)
                    _ammo.text = state.Reloading ? _reloadText : state.Ammo + "/" + state.Magazine;
                if (shown) _ammo.color = state.Reloading ? _style.Accent : _style.Text;
            }

            if (_reloadHint != null)
            {
                var canReload = state.Active && state.Equipped && !state.Reloading && state.Magazine > 0 &&
                                state.Ammo < state.Magazine;
                if (_reloadHint.enabled != canReload) _reloadHint.enabled = canReload;
                if (canReload) _reloadHint.color = _style.MutedText;
            }

            if (_reload != null)
            {
                var reloading = state.Equipped && state.Reloading;
                if (_reload.gameObject.activeSelf != reloading) _reload.gameObject.SetActive(reloading);
                if (reloading) _reload.anchorMax = new Vector2(Mathf.Clamp01(state.Reload), _reload.anchorMax.y);
            }
        }
    }
}
