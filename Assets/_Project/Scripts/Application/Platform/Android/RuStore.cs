using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Maze.Application.Platform
{
    /// <summary>
    /// Keys of the Android build for RuStore — the one place to set them; rebuild after a change. Empty means "not yet":
    /// MyTracker stays off (events only go to the log), ads use VK Ad Network's demo slots (test ads, no income).
    /// </summary>
    public static class RuStoreKeys
    {
        /// <summary>MyTracker SDK key (tracker.my.com → the app's settings).</summary>
        public const string MyTrackerSdkKey = "08865277942125933786";

        /// <summary>Rewarded ad slot id (VK Ad Network partner interface → the ad unit, <c>slot_id</c>); 0 = not yet.</summary>
        public const uint RewardedSlotId = 2088152;

        /// <summary>Interstitial ad slot id; 0 = not yet.</summary>
        public const uint InterstitialSlotId = 2088149;

        /// <summary>VK Ad Network's demo slots (its demo app): rewarded video and a video interstitial.</summary>
        public const uint DemoRewardedSlotId = 45101;

        public const uint DemoInterstitialSlotId = 10138;

        public static uint Rewarded => RewardedSlotId != 0 ? RewardedSlotId : DemoRewardedSlotId;

        public static uint Interstitial => InterstitialSlotId != 0 ? InterstitialSlotId : DemoInterstitialSlotId;

        /// <summary>The ads are VK's demo ones (no real slots yet).</summary>
        public static bool DemoAds => RewardedSlotId == 0 || InterstitialSlotId == 0;
    }

    /// <summary>
    /// The Android app from RuStore: no site SDK — the language is the system's, saves are local (<c>PlayerPrefs</c>);
    /// the store's rules: a support contact and the privacy policy in the settings. Ads — <see cref="VkAdsProvider"/>,
    /// analytics — <see cref="MyTrackerSink"/>.
    /// </summary>
    public sealed class RuStorePlatform : IGamePlatform
    {
        private static readonly PlatformTraits RuStoreTraits = new PlatformTraits
        {
            SupportEmail = Contacts.SupportEmail,
            PrivacyPolicyUrl = Contacts.PrivacyPolicyUrl,
        };

        public string Name => "RuStore";
        public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;
        public string Language => null;
        public ICloudSave CloudSave => null;
        public void LoadingFinished() { }
        public void GameplayStarted() { }
        public void GameplayStopped() { }
        public void HappyMoment() { }
        public void GameProgress(int percent) { }
        public bool SupportsSignIn => false;
        public bool IsSignedIn => false;
        public UniTask<bool> SignInAsync(CancellationToken cancellation) => UniTask.FromResult(false);
        public event Action<bool> PauseRequested { add { } remove { } }
        public PlatformTraits Traits => RuStoreTraits;
        public bool MuteAudio => false;
        public event Action<bool> MuteAudioChanged { add { } remove { } }
        public event Action SignedIn { add { } remove { } }
    }
}
