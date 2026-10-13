using System;
using Maze.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// A level tile (on its button): the number; under it three star slots once completed (earned ones filled with the
    /// accent); the next level to play outlined in the accent; a locked level faint with a padlock — a tap on it shakes
    /// the tile (the button's denied sound comes from <see cref="UiSound"/>). A completed level shows its best time at
    /// the bottom of the button (where a locked one has the padlock).
    /// </summary>
    public sealed class LevelTile : MonoBehaviour, IPointerClickHandler
    {
        private const float ShakeTime = 0.35f;
        private const float ShakeDistance = 9f;
        private const float ShakeFrequency = 34f;

        [SerializeField] private UiStyle _style;
        [SerializeField] private UiButton _button;
        [SerializeField] private TMP_Text _number;
        [SerializeField] private GameObject _lock;
        [SerializeField] private TMP_Text _bestTime;

        [Tooltip("Faint for locked levels that development builds still let you start.")]
        [SerializeField] private CanvasGroup _group;

        [SerializeField] private GameObject _stars;
        [SerializeField] private Image[] _starImages;
        [SerializeField] private Sprite _star;
        [SerializeField] private Sprite _starFilled;

        private float _shake = -1f;
        private Vector2 _rest;

        public string LevelId { get; private set; }

        public event Action<string> Selected;

        private void Awake()
        {
            _rest = _button.GetComponent<RectTransform>().anchoredPosition;
            _button.onClick.AddListener(() => Selected?.Invoke(LevelId));
        }

        private void OnDisable() => StopShake();

        public void Set(in LevelTileData data)
        {
            LevelId = data.LevelId;
            _number.text = data.Caption ?? data.Number.ToString();
            _lock.SetActive(!data.Unlocked);
            if (_bestTime != null)
            {
                var shown = data.Unlocked && data.BestTime > 0f;
                _bestTime.gameObject.SetActive(shown);
                if (shown) _bestTime.text = TimeFormat.ToText(data.BestTime);
            }
            _button.interactable = data.Playable;
            _button.Accent = data.IsNext;
            if (_group != null) _group.alpha = !data.Unlocked && data.Playable ? 0.5f : 1f;

            _stars.SetActive(data.Completed);
            for (var i = 0; i < _starImages.Length; i++)
            {
                var earned = i < data.Stars;
                _starImages[i].sprite = earned ? _starFilled : _star;
                var color = earned ? _style.Accent : _style.Line;
                if (!earned) color.a *= 0.55f;
                _starImages[i].color = color;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_button.IsInteractable()) _shake = 0f;
        }

        private void Update()
        {
            if (_shake < 0f) return;
            _shake += Time.unscaledDeltaTime;
            if (_shake >= ShakeTime)
            {
                StopShake();
                return;
            }

            var fade = 1f - _shake / ShakeTime;
            var offset = Mathf.Sin(_shake * ShakeFrequency) * ShakeDistance * fade;
            ((RectTransform)_button.transform).anchoredPosition = _rest + new Vector2(offset, 0f);
        }

        private void StopShake()
        {
            if (_shake < 0f) return;
            _shake = -1f;
            ((RectTransform)_button.transform).anchoredPosition = _rest;
        }
    }
}
