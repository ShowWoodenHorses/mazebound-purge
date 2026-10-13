using System;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;

namespace Maze.Application.Platform
{
    /// <summary>
    /// Calls into the CrazyGames HTML5 SDK v3 (<c>Plugins/WebGL/MazeCrazy.jslib</c>). Only a WebGL player on CrazyGames
    /// (or localhost, the SDK's local mode) has it: elsewhere every call throws, so the game works as
    /// <see cref="GenericPlatform"/>.
    /// </summary>
    public static class CrazyBridge
    {
        private static readonly WebCalls Calls = new WebCalls();

        /// <summary>The SDK answered <see cref="InitAsync"/> and works on this page.</summary>
        public static bool IsReady { get; private set; }

        /// <summary>The ad asked for has really started (the game goes silent).</summary>
        public static event Action AdStarted;

        /// <summary>The site's mute setting changed.</summary>
        public static event Action<bool> MuteAudioChanged;

        /// <summary>The player signed in through the site itself.</summary>
        public static event Action SignedIn;

        /// <summary>Starts the SDK; the reply text is the player's locale ("en-US").</summary>
        public static async UniTask<WebReply> InitAsync(CancellationToken cancellation)
        {
            var reply = await Calls.Call(id => Native.Init(OnReply, id), cancellation);
            IsReady = reply.Code == 1;
            return reply;
        }

        public static bool MuteAudio => IsReady && Native.MuteAudio() != 0;

        /// <summary>CrazyGames accounts work here (not when the game is embedded elsewhere).</summary>
        public static bool AccountAvailable => IsReady && Native.AccountAvailable() != 0;

        public static bool IsSignedIn => IsReady && Native.IsSignedIn() != 0;

        public static UniTask<WebReply> SignInAsync(CancellationToken cancellation) => Calls.Call(Native.SignIn, cancellation);

        public static UniTask<WebReply> GetDataAsync(CancellationToken cancellation) => Calls.Call(Native.GetData, cancellation);

        public static UniTask<WebReply> SetDataAsync(string json, CancellationToken cancellation) =>
            Calls.Call(id => Native.SetData(id, json), cancellation);

        /// <summary>Reply code = <see cref="AdResult"/>.</summary>
        public static UniTask<WebReply> ShowAdAsync(AdKind kind, CancellationToken cancellation) =>
            Calls.Call(id => Native.ShowAd(id, kind == AdKind.Rewarded ? 1 : 0), cancellation);

        public static void LoadingStop()
        {
            if (IsReady) Native.LoadingStop();
        }

        public static void Gameplay(bool started)
        {
            if (IsReady) Native.Gameplay(started ? 1 : 0);
        }

        public static void HappyTime()
        {
            if (IsReady) Native.HappyTime();
        }

        public static void GameProgress(int percent)
        {
            if (IsReady) Native.GameProgress(percent);
        }

        private delegate void ReplyCallback(int id, int code, IntPtr text);

        [AOT.MonoPInvokeCallback(typeof(ReplyCallback))]
        private static void OnReply(int id, int code, IntPtr text)
        {
            if (id != 0)
            {
                Calls.Complete(id, code, WebCalls.Text(text));
                return;
            }

            // Events from the site: 3 — an ad started, 4 / 5 — mute on / off, 6 — signed in through the site.
            switch (code)
            {
                case 3: AdStarted?.Invoke(); break;
                case 4: MuteAudioChanged?.Invoke(true); break;
                case 5: MuteAudioChanged?.Invoke(false); break;
                case 6: SignedIn?.Invoke(); break;
            }
        }

        private static class Native
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            [DllImport("__Internal", EntryPoint = "MazeCrazy_Init")]
            public static extern void Init(ReplyCallback callback, int id);

            [DllImport("__Internal", EntryPoint = "MazeCrazy_MuteAudio")]
            public static extern int MuteAudio();

            [DllImport("__Internal", EntryPoint = "MazeCrazy_AccountAvailable")]
            public static extern int AccountAvailable();

            [DllImport("__Internal", EntryPoint = "MazeCrazy_IsSignedIn")]
            public static extern int IsSignedIn();

            [DllImport("__Internal", EntryPoint = "MazeCrazy_SignIn")]
            public static extern void SignIn(int id);

            [DllImport("__Internal", EntryPoint = "MazeCrazy_GetData")]
            public static extern void GetData(int id);

            [DllImport("__Internal", EntryPoint = "MazeCrazy_SetData")]
            public static extern void SetData(int id, string json);

            [DllImport("__Internal", EntryPoint = "MazeCrazy_ShowAd")]
            public static extern void ShowAd(int id, int rewarded);

            [DllImport("__Internal", EntryPoint = "MazeCrazy_LoadingStop")]
            public static extern void LoadingStop();

            [DllImport("__Internal", EntryPoint = "MazeCrazy_Gameplay")]
            public static extern void Gameplay(int start);

            [DllImport("__Internal", EntryPoint = "MazeCrazy_HappyTime")]
            public static extern void HappyTime();

            [DllImport("__Internal", EntryPoint = "MazeCrazy_GameProgress")]
            public static extern void GameProgress(int percent);
#else
            private static Exception NoSdk() => new PlatformNotSupportedException("The CrazyGames SDK exists only in a WebGL build.");
            public static void Init(ReplyCallback callback, int id) => throw NoSdk();
            public static int MuteAudio() => 0;
            public static int AccountAvailable() => 0;
            public static int IsSignedIn() => 0;
            public static void SignIn(int id) => throw NoSdk();
            public static void GetData(int id) => throw NoSdk();
            public static void SetData(int id, string json) => throw NoSdk();
            public static void ShowAd(int id, int rewarded) => throw NoSdk();
            public static void LoadingStop() { }
            public static void Gameplay(int start) { }
            public static void HappyTime() { }
            public static void GameProgress(int percent) { }
#endif
        }
    }

    /// <summary>
    /// CrazyGames (build symbol <c>MAZE_CRAZYGAMES</c>): the player's locale, saves in the Data module (local for a
    /// guest; the SDK moves them to the account at sign-in and keeps them in sync), sign-in on request, the site's mute,
    /// loading/gameplay events and happy time. Site rules: a new player lands in gameplay; pauses for focus are the
    /// site's business.
    /// </summary>
    public sealed class CrazyGamesPlatform : IGamePlatform, ICloudSave
    {
        public static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(10);

        private static readonly PlatformTraits CrazyTraits = new PlatformTraits
        {
            StartsInGameplay = true,
            TracksFocusItself = true,
        };

        public string Name => "CrazyGames";

        public string Language { get; private set; }

        public ICloudSave CloudSave => CrazyBridge.IsReady ? this : null;

        public bool SupportsSignIn => CrazyBridge.AccountAvailable;

        public bool IsSignedIn => CrazyBridge.IsSignedIn;

        public PlatformTraits Traits => CrazyTraits;

        public bool MuteAudio => CrazyBridge.MuteAudio;

        /// <summary>CrazyGames never asks the game to pause (ads are asked for by the game).</summary>
        public event Action<bool> PauseRequested { add { } remove { } }

        public event Action<bool> MuteAudioChanged;

        public event Action SignedIn;

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            var reply = await CrazyBridge.InitAsync(cancellation).Timeout(InitTimeout);
            if (reply.Code != 1) throw new InvalidOperationException("CrazyGames SDK: " + reply.Text);
            Language = string.IsNullOrEmpty(reply.Text) ? null : reply.Text;
            CrazyBridge.MuteAudioChanged += mute => MuteAudioChanged?.Invoke(mute);
            CrazyBridge.SignedIn += () => SignedIn?.Invoke();
        }

        public async UniTask<bool> SignInAsync(CancellationToken cancellation)
        {
            var reply = await CrazyBridge.SignInAsync(cancellation);
            if (reply.Code != 1 && !string.IsNullOrEmpty(reply.Text))
                GameLog.Info(LogChannel.Bootstrap, "CrazyGames sign-in: " + reply.Text);
            return reply.Code == 1;
        }

        public void LoadingFinished() => CrazyBridge.LoadingStop();
        public void GameplayStarted() => CrazyBridge.Gameplay(true);
        public void GameplayStopped() => CrazyBridge.Gameplay(false);
        public void HappyMoment() => CrazyBridge.HappyTime();
        public void GameProgress(int percent) => CrazyBridge.GameProgress(percent);

        async UniTask<string> ICloudSave.ReadAsync(CancellationToken cancellation)
        {
            var reply = await CrazyBridge.GetDataAsync(cancellation);
            if (reply.Code != 1) throw new InvalidOperationException("CrazyGames data: " + reply.Text);
            return string.IsNullOrEmpty(reply.Text) ? null : reply.Text;
        }

        async UniTask ICloudSave.WriteAsync(string json, CancellationToken cancellation)
        {
            var reply = await CrazyBridge.SetDataAsync(json, cancellation);
            if (reply.Code != 1) throw new InvalidOperationException("CrazyGames data: " + reply.Text);
        }
    }

    /// <summary>
    /// CrazyGames ads: midgame (interstitial) and rewarded. The SDK keeps its own interval (3 min, rewarded included):
    /// a midgame asked for too soon is <see cref="AdResult.Unavailable"/>. In Basic Launch ads are off —
    /// <see cref="AdResult.Disabled"/>, rewards are given without them. Midgame ads only between levels (site rules).
    /// </summary>
    public sealed class CrazyGamesAdProvider : IAdProvider
    {
        public string Name => "CrazyGames";

        public bool InterstitialBetweenLevelsOnly => true;

        public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;

        public async UniTask<AdResult> ShowAsync(AdKind kind, string placement, Action started, CancellationToken cancellation)
        {
            if (!CrazyBridge.IsReady) return AdResult.Unavailable;
            WebReply reply;
            CrazyBridge.AdStarted += started;
            try
            {
                reply = await CrazyBridge.ShowAdAsync(kind, cancellation);
            }
            finally
            {
                CrazyBridge.AdStarted -= started;
            }

            if (!string.IsNullOrEmpty(reply.Text))
                GameLog.Info(LogChannel.Analytics, $"CrazyGames {kind} ad '{placement}': {reply.Text}");
            return Enum.IsDefined(typeof(AdResult), reply.Code) ? (AdResult)reply.Code : AdResult.Failed;
        }
    }
}
