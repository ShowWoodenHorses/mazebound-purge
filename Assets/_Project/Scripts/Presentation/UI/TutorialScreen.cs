using System;
using Maze.Core.Level;
using Maze.Presentation.UI.Shapes;
using Maze.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// Tutorial layer over the HUD (shown in the tutorial level only): the reminder of the current step at the top, an
    /// arrow over the step's target (at the screen edge, turned to it, when the target is off screen), a pulsing ring
    /// around the control the step talks about, and the step window (dim + panel + Continue) while the flow is in
    /// <c>GameFlowState.Tip</c>. Passive: <see cref="TutorialPresenter"/> fills it, <see cref="ScreenRouter"/> shows it.
    /// Nothing here catches touches except the window.
    /// </summary>
    public sealed class TutorialScreen : UIScreen
    {
        /// <summary>Continue is ignored this long after the window opened (a tap meant for the game).</summary>
        private const float ContinueDelay = 0.35f;

        /// <summary>Arrow: distance above the target on screen, bobbing amplitude and speed (canvas units, Hz).</summary>
        private const float PointerLift = 70f;

        private const float BobAmplitude = 14f;
        private const float BobSpeed = 2.2f;

        /// <summary>The arrow stays this far inside the screen edges.</summary>
        private const float EdgeMargin = 90f;

        [SerializeField] private UiStyle _style;
        [SerializeField] private GameObject _hintRow;
        [SerializeField] private TMP_Text _hint;
        [SerializeField] private RectTransform _pointer;
        [SerializeField] private UiShape _ring;
        [SerializeField] private GameObject _dialog;
        [SerializeField] private TMP_Text _dialogTitle;
        [SerializeField] private TMP_Text _dialogText;
        [SerializeField] private Button _continueButton;

        [Header("Highlighted controls")]
        [SerializeField] private RectTransform _stick;
        [SerializeField] private RectTransform _touchAttack;
        [SerializeField] private RectTransform _touchUse;
        [SerializeField] private RectTransform _touchMelee;
        [SerializeField] private RectTransform _touchRanged;
        [SerializeField] private RectTransform _map;
        [SerializeField] private RectTransform _meleeSlot;
        [SerializeField] private RectTransform _rangedSlot;

        private readonly Vector3[] _corners = new Vector3[4];
        private TutorialHighlight _highlight;
        private bool _pointerShown;
        private Vector2 _pointerScreen;
        private float _dialogShownAt;

        public event Action ContinueClicked;

        /// <summary>The window has been open long enough to take Continue.</summary>
        public bool CanContinue => _dialog != null && _dialog.activeSelf && Time.unscaledTime - _dialogShownAt >= ContinueDelay;

        private void Awake()
        {
            Bind(_continueButton, () =>
            {
                if (CanContinue) ContinueClicked?.Invoke();
            });
        }

        /// <summary>The reminder line; null or empty hides it.</summary>
        public void SetHint(string text)
        {
            var shown = !string.IsNullOrEmpty(text);
            if (_hintRow != null) _hintRow.SetActive(shown);
            if (shown && _hint != null) _hint.text = text;
        }

        public void SetDialog(string title, string text)
        {
            if (_dialogTitle != null) _dialogTitle.text = title ?? string.Empty;
            if (_dialogText != null) _dialogText.text = text ?? string.Empty;
        }

        public void SetDialogVisible(bool visible)
        {
            if (_dialog == null || _dialog.activeSelf == visible) return;
            _dialog.SetActive(visible);
            if (visible) _dialogShownAt = Time.unscaledTime;
        }

        /// <summary>The arrow points at <paramref name="screenPoint"/> (pixels); hidden when <paramref name="shown"/> is false.</summary>
        public void SetPointer(bool shown, Vector2 screenPoint)
        {
            _pointerShown = shown;
            _pointerScreen = screenPoint;
        }

        public void SetHighlight(TutorialHighlight highlight) => _highlight = highlight;

        public void Clear()
        {
            SetHint(null);
            SetDialogVisible(false);
            _pointerShown = false;
            _highlight = TutorialHighlight.None;
            LateUpdate();
        }

        private void LateUpdate()
        {
            var bob = Mathf.Sin(Time.unscaledTime * BobSpeed * 2f * Mathf.PI) * 0.5f + 0.5f;
            PlacePointer(bob);
            PlaceRing(bob);
        }

        private void PlacePointer(float bob)
        {
            if (_pointer == null) return;
            if (_pointer.gameObject.activeSelf != _pointerShown) _pointer.gameObject.SetActive(_pointerShown);
            if (!_pointerShown) return;

            var area = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, _pointerScreen, null, out var target);
            var rect = area.rect;
            var inner = new Rect(rect.xMin + EdgeMargin, rect.yMin + EdgeMargin, rect.width - 2f * EdgeMargin,
                rect.height - 2f * EdgeMargin);

            Vector2 direction;
            Vector2 tip;
            if (inner.Contains(target))
            {
                // Over the target, pointing down at it (the camera looks almost straight down: up is North).
                direction = Vector2.down;
                tip = target + Vector2.up * (PointerLift + bob * BobAmplitude);
            }
            else
            {
                // At the edge towards the target.
                var center = inner.center;
                direction = (target - center).normalized;
                tip = ClampToRect(center, direction, inner) - direction * (bob * BobAmplitude);
            }

            _pointer.anchoredPosition = tip + RootOffset(area);
            _pointer.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }

        private void PlaceRing(float bob)
        {
            if (_ring == null) return;
            var target = Target(_highlight);
            var shown = target != null && target.gameObject.activeInHierarchy;
            if (_ring.gameObject.activeSelf != shown) _ring.gameObject.SetActive(shown);
            if (!shown) return;

            // Overlay canvases: world corners are screen pixels, whichever canvas the control is on.
            target.GetWorldCorners(_corners);
            var area = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, _corners[0], null, out var min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, _corners[2], null, out var max);
            var size = Mathf.Max(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y)) * 1.15f + 24f + bob * 16f;
            var ring = _ring.rectTransform;
            ring.anchoredPosition = (min + max) * 0.5f + RootOffset(area);
            ring.sizeDelta = new Vector2(size, size);
            var color = _style != null ? _style.Accent : Color.yellow;
            color.a = Mathf.Lerp(1f, 0.45f, bob);
            _ring.color = color;
        }

        /// <summary>The pointer and the ring are anchored at the centre of this screen.</summary>
        private static Vector2 RootOffset(RectTransform area) => -area.rect.center;

        private RectTransform Target(TutorialHighlight highlight)
        {
            var touch = HudScreen.TouchShown;
            switch (highlight)
            {
                case TutorialHighlight.Stick: return touch ? _stick : null;
                case TutorialHighlight.Attack: return touch ? _touchAttack : null;
                case TutorialHighlight.Use: return touch ? _touchUse : null;
                case TutorialHighlight.Melee: return touch ? _touchMelee : _meleeSlot;
                case TutorialHighlight.Ranged: return touch ? _touchRanged : _rangedSlot;
                case TutorialHighlight.Map: return _map;
                default: return null;
            }
        }

        /// <summary>Where the ray from <paramref name="from"/> along <paramref name="direction"/> leaves <paramref name="rect"/>.</summary>
        private static Vector2 ClampToRect(Vector2 from, Vector2 direction, Rect rect)
        {
            var tx = direction.x > 0f ? (rect.xMax - from.x) / direction.x
                : direction.x < 0f ? (rect.xMin - from.x) / direction.x
                : float.PositiveInfinity;
            var ty = direction.y > 0f ? (rect.yMax - from.y) / direction.y
                : direction.y < 0f ? (rect.yMin - from.y) / direction.y
                : float.PositiveInfinity;
            return from + direction * Mathf.Min(tx, ty);
        }
    }
}
