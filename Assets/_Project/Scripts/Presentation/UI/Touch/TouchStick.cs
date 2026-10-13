using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.OnScreen;

namespace Maze.Presentation.UI.Touch
{
    /// <summary>
    /// On-screen stick (ТЗ §64) that drives a gamepad stick through the Input System. Lives on an invisible zone
    /// (its side of the screen); the ring rests near the zone's bottom outer corner. Floating: the ring jumps to where
    /// the thumb touches the zone. Fixed: only touches on the ring count. Deflection is measured in canvas units
    /// (physical, see <see cref="TouchControls"/>) and shaped by <see cref="StickResponse"/>.
    /// The ring is anchored to the zone's bottom-left corner. The player may move its rest point (inside the zone) and
    /// scale it (<see cref="SetPlacement"/>); a scaled stick has a proportionally longer travel.
    /// </summary>
    public sealed class TouchStick : OnScreenControl, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private const int NoPointer = int.MinValue;

        /// <summary>A fixed stick also takes touches slightly outside the ring.</summary>
        private const float FixedGrabScale = 1.3f;

        [InputControl(layout = "Vector2")]
        [SerializeField] private string _controlPath = "<Gamepad>/leftStick";

        [SerializeField] private RectTransform _ring;
        [SerializeField] private RectTransform _knob;

        [Tooltip("Aim-mode marks around the ring; the one the stick points at lights up.")]
        [SerializeField] private TouchStickMarks _marks;

        [Tooltip("Ring centre at rest, from the zone's bottom outer corner (canvas units).")]
        [SerializeField] private Vector2 _restFromCorner = new Vector2(220f, 220f);

        [Tooltip("Full travel of the stick (canvas units).")]
        [SerializeField] private float _radius = 100f;

        private float _deadZone = 0.2f;
        private float _exponent = 1.6f;
        private bool _floating = true;
        private bool _mirrored;
        private int _pointerId = NoPointer;
        private bool _moved;
        private Vector2 _restInSafeArea;
        private float _scale = 1f;

        protected override string controlPathInternal
        {
            get => _controlPath;
            set => _controlPath = value;
        }

        private bool IsHeld => _pointerId != NoPointer;

        private RectTransform Zone => (RectTransform)transform;

        public void Configure(float deadZone, float exponent, bool floating, bool mirrored)
        {
            _deadZone = deadZone;
            _exponent = exponent;
            _floating = floating;
            _mirrored = mirrored;
            if (!IsHeld) ResetToRest();
        }

        /// <summary>
        /// Rest point (ring centre, normalized to the parent safe area as shown, i.e. already mirrored) or the default
        /// corner, and the ring scale. Applied at once, also while the component is disabled (layout editing).
        /// </summary>
        public void SetPlacement(bool moved, Vector2 restInSafeArea, float scale)
        {
            _moved = moved;
            _restInSafeArea = restInSafeArea;
            _scale = scale;
            if (_ring != null) _ring.localScale = new Vector3(scale, scale, 1f);
            if (!IsHeld) ResetToRest();
        }

        /// <summary>The ring (drawn stick base).</summary>
        public RectTransform Ring => _ring;

        /// <summary>Radius of the drawn ring, canvas units, with the scale.</summary>
        public float RingRadius => _ring != null ? _ring.rect.width * 0.5f * _scale : 0f;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsHeld || !ToLocal(eventData, out var point)) return;

            if (_floating)
            {
                _ring.anchoredPosition = KeepInside(point);
            }
            else
            {
                var grab = RingRadius * FixedGrabScale;
                if ((point - _ring.anchoredPosition).sqrMagnitude > grab * grab) return;
            }

            _pointerId = eventData.pointerId;
            Deflect(point);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId || !ToLocal(eventData, out var point)) return;
            Deflect(point);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            Release();
            SendValueToControl(Vector2.zero);
        }

        protected override void OnDisable()
        {
            Release();
            base.OnDisable(); // Resets the control.
        }

        private void LateUpdate()
        {
            // The rest point depends on the zone size (safe area, screen size), so it is followed while idle.
            if (!IsHeld) ResetToRest();
        }

        private void Deflect(Vector2 point)
        {
            // The knob lives in the scaled ring: its local offset is divided by the scale.
            var offset = (point - _ring.anchoredPosition) / _scale;
            if (_knob != null) _knob.anchoredPosition = Vector2.ClampMagnitude(offset, _radius);
            SendValueToControl(StickResponse.Shape(offset / _radius, _deadZone, _exponent));
            if (_marks != null) _marks.Point(Vector2.ClampMagnitude(offset / _radius, 1f));
        }

        private void Release()
        {
            _pointerId = NoPointer;
            ResetToRest();
            if (_marks != null) _marks.Point(Vector2.zero);
        }

        private void ResetToRest()
        {
            if (_ring == null) return;
            Vector2 rest;
            if (_moved && Zone.parent is RectTransform area)
            {
                // Zone corner in the safe area: its anchors (offsets are zero).
                rest = Vector2.Scale(_restInSafeArea - Zone.anchorMin, area.rect.size);
            }
            else
            {
                var width = Zone.rect.width;
                rest = _mirrored ? new Vector2(width - _restFromCorner.x, _restFromCorner.y) : _restFromCorner;
            }

            _ring.anchoredPosition = KeepInside(rest);
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
        }

        /// <summary>Keeps the whole ring inside the zone.</summary>
        private Vector2 KeepInside(Vector2 point)
        {
            var size = Zone.rect.size;
            var half = RingRadius;
            return new Vector2(
                Mathf.Clamp(point.x, half, Mathf.Max(half, size.x - half)),
                Mathf.Clamp(point.y, half, Mathf.Max(half, size.y - half)));
        }

        /// <summary>Point relative to the zone's bottom-left corner.</summary>
        private bool ToLocal(PointerEventData eventData, out Vector2 point)
        {
            var zone = Zone;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(zone, eventData.position, eventData.pressEventCamera, out point))
                return false;
            point -= zone.rect.min;
            return true;
        }
    }
}
