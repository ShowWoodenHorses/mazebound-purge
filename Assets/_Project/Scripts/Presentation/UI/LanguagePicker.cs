using System;
using System.Collections.Generic;
using Maze.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// The language list of the settings (a drop-down: with a dozen languages arrows would mean many taps). Every
    /// language by its own name, the shown one in the accent; a tap outside the list closes it. Items are made from
    /// <see cref="_item"/> on the first opening (the list is in a menu, not in a level), as many as there are languages.
    /// Closed together with the settings screen (it is its child).
    /// </summary>
    public sealed class LanguagePicker : MonoBehaviour
    {
        [Tooltip("The overlay: a transparent blocker over the screen and the list panel.")]
        [SerializeField] private GameObject _root;

        [Tooltip("Covers the screen under the list: a tap on it closes the list.")]
        [SerializeField] private Button _blocker;

        [SerializeField] private ScrollRect _scroll;

        [Tooltip("The first item; more are cloned from it (same parent).")]
        [SerializeField] private UiButton _item;

        private readonly List<UiButton> _items = new List<UiButton>();
        private readonly List<TMP_Text> _labels = new List<TMP_Text>();

        /// <summary>Index of the chosen language (in the order given to <see cref="Show"/>).</summary>
        public event Action<int> Picked;

        public bool IsOpen => _root != null && _root.activeSelf;

        private void Awake()
        {
            if (_blocker != null) _blocker.onClick.AddListener(Hide);
            if (_item != null) Add(_item);
            Hide();
        }

        private void OnDisable() => Hide();

        public void Show(IReadOnlyList<string> names, int current)
        {
            if (_item == null || _root == null) return;
            while (_items.Count < names.Count)
                Add(Instantiate(_item, _item.transform.parent));

            for (var i = 0; i < _items.Count; i++)
            {
                var shown = i < names.Count;
                _items[i].gameObject.SetActive(shown);
                if (!shown) continue;
                if (_labels[i] != null) _labels[i].text = names[i];
                _items[i].Accent = i == current;
            }

            _root.SetActive(true);
            Canvas.ForceUpdateCanvases();
            if (_scroll != null && names.Count > 1)
                _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(current / (float)(names.Count - 1));
            if (current >= 0 && current < _items.Count && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_items[current].gameObject); // gamepad / keyboard start here
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void Add(UiButton item)
        {
            var index = _items.Count;
            item.name = "Language" + index;
            item.onClick.RemoveAllListeners();
            item.onClick.AddListener(() => OnItemClicked(index));
            _items.Add(item);
            _labels.Add(item.GetComponentInChildren<TMP_Text>(true));
        }

        private void OnItemClicked(int index)
        {
            Hide();
            Picked?.Invoke(index);
        }
    }
}
