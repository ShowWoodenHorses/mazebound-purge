using System;
using Purge.Presentation.UI.Shapes;
using Purge.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    public sealed class CharacterScreen : UIScreen
    {
        [SerializeField] private Button[] _nodes;
        [SerializeField] private UiShape[] _incoming;
        [SerializeField] private UiStyle _style;
        [SerializeField] private Button _back;
        [SerializeField] private Button _buy;
        [SerializeField] private Button _reset;
        [SerializeField] private TMP_Text _money;
        [SerializeField] private TMP_Text _details;
        [SerializeField] private TMP_Text _buyLabel;
        private TMP_Text[] _labels;

        public event Action BackClicked;
        public event Action BuyClicked;
        public event Action ResetClicked;
        public event Action<int> NodeClicked;

        private void Awake()
        {
            Bind(_back, () => BackClicked?.Invoke());
            Bind(_buy, () => BuyClicked?.Invoke());
            Bind(_reset, () => ResetClicked?.Invoke());

            _labels = new TMP_Text[_nodes.Length];
            for (var i = 0; i < _nodes.Length; i++)
            {
                var index = i;
                _labels[i] = _nodes[i].GetComponentInChildren<TMP_Text>(true);
                Bind(_nodes[i], () => NodeClicked?.Invoke(index));
            }
        }

        public void SetNode(int index, string label, bool selected, bool owned, bool available)
        {
            _labels[index].text = label;
            if (_nodes[index] is UiButton button) button.Accent = selected;
            if (_style == null || _incoming[index] == null) return;

            var color = owned ? _style.Accent : _style.Line;
            if (!owned && !available) color.a *= 0.5f;
            _incoming[index].Fill = color;
        }

        public void SetSummary(int money, string details, string buyLabel, bool canBuy, bool canReset)
        {
            _money.text = money.ToString();
            _details.text = details;
            _buyLabel.text = buyLabel;
            _buy.interactable = canBuy;
            _reset.interactable = canReset;
        }
    }
}
