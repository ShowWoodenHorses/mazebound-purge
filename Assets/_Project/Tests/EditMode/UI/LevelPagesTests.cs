using System.Collections.Generic;
using Maze.Presentation.UI;
using NUnit.Framework;

namespace Maze.Tests.EditMode.UI
{
    public class LevelPagesTests
    {
        /// <summary>Levels 1..count: the first <paramref name="completed"/> done, the next one open, the rest locked.</summary>
        private static List<LevelTileData> Levels(int count, int completed)
        {
            var levels = new List<LevelTileData>();
            for (var i = 0; i < count; i++)
            {
                var unlocked = i <= completed;
                levels.Add(new LevelTileData("L" + i, i + 1, i < completed ? 2 : 0, unlocked, unlocked));
            }

            return levels;
        }

        [Test]
        public void PageCount_TenPerPage_AtLeastOne()
        {
            Assert.AreEqual(1, LevelPages.PageCount(0));
            Assert.AreEqual(1, LevelPages.PageCount(10));
            Assert.AreEqual(2, LevelPages.PageCount(11));
            Assert.AreEqual(2, LevelPages.PageCount(20));
        }

        [Test]
        public void NextLevel_IsTheFirstOpenNotCompleted()
        {
            Assert.AreEqual(6, LevelPages.NextLevel(Levels(20, 6)));
            Assert.AreEqual(0, LevelPages.NextLevel(Levels(20, 0)), "Fresh progress: level 1.");
            Assert.AreEqual(-1, LevelPages.NextLevel(Levels(20, 20)), "All done.");
        }

        [Test]
        public void StartPage_IsTheNextLevelsPage_OrTheLastOpenOne()
        {
            Assert.AreEqual(0, LevelPages.StartPage(Levels(20, 6)));
            Assert.AreEqual(1, LevelPages.StartPage(Levels(20, 10)), "Level 11 is next: page 2.");
            Assert.AreEqual(1, LevelPages.StartPage(Levels(20, 20)), "All done: the last page.");
            Assert.AreEqual(0, LevelPages.StartPage(new List<LevelTileData>()));
        }

        [Test]
        public void DevLevels_StartOnANewPage_AndAreNeverNext()
        {
            var combined = new List<LevelTileData>();
            var dev = new List<LevelTileData> { new LevelTileData("Level_Dev", 1, 0, true, true, caption: "Dev") };
            LevelPages.Combine(Levels(12, 12), dev, combined);
            Assert.AreEqual(21, combined.Count, "12 levels, 8 fillers, the dev level on page 3.");
            Assert.IsNull(combined[12].LevelId, "A filler.");
            Assert.AreEqual("Level_Dev", combined[20].LevelId);
            Assert.AreEqual(-1, LevelPages.NextLevel(combined), "All game levels done: a dev level is not 'next'.");
            Assert.AreEqual(1, LevelPages.StartPage(combined), "The last page with game levels.");

            LevelPages.Combine(Levels(10, 0), null, combined);
            Assert.AreEqual(10, combined.Count, "No dev levels: nothing added.");
        }

        [Test]
        public void AsNext_KeepsTheData()
        {
            var tile = new LevelTileData("L3", 4, 0, true, true).AsNext();
            Assert.IsTrue(tile.IsNext);
            Assert.AreEqual("L3", tile.LevelId);
            Assert.AreEqual(4, tile.Number);
            Assert.IsFalse(tile.Completed);
        }
    }
}
