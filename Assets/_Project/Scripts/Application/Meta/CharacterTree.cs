using System;
using System.Collections.Generic;
using Purge.Application.Save;
using Purge.Application.Platform;
using Purge.Core.Content;
using Purge.Core.Definitions;

namespace Purge.Application.Meta
{
    public enum CharacterBranch { Trunk, Shooter, Demolisher, Survivor }

    public sealed class CharacterNode
    {
        internal CharacterNode(string id, CharacterBranch branch, int price, CharacterSkills skill = CharacterSkills.None,
            int requiredBranchNodes = 0)
        {
            Id = id;
            Branch = branch;
            Price = price;
            Skill = skill;
            RequiredBranchNodes = requiredBranchNodes;
        }

        public string Id { get; }
        public CharacterBranch Branch { get; }
        public int Price { get; }
        public CharacterSkills Skill { get; }
        public int RequiredBranchNodes { get; }
    }

    public sealed class CharacterTree
    {
        public const int ResetPrice = 200;

        public static IReadOnlyList<CharacterNode> Nodes { get; } = Array.AsReadOnly(new[]
        {
            new CharacterNode("Uncommon", CharacterBranch.Trunk, 1700),
            new CharacterNode("Rare", CharacterBranch.Trunk, 4500, requiredBranchNodes: 3),
            new CharacterNode("Epic", CharacterBranch.Trunk, 11000, requiredBranchNodes: 7),
            new CharacterNode("Legendary", CharacterBranch.Trunk, 28000, requiredBranchNodes: 12),
            new CharacterNode("QuickHands", CharacterBranch.Shooter, 210, CharacterSkills.QuickHands),
            new CharacterNode("SharpEye", CharacterBranch.Shooter, 370, CharacterSkills.SharpEye),
            new CharacterNode("WideChoice", CharacterBranch.Shooter, 630, CharacterSkills.WideChoice),
            new CharacterNode("CardControl", CharacterBranch.Shooter, 1100, CharacterSkills.CardControl),
            new CharacterNode("Tactician", CharacterBranch.Shooter, 1900, CharacterSkills.Tactician),
            new CharacterNode("SecondBarrel", CharacterBranch.Shooter, 2700),
            new CharacterNode("Headhunter", CharacterBranch.Shooter, 3900),
            new CharacterNode("Sapper", CharacterBranch.Demolisher, 250, CharacterSkills.Sapper),
            new CharacterNode("Preparation", CharacterBranch.Demolisher, 440, CharacterSkills.Preparation),
            new CharacterNode("Luck", CharacterBranch.Demolisher, 760, CharacterSkills.Luck),
            new CharacterNode("Minefield", CharacterBranch.Demolisher, 1300),
            new CharacterNode("SecondWind", CharacterBranch.Survivor, 300, CharacterSkills.SecondWind),
            new CharacterNode("LastStand", CharacterBranch.Survivor, 530, CharacterSkills.LastStand),
            new CharacterNode("Barrier", CharacterBranch.Survivor, 910, CharacterSkills.Barrier),
            new CharacterNode("Instinct", CharacterBranch.Survivor, 1600, CharacterSkills.Instinct),
            new CharacterNode("QuietSteps", CharacterBranch.Survivor, 2300, CharacterSkills.QuietSteps),
            new CharacterNode("Buyer", CharacterBranch.Survivor, 3300, CharacterSkills.Buyer),
            new CharacterNode("ChestHunter", CharacterBranch.Survivor, 4700, CharacterSkills.ChestHunter),
            new CharacterNode("QuietStart", CharacterBranch.Survivor, 5600),
            new CharacterNode("DropLoad", CharacterBranch.Survivor, 6800),
            new CharacterNode("SecondDome", CharacterBranch.Survivor, 8100),
        });

        private readonly SaveService _save;

        private readonly AnalyticsService _analytics;

        public CharacterTree(SaveService save, AnalyticsService analytics = null)
        {
            _save = save;
            _analytics = analytics;
        }

        public int Money => _save.Data.Money;
        public bool Has(string id) => _save.Data.CharacterNodes.Contains(id);

        public Rarity StartRarity
        {
            get
            {
                for (var rarity = Rarity.Legendary; rarity > Rarity.Common; rarity--)
                    if (Has(rarity.ToString())) return rarity;
                return Rarity.Common;
            }
        }

        public CharacterSkills Skills
        {
            get
            {
                var skills = CharacterSkills.None;
                foreach (var node in Nodes)
                    if (Has(node.Id)) skills |= node.Skill;
                return skills;
            }
        }

        public int BranchCount
        {
            get
            {
                var count = 0;
                foreach (var node in Nodes)
                    if (node.Branch != CharacterBranch.Trunk && Has(node.Id)) count++;
                return count;
            }
        }

        public int ResetRefund
        {
            get
            {
                var total = 0;
                foreach (var node in Nodes)
                    if (node.Branch != CharacterBranch.Trunk && Has(node.Id)) total += node.Price;
                return Math.Max(0, total - ResetPrice);
            }
        }

        public bool MeetsRequirements(CharacterNode node)
        {
            if (node == null || BranchCount < node.RequiredBranchNodes) return false;
            foreach (var entry in Nodes)
            {
                if (entry.Branch != node.Branch) continue;
                if (entry == node) return true;
                if (!Has(entry.Id)) return false;
            }
            return false;
        }

        public bool CanPurchase(CharacterNode node) =>
            node != null && !Has(node.Id) && Money >= node.Price && MeetsRequirements(node);

        public bool TryPurchase(CharacterNode node)
        {
            if (!CanPurchase(node)) return false;
            var data = _save.Data;
            data.Money -= node.Price;
            data.CharacterNodes.Add(node.Id);
            _save.Save();
            if (_analytics != null && !_save.IsDevProgress)
            {
                var fields = _analytics.Begin();
                fields["node"] = node.Id;
                fields["price"] = node.Price;
                _analytics.Send("character_purchase");
            }
            return true;
        }

        public bool ResetBranches()
        {
            if (BranchCount == 0) return false;
            var data = _save.Data;
            var refund = ResetRefund;
            data.Money = (int)Math.Min(int.MaxValue, (long)data.Money + refund);
            data.CharacterNodes.RemoveAll(id => !IsTrunkId(id));
            if (data.CharacterResetGeneration < int.MaxValue) data.CharacterResetGeneration++;
            _save.Save();
            if (_analytics != null && !_save.IsDevProgress)
            {
                _analytics.Begin()["refund"] = refund;
                _analytics.Send("character_reset");
            }
            return true;
        }

        internal static bool IsTrunkId(string id) =>
            id == "Uncommon" || id == "Rare" || id == "Epic" || id == "Legendary";
    }
}
