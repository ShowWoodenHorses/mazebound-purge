using Maze.Application.Save;
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace Maze.Presentation.UI.Touch
{
    /// <summary>
    /// Root of the on-screen controls (ТЗ §64): a separate overlay canvas under the application UI, so sizes are
    /// physical rather than a share of the screen. One canvas unit = 0.1 mm × <see cref="ControlsSettings.Size"/>,
    /// limited so the layout (<see cref="_designHeight"/>) always fits the safe area of small screens.
    /// Applies <see cref="ControlsSettings"/>: stick response, size, opacity, mirrored layout and the player's
    /// <see cref="TouchLayout"/> (moved controls are anchored at their point of the safe area and kept inside it;
    /// the stick inside its zone). While <see cref="TouchLayoutEditor"/> edits the layout the controls are shown over
    /// the whole UI and do not send input.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class TouchControls : MonoBehaviour
    {
        private const float UnitsPerInch = 254f;
        private const float FallbackDpi = 160f;

        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TouchStick _stick;

        [Tooltip("Safe area the controls live in (layout positions are relative to it).")]
        [SerializeField] private RectTransform _area;

        [Tooltip("Stick zone, attack cluster and Use in TouchElement order (the zone stands for the stick, the cluster " +
                 "for Attack with its Melee and Ranged slots); mirrored horizontally for the left-handed layout.")]
        [SerializeField] private RectTransform[] _mirrored;

        [SerializeField] private TouchAttackCluster _cluster;
        [SerializeField] private TouchStickMarks _marks;

        [Tooltip("Height (canvas units) the layout needs.")]
        [SerializeField] private float _designHeight = 540f;

        [Tooltip("Canvas sorting order while the layout is edited: over the application UI.")]
        [SerializeField] private int _editSortingOrder = 10;

        [SerializeField] private TouchLayoutEditor _editor;

        private Placement[] _rightHanded;
        private float _size = 1f;
        private ControlsSettings _settings = ControlsSettings.Default;
        private bool _shownByHud;
        private bool _editing;
        private int _normalSortingOrder;
        private Vector2 _appliedAreaSize;
        private OnScreenButton[] _buttons;
        private TouchPress[] _presses;
        private TouchLayoutHandle[] _handles;

        public TouchLayoutEditor LayoutEditor => _editor;

        /// <summary>Safe area of the controls.</summary>
        public RectTransform Area => _area;

        public bool LeftHanded => _settings.LeftHanded;

        public void SetShown(bool shown)
        {
            _shownByHud = shown;
            RefreshActive();
        }

        public void Apply(ControlsSettings settings)
        {
            _settings = settings;
            _size = settings.Size;
            if (_group != null) _group.alpha = settings.Opacity;
            SetLeftHanded(settings.LeftHanded);
            if (_cluster != null) _cluster.SetMirrored(settings.LeftHanded);
            if (_marks != null) _marks.SetMode(settings.AimMode);
            UpdateScale();
            if (_stick != null)
                _stick.Configure(settings.StickDeadZone, StickResponse.ExponentFor(settings.StickSensitivity),
                    settings.FloatingStick, settings.LeftHanded);
            ApplyLayout();
        }

        /// <summary>Weapon slots of the attack cluster (icons, active slot, ammo, reload).</summary>
        public void SetWeapons(in WeaponSlotState melee, in WeaponSlotState ranged)
        {
            if (_cluster != null) _cluster.SetWeapons(melee, ranged);
        }

        /// <summary>What Use would do now (lights the button).</summary>
        public void SetUse(UseTarget target)
        {
            if (_cluster != null) _cluster.SetUse(target);
        }

        /// <summary>Layout editing: shown over the UI, input off, drag handles on.</summary>
        public void SetEditing(bool editing)
        {
            if (_editing == editing) return;
            _editing = editing;
            CacheComponents();
            if (_canvas != null)
            {
                if (editing) _normalSortingOrder = _canvas.sortingOrder;
                _canvas.sortingOrder = editing ? _editSortingOrder : _normalSortingOrder;
            }

            foreach (var button in _buttons) button.enabled = !editing;
            // Pressed looks also catch the pointer: off, so drags reach the layout handles above them.
            foreach (var press in _presses) press.enabled = !editing;
            if (_stick != null) _stick.enabled = !editing;
            foreach (var handle in _handles) handle.enabled = editing;
            // The stick zone (where the stick may go) is invisible in play, faintly drawn while editing.
            var zone = _mirrored[0].GetComponent<Image>();
            if (zone != null) zone.color = editing ? new Color(1f, 1f, 1f, 0.06f) : Color.clear;
            RefreshActive();
            ApplyLayout();
        }

        /// <summary>The drawn control: a button or the stick ring.</summary>
        public RectTransform ElementRect(TouchElement element) =>
            element == TouchElement.Stick ? _stick.Ring : _mirrored[(int)element];

        /// <summary>Centre of the control as shown, normalized to the safe area.</summary>
        public Vector2 DisplayedCenter(TouchElement element)
        {
            var size = _area.rect.size;
            if (size.x <= 0f || size.y <= 0f) return new Vector2(0.5f, 0.5f);
            var rect = ElementRect(element);
            var local = (Vector2)_area.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            return (local - _area.rect.min) / size;
        }

        /// <summary>Shown point (normalized) ↔ layout point: the left-handed layout mirrors x.</summary>
        public Vector2 ToLayout(Vector2 displayed) => _settings.LeftHanded ? new Vector2(1f - displayed.x, displayed.y) : displayed;

        /// <summary>Keeps a shown centre so the whole control (at that scale) stays in the safe area; the stick in its zone.</summary>
        public Vector2 ClampDisplayed(TouchElement element, Vector2 displayed, float scale)
        {
            var size = _area.rect.size;
            if (size.x <= 0f || size.y <= 0f) return displayed;

            Vector2 min, max, half;
            if (element == TouchElement.Stick)
            {
                var zone = _mirrored[0];
                min = zone.anchorMin;
                max = zone.anchorMax;
                var radius = _stick.Ring.rect.width * 0.5f * scale;
                half = new Vector2(radius, radius);
            }
            else
            {
                min = Vector2.zero;
                max = Vector2.one;
                half = _mirrored[(int)element].rect.size * (0.5f * scale);
            }

            return new Vector2(
                ClampAxis(displayed.x, min.x + half.x / size.x, max.x - half.x / size.x),
                ClampAxis(displayed.y, min.y + half.y / size.y, max.y - half.y / size.y));
        }

        private static float ClampAxis(float value, float min, float max) =>
            min > max ? (min + max) * 0.5f : Mathf.Clamp(value, min, max);

        private void Update()
        {
            UpdateScale();
            // Moved controls are clamped against the safe area size, which follows the resolution.
            if (_area != null && _area.rect.size != _appliedAreaSize) ApplyLayout();
        }

        private void RefreshActive()
        {
            var active = _shownByHud || _editing;
            if (gameObject.activeSelf == active) return;
            if (active) UpdateScale();
            gameObject.SetActive(active);
        }

        private void CacheComponents()
        {
            if (_buttons != null) return;
            _buttons = GetComponentsInChildren<OnScreenButton>(true);
            _presses = GetComponentsInChildren<TouchPress>(true);
            _handles = GetComponentsInChildren<TouchLayoutHandle>(true);
        }

        /// <summary>Pixels per canvas unit; cheap, so checked every frame (resolution or DPI may change).</summary>
        private void UpdateScale()
        {
            if (_canvas == null) return;
            var dpi = Screen.dpi > 0f ? Screen.dpi : FallbackDpi;
            var scale = dpi / UnitsPerInch * _size;
            var height = Maze.Application.Platform.WebPage.SafeArea(Screen.safeArea, Screen.width, Screen.height).height;
            if (height > 0f) scale = Mathf.Min(scale, height / _designHeight);
            if (!Mathf.Approximately(_canvas.scaleFactor, scale))
                _canvas.scaleFactor = scale;
        }

        /// <summary>On top of the built-in (possibly mirrored) places from <see cref="SetLeftHanded"/>.</summary>
        private void ApplyLayout()
        {
            if (_area == null || _mirrored == null || _stick == null) return;
            _appliedAreaSize = _area.rect.size;
            var layout = _settings.Layout;

            for (var element = TouchElement.Attack; (int)element < _mirrored.Length; element++)
            {
                var placement = layout[element];
                var rect = _mirrored[(int)element];
                rect.localScale = new Vector3(placement.Scale, placement.Scale, 1f);
                if (!placement.Moved) continue;
                var point = ClampDisplayed(element, ToLayout(placement.Position), placement.Scale);
                rect.anchorMin = rect.anchorMax = point;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
            }

            var stick = layout.Stick;
            var rest = stick.Moved ? ClampDisplayed(TouchElement.Stick, ToLayout(stick.Position), stick.Scale) : Vector2.zero;
            _stick.SetPlacement(stick.Moved, rest, stick.Scale);
        }

        private void SetLeftHanded(bool leftHanded)
        {
            if (_mirrored == null) return;
            if (_rightHanded == null)
            {
                _rightHanded = new Placement[_mirrored.Length];
                for (var i = 0; i < _mirrored.Length; i++)
                    _rightHanded[i] = new Placement(_mirrored[i]);
            }

            for (var i = 0; i < _mirrored.Length; i++)
                _rightHanded[i].ApplyTo(_mirrored[i], leftHanded);
        }

        private readonly struct Placement
        {
            private readonly Vector2 _anchorMin;
            private readonly Vector2 _anchorMax;
            private readonly Vector2 _pivot;
            private readonly Vector2 _position;
            private readonly Vector2 _size;

            public Placement(RectTransform rect)
            {
                _anchorMin = rect.anchorMin;
                _anchorMax = rect.anchorMax;
                _pivot = rect.pivot;
                _position = rect.anchoredPosition;
                _size = rect.sizeDelta;
            }

            public void ApplyTo(RectTransform rect, bool mirrored)
            {
                if (mirrored)
                {
                    rect.anchorMin = new Vector2(1f - _anchorMax.x, _anchorMin.y);
                    rect.anchorMax = new Vector2(1f - _anchorMin.x, _anchorMax.y);
                    rect.pivot = new Vector2(1f - _pivot.x, _pivot.y);
                    rect.anchoredPosition = new Vector2(-_position.x, _position.y);
                }
                else
                {
                    rect.anchorMin = _anchorMin;
                    rect.anchorMax = _anchorMax;
                    rect.pivot = _pivot;
                    rect.anchoredPosition = _position;
                }

                rect.sizeDelta = _size;
            }
        }
    }
}
