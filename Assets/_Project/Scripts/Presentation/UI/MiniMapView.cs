using System.Collections.Generic;
using Purge.Presentation.Map;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The mini-map in the HUD (stage 7г, top right, in place of the Map button: a tap opens the full map). A square
    /// window of <see cref="MiniMapPresenter.WindowCells"/> cells around the player over the level's map texture
    /// (shared with <see cref="MapScreen"/>): the map image and the icons are children of one content rect that moves
    /// under a <see cref="RectMask2D"/> every frame (smooth scroll, no texture work); the player arrow stays in the
    /// centre. Icons are Images made when a level first sets them, then reused. Its own Canvas keeps its per-frame
    /// changes from rebuilding the rest of the HUD.
    /// </summary>
    public sealed class MiniMapView : MonoBehaviour
    {
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;
        [SerializeField] private RawImage _image;
        [SerializeField] private RectTransform _iconRoot;
        [SerializeField] private Image _player;

        [Tooltip("Smallest icon, canvas units.")]
        [SerializeField, Min(0f)] private float _minIconSize = 7f;

        private readonly List<Image> _iconImages = new List<Image>();
        private Vector2Int _mapSize;
        private float _cell;
        private int _windowCells = 32;

        /// <summary>Shows <paramref name="map"/> (a pixel per cell); <paramref name="windowCells"/> fit the window.</summary>
        public void SetMap(Texture map, int windowCells)
        {
            _windowCells = Mathf.Max(4, windowCells);
            _image.texture = map;
            _image.enabled = map != null;
            _mapSize = map != null ? new Vector2Int(map.width, map.height) : Vector2Int.zero;
            _cell = _viewport.rect.width / _windowCells;
            _content.sizeDelta = new Vector2(_mapSize.x * _cell, _mapSize.y * _cell);
        }

        public void SetPlayer(Sprite sprite, Color color, bool shown)
        {
            if (_player == null) return;
            _player.sprite = sprite;
            _player.color = color;
            if (_player.gameObject.activeSelf != shown) _player.gameObject.SetActive(shown);
        }

        /// <summary>Puts the window's centre at <paramref name="center"/> (map cells from the south-west corner).</summary>
        public void SetView(Vector2 center, float playerRotation)
        {
            if (_mapSize.x == 0) return;
            _content.anchoredPosition = -center * _cell;
            if (_player != null) _player.rectTransform.localEulerAngles = new Vector3(0f, 0f, playerRotation);
        }

        /// <summary>Sets the icons; Images are created on the first calls (loading) and reused.</summary>
        public void SetIcons(IReadOnlyList<MapIcon> icons, MapIconSet set)
        {
            while (_iconImages.Count < icons.Count) _iconImages.Add(CreateIcon(_iconImages.Count));
            for (var i = 0; i < _iconImages.Count; i++)
            {
                var image = _iconImages[i];
                var shown = i < icons.Count && icons[i].Visible && set != null && _mapSize.x > 0;
                if (image.gameObject.activeSelf != shown) image.gameObject.SetActive(shown);
                if (!shown) continue;

                var icon = icons[i];
                var sprite = set.SpriteOf(icon.Kind);
                if (image.sprite != sprite) image.sprite = sprite;
                image.color = icon.Color;
                var rect = image.rectTransform;
                rect.anchoredPosition = icon.Center * _cell;
                var side = Mathf.Max(icon.Size * _cell, _minIconSize);
                rect.sizeDelta = new Vector2(side, side);
                rect.localEulerAngles = new Vector3(0f, 0f, icon.Rotation);
            }
        }

        public void Clear()
        {
            _image.texture = null;
            _image.enabled = false;
            _mapSize = Vector2Int.zero;
            foreach (var image in _iconImages) image.gameObject.SetActive(false);
        }

        private Image CreateIcon(int index)
        {
            var go = new GameObject("Icon" + index, typeof(RectTransform));
            go.transform.SetParent(_iconRoot, false);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero; // positions in map cells from the south-west corner
            rect.pivot = new Vector2(0.5f, 0.5f);
            return image;
        }
    }
}
