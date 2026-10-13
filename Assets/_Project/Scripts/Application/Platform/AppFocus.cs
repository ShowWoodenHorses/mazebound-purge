using System;
using System.Runtime.InteropServices;
using Maze.Application.Flow;
using Maze.Application.Save;
using UnityEngine;

namespace Maze.Application.Platform
{
    /// <summary>Why the whole game is silent (<see cref="AudioListener.pause"/>).</summary>
    [Flags]
    public enum SoundSuspendReason
    {
        None = 0,
        Ad = 1,

        /// <summary>The page is hidden or has lost focus (another tab, a click outside the game's frame), the app is in the background.</summary>
        Background = 2,

        /// <summary>The site wants the game silent (CrazyGames <c>muteAudio</c>).</summary>
        Platform = 4,
    }

    /// <summary>
    /// Silences the game while any reason holds it (an ad, the page in the background) and gives the sound back once
    /// none does: an ad ending in a hidden tab must not turn the sound on.
    /// </summary>
    public sealed class SoundSuspension
    {
        private SoundSuspendReason _reasons;

        public SoundSuspendReason Reasons => _reasons;

        public void Set(SoundSuspendReason reason, bool on)
        {
            var next = on ? _reasons | reason : _reasons & ~reason;
            if (next == _reasons) return;
            _reasons = next;
            AudioListener.pause = next != SoundSuspendReason.None;
        }
    }

    /// <summary>
    /// The game in the background (platform rules: Yandex Games and others): while the page is hidden or unfocused —
    /// or the app is — the sound is off, a running level is paused (the player resumes it from the pause screen) and
    /// a pending cloud save is written. The same while the site asks the game to stop (<see cref="PlatformService.PauseRequested"/>);
    /// silent while the site mutes the game (<see cref="PlatformService.MuteAudioChanged"/>).
    /// Off in the editor: clicking another editor window must not pause play mode or tests.
    /// </summary>
    public sealed class AppFocusController : IDisposable
    {
        private readonly GameFlow _flow;
        private readonly SoundSuspension _sound;
        private readonly SaveService _save;
        private readonly PlatformService _platform;
        private bool _initialized;
        private bool _focused = true;
        private bool _visible = true;
        private bool _sitePaused;

        public AppFocusController(GameFlow flow, SoundSuspension sound, SaveService save = null, PlatformService platform = null)
        {
            _platform = platform;
            _save = save;
            _flow = flow;
            _sound = sound;
        }

        public void Initialize()
        {
            if (_initialized || UnityEngine.Application.isEditor) return;
            _initialized = true;
            UnityEngine.Application.focusChanged += OnFocusChanged;
            WebPage.VisibilityChanged += OnVisibilityChanged;
            WebPage.Listen();
            if (_platform != null)
            {
                _platform.PauseRequested += OnSitePauseRequested;
                _platform.MuteAudioChanged += OnSiteMuteChanged;
                OnSiteMuteChanged(_platform.MuteAudio);
            }
        }

        public void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;
            UnityEngine.Application.focusChanged -= OnFocusChanged;
            WebPage.VisibilityChanged -= OnVisibilityChanged;
            if (_platform != null)
            {
                _platform.PauseRequested -= OnSitePauseRequested;
                _platform.MuteAudioChanged -= OnSiteMuteChanged;
            }
        }

        /// <summary>The game is in front of the player: focused, visible and not stopped by the site.</summary>
        public bool IsActive => _focused && _visible && !_sitePaused;

        public void OnFocusChanged(bool focused)
        {
            _focused = focused;
            Apply();
        }

        public void OnVisibilityChanged(bool visible)
        {
            _visible = visible;
            Apply();
        }

        public void OnSitePauseRequested(bool pause)
        {
            _sitePaused = pause;
            Apply();
        }

        /// <summary>The site's own mute: stronger than the game's volume settings, they do not touch it.</summary>
        public void OnSiteMuteChanged(bool mute) => _sound.Set(SoundSuspendReason.Platform, mute);

        private void Apply()
        {
            var active = IsActive;
            _sound.Set(SoundSuspendReason.Background, !active);
            if (active) return;
            if (_flow.State == GameFlowState.Playing) _flow.PauseGameplay();
            _save?.FlushCloud(); // the tab may be closed next
        }
    }

    /// <summary>
    /// The page around a WebGL build (<c>Plugins/WebGL/MazePage.jslib</c>): tab visibility. Elsewhere — nothing.
    /// </summary>
    public static class WebPage
    {
        /// <summary>True when the page became visible, false when hidden (another tab, minimized browser).</summary>
        public static event Action<bool> VisibilityChanged;

        /// <summary>
        /// Edges the game keeps its controls clear of, in screen pixels (left, top, right, bottom): system bars over
        /// the page (the VK app on a phone), notches the browser reports in CSS (<c>env(safe-area-inset-*)</c>), beyond
        /// what <see cref="Screen.safeArea"/> knows. Zero outside a web build.
        /// </summary>
        public static RectInt Insets { get; private set; }

        private static bool _listening;

        public static void Listen()
        {
            if (_listening) return;
            _listening = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            MazePage_ListenVisibility(OnVisibility);
            MazePage_ListenInsets(OnInsets);
#endif
        }

        /// <summary>The safe area narrowed by <see cref="Insets"/>.</summary>
        public static Rect SafeArea(Rect safeArea, int screenWidth, int screenHeight)
        {
            var insets = Insets;
            if (insets.x == 0 && insets.y == 0 && insets.width == 0 && insets.height == 0) return safeArea;
            var xMin = Mathf.Max(safeArea.xMin, insets.x);
            var xMax = Mathf.Min(safeArea.xMax, screenWidth - insets.width);
            var yMin = Mathf.Max(safeArea.yMin, insets.height); // screen y grows upwards: the bottom inset
            var yMax = Mathf.Min(safeArea.yMax, screenHeight - insets.y);
            return xMax > xMin && yMax > yMin ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : safeArea;
        }

        /// <summary>Sets the insets (left, top, right, bottom in RectInt's x, y, width, height); tests and the page.</summary>
        public static void SetInsets(int left, int top, int right, int bottom) =>
            Insets = new RectInt(Mathf.Max(0, left), Mathf.Max(0, top), Mathf.Max(0, right), Mathf.Max(0, bottom));

#if UNITY_WEBGL && !UNITY_EDITOR
        private delegate void IntCallback(int value);

        private delegate void InsetsCallback(int left, int top, int right, int bottom);

        [DllImport("__Internal")]
        private static extern void MazePage_ListenVisibility(IntCallback callback);

        [DllImport("__Internal")]
        private static extern void MazePage_ListenInsets(InsetsCallback callback);

        [AOT.MonoPInvokeCallback(typeof(IntCallback))]
        private static void OnVisibility(int visible) => Raise(visible != 0);

        [AOT.MonoPInvokeCallback(typeof(InsetsCallback))]
        private static void OnInsets(int left, int top, int right, int bottom)
        {
            SetInsets(left, top, right, bottom);
            Maze.Core.Common.GameLog.Info(Maze.Core.Common.LogChannel.Bootstrap,
                $"Screen insets: left {left}, top {top}, right {right}, bottom {bottom} px.");
        }
#endif

        private static void Raise(bool visible) => VisibilityChanged?.Invoke(visible);
    }
}
