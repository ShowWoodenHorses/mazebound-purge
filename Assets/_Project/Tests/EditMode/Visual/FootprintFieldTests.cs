using System.Linq;
using Maze.Core.Visual;
using Maze.Presentation.Visual;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Visual
{
    public class FootprintFieldTests
    {
        private static FootprintKind Kind(float step = 0.5f, float fadeFrom = 1f, float fadeTo = 2f, float lifetime = 10f) =>
            new FootprintKind { StepLength = step, Spacing = 0.1f, FadeFrom = fadeFrom, FadeTo = fadeTo, Lifetime = lifetime };

        /// <summary>Walks from (0, 0) north in small steps, returns the slots of prints left.</summary>
        private static int[] WalkNorth(FootprintField field, int walker, float distance, float from = 0f)
        {
            var slots = new System.Collections.Generic.List<int>();
            for (var y = from + 0.05f; y <= from + distance + 1e-4f; y += 0.05f)
            {
                var slot = field.Move(walker, new Vector2(0f, y));
                if (slot >= 0) slots.Add(slot);
            }
            return slots.ToArray();
        }

        [Test]
        public void Prints_EveryStep_AlternatingSides_FacingTheWay()
        {
            var field = new FootprintField(16);
            var walker = field.AddWalker(Kind(step: 0.5f));
            field.Move(walker, Vector2.zero);

            var slots = WalkNorth(field, walker, 2.4f);

            // First print after half a step, then every step: 0.25, 0.75, 1.25, 1.75, 2.25 (the next at 2.75).
            Assert.AreEqual(5, slots.Length);
            var prints = slots.Select(s => field.Prints[s]).ToArray();
            for (var i = 0; i < prints.Length; i++)
            {
                Assert.AreEqual(Vector2.up.x, prints[i].Direction.x, 1e-3f);
                Assert.AreEqual(Vector2.up.y, prints[i].Direction.y, 1e-3f);
                Assert.AreEqual(0.1f, Mathf.Abs(prints[i].Position.x), 1e-3f, "Beside the path by Spacing.");
                if (i > 0)
                    Assert.AreNotEqual(prints[i - 1].LeftFoot, prints[i].LeftFoot, "Left and right in turn.");
            }

            var left = prints.First(p => p.LeftFoot);
            Assert.Less(left.Position.x, 0f, "Going north, the left foot is to the west.");
        }

        [Test]
        public void StandingStill_LeavesNoPrints_AndJumpsLeaveNone()
        {
            var field = new FootprintField(16);
            var walker = field.AddWalker(Kind(step: 0.5f));
            field.Move(walker, Vector2.zero);
            for (var i = 0; i < 10; i++)
                Assert.AreEqual(-1, field.Move(walker, Vector2.zero));

            Assert.AreEqual(-1, field.Move(walker, new Vector2(5f, 5f)), "A teleport is not a step.");
            Assert.AreEqual(0, field.AliveCount);
        }

        [Test]
        public void Prints_FadeAsTheWalkerMovesAway()
        {
            var field = new FootprintField(64);
            var walker = field.AddWalker(Kind(step: 0.5f, fadeFrom: 1f, fadeTo: 2f));
            field.Move(walker, Vector2.zero);
            var first = WalkNorth(field, walker, 0.3f)[0]; // at y = 0.25

            WalkNorth(field, walker, 0.7f, 0.3f); // walker at y = 1.0: 0.75 away
            field.Update(0f);
            Assert.AreEqual(1f, field.Prints[first].Alpha, 1e-3f, "Closer than FadeFrom: full.");

            WalkNorth(field, walker, 0.75f, 1f); // y = 1.75: 1.5 away
            field.Update(0f);
            Assert.That(field.Prints[first].Alpha, Is.InRange(0.05f, 0.95f), "Between FadeFrom and FadeTo: fading.");

            WalkNorth(field, walker, 1f, 1.75f); // y = 2.75: 2.5 away
            field.Update(0f);
            Assert.IsFalse(field.Prints[first].Alive, "Past FadeTo: gone.");
        }

        [Test]
        public void Prints_DisappearWithAge_WhenTheWalkerStands()
        {
            var field = new FootprintField(16);
            var walker = field.AddWalker(Kind(step: 0.5f, lifetime: 2f));
            field.Move(walker, Vector2.zero);
            var slot = WalkNorth(field, walker, 0.3f)[0];

            field.Update(1f);
            Assert.AreEqual(1f, field.Prints[slot].Alpha, 1e-3f, "Fresh for the first 60 % of the lifetime.");
            field.Update(0.6f);
            Assert.That(field.Prints[slot].Alpha, Is.InRange(0.05f, 0.95f));
            field.Update(0.5f);
            Assert.IsFalse(field.Prints[slot].Alive);
            Assert.AreEqual(0, field.AliveCount);
        }

        [Test]
        public void Capacity_ReusesTheOldestPrint()
        {
            var field = new FootprintField(4);
            var walker = field.AddWalker(Kind(step: 0.5f, fadeFrom: 100f, fadeTo: 200f));
            field.Move(walker, Vector2.zero);

            var slots = WalkNorth(field, walker, 3f); // 6 prints into 4 slots

            Assert.AreEqual(6, slots.Length);
            Assert.AreEqual(4, field.AliveCount);
            Assert.AreEqual(slots[0], slots[4], "The fifth print takes the first one's slot.");
            Assert.Greater(field.Prints[slots[4]].Position.y, 2f);
        }

        [Test]
        public void Walkers_HaveTheirOwnKinds()
        {
            var player = Kind(step: 0.5f);
            var zombie = Kind(step: 1f);
            var field = new FootprintField(32);
            var a = field.AddWalker(player);
            var b = field.AddWalker(zombie);
            field.Move(a, Vector2.zero);
            field.Move(b, new Vector2(3f, 0f));

            var aPrints = 0;
            var bPrints = 0;
            for (var y = 0.05f; y <= 2.001f; y += 0.05f)
            {
                var slotA = field.Move(a, new Vector2(0f, y));
                var slotB = field.Move(b, new Vector2(3f, y));
                if (slotA >= 0) { aPrints++; Assert.AreSame(player, field.KindOf(field.Prints[slotA].Walker)); }
                if (slotB >= 0) { bPrints++; Assert.AreSame(zombie, field.KindOf(field.Prints[slotB].Walker)); }
            }

            Assert.AreEqual(4, aPrints);
            Assert.AreEqual(2, bPrints);
        }
    }
}
