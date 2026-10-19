using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Platform;
using Purge.Application.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Purge.Tests.EditMode.Progress
{
    public sealed class CloudSaveTests
    {
        [Test]
        public void CloudSave_WinsOverLocal_AndLocalFollowsIt()
        {
            var local = new MemoryStorage { Json = Json(kills: 3) };
            var cloud = new FakeCloud { Json = Json(kills: 7) };
            using var save = Start(local, cloud);

            Assert.AreEqual(7, save.Data.Stats.TotalKills);
            Assert.IsTrue(save.UsesCloud);
            StringAssert.Contains("\"TotalKills\":7", local.Json, "The browser's copy is replaced by the cloud save.");
            Assert.AreEqual(0, cloud.Writes);
        }

        [Test]
        public void EmptyCloud_GetsTheLocalSave()
        {
            var local = new MemoryStorage { Json = Json(kills: 3) };
            var cloud = new FakeCloud();
            using var save = Start(local, cloud);

            Assert.AreEqual(3, save.Data.Stats.TotalKills);
            Assert.AreEqual(1, cloud.Writes);
            StringAssert.Contains("\"TotalKills\":3", cloud.Json);
        }

        [Test]
        public void Saves_GoLocallyAtOnce_AndToTheCloudAsOneWrite()
        {
            var local = new MemoryStorage();
            var cloud = new FakeCloud();
            using var save = Start(local, cloud);
            save.CloudDelay = 3600f; // only the flush writes in this test

            save.Data.Stats.TotalKills = 1;
            save.Save();
            save.Data.Stats.TotalKills = 2;
            save.Save();

            Assert.AreEqual(2, local.Writes);
            Assert.AreEqual(0, cloud.Writes, "The cloud waits for more changes.");

            save.FlushCloudAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, cloud.Writes);
            StringAssert.Contains("\"TotalKills\":2", cloud.Json);

            save.FlushCloudAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, cloud.Writes, "Nothing new: nothing written.");
        }

        [Test]
        public void CloudThatFailedToAnswer_IsNotWritten()
        {
            LogAssert.ignoreFailingMessages = true; // the failure is logged as an error
            var local = new MemoryStorage { Json = Json(kills: 3) };
            var cloud = new FakeCloud { FailRead = true };
            using var save = Start(local, cloud);

            Assert.AreEqual(3, save.Data.Stats.TotalKills);
            Assert.IsFalse(save.UsesCloud);
            save.Save();
            save.FlushCloudAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, cloud.Writes);
        }

        [Test]
        public void NoCloud_OnlyLocal()
        {
            var local = new MemoryStorage { Json = Json(kills: 5) };
            using var save = new SaveService(local, new PlatformService(new GenericPlatform()));
            save.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(5, save.Data.Stats.TotalKills);
            Assert.IsFalse(save.UsesCloud);
        }

        [Test]
        public void SignIn_MergesTheAccountSave_IntoTheCurrentOne()
        {
            var local = new MemoryStorage();
            var guest = new SaveData { Stats = new SaveStats { TotalKills = 3 } };
            guest.Levels.Add(new LevelRecord { LevelId = "L1", CompletedMask = 1 });
            guest.Levels.Add(new LevelRecord { LevelId = "L2" });
            var cloud = new FakeCloud { Json = JsonUtility.ToJson(guest) };
            var site = new CloudPlatform(cloud);
            using var save = Start(local, cloud, site);
            var account = new AccountService(_platform, save);
            Assert.IsTrue(account.CanSignIn);

            // The account's save, made on another device.
            var other = new SaveData { Stats = new SaveStats { TotalKills = 5 } };
            other.Levels.Add(new LevelRecord { LevelId = "L1", CompletedMask = 4 });
            other.Levels.Add(new LevelRecord { LevelId = "L3", CompletedMask = 1 });
            other.Levels.Add(new LevelRecord { LevelId = "L4" });
            cloud.Json = JsonUtility.ToJson(other);
            var changes = 0;
            account.Changed += () => changes++;

            Assert.IsTrue(account.SignInAsync(CancellationToken.None).GetAwaiter().GetResult());

            Assert.AreEqual(5, save.Data.Stats.TotalKills);
            var l1 = save.Data.FindLevel("L1");
            Assert.AreEqual(5, l1.CompletedMask, "Difficulties of both.");
            Assert.IsNotNull(save.Data.FindLevel("L3"));
            Assert.IsNotNull(save.Data.FindLevel("L2"));
            Assert.IsNotNull(save.Data.FindLevel("L4"));
            StringAssert.Contains("\"TotalKills\":5", cloud.Json, "The merged save goes to the account.");
            StringAssert.Contains("\"LevelId\":\"L3\"", local.Json);
            Assert.IsFalse(account.CanSignIn, "Signed in: no more offer.");
            Assert.AreEqual(2, changes, "Started and ended.");
        }

        [Test]
        public void SignInClosed_ChangesNothing()
        {
            var local = new MemoryStorage { Json = Json(kills: 3) };
            var cloud = new FakeCloud { Json = Json(kills: 3) };
            var site = new CloudPlatform(cloud) { SignInResult = false };
            using var save = Start(local, cloud, site);
            var account = new AccountService(_platform, save);
            cloud.Json = Json(kills: 9);

            Assert.IsFalse(account.SignInAsync(CancellationToken.None).GetAwaiter().GetResult());

            Assert.AreEqual(3, save.Data.Stats.TotalKills);
            Assert.AreEqual(0, cloud.Writes);
            Assert.IsTrue(account.CanSignIn, "The offer stays.");
        }

        [Test]
        public void SignInThroughTheSite_AlsoMerges()
        {
            var local = new MemoryStorage();
            var cloud = new FakeCloud { Json = Json(kills: 2) };
            var site = new CloudPlatform(cloud);
            using var save = Start(local, cloud, site);
            var account = new AccountService(_platform, save);
            cloud.Json = Json(kills: 8);

            site.SignInOnTheSite();

            Assert.AreEqual(8, save.Data.Stats.TotalKills);
            Assert.IsFalse(account.CanSignIn);
        }

        [Test]
        public void SiteMute_IsPassedOn()
        {
            var site = new CloudPlatform(null) { MuteAudio = true };
            var platform = new PlatformService(site);
            var muted = new System.Collections.Generic.List<bool>();
            platform.MuteAudioChanged += muted.Add;
            platform.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            site.SetMute(false);
            CollectionAssert.AreEqual(new[] { true, false }, muted, "Muted from the start, then unmuted.");
        }

        [Test]
        public void GameProgress_IsCompletedLevelsInPercent_HundredOnlyForAll()
        {
            Assert.AreEqual(0, PlatformEvents.CompletedPercent(0, 30));
            Assert.AreEqual(3, PlatformEvents.CompletedPercent(1, 30));
            Assert.AreEqual(96, PlatformEvents.CompletedPercent(29, 30));
            Assert.AreEqual(100, PlatformEvents.CompletedPercent(30, 30));
            Assert.AreEqual(0, PlatformEvents.CompletedPercent(0, 0));
        }

        [Test]
        public void Merge_KeepsTheBestOfBoth()
        {
            var mine = new SaveData { Money = 100, Stats = new SaveStats { TotalKills = 10 } };
            mine.Levels.Add(new LevelRecord { LevelId = "A", CompletedMask = 1 });
            mine.Arsenal.Add(new OwnedItem { Id = "ak_74", Level = 2, Forks = 1, Mark = PoolMark.Favorite });
            mine.CharacterNodes.Add("shooter_1");
            mine.Run.Active = true;
            mine.Run.LevelId = "A";
            mine.Settings.MusicVolume = 0.3f;
            var theirs = new SaveData { Money = 250, Stats = new SaveStats { TotalKills = 4, RunsStarted = 9 } };
            theirs.Levels.Add(new LevelRecord { LevelId = "A", CompletedMask = 2 });
            theirs.Arsenal.Add(new OwnedItem { Id = "ak_74", Level = 5, Forks = 2 });
            theirs.Arsenal.Add(new OwnedItem { Id = "bat" });
            theirs.Survival.Add(new SurvivalRecord { LocationId = "forest", BestScore = 1200, BestWave = 7 });
            theirs.CharacterNodes.Add("shooter_1");
            theirs.CharacterNodes.Add("survivor_1");
            theirs.Run.Active = true;
            theirs.Run.LevelId = "B";
            theirs.Settings.MusicVolume = 1f;

            mine.MergeFrom(theirs);

            Assert.AreEqual(250, mine.Money, "The larger amount, not the sum.");
            Assert.AreEqual(10, mine.Stats.TotalKills);
            Assert.AreEqual(9, mine.Stats.RunsStarted);
            Assert.AreEqual(3, mine.Levels[0].CompletedMask);
            Assert.AreEqual(5, mine.FindItem("ak_74").Level, "The higher upgrade, with its forks.");
            Assert.AreEqual(2, mine.FindItem("ak_74").Forks);
            Assert.AreEqual(PoolMark.Favorite, mine.FindItem("ak_74").Mark, "The mark stays this device's.");
            Assert.IsNotNull(mine.FindItem("bat"));
            Assert.AreEqual(1200, mine.Survival[0].BestScore);
            CollectionAssert.AreEqual(new[] { "shooter_1", "survivor_1" }, mine.CharacterNodes);
            Assert.AreEqual("A", mine.Run.LevelId, "This device's run stays.");
            Assert.AreEqual(0.3f, mine.Settings.MusicVolume, "Settings stay these.");
        }

        [Test]
        public void SitePause_IsPassedOn()
        {
            var site = new CloudPlatform(null);
            var platform = new PlatformService(site);
            platform.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            bool? asked = null;
            platform.PauseRequested += pause => asked = pause;

            site.RequestPause(true);
            Assert.AreEqual(true, asked);
            site.RequestPause(false);
            Assert.AreEqual(false, asked);
        }

        [Test]
        public void MetrikaParameters_AreJson()
        {
            var parameters = new System.Collections.Generic.Dictionary<string, object>
            {
                ["level"] = 3, ["time"] = 12.5f, ["won"] = true, ["from"] = "menu \"x\"",
            };
            Assert.AreEqual("{\"level\":3,\"time\":12.5,\"won\":true,\"from\":\"menu \\\"x\\\"\"}",
                new YandexMetrikaSink().ToJson(parameters));
        }

        private PlatformService _platform;

        private SaveService Start(MemoryStorage local, FakeCloud cloud, CloudPlatform site)
        {
            _platform = new PlatformService(site);
            _platform.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            var save = new SaveService(local, _platform);
            save.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return save;
        }

        private static SaveService Start(MemoryStorage local, FakeCloud cloud)
        {
            var platform = new PlatformService(new CloudPlatform(cloud));
            platform.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            var save = new SaveService(local, platform);
            save.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return save;
        }

        private static string Json(int kills) => JsonUtility.ToJson(new SaveData { Stats = new SaveStats { TotalKills = kills } });

        private sealed class MemoryStorage : ISaveStorage
        {
            public string Json;
            public int Writes;

            public string Read() => Json;

            public void Write(string json)
            {
                Json = json;
                Writes++;
            }
        }

        private sealed class FakeCloud : ICloudSave
        {
            public string Json;
            public int Writes;
            public bool FailRead;

            public UniTask<string> ReadAsync(CancellationToken cancellation) =>
                FailRead ? UniTask.FromException<string>(new InvalidOperationException("No network.")) : UniTask.FromResult(Json);

            public UniTask WriteAsync(string json, CancellationToken cancellation)
            {
                Json = json;
                Writes++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class CloudPlatform : IGamePlatform
        {
            public CloudPlatform(ICloudSave cloud) => CloudSave = cloud;
            public string Name => "Test";
            public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;
            public string Language => null;
            public ICloudSave CloudSave { get; }
            public void LoadingFinished() { }
            public void GameplayStarted() { }
            public void GameplayStopped() { }
            public void HappyMoment() { }
            public void GameProgress(int percent) { }
            public bool SupportsSignIn => true;
            public bool IsSignedIn { get; set; }
            public bool SignInResult = true;

            public UniTask<bool> SignInAsync(CancellationToken cancellation)
            {
                IsSignedIn = SignInResult;
                return UniTask.FromResult(SignInResult);
            }

            public event Action<bool> PauseRequested;
            public void RequestPause(bool pause) => PauseRequested?.Invoke(pause);
            public PlatformTraits Traits => PlatformTraits.Default;
            public bool MuteAudio { get; set; }
            public event Action<bool> MuteAudioChanged;
            public void SetMute(bool mute) { MuteAudio = mute; MuteAudioChanged?.Invoke(mute); }
            public event Action SignedIn;

            public void SignInOnTheSite()
            {
                IsSignedIn = true;
                SignedIn?.Invoke();
            }
        }
    }
}
