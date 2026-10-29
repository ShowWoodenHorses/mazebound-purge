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
    /// and turns screen events into GameFlow commands. The development level list (stage 11a, DEV in the menu of
    /// development builds) is part of the main menu state: DEV opens it over the menu, Back returns. The tutorial (started
    /// from that list until stage 12) has its own caption, its window pauses the level
    /// (<see cref="GameFlowState.Tip"/>; Continue, Enter / Space / gamepad South close it) and its completion shows only
    /// "Tutorial complete" with Next leading to the first level. A saved run (stage 6e): «Продолжить» in the menu, a new run
    /// (Play, a level, Retry, Next) asks first in <see cref="ConfirmScreen"/>. Play opens the run's choice and preparation
    /// (stage 11б, <see cref="RunSetupPresenter"/>) over the menu; Next opens it on the next level.
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
        private readonly RunSetupPresenter _setup;
        private readonly List<KeyValuePair<string, string>> _devLevels = new List<KeyValuePair<string, string>>();
        private bool _devLevelsOpen;
        private bool _initialized;

        public ScreenRouter(GameFlow flow, ILevelCatalog catalog, UIRoot ui, IProgressService progress,
            LocalizationService texts, IInputService input = null, AccountService account = null,
            RunSetupPresenter setup = null)
        {
            _setup = setup;
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
            _ui.MainMenu.ContinueClicked += OnContinueClicked;
            _ui.MainMenu.PlayClicked += OnPlayClicked;
            _ui.MainMenu.DevClicked += OnDevClicked;
            _ui.MainMenu.DebugResetProgressClicked += OnResetProgressRequested;
            _ui.MainMenu.SignInClicked += OnSignInClicked;
            if (_account != null) _account.Changed += OnAccountChanged;
            if (_ui.Tutorial != null) _ui.Tutorial.ContinueClicked += _flow.CloseTip;
            if (_input != null) _input.ConfirmRequested += OnConfirmRequested;
            if (_ui.RotateDevice != null) _ui.RotateDevice.Changed += OnRotateDeviceChanged;
            if (_ui.DevLevels != null)
            {
                _ui.DevLevels.LevelSelected += OnLevelSelected;
                _ui.DevLevels.BackClicked += OnDevLevelsBack;
            }
            if (_setup != null)
            {
                _setup.Initialize();
                _setup.FightRequested += OnFightRequested;
                _setup.BackRequested += OnSetupBack;
            }
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

            _ui.MainMenu.ContinueClicked -= OnContinueClicked;
            _ui.MainMenu.PlayClicked -= OnPlayClicked;
            _ui.MainMenu.DevClicked -= OnDevClicked;
            if (_ui.Tutorial != null) _ui.Tutorial.ContinueClicked -= _flow.CloseTip;
            _ui.MainMenu.DebugResetProgressClicked -= OnResetProgressRequested;
            _ui.MainMenu.SignInClicked -= OnSignInClicked;
            if (_ui.DevLevels != null)
            {
                _ui.DevLevels.LevelSelected -= OnLevelSelected;
                _ui.DevLevels.BackClicked -= OnDevLevelsBack;
            }
            if (_setup != null)
            {
                _setup.FightRequested -= OnFightRequested;
                _setup.BackRequested -= OnSetupBack;
            }
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

            if (!menu)
            {
                _devLevelsOpen = false;
                _setup?.Close();
            }
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

            var setupOpen = _setup != null && _setup.IsOpen;
            _ui.MainMenu.SetVisible(menu && !_devLevelsOpen && !setupOpen);
            if (_ui.DevLevels != null) _ui.DevLevels.SetVisible(menu && _devLevelsOpen);
            if (_ui.RunSetup != null) _ui.RunSetup.SetVisible(menu && setupOpen);
            _ui.Loading.SetVisible(loading);
            _ui.Hud.SetVisible(inLevel);
            _ui.Pause.SetVisible(state == GameFlowState.Paused);
            _ui.Result.SetVisible(result);
            _ui.Error.SetVisible(error);
            _ui.Map.SetVisible(state == GameFlowState.Map);
            if (_ui.Cards != null) _ui.Cards.SetVisible(state == GameFlowState.Cards);
            if (_ui.Death != null) _ui.Death.SetVisible(state == GameFlowState.Death);
            if (_ui.Confirm != null && (_ui.Confirm.IsOpen || _ui.Confirm.IsVisible)) _ui.Confirm.Close();
            if (_ui.Tutorial != null)
            {
                _ui.Tutorial.SetVisible(inLevel && _flow.IsTutorialLevel);
                _ui.Tutorial.SetDialogVisible(state == GameFlowState.Tip);
            }
        }

        /// <summary>
        /// The main menu's money and buttons; in development builds (and the editor) the level list: the tutorial, every
        /// catalog level (locked ones too, so any level can be tested) and the development levels.
        /// </summary>
        private void RefreshMenus()
        {
            var levels = _catalog.Levels;
            _ui.MainMenu.SetMoney(_progress.Money);
            _ui.MainMenu.SetPlayable(levels.Count > 0);
            _ui.MainMenu.SetContinueAvailable(_flow.HasSavedRun);
            RefreshSignIn();
            if (_ui.DevLevels == null || !Debug.isDebugBuild) return;

            _devLevels.Clear();
            if (_catalog.Tutorial != null)
                _devLevels.Add(new KeyValuePair<string, string>(_catalog.Tutorial.LevelId, _texts.Get(TextKeys.MenuTutorial)));
            for (var i = 0; i < levels.Count; i++)
                _devLevels.Add(new KeyValuePair<string, string>(levels[i].LevelId, _texts.Format(TextKeys.HudLevel, i + 1)));
            var devLevels = _catalog.DevLevels;
            for (var i = 0; i < devLevels.Count; i++)
                _devLevels.Add(new KeyValuePair<string, string>(devLevels[i].LevelId, DevCaption(devLevels[i].LevelId)));
            _ui.DevLevels.SetLevels(_devLevels);
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
        /// Play opens the run's choice and preparation on the first open level not completed yet (when every level is
        /// completed — the last one); without that screen (test scenes) it starts that level at once.
        /// </summary>
        private void OnPlayClicked()
        {
            if (_flow.State != GameFlowState.MainMenu) return;
            var next = LevelToPlay();
            if (next == null) return;
            if (_setup != null && _setup.IsAvailable) OpenSetup(next);
            else StartNewRun(() => _flow.StartLevel(next).Forget());
        }

        private void OpenSetup(string levelId)
        {
            _devLevelsOpen = false;
            _setup.Open(levelId);
            Show(_flow.State);
        }

        private void OnSetupBack()
        {
            _setup.Close();
            Show(_flow.State);
        }

        /// <summary>«В БОЙ» of the preparation: a new run with the choice (asked first over a saved one).</summary>
        private void OnFightRequested(string levelId, Gameplay.Level.LevelLaunchOptions options, int cost)
        {
            if (_flow.State != GameFlowState.MainMenu) return;
            StartNewRun(() => _flow.StartLevel(levelId, options, cost).Forget());
        }

        /// <summary>«Продолжить»: the saved run (stage 6e).</summary>
        private void OnContinueClicked()
        {
            if (_flow.State == GameFlowState.MainMenu && _flow.HasSavedRun) _flow.ContinueRun().Forget();
        }

        /// <summary>
        /// A new run while one is played or saved (stage 6e): asked first — «Текущий забег будет потерян» (its money goes
        /// to the save); otherwise at once.
        /// </summary>
        private void StartNewRun(Action start)
        {
            if ((_flow.IsRunActive || _flow.HasSavedRun) && _ui.Confirm != null)
            {
                _ui.Confirm.Open(_texts.Get(TextKeys.ConfirmNewRun), start);
                _ui.RequestSound(UiSoundKind.Window);
            }
            else
                start();
        }

        private void OnDevClicked()
        {
            if (_flow.State != GameFlowState.MainMenu || _ui.DevLevels == null || !Debug.isDebugBuild) return;
            _devLevelsOpen = true;
            Show(_flow.State);
        }

        private string LevelToPlay()
        {
            var levels = _catalog.Levels;
            foreach (var level in levels)
                if (_progress.IsUnlocked(level.LevelId) && !_progress.IsCompleted(level.LevelId))
                    return level.LevelId;
            return levels.Count > 0 ? levels[levels.Count - 1].LevelId : null;
        }

        private void OnDevLevelsBack()
        {
            _devLevelsOpen = false;
            Show(_flow.State);
        }

        /// <summary>A level from the development list; the tutorial is outside the runs and starts at once.</summary>
        private void OnLevelSelected(string levelId)
        {
            if (_flow.State != GameFlowState.MainMenu) return;
            if (_catalog.Tutorial?.LevelId == levelId) _flow.StartLevel(levelId).Forget();
            else StartNewRun(() => _flow.StartLevel(levelId).Forget());
        }

        /// <summary>Enter / Space / gamepad South close the tutorial window (not right after it opened).</summary>
        private void OnConfirmRequested()
        {
            if (_flow.State == GameFlowState.Tip && _ui.Tutorial != null && _ui.Tutorial.CanContinue) _flow.CloseTip();
        }

        private void OnRetryRequested() => StartNewRun(() => _flow.RetryLevel().Forget());

        /// <summary>Next: the menu with the preparation on the next level (stage 11б); without it — that level at once.</summary>
        private void OnNextRequested()
        {
            var next = NextLevelId();
            if (next == null) return;
            // A level still closed (development builds offer it) is started at once: the preparation shows only open ones.
            if (_setup != null && _setup.IsAvailable && _progress.IsUnlocked(next)) OpenSetupFromResult(next).Forget();
            else StartNewRun(() => _flow.StartLevel(next).Forget());
        }

        private async UniTaskVoid OpenSetupFromResult(string levelId)
        {
            await _flow.ExitToMenu();
            if (_flow.State == GameFlowState.MainMenu) OpenSetup(levelId);
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
