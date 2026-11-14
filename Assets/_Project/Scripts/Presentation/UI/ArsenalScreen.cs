using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace Purge.Presentation.UI
{
    public sealed class ArsenalScreen : UIScreen
    {
        [SerializeField] private Button _back, _previous, _next, _buy, _favorite, _exclude;
        [SerializeField] private TMP_Text _className, _money, _title, _details, _status;
        [SerializeField] private RectTransform _grid;
        [SerializeField] private Button _template;
        [SerializeField] private ArsenalPreview _preview;
        private readonly List<Button> _tiles = new List<Button>();
        public ArsenalPreview Preview => _preview;
        public event Action BackClicked, PreviousClicked, NextClicked, BuyClicked, FavoriteClicked, ExcludeClicked;
        public event Action<int> Selected;
        private void Awake()
        {
            Bind(_back, () => BackClicked?.Invoke()); Bind(_previous, () => PreviousClicked?.Invoke());
            Bind(_next, () => NextClicked?.Invoke()); Bind(_buy, () => BuyClicked?.Invoke());
            Bind(_favorite, () => FavoriteClicked?.Invoke()); Bind(_exclude, () => ExcludeClicked?.Invoke());
        }
        public void List(string category, int money, IReadOnlyList<string> labels, IReadOnlyList<Sprite> icons, int selected)
        {
            _className.text = category; _money.text = money.ToString();
            while (_tiles.Count < labels.Count)
            {
                int index = _tiles.Count; var tile = Instantiate(_template, _grid);
                tile.onClick.AddListener(() => Selected?.Invoke(index)); _tiles.Add(tile);
            }
            _template.gameObject.SetActive(false);
            for (int i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i]; tile.gameObject.SetActive(i < labels.Count); if (i >= labels.Count) continue;
                tile.GetComponentInChildren<TMP_Text>(true).text = labels[i];
                var icon = tile.transform.Find("Icon").GetComponent<Image>(); icon.sprite = icons[i]; icon.enabled = icons[i] != null;
                tile.GetComponent<Purge.Presentation.UI.Style.UiButton>().Accent = i == selected;
            }
        }
        public void Card(string title, string details, string status, string buy, string favorite, string exclude, bool canBuy, bool open)
        {
            _title.text = title; _details.text = details; _status.text = status;
            _buy.GetComponentInChildren<TMP_Text>(true).text = buy; _buy.interactable = canBuy;
            _favorite.GetComponentInChildren<TMP_Text>(true).text = favorite;
            _exclude.GetComponentInChildren<TMP_Text>(true).text = exclude;
            _favorite.interactable = _exclude.interactable = open;
        }
    }
}
