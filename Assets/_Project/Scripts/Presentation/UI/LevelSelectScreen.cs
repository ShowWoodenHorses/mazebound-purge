using System;
using System.Collections.Generic;
using Maze.Presentation.UI.Shapes;
using Maze.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// Level select (opened by Play in the main menu): pages of ten levels in two rows of five
    /// (<see cref="LevelPages"/>), the arrows and a horizontal swipe switch pages, diamonds show the page; total stars
    /// at the top right. Opens on the page of the next level to play. Development levels (editor and development
    /// builds) follow on their own pages, captioned "DEV". A passive view: the router fills it and starts the chosen
    /// level.
    /// </summary>
    public sealed class LevelSelectScreen : UIScreen, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>Horizontal drag (canvas units) that turns the page.</summary>
        private const float SwipeDistance = 120f;
        private const float PageFadeTime = 0.18f;

        [SerializeField] private UiStyle _style;
        [SerializeField] private Button _backButton;
        [SerializeField] private TMP_Text _totalStars;
        [SerializeField] private LevelTile[] _tiles;

        [Tooltip("Tiles fade in when the page changes.")]
        [SerializeField] private CanvasGroup _grid;

        [SerializeField] private Button _previousButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private TMP_Text _range;
        [SerializeField] private RectTransform _dots;
        [SerializeField] private UiShape _dotTemplate;
        [SerializeField] private TMP_Text _emptyLabel;

        private readonly List<LevelTileData> _levels = new List<LevelTileData>();
        private readonly List<LevelTileData> _combined = new List<LevelTileData>();
        private int _gameLevels;
        private readonly List<UiShape> _dotShapes = new List<UiShape>();
        private int _page;
        private float _fade = -1f;
        private Vector2 _dragStart;

        public event Action<string> LevelSelected;
        public event Action BackClicked;

        private void Awake()
        {
            Bind(_backButton, () => BackClicked?.Invoke());
            Bind(_previousButton, () => ShowPage(_page - 1));
            Bind(_nextButton, () => ShowPage(_page + 1));
            foreach (var tile in _tiles)
                tile.Selected += id => LevelSelected?.Invoke(id);
            if (_dotTemplate != null) _dotTemplate.gameObject.SetActive(false);
        }

        /// <summary>
        /// All catalog levels in order (development levels after them, may be null), and the total stars line; shows
        /// the start page.
        /// </summary>
        public void SetLevels(IReadOnlyList<LevelTileData> levels, int stars, int maxStars,
            IReadOnlyList<LevelTileData> devLevels = null)
        {
            LevelPages.Combine(levels, devLevels, _combined);
            _gameLevels = levels.Count;
            _levels.Clear();
            var next = LevelPages.NextLevel(_combined);
            for (var i = 0; i < _combined.Count; i++)
                _levels.Add(i == next ? _combined[i].AsNext() : _combined[i]);

            if (_totalStars != null) _totalStars.text = stars + " / " + maxStars;
            if (_emptyLabel != null) _emptyLabel.gameObject.SetActive(levels.Count == 0);
            BuildDots();
            ShowPage(LevelPages.StartPage(_levels), instant: true);
        }

        private void ShowPage(int page, bool instant = false)
        {
            var count = LevelPages.PageCount(_levels.Count);
            page = Mathf.Clamp(page, 0, count - 1);
            if (page == _page && !instant) return;
            _page = page;

            var first = page * LevelPages.PerPage;
            for (var i = 0; i < _tiles.Length; i++)
            {
                var index = first + i;
                var shown = index < _levels.Count && _levels[index].LevelId != null;
                _tiles[i].transform.parent.gameObject.SetActive(shown);
                if (shown) _tiles[i].Set(_levels[index]);
            }

            if (_previousButton != null) _previousButton.interactable = page > 0;
            if (_nextButton != null) _nextButton.interactable = page < count - 1;
            if (_range != null)
                _range.text = _levels.Count == 0
                    ? string.Empty
                    : first >= _gameLevels
                        ? "DEV"
                        : (first + 1) + " - " + Mathf.Min(first + LevelPages.PerPage, _gameLevels);
            PaintDots();

            if (_grid != null)
            {
                _fade = instant ? -1f : 0f;
                _grid.alpha = instant ? 1f : 0f;
            }
        }

        private void Update()
        {
            if (_fade < 0f || _grid == null) return;
            _fade += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(_fade / PageFadeTime);
            _grid.alpha = t * (2f - t);
            if (t >= 1f) _fade = -1f;
        }

        private void BuildDots()
        {
            if (_dotTemplate == null) return;
            var count = LevelPages.PageCount(_levels.Count);
            while (_dotShapes.Count < count)
            {
                var dot = Instantiate(_dotTemplate, _dots);
                dot.gameObject.name = "Dot" + _dotShapes.Count;
                _dotShapes.Add(dot);
            }

            for (var i = 0; i < _dotShapes.Count; i++)
                _dotShapes[i].gameObject.SetActive(i < count && count > 1);
        }

        private void PaintDots()
        {
            for (var i = 0; i < _dotShapes.Count; i++)
            {
                var current = i == _page;
                _dotShapes[i].Stroke = current ? _style.Accent : _style.Line;
                _dotShapes[i].Fill = current ? _style.Accent : Color.clear;
            }
        }

        // ---- swipe (drags that start on tiles come here too) ----

        public void OnBeginDrag(PointerEventData eventData) => _dragStart = eventData.position;

        public void OnDrag(PointerEventData eventData)
        {
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            var canvas = GetComponentInParent<Canvas>();
            var scale = canvas != null ? canvas.scaleFactor : 1f;
            var delta = (eventData.position - _dragStart) / Mathf.Max(scale, 0.01f);
            if (Mathf.Abs(delta.x) < SwipeDistance || Mathf.Abs(delta.x) < Mathf.Abs(delta.y)) return;
            ShowPage(delta.x < 0f ? _page + 1 : _page - 1);
        }
    }
}
