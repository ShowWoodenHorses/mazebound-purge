using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Presentation.Visual;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Visibility
{
    /// <summary>Fog of war mask: border, instant first reveal, fading only of changed cells.</summary>
    public class FogMaskTests
    {
        private static readonly GridPosition A = new GridPosition(1, 1);
        private static readonly GridPosition B = new GridPosition(2, 1);

        [Test]
        public void Texture_HasHiddenBorder_AndStartsHidden()
        {
            var mask = new FogMask(4, 3);

            Assert.AreEqual(6, mask.TextureWidth);
            Assert.AreEqual(5, mask.TextureHeight);
            Assert.AreEqual(30, mask.Pixels.Length);
            foreach (var pixel in mask.Pixels)
                Assert.AreEqual(0, pixel);
        }

        [Test]
        public void FirstReveal_IsInstant_LaterChangesFade()
        {
            var mask = new FogMask(4, 3);
            Assert.IsTrue(mask.SetRevealed(new List<GridPosition> { A }, instant: true));
            Assert.AreEqual(1f, mask.ValueOf(A));
            Assert.AreEqual(255, mask.Pixels[2 * 6 + 2], "Cell (1,1) is texel (2,2): one-cell border.");
            Assert.IsFalse(mask.IsFading);

            Assert.IsFalse(mask.SetRevealed(new List<GridPosition> { B }, instant: false));
            Assert.IsTrue(mask.IsFading);
            Assert.AreEqual(1f, mask.ValueOf(A), "Nothing changes until stepped.");

            Assert.IsTrue(mask.Step(0.15f, fadeSeconds: 0.3f));
            Assert.AreEqual(0.5f, mask.ValueOf(A), 0.01f, "Halfway hidden.");
            Assert.AreEqual(0.5f, mask.ValueOf(B), 0.01f, "Halfway revealed.");

            mask.Step(0.2f, 0.3f);
            Assert.AreEqual(0f, mask.ValueOf(A));
            Assert.AreEqual(1f, mask.ValueOf(B));
            Assert.IsFalse(mask.IsFading, "Fading stops: no more uploads.");
            Assert.IsFalse(mask.Step(0.1f, 0.3f));
        }

        [Test]
        public void JustCovered_ListsCellsOnceFullyFogged()
        {
            var mask = new FogMask(4, 3);
            mask.SetRevealed(new List<GridPosition> { A, B }, instant: true);
            mask.SetRevealed(new List<GridPosition> { B }, instant: false);

            mask.Step(0.1f, 0.3f);
            Assert.AreEqual(0, mask.JustCovered.Count, "Still fading: its geometry must stay.");

            mask.Step(0.25f, 0.3f);
            CollectionAssert.AreEqual(new[] { A }, mask.JustCovered, "Fully fogged now: geometry can be hidden.");

            mask.Step(0.1f, 0.3f);
            Assert.AreEqual(0, mask.JustCovered.Count, "Reported once.");
        }

        [Test]
        public void TargetChangedMidFade_TurnsBack()
        {
            var mask = new FogMask(4, 3);
            mask.SetRevealed(new List<GridPosition> { A }, instant: true);
            mask.SetRevealed(new List<GridPosition>(), instant: false);
            mask.Step(0.1f, 0.4f);
            Assert.AreEqual(0.75f, mask.ValueOf(A), 0.01f);

            mask.SetRevealed(new List<GridPosition> { A }, instant: false);
            mask.Step(0.1f, 0.4f);
            Assert.AreEqual(1f, mask.ValueOf(A), 0.01f);
            Assert.IsFalse(mask.IsFading);
        }

        [Test]
        public void ZeroFadeTime_SnapsInOneStep_OutsideCellsIgnored()
        {
            var mask = new FogMask(4, 3);
            mask.SetRevealed(new List<GridPosition> { A, new GridPosition(-1, 0), new GridPosition(4, 3) }, instant: false);

            mask.Step(0.016f, fadeSeconds: 0f);

            Assert.AreEqual(1f, mask.ValueOf(A));
            Assert.IsFalse(mask.IsFading);
            Assert.AreEqual(0, mask.Pixels[0], "Border stays hidden.");
        }
    }
}
