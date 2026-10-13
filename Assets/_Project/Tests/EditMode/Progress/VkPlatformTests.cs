using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Platform;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Progress
{
    public sealed class VkPlatformTests
    {
        [Test]
        public void SlottedSave_RoundTrip_SplitsIntoParts()
        {
            var storage = new MemoryKeyValues();
            var json = Json(2500);
            new SlottedCloudSave(storage).WriteAsync(json, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("A:3", storage.Values[SlottedCloudSave.PointerKey]);
            foreach (var pair in storage.Values)
                Assert.LessOrEqual(pair.Value.Length, SlottedCloudSave.PartSize, pair.Key);
            Assert.AreEqual(json, new SlottedCloudSave(storage).ReadAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void SlottedSave_Empty_ReadsNull()
        {
            Assert.IsNull(new SlottedCloudSave(new MemoryKeyValues()).ReadAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void SlottedSave_NextWrite_GoesToOtherSlot_AndSkipsUnchanged()
        {
            var storage = new MemoryKeyValues();
            var save = new SlottedCloudSave(storage);
            save.WriteAsync(Json(1500), CancellationToken.None).GetAwaiter().GetResult();
            save.WriteAsync(Json(800), CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("B:1", storage.Values[SlottedCloudSave.PointerKey]);

            var calls = storage.SetCalls;
            save.WriteAsync(Json(800), CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(calls, storage.SetCalls, "an unchanged save is not written again");

            // Read by a new session: the slot in use is known, the next write goes to the other one.
            var next = new SlottedCloudSave(storage);
            Assert.AreEqual(Json(800), next.ReadAsync(CancellationToken.None).GetAwaiter().GetResult());
            next.WriteAsync(Json(900), CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("A:1", storage.Values[SlottedCloudSave.PointerKey]);
        }

        [Test]
        public void SlottedSave_WriteCutShort_KeepsOldSave()
        {
            var storage = new MemoryKeyValues();
            var old = Json(1200);
            var save = new SlottedCloudSave(storage);
            save.WriteAsync(old, CancellationToken.None).GetAwaiter().GetResult();

            // The tab closes after the first part of the next save: the pointer still names the old slot.
            storage.FailAfter = storage.SetCalls + 1;
            Assert.Throws<System.InvalidOperationException>(() =>
                save.WriteAsync(Json(2200), CancellationToken.None).GetAwaiter().GetResult());
            storage.FailAfter = int.MaxValue;

            Assert.AreEqual(old, new SlottedCloudSave(storage).ReadAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void SlottedSave_MissingPart_Throws()
        {
            var storage = new MemoryKeyValues();
            new SlottedCloudSave(storage).WriteAsync(Json(1500), CancellationToken.None).GetAwaiter().GetResult();
            storage.Values.Remove(SlottedCloudSave.PartKey('A', 1));
            Assert.Throws<System.FormatException>(() =>
                new SlottedCloudSave(storage).ReadAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        [TestCase("A:3", true, 'A', 3)]
        [TestCase("B:12", true, 'B', 12)]
        [TestCase("C:1", false, '\0', 0)]
        [TestCase("A:0", false, 'A', 0)]
        [TestCase("A3", false, '\0', 0)]
        [TestCase("", false, '\0', 0)]
        public void SlottedSave_ParsesPointer(string pointer, bool valid, char slot, int count)
        {
            Assert.AreEqual(valid, SlottedCloudSave.TryParsePointer(pointer, out var parsedSlot, out var parsedCount));
            if (!valid) return;
            Assert.AreEqual(slot, parsedSlot);
            Assert.AreEqual(count, parsedCount);
        }

        [TestCase("ru", "ru")]
        [TestCase("kz", "kk")]
        [TestCase("ua", "uk")]
        [TestCase("en", "en")]
        [TestCase("", null)]
        [TestCase(null, null)]
        public void Language_FromVk(string vk, string game) => Assert.AreEqual(game, VkPlatform.ToGameLanguage(vk));

        [Test]
        public void Banner_OnDesktopOnly_UntilCheckedOnPhones()
        {
            Assert.IsTrue(VkPlatform.ShowsBanner("desktop_web"));
            Assert.AreEqual(VkPlatform.MobileBanner, VkPlatform.ShowsBanner("mobile_android"));
            Assert.IsFalse(VkPlatform.ShowsBanner(null));
        }

        [Test]
        public void PageInsets_NarrowSafeArea()
        {
            try
            {
                var full = new UnityEngine.Rect(0f, 0f, 2340f, 1080f);
                WebPage.SetInsets(0, 0, 0, 0);
                Assert.AreEqual(full, WebPage.SafeArea(full, 2340, 1080));

                // A system bar on the right (the VK app in landscape), a status bar on top.
                WebPage.SetInsets(0, 60, 120, 0);
                Assert.AreEqual(UnityEngine.Rect.MinMaxRect(0f, 0f, 2220f, 1020f), WebPage.SafeArea(full, 2340, 1080));

                // The device's own safe area (a notch on the left) stays when it is narrower.
                var notch = UnityEngine.Rect.MinMaxRect(90f, 0f, 2340f, 1080f);
                Assert.AreEqual(UnityEngine.Rect.MinMaxRect(90f, 0f, 2220f, 1020f), WebPage.SafeArea(notch, 2340, 1080));
            }
            finally
            {
                WebPage.SetInsets(0, 0, 0, 0);
            }
        }

        [Test]
        public void Storage_ParsesReply()
        {
            var values = VkPlatform.ParseStorage("{\"keys\":[{\"key\":\"save_slot\",\"value\":\"A:1\"},{\"key\":\"save_A0\",\"value\":\"{\\\"a\\\":1}\"},{\"key\":\"none\",\"value\":\"\"}]}");
            Assert.AreEqual("A:1", values["save_slot"]);
            Assert.AreEqual("{\"a\":1}", values["save_A0"]);
            Assert.AreEqual("", values["none"]);
            Assert.AreEqual(0, VkPlatform.ParseStorage("").Count);
        }

        /// <summary>A JSON-like string of the given length with quotes in it (parts must not cut it wrongly).</summary>
        private static string Json(int length)
        {
            var text = new StringBuilder(length);
            while (text.Length < length) text.Append("{\"id\":\"Level_").Append(text.Length % 97).Append("\"},");
            return text.ToString(0, length);
        }

        private sealed class MemoryKeyValues : IKeyValueStorage
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            public int SetCalls;
            public int FailAfter = int.MaxValue;

            public UniTask<IReadOnlyDictionary<string, string>> GetAsync(IReadOnlyList<string> keys, CancellationToken cancellation)
            {
                var result = new Dictionary<string, string>();
                foreach (var key in keys) result[key] = Values.TryGetValue(key, out var value) ? value : "";
                return UniTask.FromResult<IReadOnlyDictionary<string, string>>(result);
            }

            public UniTask SetAsync(string key, string value, CancellationToken cancellation)
            {
                if (SetCalls >= FailAfter) throw new System.InvalidOperationException("The tab is closed.");
                SetCalls++;
                Values[key] = value;
                return UniTask.CompletedTask;
            }
        }
    }
}
