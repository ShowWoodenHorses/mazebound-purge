using Maze.Application.Platform;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Progress
{
    public sealed class SoundSuspensionTests
    {
        private bool _paused;

        [SetUp]
        public void SetUp() => _paused = AudioListener.pause;

        [TearDown]
        public void TearDown() => AudioListener.pause = _paused;

        [Test]
        public void SoundReturns_OnlyWhenNoReasonHolds()
        {
            var sound = new SoundSuspension();
            AudioListener.pause = false;

            sound.Set(SoundSuspendReason.Background, true);
            sound.Set(SoundSuspendReason.Ad, true);
            Assert.IsTrue(AudioListener.pause);

            // The ad ends while the page is still hidden: silence stays.
            sound.Set(SoundSuspendReason.Ad, false);
            Assert.IsTrue(AudioListener.pause);

            sound.Set(SoundSuspendReason.Background, false);
            Assert.IsFalse(AudioListener.pause);
            Assert.AreEqual(SoundSuspendReason.None, sound.Reasons);
        }
    }
}
