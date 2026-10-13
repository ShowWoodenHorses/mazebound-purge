using Maze.Presentation.UI.Shapes;
using Maze.Presentation.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI.Touch
{
    /// <summary>
    /// Look of the attack cluster and the Use button (design: a quarter-circle Attack with its right angle at the
    /// bottom-right, the Ranged and Melee slots as ring sectors along its arc, Use apart on the left): the active slot
    /// is outlined in the accent, an empty slot is faint, the ranged slot shows the magazine as ticks on its outer arc
    /// and the reload as an arc on its inner edge; Use lights up when it would do something. Pressed buttons get the
    /// light fill. Only the look: the buttons themselves are <c>OnScreenButton</c>s. The left-handed layout mirrors the
    /// content (icons stay readable).
    /// </summary>
    public sealed class TouchAttackCluster : MonoBehaviour
    {
        private const int MaxTicks = 12;
        private const float TickMargin = 3f;

        [SerializeField] private UiStyle _style;

        [Tooltip("Mirrored (scale x = -1) in the left-handed layout.")]
        [SerializeField] private RectTransform _content;

        [SerializeField] private UiShape _attack;
        [SerializeField] private UiShape _melee;
        [SerializeField] private UiShape _ranged;
        [SerializeField] private Image _meleeIcon;
        [SerializeField] private Image _rangedIcon;

        [Tooltip("Thin sector on the inner edge of the ranged slot: its sweep shows the reload.")]
        [SerializeField] private UiShape _reload;

        [Tooltip("Magazine ticks along the outer arc of the ranged slot (placed at runtime by the magazine size).")]
        [SerializeField] private UiShape[] _ticks;

        [Header("Geometry (content space)")]
        [SerializeField] private Vector2 _center;
        [SerializeField] private float _tickRadius = 275f;
        [SerializeField] private float _rangedStart = 93f;
        [SerializeField] private float _rangedSweep = 40f;

        [Header("Use")]
        [SerializeField] private UiShape _use;
        [SerializeField] private Image _useIcon;
        [SerializeField] private Sprite _doorIcon;
        [SerializeField] private Sprite _pickUpIcon;

        [Header("Generic slot icons")]
        [SerializeField] private Sprite _meleeIconFallback;
        [SerializeField] private Sprite _rangedIconFallback;

        private WeaponSlotState _meleeState;
        private WeaponSlotState _rangedState;
        private UseTarget _useTarget;
        private int _placedTicks = -1;
        private bool _mirrored;
        private TouchPress _attackPress;
        private TouchPress _meleePress;
        private TouchPress _rangedPress;
        private TouchPress _usePress;

        private void Awake()
        {
            _attackPress = Watch(_attack);
            _meleePress = Watch(_melee);
            _rangedPress = Watch(_ranged);
            _usePress = Watch(_use);
            Refresh();
        }

        private TouchPress Watch(Component target)
        {
            var press = target != null ? target.GetComponent<TouchPress>() : null;
            if (press != null) press.Changed += Refresh;
            return press;
        }

        public void SetMirrored(bool mirrored)
        {
            _mirrored = mirrored;
            if (_content != null) _content.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
            Unmirror(_meleeIcon);
            Unmirror(_rangedIcon);
        }

        private void Unmirror(Graphic icon)
        {
            if (icon == null) return;
            var scale = icon.rectTransform.localScale;
            scale.x = Mathf.Abs(scale.x) * (_mirrored ? -1f : 1f);
            icon.rectTransform.localScale = scale;
        }

        public void SetWeapons(in WeaponSlotState melee, in WeaponSlotState ranged)
        {
            if (melee.Same(_meleeState) && ranged.Same(_rangedState)) return;
            _meleeState = melee;
            _rangedState = ranged;
            Refresh();
        }

        public void SetUse(UseTarget target)
        {
            if (_useTarget == target) return;
            _useTarget = target;
            Refresh();
        }

        private void Refresh()
        {
            if (_style == null) return;
            PaintButton(_attack, true, false, _attackPress);
            PaintSlot(_melee, _meleeIcon, _meleeState, _meleeIconFallback, _meleePress);
            PaintSlot(_ranged, _rangedIcon, _rangedState, _rangedIconFallback, _rangedPress);
            PaintAmmo();
            PaintUse();
        }

        private void PaintButton(UiShape shape, bool enabled, bool accent, TouchPress press)
        {
            if (shape == null) return;
            var line = accent ? _style.Accent : _style.Line;
            var fill = press != null && press.IsPressed ? _style.PressedFill : _style.Fill;
            if (!enabled)
            {
                line.a *= _style.DisabledAlpha;
                fill.a *= _style.DisabledAlpha;
            }

            shape.Stroke = line;
            shape.Fill = fill;
        }

        private void PaintSlot(UiShape shape, Image icon, in WeaponSlotState state, Sprite fallback, TouchPress press)
        {
            PaintButton(shape, state.Equipped, state.Active, press);
            if (icon == null) return;
            icon.enabled = state.Equipped;
            if (!state.Equipped) return;
            icon.sprite = state.Icon != null ? state.Icon : fallback;
            var color = _style.Text;
            if (state.Reloading) color.a *= 0.5f;
            icon.color = color;
        }

        private void PaintAmmo()
        {
            var shown = _rangedState.Equipped && _rangedState.Magazine > 0;
            var count = shown ? Mathf.Min(_rangedState.Magazine, MaxTicks) : 0;
            PlaceTicks(count);

            // More rounds than ticks: each tick stands for a share of the magazine.
            var lit = shown && !_rangedState.Reloading
                ? Mathf.CeilToInt(_rangedState.Ammo * count / (float)_rangedState.Magazine)
                : 0;
            var dim = _style.Line;
            dim.a *= 0.3f;
            for (var i = 0; i < _ticks.Length; i++)
            {
                if (_ticks[i] == null) continue;
                _ticks[i].Fill = i < lit ? _style.Text : dim;
            }

            if (_reload != null)
            {
                var reloading = _rangedState.Equipped && _rangedState.Reloading;
                _reload.enabled = reloading;
                if (reloading)
                {
                    // Grows from the start of the slot (its top) towards Melee.
                    var sweep = _rangedSweep * Mathf.Clamp01(_rangedState.Reload);
                    _reload.SetSector(_reload.Center, _reload.InnerRadius, _reload.OuterRadius, _rangedStart, sweep);
                    _reload.Fill = _style.Accent;
                }
            }
        }

        /// <summary>Spreads <paramref name="count"/> ticks evenly over the slot's outer arc (only when the count changes).</summary>
        private void PlaceTicks(int count)
        {
            if (count == _placedTicks || _ticks == null) return;
            _placedTicks = count;
            var from = _rangedStart + TickMargin;
            var to = _rangedStart + _rangedSweep - TickMargin;
            for (var i = 0; i < _ticks.Length; i++)
            {
                var tick = _ticks[i];
                if (tick == null) continue;
                var visible = i < count;
                tick.gameObject.SetActive(visible);
                if (!visible) continue;
                var angle = count > 1 ? Mathf.Lerp(from, to, i / (float)(count - 1)) : (from + to) * 0.5f;
                var rad = angle * Mathf.Deg2Rad;
                var rect = tick.rectTransform;
                rect.anchoredPosition = _center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * _tickRadius;
                rect.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            }
        }

        private void PaintUse()
        {
            var available = _useTarget != UseTarget.None;
            PaintButton(_use, available, available, _usePress);
            if (_use != null && !available)
            {
                // Still a button (nothing happens): faint, not disabled-looking grey.
                var line = _style.Line;
                line.a *= 0.55f;
                _use.Stroke = line;
            }

            if (_useIcon == null) return;
            if (_useTarget == UseTarget.PickUp && _pickUpIcon != null) _useIcon.sprite = _pickUpIcon;
            else if (_useTarget == UseTarget.Door && _doorIcon != null) _useIcon.sprite = _doorIcon;
            var color = available ? _style.Accent : _style.Text;
            if (!available) color.a *= 0.45f;
            _useIcon.color = color;
        }
    }
}
