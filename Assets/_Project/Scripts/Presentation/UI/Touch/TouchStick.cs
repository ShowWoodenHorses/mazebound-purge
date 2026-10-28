using Purge.Gameplay.Combat;
using Purge.Presentation.UI.Shapes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.OnScreen;

namespace Purge.Presentation.UI.Touch
{
    /// <summary>
    /// On-screen stick (ТЗ §64) that drives a gamepad stick through the Input System. Lives on an invisible zone
    /// (its side of the screen); the ring rests near the zone's bottom outer corner. Floating: the ring jumps to where
    /// the thumb touches the zone. Fixed: only touches on the ring count. Deflection is measured in canvas units
    /// (physical, see <see cref="TouchControls"/>) and shaped by <see cref="StickResponse"/>.
    /// The ring is anchored to the zone's bottom-left corner. The player may move its rest point (inside the zone) and
    /// scale it (<see cref="SetPlacement"/>); a scaled stick has a proportionally longer travel.
    /// The aim stick of the two-stick scheme lives in the attack cluster: its ring may leave the zone (a touch is never
    /// shifted, so it starts undeflected), and in the mirrored (left-handed) cluster its value is mirrored back.
    /// Steering (<see cref="SetSteering"/>, in a vehicle): the ring shows a wheel at its rest point instead of the
    /// knob; a thumb anywhere in the zone turns it around its centre, the turn up to <see cref="WheelLock"/> is the
    /// stick's x. Released — the car steers straight at once, the wheel springs back.
    /// </summary>
    public sealed class TouchStick : OnScreenControl, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private const int NoPointer = int.MinValue;

        /// <summary>A fixed stick also takes touches slightly outside the ring.</summary>
        private const float FixedGrabScale = 1.3f;

        /// <summary>Wheel turn, degrees, of a full steer.</summary>
        private const float WheelLock = 180f;

        /// <summary>Closer to the wheel's centre (share of the ring radius) the thumb's angle is not followed.</summary>
        private const float WheelHub = 0.3f;

        /// <summary>Released wheel springs back, degrees per second.</summary>
        private const float WheelReturn = 720f;

        [InputControl(layout = "Vector2")]
        [SerializeField] private string _controlPath = "<Gamepad>/leftStick";

        [SerializeField] private RectTransform _ring;
        [SerializeField] private RectTransform _knob;

        [Tooltip("The wheel drawn on the ring while steering (off otherwise).")]
        [SerializeField] private RectTransform _wheel;

        [Tooltip("What the stick draws on its ring and the wheel hides (knob, hairline, marks).")]
        [SerializeField] private GameObject _stickParts;

        [Tooltip("Aim-mode marks around the ring; the one the stick points at lights up.")]
        [SerializeField] private TouchStickMarks _marks;

        [Tooltip("Optional ring of the fire threshold (the aim stick): sized to PlayerCombat.StickFireFrom, lit past it.")]
        [SerializeField] private UiShape _fireRing;

        [SerializeField] private Color _fireRingLit = Color.white;

        [Tooltip("Ring centre at rest, from the zone's bottom outer corner (canvas units).")]
        [SerializeField] private Vector2 _restFromCorner = new Vector2(220f, 220f);

        [Tooltip("Full travel of the stick (canvas units).")]
        [SerializeField] private float _radius = 100f;

        [Tooltip("Keep the whole ring inside the zone (the movement stick); off — the ring is centred on the touch.")]
        [SerializeField] private bool _keepInZone = true;

        private float _deadZone = 0.2f;
        private float _exponent = 1.6f;
        private bool _floating = true;
        private bool _mirrored;
        private Color? _fireRingIdle;
        private bool _fireRingShown;
        private int _pointerId = NoPointer;
        private bool _moved;
        private Vector2 _restInSafeArea;
        private float _scale = 1f;
        private bool _steering;
        private float _wheelAngle;
        private float _thumbAngle = float.NaN;

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
            if (_fireRing != null)
            {
                if (_fireRingIdle == null) _fireRingIdle = _fireRing.Stroke;
                var diameter = 2f * _radius * StickResponse.DeflectionFor(PlayerCombat.StickFireFrom, deadZone, exponent);
                _fireRing.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            }

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

        /// <summary>A steering wheel instead of the stick (the player drives a vehicle).</summary>
        public void SetSteering(bool steering)
        {
            if (_steering == steering) return;
            if (IsHeld)
            {
                Release();
                SendValueToControl(Vector2.zero);
            }

            _steering = steering;
            _wheelAngle = 0f;
            if (_wheel != null) _wheel.gameObject.SetActive(steering);
            if (_stickParts != null) _stickParts.SetActive(!steering);
            TurnWheel();
        }

        /// <summary>The ring (drawn stick base).</summary>
        public RectTransform Ring => _ring;

        /// <summary>Radius of the drawn ring, canvas units, with the scale.</summary>
        public float RingRadius => _ring != null ? _ring.rect.width * 0.5f * _scale : 0f;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsHeld || !ToLocal(eventData, out var point)) return;

            if (_steering)
            {
                _pointerId = eventData.pointerId;
                _thumbAngle = ThumbAngle(eventData);
                return;
            }

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
            if (eventData.pointerId != _pointerId) return;
            if (_steering)
            {
                Steer(eventData);
                return;
            }

            if (!ToLocal(eventData, out var point)) return;
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
            if (IsHeld) return;
            ResetToRest();
            if (_steering && _wheelAngle != 0f)
            {
                _wheelAngle = Mathf.MoveTowards(_wheelAngle, 0f, WheelReturn * Time.unscaledDeltaTime);
                TurnWheel();
            }
        }

        private void Steer(PointerEventData eventData)
        {
            var angle = ThumbAngle(eventData);
            if (float.IsNaN(angle)) return;
            // Screen angles grow counter-clockwise; the wheel's turn is clockwise (to the right).
            if (!float.IsNaN(_thumbAngle))
                _wheelAngle = Mathf.Clamp(_wheelAngle - Mathf.DeltaAngle(_thumbAngle, angle), -WheelLock, WheelLock);
            _thumbAngle = angle;
            TurnWheel();
            SendValueToControl(new Vector2(_wheelAngle / WheelLock, 0f));
        }

        /// <summary>The thumb's angle around the wheel, screen degrees; NaN on the hub.</summary>
        private float ThumbAngle(PointerEventData eventData)
        {
            var center = RectTransformUtility.WorldToScreenPoint(eventData.pressEventCamera, _ring.position);
            var offset = eventData.position - center;
            // Screen pixels: the ring's world scale (an overlay canvas) already holds the canvas and ring scales.
            var hub = _ring.rect.width * 0.5f * Mathf.Abs(_ring.lossyScale.y) * WheelHub;
            if (offset.sqrMagnitude < hub * hub) return float.NaN;
            return Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
        }

        private void TurnWheel()
        {
            if (_wheel == null) return;
            // A mirrored parent flips the rotation as shown.
            var shown = transform.lossyScale.x < 0f ? _wheelAngle : -_wheelAngle;
            _wheel.localRotation = Quaternion.Euler(0f, 0f, shown);
        }

        private void Deflect(Vector2 point)
        {
            // The knob lives in the scaled ring: its local offset is divided by the scale.
            var offset = (point - _ring.anchoredPosition) / _scale;
            if (_knob != null) _knob.anchoredPosition = Vector2.ClampMagnitude(offset, _radius);
            // Local space of a mirrored parent is flipped; the value is in screen directions.
            var screen = transform.lossyScale.x < 0f ? new Vector2(-offset.x, offset.y) : offset;
            var value = StickResponse.Shape(screen / _radius, _deadZone, _exponent);
            SendValueToControl(value);
            if (_marks != null) _marks.Point(Vector2.ClampMagnitude(offset / _radius, 1f));
            LightFireRing(value.sqrMagnitude >= PlayerCombat.StickFireFrom * PlayerCombat.StickFireFrom);
        }

        private void LightFireRing(bool lit)
        {
            if (_fireRing == null || _fireRingIdle == null || _fireRingShown == lit) return;
            _fireRingShown = lit;
            _fireRing.Stroke = lit ? _fireRingLit : _fireRingIdle.Value;
        }

        private void Release()
        {
            _pointerId = NoPointer;
            _thumbAngle = float.NaN;
            LightFireRing(false);
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
            if (!_keepInZone) return point;
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
