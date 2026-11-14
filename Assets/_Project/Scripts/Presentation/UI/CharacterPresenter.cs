using System;
using Purge.Application.Meta;
using Purge.Application.Services;
using Purge.Presentation.Localization;

namespace Purge.Presentation.UI
{
    internal sealed class CharacterPresenter : IDisposable
    {
        private readonly CharacterTree _tree;
        private readonly LocalizationService _texts;
        private CharacterScreen _screen;
        private CharacterNode _selected;

        public CharacterPresenter(CharacterTree tree, LocalizationService texts)
        {
            _tree = tree;
            _texts = texts;
        }

        public void Attach(CharacterScreen screen)
        {
            if (screen == null || _screen == screen) return;
            Dispose();
            _screen = screen;
            _screen.NodeClicked += Select;
            _screen.BuyClicked += Buy;
        }

        public void Dispose()
        {
            if (_screen == null) return;
            _screen.NodeClicked -= Select;
            _screen.BuyClicked -= Buy;
            _screen = null;
        }

        public void Open() => Select(0);

        public void Refresh()
        {
            if (_selected == null || _screen == null) return;
            for (var i = 0; i < CharacterTree.Nodes.Count; i++)
            {
                var node = CharacterTree.Nodes[i];
                var owned = _tree.Has(node.Id);
                _screen.SetNode(i, (owned ? "✓ " : "") + _texts.Get(TextKeys.CharacterNodePrefix + node.Id), node == _selected);
            }

            var selectedOwned = _tree.Has(_selected.Id);
            var available = _tree.MeetsRequirements(_selected);
            var details = _texts.Get(TextKeys.CharacterNodePrefix + _selected.Id + ".text");
            var branchCount = _tree.BranchCount;
            if (_selected.RequiredBranchNodes > 0)
                details += "\n" + string.Format(_texts.Get(TextKeys.CharacterRequired), branchCount, _selected.RequiredBranchNodes);
            if (!selectedOwned && !available && branchCount >= _selected.RequiredBranchNodes)
                details += "\n" + _texts.Get(TextKeys.CharacterLocked);
            var label = selectedOwned ? _texts.Get(TextKeys.CharacterOwned)
                : string.Format(_texts.Get(TextKeys.CharacterBuy), _selected.Price);
            _screen.SetSummary(_tree.Money, details, label, !selectedOwned && available && _tree.Money >= _selected.Price, branchCount > 0);
        }

        private void Select(int index)
        {
            _selected = CharacterTree.Nodes[index];
            Refresh();
        }

        private void Buy()
        {
            if (_tree.TryPurchase(_selected)) Refresh();
        }
    }
}
