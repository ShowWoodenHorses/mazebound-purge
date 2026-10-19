using System;
using Purge.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// A level tile (on its button): the number; the next level to play outlined in the accent; a locked level faint with
    /// a padlock — a tap on it shakes the tile (the button's denied sound comes from <see cref="UiSound"/>). The stars and
    /// best time of Maze are gone (stage 6d); the menu is redone at stage 10.
    /// </summary>
    public sealed class LevelTile : MonoBehaviour, IPointerClickHandler
    {
        private const float ShakeTime = 0.35f;
        private const float ShakeDistance = 9f;
        private const float ShakeFrequency = 34f;

        [SerializeField] private UiButton _button;
        [SerializeField] private TMP_Text _number;
        [SerializeField] private GameObject _lock;

        [Tooltip("Faint for locked levels that development builds still let you start.")]
        [SerializeField] private CanvasGroup _group;

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
            _button.interactable = data.Playable;
            _button.Accent = data.IsNext;
            if (_group != null) _group.alpha = !data.Unlocked && data.Playable ? 0.5f : 1f;
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
