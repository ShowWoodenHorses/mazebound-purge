using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Levels;
using Maze.Application.Services;
using Maze.Core.Common;
using UnityEngine;

namespace Maze.Application.Platform
{
    public enum AdKind
    {
        Interstitial = 0,
        Rewarded = 1,
    }

    public enum AdResult
    {
        /// <summary>A rewarded ad was watched to the end: give the reward.</summary>
        Rewarded = 0,

        /// <summary>Shown and closed (an interstitial; a rewarded one closed early — no reward).</summary>
        Closed = 1,

        Failed = 2,

        /// <summary>Not loaded / not allowed now (the platform's own limits).</summary>
        Unavailable = 3,

        /// <summary>
        /// The site has ads switched off for the game (CrazyGames Basic Launch). A rewarded button must still work
        /// there: the reward is given without an ad.
        /// </summary>
        Disabled = 4,
    }

    /// <summary>
    /// The ad SDK of a platform (Yandex Games SDK, VK Ad Network…). Shows one ad and reports how it ended; knows
    /// nothing about the game. A new platform = a new provider.
    /// </summary>
    public interface IAdProvider
    {
        string Name { get; }

        UniTask InitializeAsync(CancellationToken cancellation);

        /// <summary>
        /// The site allows an interstitial only between levels (after a level ended), not when a level is started from
        /// the menu (CrazyGames: no ads on navigation).
        /// </summary>
        bool InterstitialBetweenLevelsOnly { get; }

        /// <summary>
        /// The site's SDK limits interstitials itself and wants one at every break: each level start (the first PLAY
        /// included) and each return to the menu after a level (GameDistribution: pre-roll and mid-roll). The game's
        /// <see cref="AdPolicy"/> is not applied then.
        /// </summary>
        bool ManagesInterstitialFrequency => false;

        /// <param name="started">Called when the ad really starts playing (the game goes silent then, not at the request).</param>
        UniTask<AdResult> ShowAsync(AdKind kind, string placement, Action started, CancellationToken cancellation);
    }

    /// <summary>
    /// How often an interstitial may show before a level: not before the first <see cref="FirstLevelWithAds"/> − 1
    /// levels, not for development levels, not sooner than <see cref="Interval"/> seconds after the last ad of any
    /// kind (a rewarded one restarts the wait). Times are real seconds (ads run while the game is paused).
    /// </summary>
    public sealed class AdPolicy
    {
        public float Interval = 90f;

        /// <summary>1-based catalog number of the first level that may get an interstitial before it.</summary>
        public int FirstLevelWithAds = 4;

        private float _lastAd = float.NegativeInfinity;

        /// <param name="levelNumber">1-based catalog number; 0 = not a game level (development).</param>
        public bool AllowsInterstitial(int levelNumber, float now) =>
            levelNumber >= FirstLevelWithAds && now - _lastAd >= Interval;

        public void OnAdShown(float now) => _lastAd = now;
    }

    /// <summary>
    /// Ads of the game: rewarded ads for hints and map layers, an interstitial before a level start (retry, next,
    /// from the menu) by <see cref="AdPolicy"/> — or, where the site keeps the frequency itself
    /// (<see cref="IAdProvider.ManagesInterstitialFrequency"/>), at every level start and every return to the menu after
    /// a level. From the request to the end the ad is <see cref="IsShowing"/> (the game
    /// blocks progress); the sound is paused only while the ad really plays (<see cref="SoundSuspension"/>).
    /// Reports every ad to analytics. Failures never break the flow: no ad = no reward / just go on. Ads switched off
    /// by the site (<see cref="AdResult.Disabled"/>) give rewards for free.
    /// </summary>
    public sealed class AdsService : IApplicationService
    {
        private readonly IAdProvider _provider;
        private readonly AnalyticsService _analytics;
        private readonly ILevelCatalog _catalog;
        private readonly SoundSuspension _sound;
        private readonly AdPolicy _policy = new AdPolicy();

        public AdsService(IAdProvider provider, AnalyticsService analytics, ILevelCatalog catalog, SoundSuspension sound = null)
        {
            _sound = sound ?? new SoundSuspension();
            _provider = provider;
            _analytics = analytics;
            _catalog = catalog;
        }

        public string Name => "Ads";

        public AdPolicy Policy => _policy;

        public bool IsShowing { get; private set; }

        /// <summary>Real time source (tests replace it).</summary>
        public Func<float> Clock { get; set; } = () => Time.realtimeSinceStartup;

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            try
            {
                await _provider.InitializeAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Analytics, e, $"Ad provider '{_provider.Name}' failed to initialize");
            }
        }

        /// <summary>Shows a rewarded ad; true when it was watched and the reward is due.</summary>
        public async UniTask<bool> ShowRewardedAsync(string placement, CancellationToken cancellation)
        {
            var result = await ShowAsync(AdKind.Rewarded, placement, cancellation);
            return result == AdResult.Rewarded;
        }

        /// <summary>
        /// Shows a rewarded ad and tells how it went: <see cref="AdResult.Closed"/> — the player closed it early,
        /// <see cref="AdResult.Failed"/> / <see cref="AdResult.Unavailable"/> — no ad (worth telling the player: an ad
        /// blocker, no fill, a network error).
        /// </summary>
        public UniTask<AdResult> ShowRewardedResultAsync(string placement, CancellationToken cancellation) =>
            ShowAsync(AdKind.Rewarded, placement, cancellation);

        /// <summary>Before a level starts: an interstitial when the policy (and the site) allows it.</summary>
        /// <param name="fromMenu">The level is started from the menu, not after another level ended.</param>
        public async UniTask ShowBeforeLevelAsync(string levelId, bool fromMenu, CancellationToken cancellation)
        {
            if (fromMenu && _provider.InterstitialBetweenLevelsOnly) return;
            if (!_provider.ManagesInterstitialFrequency && !_policy.AllowsInterstitial(LevelNumber(levelId), Clock())) return;
            await ShowAsync(AdKind.Interstitial, "level_start", cancellation);
        }

        /// <summary>Back to the menu after a level ended: an interstitial only where the site wants one there.</summary>
        public async UniTask ShowOnMenuReturnAsync(CancellationToken cancellation)
        {
            if (!_provider.ManagesInterstitialFrequency) return;
            await ShowAsync(AdKind.Interstitial, "menu_return", cancellation);
        }

        private async UniTask<AdResult> ShowAsync(AdKind kind, string placement, CancellationToken cancellation)
        {
            if (IsShowing) return AdResult.Unavailable;
            IsShowing = true;
            AdResult result;
            try
            {
                result = await _provider.ShowAsync(kind, placement, OnAdStarted, cancellation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Analytics, e, $"Ad '{placement}' failed");
                result = AdResult.Failed;
            }
            finally
            {
                _sound.Set(SoundSuspendReason.Ad, false);
                IsShowing = false;
            }

            if (result == AdResult.Rewarded || result == AdResult.Closed) _policy.OnAdShown(Clock());
            var parameters = _analytics.Begin();
            parameters["kind"] = kind == AdKind.Rewarded ? "rewarded" : "interstitial";
            parameters["placement"] = placement;
            parameters["result"] = result.ToString().ToLowerInvariant();
            _analytics.Send(AnalyticsEvents.Ad);
            if (result == AdResult.Disabled && kind == AdKind.Rewarded) return AdResult.Rewarded; // the site has no ads now
            return result;
        }

        private void OnAdStarted()
        {
            if (IsShowing) _sound.Set(SoundSuspendReason.Ad, true);
        }

        private int LevelNumber(string levelId)
        {
            var levels = _catalog.Levels;
            for (var i = 0; i < levels.Count; i++)
                if (string.Equals(levels[i].LevelId, levelId, StringComparison.Ordinal))
                    return i + 1;
            return 0;
        }
    }

    /// <summary>
    /// Stand-in provider (editor, development builds, platforms without an ad SDK yet): every ad "shows" at once —
    /// a rewarded one is always watched — and is logged.
    /// </summary>
    public sealed class SimulatedAdProvider : IAdProvider
    {
        public string Name => "Simulated";

        public bool InterstitialBetweenLevelsOnly => false;

        public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;

        public UniTask<AdResult> ShowAsync(AdKind kind, string placement, Action started, CancellationToken cancellation)
        {
            started?.Invoke();
            GameLog.Info(LogChannel.Analytics, $"Simulated {kind} ad '{placement}'.");
            return UniTask.FromResult(kind == AdKind.Rewarded ? AdResult.Rewarded : AdResult.Closed);
        }
    }
}
