using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;

namespace Maze.Application.Platform
{
    /// <summary>
    /// Calls into the Yandex Games SDK v2 (<c>Plugins/WebGL/MazeYandex.jslib</c>). Async calls get a request id and
    /// end with one reply (code + text). Only a WebGL player has the SDK: elsewhere every call throws, so a Yandex
    /// build started in the editor works as <see cref="GenericPlatform"/>.
    /// </summary>
    public static class YandexBridge
    {
        private static readonly WebCalls Calls = new WebCalls();

        /// <summary>The SDK answered <see cref="InitAsync"/>.</summary>
        public static bool IsReady { get; private set; }

        /// <summary>The site asks to stop (true) or lets the game go on (false).</summary>
        public static event Action<bool> PauseRequested;

        /// <summary>The ad asked for has really started (the game goes silent).</summary>
        public static event Action AdStarted;

        /// <summary>Starts the SDK and gets the player (saves); the reply text is the site's language.</summary>
        public static async UniTask<WebReply> InitAsync(CancellationToken cancellation)
        {
            var reply = await Calls.Call(id => Native.Init(OnReply, id), cancellation);
            IsReady = reply.Code == 1;
            return reply;
        }

        /// <summary>A player object (saves) is there; without one there is no cloud.</summary>
        public static bool HasPlayer => IsReady && Native.HasPlayer() != 0;

        public static bool IsAuthorized => IsReady && Native.IsAuthorized() != 0;

        public static UniTask<WebReply> SignInAsync(CancellationToken cancellation) => Calls.Call(Native.SignIn, cancellation);

        public static UniTask<WebReply> GetDataAsync(CancellationToken cancellation) => Calls.Call(Native.GetData, cancellation);

        public static UniTask<WebReply> SetDataAsync(string json, CancellationToken cancellation) =>
            Calls.Call(id => Native.SetData(id, json), cancellation);

        /// <summary>Reply code = <see cref="AdResult"/>.</summary>
        public static UniTask<WebReply> ShowAdAsync(AdKind kind, CancellationToken cancellation) =>
            Calls.Call(kind == AdKind.Rewarded ? (Action<int>)Native.ShowRewarded : Native.ShowFullscreen, cancellation);

        public static void LoadingReady()
        {
            if (IsReady) Native.LoadingReady();
        }

        public static void Gameplay(bool started)
        {
            if (IsReady) Native.Gameplay(started ? 1 : 0);
        }

        /// <summary>A Yandex Metrica goal (nothing without the counter on the page).</summary>
        public static void Goal(string name, string parametersJson)
        {
            if (IsReady) Native.Goal(name, parametersJson);
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

            // Events from the site: 1 — pause, 2 — resume, 3 — an ad started.
            if (code == 3) AdStarted?.Invoke();
            else PauseRequested?.Invoke(code == 1);
        }

        private static class Native
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            [DllImport("__Internal", EntryPoint = "MazeYandex_Init")]
            public static extern void Init(ReplyCallback callback, int id);

            [DllImport("__Internal", EntryPoint = "MazeYandex_HasPlayer")]
            public static extern int HasPlayer();

            [DllImport("__Internal", EntryPoint = "MazeYandex_IsAuthorized")]
            public static extern int IsAuthorized();

            [DllImport("__Internal", EntryPoint = "MazeYandex_SignIn")]
            public static extern void SignIn(int id);

            [DllImport("__Internal", EntryPoint = "MazeYandex_GetData")]
            public static extern void GetData(int id);

            [DllImport("__Internal", EntryPoint = "MazeYandex_SetData")]
            public static extern void SetData(int id, string json);

            [DllImport("__Internal", EntryPoint = "MazeYandex_ShowFullscreen")]
            public static extern void ShowFullscreen(int id);

            [DllImport("__Internal", EntryPoint = "MazeYandex_ShowRewarded")]
            public static extern void ShowRewarded(int id);

            [DllImport("__Internal", EntryPoint = "MazeYandex_LoadingReady")]
            public static extern void LoadingReady();

            [DllImport("__Internal", EntryPoint = "MazeYandex_Gameplay")]
            public static extern void Gameplay(int start);

            [DllImport("__Internal", EntryPoint = "MazeYandex_Goal")]
            public static extern void Goal(string name, string parameters);
#else
            private static Exception NoSdk() => new PlatformNotSupportedException("The Yandex Games SDK exists only in a WebGL build.");
            public static void Init(ReplyCallback callback, int id) => throw NoSdk();
            public static int HasPlayer() => 0;
            public static int IsAuthorized() => 0;
            public static void SignIn(int id) => throw NoSdk();
            public static void GetData(int id) => throw NoSdk();
            public static void SetData(int id, string json) => throw NoSdk();
            public static void ShowFullscreen(int id) => throw NoSdk();
            public static void ShowRewarded(int id) => throw NoSdk();
            public static void LoadingReady() { }
            public static void Gameplay(int start) { }
            public static void Goal(string name, string parameters) { }
#endif
        }
    }

    /// <summary>
    /// Yandex Games (build symbol <c>MAZE_YANDEX</c>): the site's language, saves in the player's data (they follow
    /// the account between devices and new build addresses), sign-in on request, loading/gameplay events, pauses
    /// asked by the site.
    /// </summary>
    public sealed class YandexPlatform : IGamePlatform, ICloudSave
    {
        /// <summary>A site that does not answer in this time is treated as absent (the game starts without it).</summary>
        public static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(10);

        public string Name => "Yandex";

        public string Language { get; private set; }

        public ICloudSave CloudSave => YandexBridge.HasPlayer ? this : null;

        public bool SupportsSignIn => YandexBridge.IsReady;

        public bool IsSignedIn => YandexBridge.IsAuthorized;

        public event Action<bool> PauseRequested;

        public PlatformTraits Traits => PlatformTraits.Default;

        /// <summary>Yandex Games has no sound setting of its own (it pauses the game instead).</summary>
        public bool MuteAudio => false;

        public event Action<bool> MuteAudioChanged { add { } remove { } }

        /// <summary>Signing in happens only through the game's button here.</summary>
        public event Action SignedIn { add { } remove { } }

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            var reply = await YandexBridge.InitAsync(cancellation).Timeout(InitTimeout);
            if (reply.Code != 1) throw new InvalidOperationException("Yandex Games SDK: " + reply.Text);
            Language = string.IsNullOrEmpty(reply.Text) ? null : reply.Text;
            YandexBridge.PauseRequested += pause => PauseRequested?.Invoke(pause);
        }

        public async UniTask<bool> SignInAsync(CancellationToken cancellation)
        {
            var reply = await YandexBridge.SignInAsync(cancellation);
            return reply.Code == 1;
        }

        public void LoadingFinished() => YandexBridge.LoadingReady();
        public void GameplayStarted() => YandexBridge.Gameplay(true);
        public void GameplayStopped() => YandexBridge.Gameplay(false);

        /// <summary>Yandex Games has no such event.</summary>
        public void HappyMoment() { }

        /// <summary>Yandex Games has no such event.</summary>
        public void GameProgress(int percent) { }

        async UniTask<string> ICloudSave.ReadAsync(CancellationToken cancellation)
        {
            var reply = await YandexBridge.GetDataAsync(cancellation);
            if (reply.Code != 1) throw new InvalidOperationException("Yandex player data: " + reply.Text);
            return string.IsNullOrEmpty(reply.Text) ? null : reply.Text;
        }

        async UniTask ICloudSave.WriteAsync(string json, CancellationToken cancellation)
        {
            var reply = await YandexBridge.SetDataAsync(json, cancellation);
            if (reply.Code != 1) throw new InvalidOperationException("Yandex player data: " + reply.Text);
        }
    }

    /// <summary>
    /// Yandex Games ads: fullscreen (interstitial) and rewarded video. The site keeps its own interval between
    /// fullscreen ads: one asked for too soon closes unshown (<see cref="AdResult.Unavailable"/>).
    /// </summary>
    public sealed class YandexAdProvider : IAdProvider
    {
        public string Name => "Yandex";

        public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;

        public bool InterstitialBetweenLevelsOnly => false;

        public async UniTask<AdResult> ShowAsync(AdKind kind, string placement, Action started, CancellationToken cancellation)
        {
            if (!YandexBridge.IsReady) return AdResult.Unavailable;
            WebReply reply;
            YandexBridge.AdStarted += started;
            try
            {
                reply = await YandexBridge.ShowAdAsync(kind, cancellation);
            }
            finally
            {
                YandexBridge.AdStarted -= started;
            }

            if (!string.IsNullOrEmpty(reply.Text))
                GameLog.Warning(LogChannel.Analytics, $"Yandex {kind} ad '{placement}': {reply.Text}");
            return Enum.IsDefined(typeof(AdResult), reply.Code) ? (AdResult)reply.Code : AdResult.Failed;
        }
    }

    /// <summary>
    /// Events as Yandex Metrica goals (<c>reachGoal</c>, parameters as the goal's parameters). Works only with the
    /// counter on the page (<c>WebPlatforms.YandexMetrikaCounter</c> in the editor's build code); without it — nothing.
    /// </summary>
    public sealed class YandexMetrikaSink : IAnalyticsSink
    {
        private readonly StringBuilder _json = new StringBuilder(256);

        public string Name => "Yandex Metrica";

        public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;

        public void Send(string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            YandexBridge.Goal(eventName, ToJson(parameters));
        }

        /// <summary>A flat JSON object of the parameters (string, number, bool values).</summary>
        public string ToJson(IReadOnlyDictionary<string, object> parameters)
        {
            _json.Clear().Append('{');
            var first = true;
            foreach (var pair in parameters)
            {
                if (!first) _json.Append(',');
                first = false;
                AppendString(pair.Key);
                _json.Append(':');
                switch (pair.Value)
                {
                    case null:
                        _json.Append("null");
                        break;
                    case bool flag:
                        _json.Append(flag ? "true" : "false");
                        break;
                    case int or long or short or byte:
                        _json.Append(Convert.ToInt64(pair.Value, CultureInfo.InvariantCulture));
                        break;
                    case float or double:
                        var number = Convert.ToDouble(pair.Value, CultureInfo.InvariantCulture);
                        if (double.IsNaN(number) || double.IsInfinity(number)) _json.Append("null");
                        else _json.Append(number.ToString("R", CultureInfo.InvariantCulture));
                        break;
                    default:
                        AppendString(Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
                        break;
                }
            }

            return _json.Append('}').ToString();
        }

        private void AppendString(string text)
        {
            _json.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': _json.Append("\\\""); break;
                    case '\\': _json.Append("\\\\"); break;
                    case '\n': _json.Append("\\n"); break;
                    case '\r': _json.Append("\\r"); break;
                    case '\t': _json.Append("\\t"); break;
                    default:
                        if (c < ' ') _json.Append("\\u").Append(((int)c).ToString("x4"));
                        else _json.Append(c);
                        break;
                }
            }

            _json.Append('"');
        }
    }
}
