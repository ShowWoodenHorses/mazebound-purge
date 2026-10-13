using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using Mycom.Target.Unity.Ads;
using Mycom.Target.Unity.Common;

namespace Maze.Application.Platform
{
    /// <summary>
    /// Ads of the Android build: VK Ad Network (ex. myTarget, plugin in <c>Assets/Plugins/MyTarget</c>), slots from
    /// <see cref="RuStoreKeys"/>. One rewarded and one interstitial ad are kept loaded ahead (an ad object shows once:
    /// a new one is loaded after each show; a failed load is retried later, longer and longer). The player's rewarded
    /// button waits a few seconds for an ad still loading; an interstitial that is not ready is skipped. The ad's own
    /// activity covers the game: Unity is paused meanwhile and the SDK's callbacks arrive when it resumes (the plugin
    /// queues them for the main thread, one per frame). <c>started</c> is the ad really on screen.
    /// </summary>
    public sealed class VkAdsProvider : IAdProvider, IDisposable
    {
        /// <summary>How long the player's rewarded button waits for an ad that is still loading.</summary>
        public static readonly TimeSpan RewardedWait = TimeSpan.FromSeconds(4);

        /// <summary>Seconds before loading again after the 1st, 2nd… failure in a row (no fill, no network).</summary>
        private static readonly int[] RetryDelays = { 10, 30, 60, 120 };

        /// <summary>A load the SDK never answers (an exception inside it) counts as failed after this.</summary>
        private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(60);

        /// <summary>Frames the result waits after the dismissal: the plugin delivers one queued callback per frame.</summary>
        private const int CallbackFrames = 3;

        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Slot _rewarded = new Slot(AdKind.Rewarded, RuStoreKeys.Rewarded);
        private readonly Slot _interstitial = new Slot(AdKind.Interstitial, RuStoreKeys.Interstitial);
        private bool _initialized;

        public string Name => "VkAds";

        public bool InterstitialBetweenLevelsOnly => false;

        public UniTask InitializeAsync(CancellationToken cancellation)
        {
            if (RuStoreKeys.DemoAds)
                GameLog.Warning(LogChannel.Analytics, "VK Ads: demo slots (RuStoreKeys) — test ads, no income.");
            MyTargetManager.DebugMode = UnityEngine.Debug.isDebugBuild;
            MyTargetManager.InitSdk();
            _initialized = true;
            Load(_rewarded);
            Load(_interstitial);
            return UniTask.CompletedTask;
        }

        public async UniTask<AdResult> ShowAsync(AdKind kind, string placement, Action started, CancellationToken cancellation)
        {
            var slot = kind == AdKind.Rewarded ? _rewarded : _interstitial;
            if (!slot.Ready)
            {
                Load(slot);
                // The player asked for the reward: worth a short wait. An interstitial is never waited for.
                if (kind == AdKind.Rewarded && slot.Loading)
                    await UniTask.WaitUntil(() => !slot.Loading, cancellationToken: cancellation)
                        .TimeoutWithoutException(RewardedWait, DelayType.Realtime);
                if (!slot.Ready) return AdResult.Unavailable;
            }

            var interstitial = slot.Interstitial;
            var rewardedAd = slot.Rewarded;
            slot.Interstitial = null;
            slot.Rewarded = null;
            slot.Shown = true;
            var completion = new UniTaskCompletionSource<AdResult>();
            var rewarded = false;
            try
            {
                if (rewardedAd != null)
                {
                    rewardedAd.AdDisplayed += (_, _) => started?.Invoke();
                    rewardedAd.AdRewarded += (_, _) => rewarded = true;
                    rewardedAd.AdDismissed += (_, _) => CompleteAfterCallbacks(completion, () => rewarded ? AdResult.Rewarded : AdResult.Closed);
                    rewardedAd.AdFailedToShow += (_, _) => Fail(completion, placement);
                    rewardedAd.Show();
                }
                else
                {
                    interstitial.AdDisplayed += (_, _) => started?.Invoke();
                    interstitial.AdDismissed += (_, _) => CompleteAfterCallbacks(completion, () => AdResult.Closed);
                    interstitial.AdFailedToShow += (_, _) => Fail(completion, placement);
                    interstitial.Show();
                }

                return await completion.Task.AttachExternalCancellation(cancellation);
            }
            finally
            {
                rewardedAd?.Dispose();
                interstitial?.Dispose();
                slot.Shown = false;
                Load(slot); // the next one, ready for the next request
            }
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            _rewarded.Clear();
            _interstitial.Clear();
        }

        /// <summary>
        /// The reward callback may come right after the dismissal (both are queued for the main thread when Unity
        /// resumes): the result is read a few frames later.
        /// </summary>
        private static void CompleteAfterCallbacks(UniTaskCompletionSource<AdResult> completion, Func<AdResult> result)
        {
            UniTask.DelayFrame(CallbackFrames, PlayerLoopTiming.Update).ContinueWith(() => completion.TrySetResult(result())).Forget();
        }

        private static void Fail(UniTaskCompletionSource<AdResult> completion, string placement)
        {
            GameLog.Warning(LogChannel.Analytics, $"VK Ads: '{placement}' failed to show.");
            completion.TrySetResult(AdResult.Failed);
        }

        private void Load(Slot slot)
        {
            if (!_initialized || slot.Ready || slot.Loading || slot.Shown || _lifetime.IsCancellationRequested) return;
            slot.Loading = true;
            if (slot.Kind == AdKind.Rewarded)
            {
                var ad = new RewardedAd(slot.SlotId);
                slot.Pending = ad;
                ad.AdLoadCompleted += (_, _) => OnLoaded(slot, ad);
                ad.AdLoadFailed += (_, e) => OnFailed(slot, ad, e);
                ad.Load();
            }
            else
            {
                var ad = new InterstitialAd(slot.SlotId);
                slot.Pending = ad;
                ad.AdLoadCompleted += (_, _) => OnLoaded(slot, ad);
                ad.AdLoadFailed += (_, e) => OnFailed(slot, ad, e);
                ad.Load();
            }

            WatchLoadAsync(slot, slot.Pending).Forget();
        }

        private async UniTaskVoid WatchLoadAsync(Slot slot, AbstractAd ad)
        {
            var cancelled = await UniTask.Delay(LoadTimeout, DelayType.Realtime, cancellationToken: _lifetime.Token)
                .SuppressCancellationThrow();
            if (!cancelled && slot.Pending == ad) OnFailed(slot, ad, null);
        }

        private void OnLoaded(Slot slot, AbstractAd ad)
        {
            if (slot.Pending != ad) return; // a cleared one
            slot.Pending = null;
            slot.Loading = false;
            slot.Failures = 0;
            if (_lifetime.IsCancellationRequested)
            {
                ad.Dispose();
                return;
            }

            slot.Rewarded = ad as RewardedAd;
            slot.Interstitial = ad as InterstitialAd;
        }

        private void OnFailed(Slot slot, AbstractAd ad, ErrorEventArgs error)
        {
            if (slot.Pending != ad) return;
            slot.Pending = null;
            slot.Loading = false;
            ad.Dispose();
            var delay = RetryDelays[Math.Min(slot.Failures, RetryDelays.Length - 1)];
            slot.Failures++;
            var reason = error == null ? "no answer from the SDK" : $"{error.Code}: {error.Message}";
            GameLog.Info(LogChannel.Analytics, $"VK Ads: no {slot.Kind} ad ({reason}); again in {delay} s.");
            RetryAsync(slot, delay).Forget();
        }

        private async UniTaskVoid RetryAsync(Slot slot, int seconds)
        {
            var cancelled = await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime,
                cancellationToken: _lifetime.Token).SuppressCancellationThrow();
            if (!cancelled) Load(slot);
        }

        /// <summary>One loaded-ahead ad of a kind.</summary>
        private sealed class Slot
        {
            public readonly AdKind Kind;
            public readonly uint SlotId;
            public RewardedAd Rewarded;
            public InterstitialAd Interstitial;
            public AbstractAd Pending;
            public bool Loading;
            public bool Shown;
            public int Failures;

            public Slot(AdKind kind, uint slotId)
            {
                Kind = kind;
                SlotId = slotId;
            }

            public bool Ready => Rewarded != null || Interstitial != null;

            public void Clear()
            {
                Rewarded?.Dispose();
                Interstitial?.Dispose();
                Pending?.Dispose();
                Rewarded = null;
                Interstitial = null;
                Pending = null;
                Loading = false;
            }
        }
    }
}
