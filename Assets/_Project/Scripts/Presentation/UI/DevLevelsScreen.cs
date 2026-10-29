using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// Development levels (stage 11a, editor and development builds; opened by DEV in the main menu): the tutorial, the
    /// catalog's levels and the dev levels as plain buttons, every one playable regardless of progress. Buttons are clones
    /// of one template, made when the list first grows. A passive view: the router fills it and starts the chosen level.
    /// </summary>
    public sealed class DevLevelsScreen : UIScreen
    {
        [SerializeField] private Button _backButton;
        [SerializeField] private RectTransform _list;
        [SerializeField] private Button _template;

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<string> _ids = new List<string>();

        public event Action<string> LevelSelected;
        public event Action BackClicked;

        private void Awake()
        {
            Bind(_backButton, () => BackClicked?.Invoke());
            if (_template != null) _template.gameObject.SetActive(false);
        }

        /// <summary>Levels to offer: ids and their captions, in order.</summary>
        public void SetLevels(IReadOnlyList<KeyValuePair<string, string>> levels)
        {
            _ids.Clear();
            for (var i = 0; i < levels.Count; i++)
            {
                _ids.Add(levels[i].Key);
                var button = ButtonAt(i);
                if (button == null) break;
                var caption = button.GetComponentInChildren<TMP_Text>(true);
                if (caption != null) caption.text = levels[i].Value;
                button.gameObject.SetActive(true);
            }

            for (var i = levels.Count; i < _buttons.Count; i++)
                _buttons[i].gameObject.SetActive(false);
        }

        private Button ButtonAt(int index)
        {
            if (_template == null || _list == null) return null;
            while (_buttons.Count <= index)
            {
                var button = Instantiate(_template, _list, false);
                button.name = "Level" + _buttons.Count;
                var slot = _buttons.Count;
                button.onClick.AddListener(() =>
                {
                    if (slot < _ids.Count) LevelSelected?.Invoke(_ids[slot]);
                });
                _buttons.Add(button);
            }

            return _buttons[index];
        }
    }
}
