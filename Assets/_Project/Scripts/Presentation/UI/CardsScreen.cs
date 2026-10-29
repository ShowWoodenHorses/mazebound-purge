using System;
using Purge.Presentation.UI.Shapes;
using Purge.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The cards of a level-up (stage 6c, <c>GameFlowState.Cards</c>): a dim over the paused level, the title with the
    /// run's level, up to three cards (rarity frame and caption, icon, name, what a level gives, its level) and the reroll
    /// for a rewarded ad. Passive: <see cref="CardsPresenter"/> fills it, <see cref="ScreenRouter"/> shows it. Clicks are
    /// ignored for a moment after it appears (a tap meant for the game); the first card is selected for a gamepad.
    /// </summary>
    public sealed class CardsScreen : UIScreen
    {
        /// <summary>Clicks are ignored this long after the cards appeared or changed.</summary>
        private const float ClickDelay = 0.4f;

        [SerializeField] private UiStyle _style;
        [SerializeField] private TMP_Text _subtitle;
        [SerializeField] private CardSlot[] _cards = Array.Empty<CardSlot>();
        [SerializeField] private UiButton _rerollButton;

        private float _shownAt;

        /// <summary>(index) a card was clicked.</summary>
        public event Action<int> CardClicked;

        public event Action RerollClicked;

        public int SlotCount => _cards.Length;

        private bool CanClick => Time.unscaledTime - _shownAt >= ClickDelay;

        private void Awake()
        {
            for (var i = 0; i < _cards.Length; i++)
            {
                var index = i;
                Bind(_cards[i].Button, () =>
                {
                    if (CanClick) CardClicked?.Invoke(index);
                });
            }

            Bind(_rerollButton, () =>
            {
                if (CanClick) RerollClicked?.Invoke();
            });
        }

        private void OnEnable() => _shownAt = Time.unscaledTime;

        public void SetSubtitle(string text)
        {
            if (_subtitle != null) _subtitle.text = text ?? string.Empty;
        }

        /// <summary>Fills a card slot; <paramref name="shown"/> false hides it.</summary>
        public void SetCard(int index, bool shown, Core.Content.Rarity rarity, string rarityName, Sprite icon, string name,
            string text, string level)
        {
            if ((uint)index >= (uint)_cards.Length) return;
            var slot = _cards[index];
            slot.Button.gameObject.SetActive(shown);
            if (!shown) return;
            var color = _style != null ? _style.RarityColor(rarity) : Color.white;
            slot.Frame.Stroke = color;
            slot.RarityName.color = color;
            slot.RarityName.text = rarityName;
            slot.Icon.sprite = icon;
            slot.Icon.enabled = icon != null;
            slot.Icon.color = color;
            slot.Name.text = name;
            slot.Text.text = text;
            slot.Level.text = level;
        }

        /// <summary>
        /// The reroll button: shown while this offer's reroll is not spent; a free one (the booster, stage 11б) has no ad
        /// mark.
        /// </summary>
        public void SetReroll(bool shown, bool free = false)
        {
            if (_rerollButton == null) return;
            _rerollButton.gameObject.SetActive(shown);
            var adMark = _rerollButton.transform.Find("Icon");
            if (adMark != null) adMark.gameObject.SetActive(!free);
        }

        /// <summary>New cards: clicks wait a moment again, the first card takes the gamepad's selection.</summary>
        public void Refresh()
        {
            _shownAt = Time.unscaledTime;
            if (_cards.Length > 0 && EventSystem.current != null && _cards[0].Button.gameObject.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(_cards[0].Button.gameObject);
        }

        [Serializable]
        public sealed class CardSlot
        {
            public UiButton Button;
            public UiShape Frame;
            public TMP_Text RarityName;
            public Image Icon;
            public TMP_Text Name;
            public TMP_Text Text;
            public TMP_Text Level;
        }
    }
}
