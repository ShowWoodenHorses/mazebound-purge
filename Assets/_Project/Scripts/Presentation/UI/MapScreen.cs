using System;
using System.Collections.Generic;
using Maze.Presentation.Map;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// Fullscreen map (ТЗ §60). The level's <c>MapPresenter</c> supplies the texture (one pixel per cell) and the icons;
    /// gameplay is paused while open. Icons are Images over the map image, placed in cell units and sized from the
    /// shown map (never smaller than <see cref="MapIconSet.MinScreenSize"/>); they are created when a level sets them
    /// (loading), reused by later levels. A switch per <see cref="MapLayer"/> (player, map pieces, zombies, weapons,
    /// medkits, keys) shows or hides those icons. The magnifier button (left of Close) asks for a route hint; the route
    /// is drawn over the map (<see cref="MapRouteGraphic"/>, under the icons) with a line of status under the caption.
    /// </summary>
    public sealed class MapScreen : UIScreen
    {
        [SerializeField] private RawImage _image;
        [SerializeField] private AspectRatioFitter _fitter;
        [SerializeField] private TMP_Text _caption;
        [SerializeField] private Button _closeButton;
        [SerializeField] private MapIconSet _icons;
        [Tooltip("A switch per MapLayer, in its order.")]
        [SerializeField] private Toggle[] _layers;

        [Tooltip("Per MapLayer: a button over the switch while the layer is not bought (null = always free).")]
        [SerializeField] private Button[] _unlockButtons;

        [Header("Route hint")]
        [SerializeField] private Button _hintButton;
        [SerializeField] private TMP_Text _hintStatus;
        [SerializeField] private MapRouteGraphic _route;

        private readonly List<Image> _iconImages = new List<Image>();
        private readonly List<MapIcon> _iconData = new List<MapIcon>();
        private int _iconCount;
        private Vector2 _laidOutSize;
        private Vector2Int _mapSize;
        private HorizontalShift _topRightShift;

        public event Action CloseClicked;
        /// <summary>The magnifier button: the player asks for a route hint.</summary>
        public event Action HintClicked;

        /// <summary>The player tapped a locked layer: offer the rewarded ad that unlocks it.</summary>
        public event Action<MapLayer> LayerUnlockRequested;

        /// <summary>The player switched a layer on or off.</summary>
        public event Action<MapLayer, bool> LayerChanged;

        /// <summary>Raised when the screen is shown (icons are refreshed then).</summary>
        public event Action Opened;

        public MapIconSet Icons => _icons;

        private void Awake()
        {
            Bind(_closeButton, () => CloseClicked?.Invoke());
            Bind(_hintButton, () => HintClicked?.Invoke());
            if (_unlockButtons != null)
                for (var i = 0; i < _unlockButtons.Length; i++)
                {
                    var layer = (MapLayer)i;
                    if (_unlockButtons[i] != null) Bind(_unlockButtons[i], () => LayerUnlockRequested?.Invoke(layer));
                }

            if (_layers == null) return;
            for (var i = 0; i < _layers.Length; i++)
            {
                var layer = (MapLayer)i;
                if (_layers[i] != null) _layers[i].onValueChanged.AddListener(value => LayerChanged?.Invoke(layer, value));
            }
        }

        private void OnEnable() => Opened?.Invoke();

        private void LateUpdate()
        {
            // The map image follows the screen size (aspect fitter); icons are re-laid out when it changes.
            if (_image != null && _image.rectTransform.rect.size != _laidOutSize) LayoutIcons();
        }

        /// <summary>Shows <paramref name="map"/> (one pixel per cell) keeping the level's aspect ratio.</summary>
        public void SetMap(Texture map, string caption)
        {
            _image.texture = map;
            _image.enabled = map != null;
            _mapSize = map != null ? new Vector2Int(map.width, map.height) : Vector2Int.zero;
            if (map != null && _fitter != null)
                _fitter.aspectRatio = (float)map.width / map.height;
            _caption.text = caption;
        }

        /// <summary>
        /// Draws the route (map units: cells, a cell centre at +0.5) with the look of <see cref="MapIconSet"/>; null or
        /// fewer than two points hide it.
        /// </summary>
        public void SetRoute(IReadOnlyList<Vector2> points)
        {
            if (_route == null) return;
            if (points == null || points.Count < 2 || _icons == null)
            {
                _route.ClearRoute();
                return;
            }

            _route.SetRoute(points, _mapSize, _icons.RouteWidth, _icons.RouteOutline, _icons.RouteMinWidth,
                _icons.RouteColor, _icons.RouteOutlineColor);
        }

        /// <summary>Moves Close and the hint left of their places (a site's bar over the right edge; 0 — in place).</summary>
        public void ShiftTopRightButtons(float left)
        {
            _topRightShift ??= new HorizontalShift(_closeButton, _hintButton);
            _topRightShift.Apply(left);
        }

        /// <summary>The line under the caption: where the hint leads, or why there is none; empty hides it.</summary>
        public void SetHintStatus(string text)
        {
            if (_hintStatus == null) return;
            _hintStatus.text = text ?? string.Empty;
            _hintStatus.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>Shows the layer's switch state without raising <see cref="LayerChanged"/>.</summary>
        public void SetLayer(MapLayer layer, bool shown)
        {
            var toggle = LayerToggle(layer);
            if (toggle != null) toggle.SetIsOnWithoutNotify(shown);
        }

        /// <summary>A locked layer shows its switch off and faint with an ad mark; a tap asks to unlock it.</summary>
        public void SetLayerLocked(MapLayer layer, bool locked)
        {
            var button = _unlockButtons != null && (int)layer < _unlockButtons.Length ? _unlockButtons[(int)layer] : null;
            if (button != null) button.gameObject.SetActive(locked);
            var toggle = LayerToggle(layer);
            if (toggle == null) return;
            toggle.interactable = !locked;
            if (locked) toggle.SetIsOnWithoutNotify(false);
        }

        public Button UnlockButton(MapLayer layer) =>
            _unlockButtons != null && (int)layer < _unlockButtons.Length ? _unlockButtons[(int)layer] : null;

        public Toggle LayerToggle(MapLayer layer) =>
            _layers != null && (int)layer < _layers.Length ? _layers[(int)layer] : null;

        /// <summary>
        /// Sets the icons (count and contents). Creates missing Images: call at level loading, later calls with the same
        /// count only update them.
        /// </summary>
        public void SetIcons(IReadOnlyList<MapIcon> icons)
        {
            _iconData.Clear();
            for (var i = 0; i < icons.Count; i++) _iconData.Add(icons[i]);
            _iconCount = icons.Count;
            while (_iconImages.Count < _iconCount) _iconImages.Add(CreateIconImage(_iconImages.Count));
            LayoutIcons();
        }

        public void Clear()
        {
            SetRoute(null);
            SetHintStatus(null);
            SetMap(null, string.Empty);
            _iconData.Clear();
            _iconCount = 0;
            LayoutIcons();
        }

        private Image CreateIconImage(int index)
        {
            var go = new GameObject("Icon" + index, typeof(RectTransform));
            go.transform.SetParent(_image.transform, false);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            var rect = image.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            return image;
        }

        private void LayoutIcons()
        {
            if (_image == null) return;
            var size = _image.rectTransform.rect.size;
            _laidOutSize = size;
            var cell = _mapSize.x > 0 ? size.x / _mapSize.x : 0f;
            var minSize = _icons != null ? _icons.MinScreenSize : 0f;

            for (var i = 0; i < _iconImages.Count; i++)
            {
                var image = _iconImages[i];
                var shown = i < _iconCount && _iconData[i].Visible && _mapSize.x > 0 && _icons != null;
                if (image.gameObject.activeSelf != shown) image.gameObject.SetActive(shown);
                if (!shown) continue;

                var icon = _iconData[i];
                image.sprite = _icons.SpriteOf(icon.Kind);
                image.color = icon.Color;
                var rect = image.rectTransform;
                var anchor = new Vector2(icon.Center.x / _mapSize.x, icon.Center.y / _mapSize.y);
                rect.anchorMin = rect.anchorMax = anchor;
                rect.anchoredPosition = Vector2.zero;
                var side = Mathf.Max(icon.Size * cell, minSize);
                rect.sizeDelta = new Vector2(side, side);
                rect.localEulerAngles = new Vector3(0f, 0f, icon.Rotation);
            }
        }
    }
}
