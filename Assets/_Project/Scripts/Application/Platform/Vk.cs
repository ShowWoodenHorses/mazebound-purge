using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using UnityEngine;

namespace Maze.Application.Platform
{
    /// <summary>
    /// Calls into VK Bridge (<c>Plugins/WebGL/MazeVk.jslib</c>; the page loads the library and sends <c>VKWebAppInit</c>
    /// itself, <c>WebPlatforms.Head</c>). Only a WebGL player has it: elsewhere every call throws, so the game works as
    /// <see cref="GenericPlatform"/>.
    /// </summary>
    public static class VkBridge
    {
        private static readonly WebCalls Calls = new WebCalls();

        public static bool IsReady { get; private set; }

        /// <summary>The VK app hid the game (true: another app, picture-in-picture) or showed it again (false).</summary>
        public static event Action<bool> PauseRequested;

        /// <summary>Starts the bridge; the reply text is "&lt;vk_language&gt;;&lt;vk_platform&gt;" from the launch parameters.</summary>
        public static async UniTask<WebReply> InitAsync(CancellationToken cancellation)
        {
            var reply = await Calls.Call(id => Native.Init(OnReply, id), cancellation);
            IsReady = reply.Code == 1;
            return reply;
        }

        /// <summary>The reply text is <c>{"keys":[{"key":…,"value":…}]}</c>.</summary>
        public static UniTask<WebReply> StorageGetAsync(string keysJson, CancellationToken cancellation) =>
            Calls.Call(id => Native.StorageGet(id, keysJson), cancellation);

        public static UniTask<WebReply> StorageSetAsync(string key, string value, CancellationToken cancellation) =>
            Calls.Call(id => Native.StorageSet(id, key, value), cancellation);

        /// <summary>Reply code = <see cref="AdResult"/>.</summary>
        public static UniTask<WebReply> ShowAdAsync(AdKind kind, CancellationToken cancellation) =>
            Calls.Call(id => Native.ShowAd(id, kind == AdKind.Rewarded ? 1 : 0), cancellation);

        /// <summary>
        /// The sticky banner at the bottom of the game (VK shrinks the game's frame by its height). Compact — the lower
        /// banner of a landscape game on a phone.
        /// </summary>
        public static void Banner(bool show, bool compact)
        {
            if (IsReady) Native.Banner(show ? 1 : 0, compact ? 1 : 0);
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

            // Events from VK: 1 — the game is hidden (VKWebAppViewHide), 2 — shown again (VKWebAppViewRestore).
            if (code == 1 || code == 2) PauseRequested?.Invoke(code == 1);
        }

        private static class Native
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            [DllImport("__Internal", EntryPoint = "MazeVk_Init")]
            public static extern void Init(ReplyCallback callback, int id);

            [DllImport("__Internal", EntryPoint = "MazeVk_StorageGet")]
            public static extern void StorageGet(int id, string keysJson);

            [DllImport("__Internal", EntryPoint = "MazeVk_StorageSet")]
            public static extern void StorageSet(int id, string key, string value);

            [DllImport("__Internal", EntryPoint = "MazeVk_ShowAd")]
            public static extern void ShowAd(int id, int rewarded);

            [DllImport("__Internal", EntryPoint = "MazeVk_Banner")]
            public static extern void Banner(int show, int compact);
#else
            private static Exception NoSdk() => new PlatformNotSupportedException("VK Bridge exists only in a WebGL build.");
            public static void Init(ReplyCallback callback, int id) => throw NoSdk();
            public static void StorageGet(int id, string keysJson) => throw NoSdk();
            public static void StorageSet(int id, string key, string value) => throw NoSdk();
            public static void ShowAd(int id, int rewarded) => throw NoSdk();
            public static void Banner(int show, int compact) { }
#endif
        }
    }

    /// <summary>
    /// VK Games (build symbol <c>MAZE_VK</c>; vk.com, m.vk.com, the VK apps): the language of VK's interface, saves in
    /// VK Storage (every player is signed in; the hosting changes the game's address with every upload, so the
    /// browser's storage does not last), pauses when the VK app hides the game, the sticky banner (desktop; phones —
    /// <see cref="MobileBanner"/>), the support contact (catalog rule). VK has no loading/gameplay events.
    /// </summary>
    public sealed class VkPlatform : IGamePlatform, IKeyValueStorage
    {
        public static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// The banner on phones too (m.vk.com, the VK apps): on for a look on a device (2026-10-12); in landscape it
        /// takes height from a short screen — turn off again if it gets in the way.
        /// </summary>
        public const bool MobileBanner = true;

        /// <summary>
        /// The VK app on a phone covers the top-right buttons with its navigation bar even with the insets fallback
        /// (MazeVk.jslib, 48 CSS px — not enough, checked by the user 2026-10-13): on phones they move left by a button.
        /// </summary>
        public const float PhoneButtonsShift = 84f;

        private static readonly PlatformTraits VkTraits = new PlatformTraits
        {
            SupportEmail = Contacts.SupportEmail,
            TopRightButtonsShift = UnityEngine.Application.isMobilePlatform ? PhoneButtonsShift : 0f,
        };

        private readonly SlottedCloudSave _cloud;
        private readonly StringBuilder _json = new StringBuilder(128);

        public VkPlatform()
        {
            _cloud = new SlottedCloudSave(this);
        }

        public string Name => "VK";

        public string Language { get; private set; }

        /// <summary>vk_platform: desktop_web, mobile_web, mobile_android, mobile_iphone…</summary>
        public string Device { get; private set; }

        public ICloudSave CloudSave => VkBridge.IsReady ? _cloud : null;

        /// <summary>Every VK player is signed in already.</summary>
        public bool SupportsSignIn => false;

        public bool IsSignedIn => VkBridge.IsReady;

        public UniTask<bool> SignInAsync(CancellationToken cancellation) => UniTask.FromResult(false);

        public PlatformTraits Traits => VkTraits;

        public bool MuteAudio => false;

        public event Action<bool> PauseRequested;

        public event Action<bool> MuteAudioChanged { add { } remove { } }

        public event Action SignedIn { add { } remove { } }

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            var reply = await VkBridge.InitAsync(cancellation).Timeout(InitTimeout);
            if (reply.Code != 1) throw new InvalidOperationException("VK Bridge: " + reply.Text);
            var parameters = (reply.Text ?? "").Split(';');
            Language = ToGameLanguage(parameters[0]);
            Device = parameters.Length > 1 && parameters[1].Length > 0 ? parameters[1] : null;
            VkBridge.PauseRequested += pause => PauseRequested?.Invoke(pause);
            if (ShowsBanner(Device)) VkBridge.Banner(true, compact: Device != "desktop_web");
        }

        /// <summary>vk_language to the game's code: kz — Kazakh (kk), ua — Ukrainian (uk); empty — null.</summary>
        public static string ToGameLanguage(string vkLanguage) => vkLanguage switch
        {
            null or "" => null,
            "kz" => "kk",
            "ua" => "uk",
            _ => vkLanguage,
        };

        /// <summary>The banner on the desktop site; on phones only with <see cref="MobileBanner"/>.</summary>
        public static bool ShowsBanner(string device) => device == "desktop_web" || (MobileBanner && device != null);

        /// <summary>VK has no such event.</summary>
        public void LoadingFinished() { }

        public void GameplayStarted() { }

        public void GameplayStopped() { }

        public void HappyMoment() { }

        public void GameProgress(int percent) { }

        async UniTask<IReadOnlyDictionary<string, string>> IKeyValueStorage.GetAsync(IReadOnlyList<string> keys,
            CancellationToken cancellation)
        {
            _json.Clear().Append('[');
            for (var i = 0; i < keys.Count; i++)
                _json.Append(i > 0 ? ",\"" : "\"").Append(keys[i]).Append('"'); // keys are [a-zA-Z0-9_-]
            // A storage that does not answer must not hold the start-up: the session then saves only locally.
            var reply = await VkBridge.StorageGetAsync(_json.Append(']').ToString(), cancellation).Timeout(InitTimeout);
            if (reply.Code != 1) throw new InvalidOperationException("VK Storage: " + reply.Text);
            return ParseStorage(reply.Text);
        }

        async UniTask IKeyValueStorage.SetAsync(string key, string value, CancellationToken cancellation)
        {
            var reply = await VkBridge.StorageSetAsync(key, value, cancellation);
            if (reply.Code != 1) throw new InvalidOperationException("VK Storage: " + reply.Text);
        }

        [Serializable]
        private sealed class StorageReply
        {
            public StorageEntry[] keys;
        }

        [Serializable]
        private sealed class StorageEntry
        {
            public string key;
            public string value;
        }

        /// <summary><c>{"keys":[{"key":…,"value":…}]}</c> to a dictionary.</summary>
        public static Dictionary<string, string> ParseStorage(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;
            var reply = JsonUtility.FromJson<StorageReply>(json);
            if (reply?.keys == null) return result;
            foreach (var entry in reply.keys)
                if (entry?.key != null)
                    result[entry.key] = entry.value;
            return result;
        }
    }

    /// <summary>
    /// VK ads (VKWebAppShowNativeAds): rewarded (a rewarded video; with none VK may show an interstitial instead and
    /// it counts) and interstitial. The page preloads both kinds and checks again on a timer. VK reports only whether an
    /// ad was shown, not when it starts: the game goes silent at the request.
    /// </summary>
    public sealed class VkAdProvider : IAdProvider
    {
        public string Name => "VK";

        /// <summary>VK allows interstitials on any change of screens, a level start from the menu too.</summary>
        public bool InterstitialBetweenLevelsOnly => false;

        public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;

        public async UniTask<AdResult> ShowAsync(AdKind kind, string placement, Action started, CancellationToken cancellation)
        {
            if (!VkBridge.IsReady) return AdResult.Unavailable;
            started?.Invoke();
            var reply = await VkBridge.ShowAdAsync(kind, cancellation);
            if (!string.IsNullOrEmpty(reply.Text))
                GameLog.Info(LogChannel.Analytics, $"VK {kind} ad '{placement}': {reply.Text}");
            return Enum.IsDefined(typeof(AdResult), reply.Code) ? (AdResult)reply.Code : AdResult.Failed;
        }
    }
}
