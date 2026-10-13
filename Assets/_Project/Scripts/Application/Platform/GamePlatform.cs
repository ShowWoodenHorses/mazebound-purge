using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Flow;
using Maze.Application.Levels;
using Maze.Application.Save;
using Maze.Application.Services;
using Maze.Core.Common;
using Maze.Gameplay.Level;

namespace Maze.Application.Platform
{
    /// <summary>
    /// Saves kept by the platform for the player (Yandex Games player data, CrazyGames Data…): they follow the player
    /// between devices and survive a new build at a new address, unlike the browser's own storage.
    /// </summary>
    public interface ICloudSave
    {
        /// <summary>The save JSON; null when the player has none yet. Throws when the platform could not answer.</summary>
        UniTask<string> ReadAsync(CancellationToken cancellation);

        UniTask WriteAsync(string json, CancellationToken cancellation);
    }

    /// <summary>How the game behaves on a site, where sites' rules differ (defaults: as on a site without rules).</summary>
    public sealed class PlatformTraits
    {
        public static readonly PlatformTraits Default = new PlatformTraits();

        /// <summary>
        /// A player who has never played lands in gameplay at once, without the menu (CrazyGames: "players land in
        /// gameplay immediately"): the tutorial, or the first level.
        /// </summary>
        public bool StartsInGameplay;

        /// <summary>
        /// The site itself knows when the game loses focus or is hidden: a pause for that reason is not reported as
        /// <see cref="IGamePlatform.GameplayStopped"/> (CrazyGames).
        /// </summary>
        public bool TracksFocusItself;

        /// <summary>
        /// The support contact shown in the settings (VK: a catalog game needs a way to reach support). Null — none
        /// (sites that forbid contacts and links outside the game).
        /// </summary>
        public string SupportEmail;

        /// <summary>
        /// The privacy policy page, opened from the settings (stores: the app collects analytics and ad identifiers).
        /// Null — none (sites with their own policy and no links out of the game).
        /// </summary>
        public string PrivacyPolicyUrl;

        /// <summary>
        /// How far the top-right buttons (HUD Pause and Map, the map's Close and hint) move left, in UI units of the
        /// 1920×1080 canvas, beyond the safe area: a site's bar over the game that it does not report (the VK app on a
        /// phone). 0 — in place.
        /// </summary>
        public float TopRightButtonsShift;
    }

    /// <summary>The developer's contacts shown to players (the same on every platform that shows them).</summary>
    public static class Contacts
    {
        public const string SupportEmail = "bukun.vv.games@gmail.com";

        /// <summary>The privacy policy (GitHub Pages; the page's source — <c>Docs/PrivacyPolicy</c>).</summary>
        public const string PrivacyPolicyUrl = "https://showwoodenhorses.github.io/mazebound/privacy.html";
    }

    /// <summary>
    /// The site the web build runs on (its SDK): start-up, language, cloud saves and the moments it wants to know about.
    /// Ads and analytics are separate (<see cref="IAdProvider"/>, <see cref="IAnalyticsSink"/>). Knows nothing about
    /// the game: <see cref="PlatformEvents"/> tells it what happens.
    /// </summary>
    public interface IGamePlatform
    {
        string Name { get; }

        /// <summary>Starts the SDK. A failure is logged and the game goes on as on <see cref="GenericPlatform"/>.</summary>
        UniTask InitializeAsync(CancellationToken cancellation);

        /// <summary>The player's language on the site ("ru", "en"…); null when the site does not say.</summary>
        string Language { get; }

        /// <summary>Null when the site keeps no saves (then only the browser's storage is used).</summary>
        ICloudSave CloudSave { get; }

        /// <summary>The game finished loading and can be played (the main menu is shown for the first time).</summary>
        void LoadingFinished();

        /// <summary>A level is running (not paused, not in a menu or a window).</summary>
        void GameplayStarted();

        void GameplayStopped();

        /// <summary>A moment of joy worth celebrating on the site (a level with all stars).</summary>
        void HappyMoment();

        /// <summary>How much of the game the player has completed, 0–100 (100 only when all of it).</summary>
        void GameProgress(int percent);

        /// <summary>The site has player accounts the player can sign in to (saves then follow the account).</summary>
        bool SupportsSignIn { get; }

        bool IsSignedIn { get; }

        /// <summary>Opens the site's sign-in window; true when the player is signed in afterwards.</summary>
        UniTask<bool> SignInAsync(CancellationToken cancellation);

        /// <summary>
        /// The site asks the game to stop (true: its own window, an ad it shows itself…) and to go on (false).
        /// </summary>
        event Action<bool> PauseRequested;

        PlatformTraits Traits { get; }

        /// <summary>The site wants the game silent (its own sound setting); stronger than the game's volume settings.</summary>
        bool MuteAudio { get; }

        event Action<bool> MuteAudioChanged;

        /// <summary>The player signed in through the site itself, not through the game's button.</summary>
        event Action SignedIn;
    }

    /// <summary>No site: the editor, a local web build, Android. Nothing to tell, no cloud.</summary>
    public sealed class GenericPlatform : IGamePlatform
    {
        public string Name => "Generic";
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
        public PlatformTraits Traits => PlatformTraits.Default;
        public bool MuteAudio => false;
        public event Action<bool> MuteAudioChanged { add { } remove { } }
        public event Action SignedIn { add { } remove { } }
    }

    /// <summary>
    /// The platform as an application service, initialized first (saves and language depend on it). A platform that
    /// fails to start never stops the game: it is then treated as having no language and no cloud, and is not told
    /// about anything.
    /// </summary>
    public sealed class PlatformService : IApplicationService
    {
        private readonly IGamePlatform _platform;
        private bool _ready;

        public PlatformService(IGamePlatform platform)
        {
            _platform = platform;
        }

        public string Name => "Platform";

        /// <summary>The platform's name ("Generic", "Yandex"…), for logs and analytics.</summary>
        public string PlatformName => _platform.Name;

        public string Language => _ready ? _platform.Language : null;

        public ICloudSave CloudSave => _ready ? _platform.CloudSave : null;

        /// <summary>The player can sign in now (the site has accounts, the player is not signed in).</summary>
        public bool CanSignIn => _ready && _platform.SupportsSignIn && !_platform.IsSignedIn;

        /// <summary>The site asks to pause (true) or lets the game go on (false); see <see cref="IGamePlatform.PauseRequested"/>.</summary>
        public event Action<bool> PauseRequested;

        /// <summary>The site's rules for the game (defaults until the platform has started).</summary>
        public PlatformTraits Traits => _ready ? _platform.Traits ?? PlatformTraits.Default : PlatformTraits.Default;

        public bool MuteAudio => _ready && _platform.MuteAudio;

        /// <summary>The site's mute changed; also raised once at start-up when the site starts muted.</summary>
        public event Action<bool> MuteAudioChanged;

        /// <summary>The player signed in through the site (not through <see cref="SignInAsync"/>).</summary>
        public event Action SignedIn;

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            try
            {
                await _platform.InitializeAsync(cancellation);
                _ready = true;
                _platform.PauseRequested += OnPauseRequested;
                _platform.MuteAudioChanged += OnMuteAudioChanged;
                _platform.SignedIn += OnSignedIn;
                if (_platform.MuteAudio) MuteAudioChanged?.Invoke(true);
                GameLog.Info(LogChannel.Bootstrap, $"Platform '{_platform.Name}': language {Language ?? "-"}, " +
                                                   $"cloud saves {(CloudSave != null ? "yes" : "no")}, " +
                                                   $"signed in {(_platform.SupportsSignIn ? _platform.IsSignedIn ? "yes" : "no" : "-")}.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Bootstrap, e, $"Platform '{_platform.Name}' failed to start; going on without it");
            }
        }

        /// <summary>Opens the site's sign-in window; false when the player did not sign in or the site failed.</summary>
        public async UniTask<bool> SignInAsync(CancellationToken cancellation)
        {
            if (!CanSignIn) return false;
            try
            {
                return await _platform.SignInAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Bootstrap, e, $"Platform '{_platform.Name}': sign-in failed");
                return false;
            }
        }

        private void OnPauseRequested(bool pause) => PauseRequested?.Invoke(pause);
        private void OnMuteAudioChanged(bool mute) => MuteAudioChanged?.Invoke(mute);
        private void OnSignedIn() => SignedIn?.Invoke();

        public void LoadingFinished() => Tell(p => p.LoadingFinished(), nameof(LoadingFinished));
        public void GameplayStarted() => Tell(p => p.GameplayStarted(), nameof(GameplayStarted));
        public void GameplayStopped() => Tell(p => p.GameplayStopped(), nameof(GameplayStopped));
        public void HappyMoment() => Tell(p => p.HappyMoment(), nameof(HappyMoment));
        public void GameProgress(int percent) => Tell(p => p.GameProgress(percent), nameof(GameProgress));

        private void Tell(Action<IGamePlatform> call, string what)
        {
            if (!_ready) return;
            try
            {
                call(_platform);
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Bootstrap, e, $"Platform '{_platform.Name}': {what} failed");
            }
        }
    }

    /// <summary>
    /// Tells the platform what the game does, from <see cref="GameFlow"/> states: loading finished (the first main
    /// menu, or the first level for a new player who skips it), gameplay running only in <see cref="GameFlowState.Playing"/> (pause, map, windows, ads between levels —
    /// stopped; a pause for the background is not told to a site that tracks focus itself), a happy moment for a level
    /// that gets all stars for the first time; the completed part of the game (levels of the catalog) at start and after
    /// every completed level.
    /// </summary>
    public sealed class PlatformEvents : IDisposable
    {
        private readonly GameFlow _flow;
        private readonly PlatformService _platform;
        private readonly AppFocusController _focus;
        private readonly ILevelCatalog _catalog;
        private readonly IProgressService _progress;
        private int _reportedProgress = -1;
        private bool _initialized;
        private bool _loaded;
        private bool _playing;

        public PlatformEvents(GameFlow flow, PlatformService platform, AppFocusController focus = null,
            ILevelCatalog catalog = null, IProgressService progress = null)
        {
            _catalog = catalog;
            _progress = progress;
            _focus = focus;
            _flow = flow;
            _platform = platform;
        }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _flow.StateChanged += OnStateChanged;
        }

        public void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;
            _flow.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(GameFlowState state)
        {
            if (!_loaded && (state == GameFlowState.MainMenu || state == GameFlowState.Playing))
            {
                _loaded = true;
                _platform.LoadingFinished();
                ReportProgress(); // at every start: new levels in an update lower it
            }

            var playing = state == GameFlowState.Playing;
            // Paused because the game went to the background: a site that tracks focus itself is not told.
            if (!playing && _playing && state == GameFlowState.Paused && _focus != null && !_focus.IsActive &&
                _platform.Traits.TracksFocusItself)
                playing = true;
            if (playing != _playing)
            {
                _playing = playing;
                if (playing) _platform.GameplayStarted();
                else _platform.GameplayStopped();
            }

            if (state == GameFlowState.Completed) ReportProgress();

            // Rarely (site rules): only the first time a level gets all stars.
            if (state == GameFlowState.Completed && _flow.LastResult is { } result &&
                result.Stars >= LevelResult.MaxStars && _flow.LastPreviousStars < LevelResult.MaxStars &&
                !_flow.IsTutorialLevel)
                _platform.HappyMoment();
        }

        /// <summary>Completed catalog levels in percent, rounded down: 100 only when every level is completed.</summary>
        public static int CompletedPercent(int completed, int total) =>
            total <= 0 ? 0 : Math.Min(100, completed * 100 / total);

        private void ReportProgress()
        {
            if (_catalog == null || _progress == null) return;
            var levels = _catalog.Levels;
            var completed = 0;
            for (var i = 0; i < levels.Count; i++)
                if (_progress.GetStars(levels[i].LevelId) > 0)
                    completed++;
            var percent = CompletedPercent(completed, levels.Count);
            if (percent == _reportedProgress) return;
            _reportedProgress = percent;
            _platform.GameProgress(percent);
        }
    }
}
