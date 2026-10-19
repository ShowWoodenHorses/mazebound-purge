using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Purge.Application.Flow;
using Purge.Application.Levels;
using Purge.Application.Platform;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Presentation.Localization;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// Presenter of the application screens: shows the screen matching <see cref="GameFlow.State"/>
    /// and turns screen events into GameFlow commands. The level select is part of the main menu state: Play opens it
    /// over the menu, Back returns. The tutorial has its own menu button and caption, its window pauses the level
    /// (<see cref="GameFlowState.Tip"/>; Continue, Enter / Space / gamepad South close it) and its completion shows only
    /// "Tutorial complete" with Next leading to the first level.
    /// </summary>
    public sealed class ScreenRouter : IDisposable
    {
        private readonly GameFlow _flow;
        private readonly ILevelCatalog _catalog;
        private readonly UIRoot _ui;
        private readonly IProgressService _progress;
        private readonly LocalizationService _texts;
        private readonly IInputService _input;
        private readonly AccountService _account;
        private readonly List<LevelTileData> _tiles = new List<LevelTileData>();
        private readonly List<LevelTileData> _devTiles = new List<LevelTileData>();
        private bool _levelSelectOpen;
        private bool _initialized;

        public ScreenRouter(GameFlow flow, ILevelCatalog catalog, UIRoot ui, IProgressService progress,
            LocalizationService texts, IInputService input = null, AccountService account = null)
        {
            _account = account;
            _input = input;
            _texts = texts;
            _progress = progress;
            _flow = flow;
            _catalog = catalog;
            _ui = ui;
        }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _ui.ScaleDragThreshold();
            _flow.StateChanged += Show;
            _texts.LanguageChanged += OnLanguageChanged;
            _ui.MainMenu.PlayClicked += OnPlayClicked;
            _ui.MainMenu.LevelsClicked += OnLevelsClicked;
            _ui.MainMenu.TutorialClicked += OnTutorialClicked;
            _ui.MainMenu.DebugResetProgressClicked += OnResetProgressRequested;
            _ui.MainMenu.SignInClicked += OnSignInClicked;
            if (_account != null) _account.Changed += OnAccountChanged;
            if (_ui.Tutorial != null) _ui.Tutorial.ContinueClicked += _flow.CloseTip;
            if (_input != null) _input.ConfirmRequested += OnConfirmRequested;
            if (_ui.RotateDevice != null) _ui.RotateDevice.Changed += OnRotateDeviceChanged;
            _ui.LevelSelect.LevelSelected += OnLevelSelected;
            _ui.LevelSelect.BackClicked += OnLevelSelectBack;
            _ui.Error.BackClicked += OnMenuRequested;
            _ui.Hud.PauseClicked += _flow.PauseGameplay;
            _ui.Hud.MapClicked += _flow.OpenMap;
            _ui.Map.CloseClicked += _flow.CloseMap;
            _ui.Pause.ResumeClicked += _flow.ResumeGameplay;
            _ui.Pause.RetryClicked += OnRetryRequested;
            _ui.Pause.ExitClicked += OnMenuRequested;
            _ui.Pause.DebugCompleteClicked += _flow.CompleteLevel;
            _ui.Pause.DebugFailClicked += _flow.FailLevel;
            _ui.Result.RetryClicked += OnRetryRequested;
            _ui.Result.MenuClicked += OnMenuRequested;
            _ui.Result.NextClicked += OnNextRequested;
            _ui.Result.DoubleClicked += OnDoubleRequested;

            Show(_flow.State);
        }

        public void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            _flow.StateChanged -= Show;
            _texts.LanguageChanged -= OnLanguageChanged;
            if (_input != null) _input.ConfirmRequested -= OnConfirmRequested;
            if (_account != null) _account.Changed -= OnAccountChanged;
            if (_ui == null) return; // Scene already destroyed on application quit.

            if (_ui.RotateDevice != null) _ui.RotateDevice.Changed -= OnRotateDeviceChanged;

            _ui.MainMenu.PlayClicked -= OnPlayClicked;
            _ui.MainMenu.LevelsClicked -= OnLevelsClicked;
            _ui.MainMenu.TutorialClicked -= OnTutorialClicked;
            if (_ui.Tutorial != null) _ui.Tutorial.ContinueClicked -= _flow.CloseTip;
            _ui.MainMenu.DebugResetProgressClicked -= OnResetProgressRequested;
            _ui.MainMenu.SignInClicked -= OnSignInClicked;
            _ui.LevelSelect.LevelSelected -= OnLevelSelected;
            _ui.LevelSelect.BackClicked -= OnLevelSelectBack;
            _ui.Error.BackClicked -= OnMenuRequested;
            _ui.Hud.PauseClicked -= _flow.PauseGameplay;
            _ui.Hud.MapClicked -= _flow.OpenMap;
            _ui.Map.CloseClicked -= _flow.CloseMap;
            _ui.Pause.ResumeClicked -= _flow.ResumeGameplay;
            _ui.Pause.RetryClicked -= OnRetryRequested;
            _ui.Pause.ExitClicked -= OnMenuRequested;
            _ui.Pause.DebugCompleteClicked -= _flow.CompleteLevel;
            _ui.Pause.DebugFailClicked -= _flow.FailLevel;
            _ui.Result.RetryClicked -= OnRetryRequested;
            _ui.Result.MenuClicked -= OnMenuRequested;
            _ui.Result.NextClicked -= OnNextRequested;
            _ui.Result.DoubleClicked -= OnDoubleRequested;
        }

        /// <summary>Portrait on a phone: the level stops under "Rotate your device"; the player resumes it from the pause.</summary>
        private void OnRotateDeviceChanged(bool shown)
        {
            if (shown && _flow.State == GameFlowState.Playing) _flow.PauseGameplay();
        }

        private void Show(GameFlowState state)
        {
            var menu = state == GameFlowState.MainMenu;
            var loading = state == GameFlowState.None || state == GameFlowState.Initializing || state == GameFlowState.Loading;
            // The map is fullscreen: the HUD is hidden under it.
            var inLevel = state == GameFlowState.Playing || state == GameFlowState.Paused || state == GameFlowState.Tip ||
                          state == GameFlowState.Cards || state == GameFlowState.Death;
            var result = state == GameFlowState.Completed || state == GameFlowState.Failed;
            var error = state == GameFlowState.Error;

            if (!menu) _levelSelectOpen = false;
            if (menu) RefreshMenus();
            if (inLevel) _ui.Hud.SetLevelName(LevelCaption(_flow.CurrentLevelId));
            if (result)
            {
                var completed = state == GameFlowState.Completed;
                if (completed && _flow.IsTutorialLevel)
                    _ui.Result.SetTutorialComplete(NextLevelId() != null);
                else
                    _ui.Result.SetResult(completed, _flow.LastResult, LevelCaption(_flow.CurrentLevelId),
                        completed && NextLevelId() != null, _flow.CanDoubleReward);
            }
            if (error) _ui.Error.SetMessage(_flow.ErrorMessage);

            _ui.MainMenu.SetVisible(menu && !_levelSelectOpen);
            _ui.LevelSelect.SetVisible(menu && _levelSelectOpen);
            _ui.Loading.SetVisible(loading);
            _ui.Hud.SetVisible(inLevel);
            _ui.Pause.SetVisible(state == GameFlowState.Paused);
            _ui.Result.SetVisible(result);
            _ui.Error.SetVisible(error);
            _ui.Map.SetVisible(state == GameFlowState.Map);
            if (_ui.Cards != null) _ui.Cards.SetVisible(state == GameFlowState.Cards);
            if (_ui.Death != null) _ui.Death.SetVisible(state == GameFlowState.Death);
            if (_ui.Tutorial != null)
            {
                _ui.Tutorial.SetVisible(inLevel && _flow.IsTutorialLevel);
                _ui.Tutorial.SetDialogVisible(state == GameFlowState.Tip);
            }
        }

        /// <summary>
        /// Totals for the main menu (money, kills) and the level tiles. Locked levels are disabled; development builds
        /// (and the editor) still let you start them so any level can be tested.
        /// </summary>
        private void RefreshMenus()
        {
            var levels = _catalog.Levels;
            _tiles.Clear();
            for (var i = 0; i < levels.Count; i++)
            {
                var entry = levels[i];
                var unlocked = _progress.IsUnlocked(entry.LevelId);
                _tiles.Add(new LevelTileData(entry.LevelId, i + 1, _progress.IsCompleted(entry.LevelId), unlocked,
                    unlocked || Debug.isDebugBuild));
            }

            // Development levels (editor, development builds): always playable, outside the progression.
            _devTiles.Clear();
            var devLevels = _catalog.DevLevels;
            for (var i = 0; i < devLevels.Count; i++)
            {
                var entry = devLevels[i];
                _devTiles.Add(new LevelTileData(entry.LevelId, i + 1, _progress.IsCompleted(entry.LevelId), true, true,
                    DevCaption(entry.LevelId)));
            }

            _ui.MainMenu.SetSummary(_progress.Money, _progress.TotalKills);
            _ui.MainMenu.SetPlayable(levels.Count > 0);
            _ui.MainMenu.SetTutorialAvailable(_catalog.Tutorial != null);
            RefreshSignIn();
            _ui.LevelSelect.SetLevels(_tiles, _devTiles);
        }

        private void RefreshSignIn()
        {
            var busy = _account != null && _account.IsBusy;
            _ui.MainMenu.SetSignIn(_account != null && (_account.CanSignIn || busy), !busy);
        }

        /// <summary>Signing in to the site's account: only on the player's request (site rules).</summary>
        private void OnSignInClicked()
        {
            if (_account == null || !_account.CanSignIn) return;
            _account.SignInAsync(System.Threading.CancellationToken.None).Forget();
        }

        /// <summary>A sign-in started or ended: the offer may go, the merged progress changes the totals.</summary>
        private void OnAccountChanged()
        {
            if (_flow.State == GameFlowState.MainMenu) RefreshMenus();
        }

        /// <summary>Tile caption of a development level: its id without the usual "Level_" prefix.</summary>
        private static string DevCaption(string levelId)
        {
            const string prefix = "Level_";
            return levelId.StartsWith(prefix, System.StringComparison.Ordinal) && levelId.Length > prefix.Length
                ? levelId.Substring(prefix.Length)
                : levelId;
        }

        /// <summary>
        /// The level after the current one in the catalog, if it is open (completing opens it); after the tutorial — the
        /// first level.
        /// </summary>
        private string NextLevelId()
        {
            var levels = _catalog.Levels;
            if (_flow.IsTutorialLevel) return levels.Count > 0 ? levels[0].LevelId : null;
            for (var i = 0; i + 1 < levels.Count; i++)
            {
                if (levels[i].LevelId != _flow.CurrentLevelId) continue;
                var next = levels[i + 1].LevelId;
                return _progress.IsUnlocked(next) || Debug.isDebugBuild ? next : null;
            }

            return null;
        }

        /// <summary>
        /// "LEVEL 3": levels are shown by their place in the catalog (the display name of a level is for development
        /// only, decided by the user); a level outside the catalog (tests) — by its id.
        /// </summary>
        private string LevelCaption(string levelId)
        {
            var levels = _catalog.Levels;
            for (var i = 0; i < levels.Count; i++)
                if (levels[i].LevelId == levelId) return _texts.Format(TextKeys.HudLevel, i + 1);
            if (levelId != null && _catalog.Tutorial?.LevelId == levelId) return _texts.Get(TextKeys.HudTutorial);
            return levelId ?? string.Empty;
        }

        /// <summary>The language can change only over the menu or the pause screen (settings): the HUD caption.</summary>
        private void OnLanguageChanged()
        {
            var state = _flow.State;
            if (state == GameFlowState.Paused || state == GameFlowState.Playing || state == GameFlowState.Tip)
                _ui.Hud.SetLevelName(LevelCaption(_flow.CurrentLevelId));
        }

        /// <summary>
        /// Play starts the first open level not completed yet at once (one click to gameplay — site rules); when every
        /// level is completed it opens the level select.
        /// </summary>
        private void OnPlayClicked()
        {
            if (_flow.State != GameFlowState.MainMenu) return;
            var next = LevelToPlay();
            if (next != null) _flow.StartLevel(next).Forget();
            else OnLevelsClicked();
        }

        private void OnLevelsClicked()
        {
            if (_flow.State != GameFlowState.MainMenu) return;
            _levelSelectOpen = true;
            Show(_flow.State);
        }

        private string LevelToPlay()
        {
            foreach (var level in _catalog.Levels)
                if (_progress.IsUnlocked(level.LevelId) && !_progress.IsCompleted(level.LevelId))
                    return level.LevelId;
            return null;
        }

        private void OnLevelSelectBack()
        {
            _levelSelectOpen = false;
            Show(_flow.State);
        }

        private void OnLevelSelected(string levelId) => _flow.StartLevel(levelId).Forget();

        private void OnTutorialClicked()
        {
            var tutorial = _catalog.Tutorial;
            if (_flow.State == GameFlowState.MainMenu && tutorial != null) _flow.StartLevel(tutorial.LevelId).Forget();
        }

        /// <summary>Enter / Space / gamepad South close the tutorial window (not right after it opened).</summary>
        private void OnConfirmRequested()
        {
            if (_flow.State == GameFlowState.Tip && _ui.Tutorial != null && _ui.Tutorial.CanContinue) _flow.CloseTip();
        }

        private void OnRetryRequested() => _flow.RetryLevel().Forget();

        private void OnNextRequested()
        {
            var next = NextLevelId();
            if (next != null) _flow.StartLevel(next).Forget();
        }

        /// <summary>The result's ×2: a rewarded ad, then the run's money once more (stage 6d).</summary>
        private void OnDoubleRequested()
        {
            if (!_flow.CanDoubleReward) return;
            DoubleAsync().Forget();
        }

        private async UniTaskVoid DoubleAsync()
        {
            _ui.Result.SetDoubleBusy(true);
            var doubled = false;
            try
            {
                doubled = await _flow.DoubleRewardAsync(System.Threading.CancellationToken.None);
            }
            finally
            {
                if (_ui != null && _ui.Result != null)
                {
                    _ui.Result.SetDoubleBusy(false);
                    _ui.Result.SetDoubled(doubled);
                }
            }
        }

        private void OnResetProgressRequested()
        {
            _progress.ResetProgress();
            if (_flow.State == GameFlowState.MainMenu) RefreshMenus();
        }

        private void OnMenuRequested() => _flow.ExitToMenu().Forget();
    }
}
