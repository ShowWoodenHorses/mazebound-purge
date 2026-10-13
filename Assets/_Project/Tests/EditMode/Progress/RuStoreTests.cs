using System.Collections.Generic;
using Maze.Application.Platform;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Progress
{
    public sealed class RuStoreTests
    {
        [Test]
        public void MyTrackerParameters_TypesAsStrings()
        {
            var parameters = new Dictionary<string, object>
            {
                ["level"] = "Level_03",
                ["stars"] = 3,
                ["time"] = 12.5f,
                ["record"] = true,
                ["none"] = null,
                ["nan"] = float.NaN,
            };

            var strings = MyTrackerSink.ToStrings(parameters);

            Assert.AreEqual("Level_03", strings["level"]);
            Assert.AreEqual("3", strings["stars"]);
            Assert.AreEqual("12.5", strings["time"]);
            Assert.AreEqual("true", strings["record"]);
            Assert.AreEqual("", strings["none"]);
            Assert.AreEqual("", strings["nan"]);
        }

        [Test]
        public void MyTrackerParameters_FloatsAreInvariant()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
                Assert.AreEqual("59.25", MyTrackerSink.ToStrings(new Dictionary<string, object> { ["fps"] = 59.25f })["fps"]);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Test]
        public void MyTrackerParameters_ClippedTo64_BufferReused()
        {
            var buffer = new Dictionary<string, string> { ["old"] = "x" };
            var key = new string('k', 70);
            var value = new string('v', 100);

            var strings = MyTrackerSink.ToStrings(new Dictionary<string, object> { [key] = value }, buffer);

            Assert.AreSame(buffer, strings);
            Assert.AreEqual(1, strings.Count);
            Assert.AreEqual(new string('v', MyTrackerSink.MaxLength), strings[new string('k', MyTrackerSink.MaxLength)]);
        }

        [Test]
        public void Platform_ShowsSupportAndPrivacy_NoCloudOrLanguage()
        {
            var platform = new RuStorePlatform();

            Assert.AreEqual(Contacts.SupportEmail, platform.Traits.SupportEmail);
            Assert.AreEqual(Contacts.PrivacyPolicyUrl, platform.Traits.PrivacyPolicyUrl);
            StringAssert.StartsWith("https://", platform.Traits.PrivacyPolicyUrl);
            Assert.IsNull(platform.CloudSave);
            Assert.IsNull(platform.Language);
            Assert.IsFalse(platform.SupportsSignIn);
        }

        [Test]
        public void AdSlots_WithoutKeys_AreVkDemoSlots()
        {
            if (!RuStoreKeys.DemoAds) Assert.Ignore("Real ad slots are set.");
            Assert.AreEqual(RuStoreKeys.DemoRewardedSlotId, RuStoreKeys.Rewarded);
            Assert.AreEqual(RuStoreKeys.DemoInterstitialSlotId, RuStoreKeys.Interstitial);
        }
    }
}
