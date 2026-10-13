using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Levels;
using Maze.Application.Platform;
using Maze.Application.Save;
using Maze.Core.Level;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Progress
{
    /// <summary>Ads: the interstitial policy, rewarded results, analytics of every ad; map layers bought per level.</summary>
    public class AdsTests
    {
        private LevelCatalog _catalogAsset;
        private FakeProvider _provider;
        private RecordingSink _sink;
        private AnalyticsService _analytics;
        private AdsService _ads;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _catalogAsset = ScriptableObject.CreateInstance<LevelCatalog>();
            for (var i = 1; i <= 6; i++) _catalogAsset.AddOrUpdate("L" + i, "Levels/L" + i, "L" + i);
            _provider = new FakeProvider();
            _sink = new RecordingSink();
            _analytics = new AnalyticsService(new IAnalyticsSink[] { _sink });
            _ads = new AdsService(_provider, _analytics, new Catalog(_catalogAsset)) { Clock = () => _now };
            _now = 1000f;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_catalogAsset);

        private void BeforeLevel(string id, bool fromMenu = false) =>
            _ads.ShowBeforeLevelAsync(id, fromMenu, CancellationToken.None).GetAwaiter().GetResult();

        [Test]
        public void Interstitial_NotOnFirstLevels_NotTooOften_RewardedRestartsTheWait()
        {
            BeforeLevel("L1");
            BeforeLevel("L3");
            BeforeLevel("dev_level");
            Assert.AreEqual(0, _provider.Shown.Count, "Not before levels 1–3, not before development levels.");

            BeforeLevel("L4");
            Assert.AreEqual(1, _provider.Shown.Count);
            _now += 30f;
            BeforeLevel("L5");
            Assert.AreEqual(1, _provider.Shown.Count, "Too soon after the last ad.");

            _now += 80f;
            Assert.IsTrue(_ads.ShowRewardedAsync("route_hint", CancellationToken.None).GetAwaiter().GetResult());
            _now += 30f;
            BeforeLevel("L5");
            Assert.AreEqual(2, _provider.Shown.Count, "A rewarded ad restarts the wait: only it was shown.");

            _now += 91f;
            BeforeLevel("L5");
            Assert.AreEqual(3, _provider.Shown.Count);
            CollectionAssert.AreEqual(new[] { AdKind.Interstitial, AdKind.Rewarded, AdKind.Interstitial }, _provider.Shown);
            Assert.AreEqual(3, _sink.Events.FindAll(e => e == AnalyticsEvents.Ad).Count, "Every ad is reported.");
        }

        [Test]
        public void Rewarded_ClosedEarlyOrFailed_GivesNoReward()
        {
            _provider.Next = AdResult.Closed;
            Assert.IsFalse(_ads.ShowRewardedAsync("map_zombies", CancellationToken.None).GetAwaiter().GetResult());
            _provider.Next = AdResult.Failed;
            Assert.IsFalse(_ads.ShowRewardedAsync("map_zombies", CancellationToken.None).GetAwaiter().GetResult());
            _provider.Throw = true;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // The SDK error is logged.
            Assert.IsFalse(_ads.ShowRewardedAsync("map_zombies", CancellationToken.None).GetAwaiter().GetResult(),
                "A crashing SDK is a failed ad, not a crash of the game.");
            Assert.IsFalse(_ads.IsShowing);
            Assert.IsFalse(AudioListener.pause, "Sound is back after the ad.");
        }

        [Test]
        public void AdsSwitchedOffBySite_GiveTheRewardForFree()
        {
            _provider.Next = AdResult.Disabled;
            Assert.IsTrue(_ads.ShowRewardedAsync("route_hint", CancellationToken.None).GetAwaiter().GetResult(),
                "Basic Launch: the rewarded button still works.");
            Assert.AreEqual(AdResult.Rewarded,
                _ads.ShowRewardedResultAsync("map_keys", CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void Interstitial_BetweenLevelsOnly_WhenTheSiteSaysSo()
        {
            BeforeLevel("L4", fromMenu: true);
            Assert.AreEqual(1, _provider.Shown.Count, "No such rule: from the menu too.");

            _provider.BetweenLevelsOnly = true;
            _now += 1000f;
            BeforeLevel("L4", fromMenu: true);
            Assert.AreEqual(1, _provider.Shown.Count, "Not on navigation from the menu.");
            BeforeLevel("L4");
            Assert.AreEqual(2, _provider.Shown.Count, "After a level ended: yes.");
        }

        [Test]
        public void Interstitial_AtEveryBreak_WhenTheSiteKeepsTheFrequency()
        {
            _ads.ShowOnMenuReturnAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(0, _provider.Shown.Count, "Our policy: no ad on the way back to the menu.");

            _provider.ManagesFrequency = true;
            BeforeLevel("L1", fromMenu: true);
            BeforeLevel("L2");
            _ads.ShowOnMenuReturnAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(3, _provider.Shown.Count,
                "GameDistribution: the first PLAY, every next level and every return to the menu; the SDK skips what is too soon.");
        }

        [Test]
        public void Sound_GoesOff_OnlyWhenTheAdReallyStarts()
        {
            _provider.CallStarted = false;
            _provider.CheckSound = () => Assert.IsFalse(AudioListener.pause, "Requested, not started: the game still sounds.");
            _ads.ShowRewardedAsync("route_hint", CancellationToken.None).GetAwaiter().GetResult();

            _provider.CallStarted = true;
            _provider.CheckSound = () => Assert.IsTrue(AudioListener.pause, "Playing: silent.");
            _ads.ShowRewardedAsync("route_hint", CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsFalse(AudioListener.pause, "Sound is back after the ad.");
        }

        [Test]
        public void MapLayers_BoughtPerLevel_KeptInTheSave()
        {
            var storage = new MemoryStorage();
            var save = new SaveService(storage);
            save.Load();
            var progress = new ProgressService(save, new Catalog(_catalogAsset));
            Assert.IsFalse(progress.IsMapLayerUnlocked("L1", MapUnlock.Zombies));
            progress.UnlockMapLayer("L1", MapUnlock.Zombies);
            progress.UnlockMapLayer("L1", MapUnlock.Keys);
            Assert.IsTrue(progress.IsMapLayerUnlocked("L1", MapUnlock.Zombies | MapUnlock.Keys));
            Assert.IsFalse(progress.IsMapLayerUnlocked("L1", MapUnlock.Weapons));
            Assert.IsFalse(progress.IsMapLayerUnlocked("L2", MapUnlock.Zombies), "Another level: not bought.");

            var reloaded = new SaveService(storage);
            reloaded.Load();
            Assert.IsTrue(new ProgressService(reloaded, new Catalog(_catalogAsset)).IsMapLayerUnlocked("L1", MapUnlock.Keys),
                "Kept for replays of the level.");
        }

        private sealed class FakeProvider : IAdProvider
        {
            public readonly List<AdKind> Shown = new List<AdKind>();
            public AdResult? Next;
            public bool Throw;
            public bool BetweenLevelsOnly;
            public bool CallStarted = true;
            public System.Action CheckSound;

            public string Name => "Fake";

            public bool InterstitialBetweenLevelsOnly => BetweenLevelsOnly;

            public bool ManagesFrequency;

            public bool ManagesInterstitialFrequency => ManagesFrequency;

            public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;

            public UniTask<AdResult> ShowAsync(AdKind kind, string placement, System.Action started, CancellationToken cancellation)
            {
                if (Throw) throw new System.InvalidOperationException("SDK crashed.");
                if (CallStarted) started();
                CheckSound?.Invoke();
                Shown.Add(kind);
                return UniTask.FromResult(Next ?? (kind == AdKind.Rewarded ? AdResult.Rewarded : AdResult.Closed));
            }
        }

        private sealed class MemoryStorage : ISaveStorage
        {
            private string _json;
            public string Read() => _json;
            public void Write(string json) => _json = json;
        }

        private sealed class RecordingSink : IAnalyticsSink
        {
            public readonly List<string> Events = new List<string>();
            public string Name => "Recording";
            public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;
            public void Send(string eventName, IReadOnlyDictionary<string, object> parameters) => Events.Add(eventName);
        }

        private sealed class Catalog : ILevelCatalog
        {
            private readonly LevelCatalog _asset;
            public Catalog(LevelCatalog asset) => _asset = asset;
            public IReadOnlyList<LevelCatalogEntry> Levels => _asset.Levels;
            public IReadOnlyList<LevelCatalogEntry> DevLevels => System.Array.Empty<LevelCatalogEntry>();
            public LevelCatalogEntry Tutorial => _asset.Tutorial;
            public LevelCatalogEntry Find(string levelId) => _asset.Find(levelId);
        }
    }
}
