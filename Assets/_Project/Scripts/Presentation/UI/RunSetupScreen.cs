using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Presentation.UI.Shapes;
using Purge.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The run's choice and preparation on one screen (stage 11б, ТЗ А.10; decision of the user 2026-10-30): on the left
    /// the five location tiles (a closed one — the lock, one without a level — «СКОРО»), the four difficulties of the
    /// chosen location (closed ones locked, completed ones starred) and «Закрытая арена» from the normal one; on the right
    /// the weapon of slot 1 (the base pistol first), the throwable («none» first) and the boosters (a row each: icon, name,
    /// what it gives, price; closed ones locked), the total and «В БОЙ». The middle is transparent: the podium shows the
    /// chosen weapon. A passive view: <see cref="RunSetupPresenter"/> fills it and handles its events. List items are
    /// clones of one template each, made when a list first grows; the chosen item is in the accent.
    /// </summary>
    public sealed class RunSetupScreen : UIScreen
    {
        [SerializeField] private UiStyle _style;
        [SerializeField] private Button _backButton;
        [SerializeField] private UiButton _fightButton;
        [SerializeField] private TMP_Text _money;
        [SerializeField] private TMP_Text _total;

        [Header("Level")]
        [SerializeField] private UiButton[] _locations = Array.Empty<UiButton>();
        [SerializeField] private UiButton[] _difficulties = Array.Empty<UiButton>();
        [SerializeField] private GameObject _arenaRow;
        [SerializeField] private Toggle _arena;

        [Header("Preparation")]
        [SerializeField] private RectTransform _weaponList;
        [SerializeField] private UiButton _weaponTemplate;
        [SerializeField] private RectTransform _throwableList;

        [Tooltip("Icon of an item without its own (the throwable «none»).")]
        [SerializeField] private Sprite _noneIcon;
        [SerializeField] private RectTransform _boosterList;
        [SerializeField] private UiButton _boosterTemplate;

        private readonly List<UiButton> _weapons = new List<UiButton>();
        private readonly List<UiButton> _throwables = new List<UiButton>();
        private readonly List<UiButton> _boosters = new List<UiButton>();
        private bool _settingArena;

        public event Action BackClicked;
        public event Action FightClicked;
        public event Action<int> LocationClicked;
        public event Action<int> DifficultyClicked;
        public event Action<bool> ArenaChanged;
        public event Action<int> WeaponClicked;
        public event Action<int> ThrowableClicked;
        public event Action<int> BoosterClicked;

        /// <summary>One item of the weapon or throwable list.</summary>
        public struct Item
        {
            public Sprite Icon;
            public Rarity Rarity;

            /// <summary>No rarity stripe (the base pistol, «none»).</summary>
            public bool Plain;
        }

        /// <summary>One booster row.</summary>
        public struct Booster
        {
            public Sprite Icon;
            public string Name;
            public string Text;
            public int Price;
            public bool Open;
            public bool Chosen;
        }

        public int LocationCount => _locations.Length;
        public int DifficultyCount => _difficulties.Length;

        private void Awake()
        {
            Bind(_backButton, () => BackClicked?.Invoke());
            Bind(_fightButton, () => FightClicked?.Invoke());
            for (var i = 0; i < _locations.Length; i++)
            {
                var index = i;
                Bind(_locations[i], () => LocationClicked?.Invoke(index));
            }

            for (var i = 0; i < _difficulties.Length; i++)
            {
                var index = i;
                Bind(_difficulties[i], () => DifficultyClicked?.Invoke(index));
            }

            if (_arena != null)
                _arena.onValueChanged.AddListener(on =>
                {
                    if (!_settingArena) ArenaChanged?.Invoke(on);
                });
            if (_weaponTemplate != null) _weaponTemplate.gameObject.SetActive(false);
            if (_boosterTemplate != null) _boosterTemplate.gameObject.SetActive(false);
        }

        public void SetMoney(int money)
        {
            if (_money != null) _money.text = money.ToString();
        }

        /// <summary>The total of the chosen boosters and whether «В БОЙ» can be pressed (the money is enough).</summary>
        public void SetTotal(int total, bool canFight)
        {
            if (_total != null) _total.text = total.ToString();
            if (_fightButton != null) _fightButton.interactable = canFight;
        }

        /// <summary>A location tile: its name; no level there — «СКОРО» and the lock; a closed level — the lock.</summary>
        public void SetLocation(int index, string name, string soon, bool exists, bool open, bool chosen)
        {
            if (index < 0 || index >= _locations.Length || _locations[index] == null) return;
            var tile = _locations[index];
            SetText(tile.transform, "Name", name);
            SetText(tile.transform, "Soon", exists ? string.Empty : soon);
            SetActive(tile.transform, "Lock", !open);
            tile.interactable = open;
            tile.Accent = chosen;
        }

        /// <summary>A difficulty: closed ones locked, completed ones starred.</summary>
        public void SetDifficulty(int index, string name, bool open, bool completed, bool chosen)
        {
            if (index < 0 || index >= _difficulties.Length || _difficulties[index] == null) return;
            var button = _difficulties[index];
            SetText(button.transform, "Label", name);
            SetActive(button.transform, "Lock", !open);
            SetActive(button.transform, "Done", open && completed);
            button.interactable = open;
            button.Accent = chosen;
        }

        /// <summary>«Закрытая арена»: shown from the normal difficulty.</summary>
        public void SetArena(bool shown, bool on)
        {
            if (_arenaRow != null) _arenaRow.SetActive(shown);
            if (_arena == null) return;
            _settingArena = true;
            _arena.isOn = on;
            _settingArena = false;
        }

        public void SetWeapons(IReadOnlyList<Item> items, int chosen) => Fill(_weapons, _weaponList, items, chosen);

        public void SetThrowables(IReadOnlyList<Item> items, int chosen) =>
            Fill(_throwables, _throwableList, items, chosen);

        public void SetBoosters(IReadOnlyList<Booster> boosters)
        {
            for (var i = 0; i < boosters.Count; i++)
            {
                var row = BoosterAt(i);
                if (row == null) break;
                var booster = boosters[i];
                var image = row.transform.Find("Icon")?.GetComponent<Image>();
                if (image != null) image.sprite = booster.Icon;
                SetText(row.transform, "Name", booster.Name);
                SetText(row.transform, "Text", booster.Text);
                SetText(row.transform, "Price", booster.Open ? booster.Price.ToString() : string.Empty);
                SetActive(row.transform, "Coin", booster.Open);
                SetActive(row.transform, "Lock", !booster.Open);
                SetActive(row.transform, "Check", booster.Open && booster.Chosen);
                row.interactable = booster.Open;
                row.Accent = booster.Chosen;
                row.gameObject.SetActive(true);
            }

            for (var i = boosters.Count; i < _boosters.Count; i++)
                _boosters[i].gameObject.SetActive(false);
        }

        private void Fill(List<UiButton> buttons, RectTransform list, IReadOnlyList<Item> items, int chosen)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var tile = TileAt(buttons, list, i);
                if (tile == null) break;
                var item = items[i];
                var image = tile.transform.Find("Icon")?.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = item.Icon != null ? item.Icon : _noneIcon;
                    image.enabled = image.sprite != null;
                }

                var stripe = tile.transform.Find("Rarity")?.GetComponent<UiShape>();
                if (stripe != null)
                {
                    stripe.gameObject.SetActive(!item.Plain);
                    if (_style != null) stripe.Fill = _style.RarityColor(item.Rarity);
                }

                tile.Accent = i == chosen;
                tile.gameObject.SetActive(true);
            }

            for (var i = items.Count; i < buttons.Count; i++)
                buttons[i].gameObject.SetActive(false);
        }

        private UiButton TileAt(List<UiButton> buttons, RectTransform list, int index)
        {
            if (_weaponTemplate == null || list == null) return null;
            while (buttons.Count <= index)
            {
                var tile = Instantiate(_weaponTemplate, list, false);
                tile.name = "Item" + buttons.Count;
                var slot = buttons.Count;
                var isWeapon = ReferenceEquals(buttons, _weapons);
                tile.onClick.AddListener(() =>
                {
                    if (isWeapon) WeaponClicked?.Invoke(slot);
                    else ThrowableClicked?.Invoke(slot);
                });
                buttons.Add(tile);
            }

            return buttons[index];
        }

        private UiButton BoosterAt(int index)
        {
            if (_boosterTemplate == null || _boosterList == null) return null;
            while (_boosters.Count <= index)
            {
                var row = Instantiate(_boosterTemplate, _boosterList, false);
                row.name = "Booster" + _boosters.Count;
                var slot = _boosters.Count;
                row.onClick.AddListener(() => BoosterClicked?.Invoke(slot));
                _boosters.Add(row);
            }

            return _boosters[index];
        }

        private static void SetText(Transform parent, string child, string text)
        {
            var label = parent.Find(child)?.GetComponent<TMP_Text>();
            if (label != null) label.text = text;
        }

        private static void SetActive(Transform parent, string child, bool active)
        {
            var target = parent.Find(child);
            if (target != null) target.gameObject.SetActive(active);
        }
    }
}
