using System.Collections.Generic;
using NUnit.Framework;
using Purge.Core.Balance;
using Purge.Core.Cards;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Gameplay.Balance;
using Purge.Gameplay.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Balance
{
    /// <summary>Stage 10a: the bot's plan, its input and the run without revives.</summary>
    public class BotTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        [Test]
        public void Plan_CardPriority_ListedFirst_UnlistedLast()
        {
            var damage = Card("card_damage");
            var speed = Card("card_speed");
            var magnet = Card("card_magnet");
            var plan = Create<BotPlan>();
            plan.Configure(new LevelData[0], new DifficultyDefinition[0], 3, new[] { speed, damage });

            Assert.AreEqual(0, plan.PriorityOf(speed));
            Assert.AreEqual(1, plan.PriorityOf(damage));
            Assert.AreEqual(int.MaxValue, plan.PriorityOf(magnet));
            Assert.AreEqual(int.MaxValue, plan.PriorityOf(null));
            Assert.AreEqual(3, plan.Runs);
        }

        [Test]
        public void Input_Presses_LastUntilCleared_NothingHeld()
        {
            var input = new BotInput();
            input.Press(PlayerAction.Interact);
            input.Press(PlayerAction.Slot2);

            Assert.IsTrue(input.WasPressed(PlayerAction.Interact));
            Assert.IsTrue(input.WasPressed(PlayerAction.Slot2));
            Assert.IsFalse(input.WasPressed(PlayerAction.Slot1));
            Assert.IsFalse(input.IsHeld(PlayerAction.Interact));
            Assert.IsFalse(input.LookIsPointer);

            input.ClearPresses();
            Assert.IsFalse(input.WasPressed(PlayerAction.Interact));
            Assert.IsFalse(input.WasPressed(PlayerAction.Slot2));
        }

        [Test]
        public void Revival_NotAllowed_DiesWithoutWindow()
        {
            var definition = Create<PlayerDefinition>();
            definition.ReviveSkill = true;
            var health = new PlayerHealth(definition);
            var revival = new PlayerRevival(health) { Allowed = false };
            var downed = 0;
            var died = 0;
            revival.Downed += () => downed++;
            revival.Died += () => died++;

            Assert.IsFalse(revival.CanReviveAtAll);
            health.Damage(health.Max);
            revival.Tick(10f);

            Assert.AreEqual(0, downed, "No window for the bot.");
            Assert.AreEqual(1, died);
            revival.Dispose();
        }

        private CardDefinition Card(string id)
        {
            var card = Create<CardDefinition>();
            card.Configure(id, Rarity.Common, 3);
            return card;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var created = ScriptableObject.CreateInstance<T>();
            _created.Add(created);
            return created;
        }
    }
}
