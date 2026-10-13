using System;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;

namespace Maze.Application.Platform
{
    /// <summary>
    /// Calls into the GameDistribution HTML5 SDK (<c>Plugins/WebGL/MazeGd.jslib</c>; the page sets <c>GD_OPTIONS</c>
    /// and loads the SDK itself, <c>WebPlatforms.Head</c>). Only a WebGL player has it: elsewhere every call throws, so
    /// the game works as <see cref="GenericPlatform"/>.
    /// </summary>
    public static class GdBridge
    {
        private static readonly WebCalls Calls = new WebCalls();

        /// <summary>The SDK reported SDK_READY: ads can be asked for.</summary>
        public static bool IsReady { get; private set; }

        /// <summary>The ad asked for has really started (the game goes silent).</summary>
        public static event Action AdStarted;

        /// <summary>The SDK paused the game for an ad of its own (true) and let it go on (false).</summary>
        public static event Action<bool> PauseRequested;

        public static async UniTask<WebReply> InitAsync(CancellationToken cancellation)
        {
            var reply = await Calls.Call(id => Native.Init(OnReply, id), cancellation);
            IsReady = reply.Code == 1;
            return reply;
        }

        /// <summary>Reply code = <see cref="AdResult"/>.</summary>
        public static UniTask<WebReply> ShowAdAsync(AdKind kind, CancellationToken cancellation) =>
            Calls.Call(id => Native.ShowAd(id, kind == AdKind.Rewarded ? 1 : 0), cancellation);

        private delegate void ReplyCallback(int id, int code, IntPtr text);

        [AOT.MonoPInvokeCallback(typeof(ReplyCallback))]
        private static void OnReply(int id, int code, IntPtr text)
        {
            if (id != 0)
            {
                Calls.Complete(id, code, WebCalls.Text(text));
                return;
            }

            // Events from the SDK: 1 / 2 — pause / go on (its own ad), 3 — the ad asked for started.
            switch (code)
            {
                case 1: PauseRequested?.Invoke(true); break;
                case 2: PauseRequested?.Invoke(false); break;
                case 3: AdStarted?.Invoke(); break;
            }
        }

        private static class Native
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            [DllImport("__Internal", EntryPoint = "MazeGd_Init")]
            public static extern void Init(ReplyCallback callback, int id);

            [DllImport("__Internal", EntryPoint = "MazeGd_ShowAd")]
            public static extern void ShowAd(int id, int rewarded);
#else
            private static Exception NoSdk() => new PlatformNotSupportedException("The GameDistribution SDK exists only in a WebGL build.");
            public static void Init(ReplyCallback callback, int id) => throw NoSdk();
            public static void ShowAd(int id, int rewarded) => throw NoSdk();
#endif
        }
    }

    /// <summary>
    /// GameDistribution (build symbol <c>MAZE_GAMEDISTRIBUTION</c>): the game runs on thousands of sites in GD's frame.
    /// The site gives no language (the system's is used), no saves (the browser's storage), no accounts and no loading
    /// or gameplay events; links and contacts are forbidden (default <see cref="PlatformTraits"/>). Only ads, and the
    /// SDK's pause around ads it shows itself.
    /// </summary>
    public sealed class GameDistributionPlatform : IGamePlatform
    {
        public static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(10);

        public string Name => "GameDistribution";

        public string Language => null;

        public ICloudSave CloudSave => null;

        public bool SupportsSignIn => false;

        public bool IsSignedIn => false;

        public PlatformTraits Traits => PlatformTraits.Default;

        public bool MuteAudio => false;

        public event Action<bool> PauseRequested;

        public event Action<bool> MuteAudioChanged { add { } remove { } }

        public event Action SignedIn { add { } remove { } }

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            var reply = await GdBridge.InitAsync(cancellation).Timeout(InitTimeout);
            if (reply.Code != 1) throw new InvalidOperationException("GameDistribution SDK: " + reply.Text);
            GdBridge.PauseRequested += pause => PauseRequested?.Invoke(pause);
        }

        public UniTask<bool> SignInAsync(CancellationToken cancellation) => UniTask.FromResult(false);

        public void LoadingFinished() { }
        public void GameplayStarted() { }
        public void GameplayStopped() { }
        public void HappyMoment() { }
        public void GameProgress(int percent) { }
    }

    /// <summary>
    /// GameDistribution ads: an interstitial (pre-roll / mid-roll) at every break — the SDK keeps the frequency and
    /// skips what is too soon — and rewarded ads (one is loaded ahead). The reward is given only for a rewarded ad
    /// watched to the end (SDK_REWARDED_WATCH_COMPLETE); no ad (no fill, an ad blocker) is
    /// <see cref="AdResult.Unavailable"/>.
    /// </summary>
    public sealed class GameDistributionAdProvider : IAdProvider
    {
        public string Name => "GameDistribution";

        public bool InterstitialBetweenLevelsOnly => false;

        public bool ManagesInterstitialFrequency => true;

        public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;

        public async UniTask<AdResult> ShowAsync(AdKind kind, string placement, Action started, CancellationToken cancellation)
        {
            if (!GdBridge.IsReady) return AdResult.Unavailable;
            WebReply reply;
            GdBridge.AdStarted += started;
            try
            {
                reply = await GdBridge.ShowAdAsync(kind, cancellation);
            }
            finally
            {
                GdBridge.AdStarted -= started;
            }

            if (!string.IsNullOrEmpty(reply.Text))
                GameLog.Info(LogChannel.Analytics, $"GameDistribution {kind} ad '{placement}': {reply.Text}");
            return Enum.IsDefined(typeof(AdResult), reply.Code) ? (AdResult)reply.Code : AdResult.Failed;
        }
    }
}
