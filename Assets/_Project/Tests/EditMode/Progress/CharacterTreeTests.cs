using System.Linq;
using NUnit.Framework;
using Purge.Application.Meta;
using Purge.Application.Save;
using Purge.Core.Content;
using Purge.Core.Definitions;

namespace Purge.Tests.EditMode.Progress
{
    public sealed class CharacterTreeTests
    {
        private sealed class Storage : ISaveStorage
        {
            private string _json;
            public string Read() => _json;
            public void Write(string json) => _json = json;
        }

        private static CharacterNode Node(string id) => CharacterTree.Nodes.Single(node => node.Id == id);

        [Test]
        public void BuyingAndResettingBranchesSurvivesReloadAndKeepsTrunk()
        {
            var storage = new Storage();
            using (var save = new SaveService(storage))
            {
                save.Load();
                save.Data.Money = 10000;
                var tree = new CharacterTree(save);
                Assert.That(tree.TryPurchase(Node("SharpEye")), Is.False);
                Assert.That(tree.TryPurchase(Node("Uncommon")), Is.True);
                Assert.That(tree.TryPurchase(Node("Rare")), Is.False);
                Assert.That(tree.TryPurchase(Node("QuickHands")), Is.True);
                Assert.That(tree.TryPurchase(Node("Sapper")), Is.True);
                Assert.That(tree.TryPurchase(Node("SecondWind")), Is.True);
                Assert.That(tree.TryPurchase(Node("Rare")), Is.True);
                Assert.That(tree.Skills, Is.EqualTo(CharacterSkills.QuickHands | CharacterSkills.Sapper | CharacterSkills.SecondWind));
                Assert.That(tree.ResetRefund, Is.EqualTo(560));
                Assert.That(tree.ResetBranches(), Is.True);
                Assert.That(tree.ResetBranches(), Is.False);
            }

            using var loaded = new SaveService(storage);
            loaded.Load();
            var reloaded = new CharacterTree(loaded);
            Assert.That(reloaded.StartRarity, Is.EqualTo(Rarity.Rare));
            Assert.That(reloaded.Skills, Is.EqualTo(CharacterSkills.None));
            Assert.That(reloaded.Money, Is.EqualTo(3600));
        }

        [Test]
        public void CloudMergeDoesNotRestoreRefundedNodes()
        {
            var updated = new SaveData { CharacterResetGeneration = 1 };
            var stale = new SaveData();
            stale.CharacterNodes.Add("QuickHands");
            stale.CharacterNodes.Add("Uncommon");
            updated.MergeFrom(stale);
            stale.MergeFrom(updated);
            CollectionAssert.AreEqual(new[] { "Uncommon" }, updated.CharacterNodes);
            CollectionAssert.AreEqual(new[] { "Uncommon" }, stale.CharacterNodes);
        }
    }
}
