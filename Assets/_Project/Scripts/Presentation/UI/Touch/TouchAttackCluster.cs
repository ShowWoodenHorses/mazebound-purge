using System;
using Purge.Application.Save;
using Purge.Presentation.UI.Shapes;
using Purge.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI.Touch
{
    /// <summary>
    /// Look of the attack cluster and the Use button (stage 4в): a quarter-circle Attack with its right angle at the
    /// bottom-right, three ring sectors along its arc — weapon slot 2 (top, RB), weapon slot 1 (LB), the throwable (LT) —
    /// and Use apart on the left. A weapon slot shows its weapon, the magazine as ticks on its outer arc, the reload as an
    /// arc on its inner edge and the reserve as a number; the active slot is outlined in the accent, a gun with neither
    /// rounds nor reserve in red; an empty slot is faint. The throwable sector shows the kind and the count, the accent
    /// while aiming. Use lights up when it would do something. Pressed buttons get the light fill. Only the look: the
    /// buttons are <c>OnScreenButton</c>s. The left-handed layout mirrors the content (icons and numbers stay readable).
    /// "Melee" / "Ranged" in the field names are the slots 1 / 2 of Maze's layout: either slot holds any weapon.
    /// The two-stick scheme (<see cref="TouchScheme"/>, stage Т) puts the aim stick in the Attack quarter instead of the
    /// button (<see cref="SetScheme"/>); the sectors stay around it.
    /// </summary>
    public sealed class TouchAttackCluster : MonoBehaviour
    {
        private const int MaxTicks = 12;
        private const float TickMargin = 2.5f;

        [Serializable]
        private sealed class SlotView
        {
            public UiShape Shape;
            public Image Icon;
            public Sprite Fallback;

            [Tooltip("Thin sector on the inner edge: its sweep shows the reload.")]
            public UiShape Reload;

            [Tooltip("Magazine ticks along the outer arc (placed at runtime by the magazine size).")]
            public UiShape[] Ticks;

            [Tooltip("Rounds left in reserve.")]
            public TMP_Text Reserve;

            public float Start;
            public float Sweep;

            [NonSerialized] public WeaponSlotState State;
            [NonSerialized] public TouchPress Press;
            [NonSerialized] public int PlacedTicks = -1;
            [NonSerialized] public int ShownReserve = int.MinValue;
        }

        [SerializeField] private UiStyle _style;

        [Tooltip("Mirrored (scale x = -1) in the left-handed layout.")]
        [SerializeField] private RectTransform _content;

        [SerializeField] private UiShape _attack;

        [Tooltip("Shown only in the Auto scheme: the Attack button and its icon.")]
        [SerializeField] private GameObject[] _attackOnly;

        [Tooltip("The aim stick's quarter (TouchStick on it): shown only in the two-stick scheme.")]
        [SerializeField] private UiShape _aimStick;

        [Tooltip("Weapon slot 1 (LB).")]
        [SerializeField] private SlotView _melee = new SlotView();

        [Tooltip("Weapon slot 2 (RB).")]
        [SerializeField] private SlotView _ranged = new SlotView();

        [Header("Geometry (content space)")]
        [SerializeField] private Vector2 _center;
        [SerializeField] private float _tickRadius = 284f;

        [Header("Use")]
        [SerializeField] private UiShape _use;
        [SerializeField] private Image _useIcon;
        [SerializeField] private Sprite _doorIcon;
        [SerializeField] private Sprite _pickUpIcon;
        [SerializeField] private Sprite _vehicleIcon;

        [Tooltip("Shown only on foot: the attack quarter, the slots, the throwable (and their icons).")]
        [SerializeField] private GameObject[] _combatOnly;

        [Tooltip("Shown only while driving (stage 9a): the Gas and Brake buttons.")]
        [SerializeField] private GameObject[] _driveOnly;

        [SerializeField] private UiShape _gas;
        [SerializeField] private UiShape _brake;

        [Header("Throwable sector (LT)")]
        [SerializeField] private UiShape _throw;
        [SerializeField] private Image _throwIcon;
        [SerializeField] private TMP_Text _throwCount;
        [SerializeField] private Sprite _throwIconFallback;

        private UseTarget _useTarget;
        private TouchScheme _scheme;
        private bool _driving;
        private WeaponSlotState _throwState;
        private TouchPress _throwPress;
        private bool _mirrored;
        private TouchPress _attackPress;
        private TouchPress _aimPress;
        private TouchPress _usePress;
        private TouchPress _gasPress;
        private TouchPress _brakePress;
        private int _shownThrowCount = int.MinValue;

        private void Awake()
        {
            _attackPress = Watch(_attack);
            _aimPress = Watch(_aimStick);
            _melee.Press = Watch(_melee.Shape);
            _ranged.Press = Watch(_ranged.Shape);
            _usePress = Watch(_use);
            _throwPress = Watch(_throw);
            _gasPress = Watch(_gas);
            _brakePress = Watch(_brake);
            Refresh();
        }

        private TouchPress Watch(Component target)
        {
            var press = target != null ? target.GetComponent<TouchPress>() : null;
            if (press != null) press.Changed += Refresh;
            return press;
        }

        /// <summary>The aim stick (two sticks) or the Attack button (auto) in the Attack quarter.</summary>
        public void SetScheme(TouchScheme scheme)
        {
            _scheme = scheme;
            ApplyParts();
        }

        /// <summary>Driving a vehicle (stage 9a): Gas and Brake in place of the attack, the slots and the throwable.</summary>
        public void SetDriving(bool driving)
        {
            if (_driving == driving) return;
            _driving = driving;
            ApplyParts();
        }

        private void ApplyParts()
        {
            var sticks = _scheme == TouchScheme.TwoSticks;
            if (_aimStick != null) _aimStick.gameObject.SetActive(sticks && !_driving);
            SetActive(_attackOnly, !sticks && !_driving);
            SetActive(_combatOnly, !_driving);
            SetActive(_driveOnly, _driving);
        }

        private static void SetActive(GameObject[] parts, bool active)
        {
            if (parts == null) return;
            foreach (var part in parts)
                if (part != null) part.SetActive(active);
        }

        /// <summary>The aim stick of the two-stick scheme (null in old scenes).</summary>
        public TouchStick AimStick => _aimStick != null ? _aimStick.GetComponent<TouchStick>() : null;

        public void SetMirrored(bool mirrored)
        {
            _mirrored = mirrored;
            if (_content != null) _content.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
            Unmirror(_melee.Icon);
            Unmirror(_ranged.Icon);
            Unmirror(_melee.Reserve);
            Unmirror(_ranged.Reserve);
            Unmirror(_throwIcon);
            Unmirror(_throwCount);
        }

        private void Unmirror(Graphic graphic)
        {
            if (graphic == null) return;
            var scale = graphic.rectTransform.localScale;
            scale.x = Mathf.Abs(scale.x) * (_mirrored ? -1f : 1f);
            graphic.rectTransform.localScale = scale;
        }

        /// <param name="first">Weapon slot 1.</param>
        /// <param name="second">Weapon slot 2.</param>
        public void SetWeapons(in WeaponSlotState first, in WeaponSlotState second)
        {
            if (first.Same(_melee.State) && second.Same(_ranged.State)) return;
            _melee.State = first;
            _ranged.State = second;
            Refresh();
        }

        /// <summary>The throwable: Icon, Ammo = count, Active = aiming.</summary>
        public void SetThrowable(in WeaponSlotState state)
        {
            if (state.Same(_throwState)) return;
            _throwState = state;
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
            PaintButton(_aimStick, true, false, _aimPress);
            PaintButton(_gas, true, false, _gasPress);
            PaintButton(_brake, true, false, _brakePress);
            PaintSlot(_melee);
            PaintSlot(_ranged);
            PaintUse();
            PaintThrow();
        }

        private void PaintThrow()
        {
            if (_throw == null) return;
            PaintButton(_throw, _throwState.Equipped, _throwState.Active, _throwPress);
            if (_throwIcon != null)
            {
                _throwIcon.sprite = _throwState.Icon != null ? _throwState.Icon : _throwIconFallback;
                var color = _throwState.Active ? _style.Accent : _style.Text;
                if (!_throwState.Equipped) color.a *= _style.DisabledAlpha;
                _throwIcon.color = color;
            }

            if (_throwCount != null)
            {
                _throwCount.enabled = _throwState.Equipped;
                if (_throwState.Equipped && _shownThrowCount != _throwState.Ammo)
                {
                    _shownThrowCount = _throwState.Ammo;
                    _throwCount.SetText("{0}", _throwState.Ammo);
                }
            }
        }

        private void PaintButton(UiShape shape, bool enabled, bool accent, TouchPress press, bool danger = false)
        {
            if (shape == null) return;
            var line = danger ? _style.Danger : accent ? _style.Accent : _style.Line;
            var fill = press != null && press.IsPressed ? _style.PressedFill : _style.Fill;
            if (!enabled)
            {
                line.a *= _style.DisabledAlpha;
                fill.a *= _style.DisabledAlpha;
            }

            shape.Stroke = line;
            shape.Fill = fill;
        }

        private void PaintSlot(SlotView slot)
        {
            var state = slot.State;
            PaintButton(slot.Shape, state.Equipped, state.Active, slot.Press, state.Equipped && state.OutOfAmmo);
            if (slot.Icon != null)
            {
                slot.Icon.enabled = state.Equipped;
                if (state.Equipped)
                {
                    slot.Icon.sprite = state.Icon != null ? state.Icon : slot.Fallback;
                    var color = _style.Text;
                    if (state.Reloading || state.OutOfAmmo) color.a *= 0.5f;
                    slot.Icon.color = color;
                }
            }

            PaintAmmo(slot);
            PaintReserve(slot);
        }

        private void PaintAmmo(SlotView slot)
        {
            var state = slot.State;
            var shown = state.Equipped && state.Magazine > 0;
            var count = shown ? Mathf.Min(state.Magazine, MaxTicks) : 0;
            PlaceTicks(slot, count);

            // More rounds than ticks: each tick stands for a share of the magazine.
            var lit = shown && !state.Reloading ? Mathf.CeilToInt(state.Ammo * count / (float)state.Magazine) : 0;
            var dim = state.OutOfAmmo ? _style.Danger : _style.Line;
            dim.a *= 0.3f;
            if (slot.Ticks != null)
                for (var i = 0; i < slot.Ticks.Length; i++)
                    if (slot.Ticks[i] != null)
                        slot.Ticks[i].Fill = i < lit ? _style.Text : dim;

            if (slot.Reload == null) return;
            var reloading = state.Equipped && state.Reloading;
            slot.Reload.enabled = reloading;
            if (!reloading) return;
            // Grows from the start of the slot (its upper end).
            var sweep = slot.Sweep * Mathf.Clamp01(state.Reload);
            slot.Reload.SetSector(slot.Reload.Center, slot.Reload.InnerRadius, slot.Reload.OuterRadius, slot.Start, sweep);
            slot.Reload.Fill = _style.Accent;
        }

        /// <summary>The reserve under the icon; hidden for melee and an endless reserve (the base pistol).</summary>
        private void PaintReserve(SlotView slot)
        {
            if (slot.Reserve == null) return;
            var state = slot.State;
            var shown = state.Equipped && state.Magazine > 0 && state.Reserve >= 0;
            slot.Reserve.enabled = shown;
            if (!shown) return;
            if (slot.ShownReserve != state.Reserve)
            {
                slot.ShownReserve = state.Reserve;
                slot.Reserve.SetText("{0}", state.Reserve);
            }

            slot.Reserve.color = state.Reserve == 0 ? _style.Danger : _style.MutedText;
        }

        /// <summary>Spreads <paramref name="count"/> ticks evenly over the slot's outer arc (only when the count changes).</summary>
        private void PlaceTicks(SlotView slot, int count)
        {
            if (count == slot.PlacedTicks || slot.Ticks == null) return;
            slot.PlacedTicks = count;
            var from = slot.Start + TickMargin;
            var to = slot.Start + slot.Sweep - TickMargin;
            for (var i = 0; i < slot.Ticks.Length; i++)
            {
                var tick = slot.Ticks[i];
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
            else if (_useTarget == UseTarget.Vehicle && _vehicleIcon != null) _useIcon.sprite = _vehicleIcon;
            var color = available ? _style.Accent : _style.Text;
            if (!available) color.a *= 0.45f;
            _useIcon.color = color;
        }
    }
}
