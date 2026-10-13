using System;
using Maze.Application.Save;
using Maze.Application.Services;
using Maze.Presentation.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Maze.Presentation.UI.Touch
{
    /// <summary>
    /// Layout editing of the on-screen controls (opened from the settings): the player drags the stick and buttons
    /// (<see cref="TouchLayoutHandle"/>) and sets the size of the selected one with a slider. Every change is raised
    /// as <see cref="Changed"/> (the presenter previews it through the settings, which re-applies
    /// <see cref="TouchControls"/>); <see cref="Done"/> ends editing. A panel with its own background on the
    /// <see cref="TouchControls"/> canvas.
    /// </summary>
    public sealed class TouchLayoutEditor : MonoBehaviour
    {
        private const float SelectionPadding = 12f;

        [SerializeField] private TouchControls _controls;

        [Tooltip("Accent frame drawn around the selected control (follows it).")]
        [SerializeField] private RectTransform _selection;

        [Tooltip("Full-screen dim behind the controls while editing; a tap on it clears the selection.")]
        [SerializeField] private GameObject _background;

        [SerializeField] private Slider _size;
        [SerializeField] private TMP_Text _sizeLabel;
        [SerializeField] private Button _resetButton;
        [SerializeField] private Button _doneButton;

        private TouchLayout _layout;
        private TouchElement _selected;
        private Vector2 _grabOffset;
        private LocalizationService _texts;
        private readonly Vector3[] _corners = new Vector3[4];

        /// <summary>The edited layout (right-handed, normalized); raised on every move, resize and reset.</summary>
        public event Action<TouchLayout> Changed;

        public event Action Done;

        public bool IsEditing { get; private set; }

        private void Awake()
        {
            _size.minValue = TouchPlacement.MinScale;
            _size.maxValue = TouchPlacement.MaxScale;
            _size.wholeNumbers = false;
            _size.onValueChanged.AddListener(OnSizeChanged);
            _resetButton.onClick.AddListener(OnReset);
            _doneButton.onClick.AddListener(() => Done?.Invoke());
        }

        public void Begin(TouchLayout layout)
        {
            _layout = layout;
            IsEditing = true;
            gameObject.SetActive(true);
            if (_background != null) _background.SetActive(true);
            _controls.SetEditing(true);
            Select(TouchElement.Attack);
        }

        public void End()
        {
            if (!IsEditing) return;
            IsEditing = false;
            _controls.SetEditing(false);
            if (_background != null) _background.SetActive(false);
            gameObject.SetActive(false);
        }

        internal void OnHandleDown(TouchElement element, PointerEventData eventData)
        {
            Select(element);
            _grabOffset = ToArea(eventData, out var point) ? _controls.DisplayedCenter(element) - point : Vector2.zero;
        }

        internal void OnHandleDrag(TouchElement element, PointerEventData eventData)
        {
            if (element != _selected || !ToArea(eventData, out var point)) return;
            var placement = _layout[element];
            var shown = _controls.ClampDisplayed(element, point + _grabOffset, placement.Scale);
            placement.Moved = true;
            placement.Position = _controls.ToLayout(shown);
            _layout[element] = placement;
            Changed?.Invoke(_layout);
        }

        private void OnSizeChanged(float scale)
        {
            var placement = _layout[_selected];
            placement.Scale = scale;
            if (placement.Moved)
            {
                // Growing near an edge: keep the whole control on screen.
                var shown = _controls.ClampDisplayed(_selected, _controls.ToLayout(placement.Position), scale);
                placement.Position = _controls.ToLayout(shown);
            }

            _layout[_selected] = placement;
            UpdateSizeLabel();
            Changed?.Invoke(_layout);
        }

        private void OnReset()
        {
            _layout = TouchLayout.Default;
            Changed?.Invoke(_layout);
            Select(_selected);
        }

        private void Select(TouchElement element)
        {
            _selected = element;
            _size.SetValueWithoutNotify(_layout[element].Scale);
            UpdateSizeLabel();
        }

        private void LateUpdate()
        {
            // The frame follows the selected control (it moves and scales while dragged or resized).
            if (!IsEditing || _selection == null || !(_selection.parent is RectTransform parent)) return;
            _controls.ElementRect(_selected).GetWorldCorners(_corners);
            var min = (Vector2)parent.InverseTransformPoint(_corners[0]);
            var max = (Vector2)parent.InverseTransformPoint(_corners[2]);
            _selection.anchorMin = _selection.anchorMax = new Vector2(0.5f, 0.5f);
            _selection.anchoredPosition = (min + max) * 0.5f - parent.rect.center;
            _selection.sizeDelta = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y)) +
                                   Vector2.one * (SelectionPadding * 2f);
        }

        /// <summary>The language changed (also once at start-up): the size caption is composed here.</summary>
        public void ApplyLanguage(LocalizationService texts)
        {
            _texts = texts;
            if (IsEditing) UpdateSizeLabel();
        }

        private void UpdateSizeLabel()
        {
            var percent = Mathf.RoundToInt(_layout[_selected].Scale * 100f);
            if (_texts == null)
            {
                _sizeLabel.text = _selected + ": " + percent + "%";
                return;
            }

            var element = _texts.Get(TextKeys.LayoutElementPrefix + _selected.ToString().ToLowerInvariant());
            _sizeLabel.text = _texts.Format(TextKeys.LayoutSize, element, _texts.Format(TextKeys.Percent, percent));
        }

        /// <summary>Pointer position normalized to the controls' safe area.</summary>
        private bool ToArea(PointerEventData eventData, out Vector2 point)
        {
            var area = _controls.Area;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, eventData.position, eventData.pressEventCamera, out var local))
            {
                point = default;
                return false;
            }

            var size = area.rect.size;
            point = size.x > 0f && size.y > 0f ? (local - area.rect.min) / size : Vector2.zero;
            return true;
        }
    }
}
