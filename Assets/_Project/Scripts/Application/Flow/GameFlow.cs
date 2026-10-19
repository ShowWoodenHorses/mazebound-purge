using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Application.Levels;
using Purge.Application.Platform;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Core.Common;
using Purge.Core.Level;
using Purge.Core.Validation;
using Purge.Gameplay.Level;

namespace Purge.Application.Flow
{
    public enum GameFlowState
    {
        None = 0,
        Initializing = 1,
        MainMenu = 2,
        Loading = 3,
        Playing = 4,
        Paused = 5,
        Completed = 6,
        Failed = 7,
        Error = 8,

        /// <summary>The fullscreen map is open: gameplay is paused (ТЗ §60).</summary>
        Map = 10,

        /// <summary>A tutorial window is shown over the level: gameplay is paused until it is closed.</summary>
        Tip = 11,

        /// <summary>The run's level-up: gameplay is paused until a card is chosen (stage 6c).</summary>
        Cards = 12,

        /// <summary>The player is dead: gameplay is paused in the death window until a revive or giving up (stage 6d).</summary>
        Death = 13,
    }

    /// <summary>
    /// Game lifecycle (ТЗ §8). Async transitions (initialize, start level, menu) run one at a time: a new one
    /// cancels the current one and waits until it has cleaned up. Any exception in a transition is a critical
    /// error (ТЗ §109): the level is fully unloaded and the flow goes to <see cref="GameFlowState.Error"/>.
    /// </summary>
    public sealed class GameFlow : IDisposable
    {
        private const int MaxReportedValidationErrors = 3;

        private readonly IReadOnlyList<IApplicationService> _services;
        private readonly ILevelCatalog _catalog;
        private readonly IAddressablesService _addressables;
        private readonly ILevelSessionFactory _sessionFactory;
        private readonly IProgressService _progress;
        private readonly AdsService _ads;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private CancellationTokenSource _transition;
        private UniTask _transitionDone = UniTask.CompletedTask;
        private ILevelSession _session;
        private LevelLaunchOptions _launchOptions = LevelLaunchOptions.Default;
        private bool _disposed;

        public GameFlow(IReadOnlyList<IApplicationService> services, ILevelCatalog catalog,
            IAddressablesService addressables, ILevelSessionFactory sessionFactory, IProgressService progress,
            AdsService ads = null)
        {
            _ads = ads;
            _progress = progress;
            _services = services;
            _catalog = catalog;
            _addressables = addressables;
            _sessionFactory = sessionFactory;
        }

        public GameFlowState State { get; private set; } = GameFlowState.None;

        /// <summary>Level being loaded or played; kept after the level ends so it can be retried.</summary>
        public string CurrentLevelId { get; private set; }

        /// <summary>Description of the last critical error, shown by the error screen.</summary>
        public string ErrorMessage { get; private set; }

        public bool HasLevel => _session != null;

        /// <summary>The current level is the tutorial (<see cref="ILevelCatalog.Tutorial"/>).</summary>
        public bool IsTutorialLevel => CurrentLevelId != null && _catalog.Tutorial?.LevelId == CurrentLevelId;

        /// <summary>Result of the last ended run (waves, kills, money, score); null until a level ends.</summary>
        public LevelResult? LastResult { get; private set; }

        /// <summary>The level of the last ended run had been completed before it.</summary>
        public bool LastWasCompletedBefore { get; private set; }

        /// <summary>The last result's money was doubled by a rewarded ad (<see cref="DoubleRewardAsync"/>).</summary>
        public bool LastRewardDoubled { get; private set; }

        /// <summary>The result window may offer ×2: a run ended with money, not doubled yet.</summary>
        public bool CanDoubleReward =>
            (State == GameFlowState.Completed || State == GameFlowState.Failed) && !LastRewardDoubled &&
            LastResult is { } result && result.Money > 0;

        /// <summary>Placement of the result's rewarded ×2.</summary>
        public const string DoublePlacement = "result_double";

        public event Action<GameFlowState> StateChanged;

        public UniTask InitializeApplication() => RunTransition("InitializeApplication", async cancellation =>
        {
            if (State != GameFlowState.None)
                throw new InvalidOperationException("Application is already initialized.");

            SetState(GameFlowState.Initializing);
            foreach (var service in _services)
            {
                GameLog.Info(LogChannel.Bootstrap, $"Initializing {service.Name}.");
                await service.InitializeAsync(cancellation);
            }

            GameLog.Info(LogChannel.Bootstrap, $"Application initialized: {_catalog.Levels.Count} level(s) in catalog.");
        });

        public UniTask OpenMainMenu() => RunTransition("OpenMainMenu", async cancellation =>
        {
            if (HasLevel)
            {
                var levelEnded = State == GameFlowState.Completed || State == GameFlowState.Failed;
                if (!levelEnded) CreditLeftRun();
                SetState(GameFlowState.Loading);
                await UnloadLevelAsync();
                // From the result window: an interstitial where the site wants one at this break (over the loading screen).
                if (levelEnded && _ads != null) await _ads.ShowOnMenuReturnAsync(cancellation);
            }

            cancellation.ThrowIfCancellationRequested();
            SetState(GameFlowState.MainMenu);
        });

        /// <param name="options">Start point and seed; default = random start (ТЗ §61).</param>
        public UniTask StartLevel(string levelId, LevelLaunchOptions options = null) =>
            RunTransition($"StartLevel({levelId})",
                cancellation => StartLevelAsync(levelId, options ?? LevelLaunchOptions.Default, cancellation));

        /// <summary>Restarts the current level with the same launch options (a forced start stays forced).</summary>
        public UniTask RetryLevel() =>
            CurrentLevelId != null ? StartLevel(CurrentLevelId, _launchOptions) : UniTask.CompletedTask;

        public UniTask ExitToMenu() => OpenMainMenu();

        /// <summary>Starts a loaded level. Called by <see cref="StartLevel"/> once all load steps are done.</summary>
        public void StartGameplay()
        {
            if (State != GameFlowState.Loading || _session == null)
                throw new InvalidOperationException($"Gameplay can start only for a loaded level (state: {State}).");

            _session.StartGameplay();
            SetState(GameFlowState.Playing);
        }

        public void PauseGameplay()
        {
            if (State != GameFlowState.Playing) return;
            _session.SetPaused(true);
            SetState(GameFlowState.Paused);
        }

        public void ResumeGameplay()
        {
            if (State != GameFlowState.Paused) return;
            _session.SetPaused(false);
            SetState(GameFlowState.Playing);
        }

        /// <summary>Opens the fullscreen map; gameplay is paused while it is open (ТЗ §60).</summary>
        public void OpenMap()
        {
            if (State != GameFlowState.Playing) return;
            _session.SetPaused(true);
            SetState(GameFlowState.Map);
        }

        public void CloseMap()
        {
            if (State != GameFlowState.Map) return;
            if (_ads != null && _ads.IsShowing) return; // a rewarded ad is asked for: the level waits for it (site rules)
            _session.SetPaused(false);
            SetState(GameFlowState.Playing);
        }

        /// <summary>Shows a tutorial window: gameplay pauses until <see cref="CloseTip"/>.</summary>
        public void OpenTip()
        {
            if (State != GameFlowState.Playing) return;
            _session.SetPaused(true);
            SetState(GameFlowState.Tip);
        }

        public void CloseTip()
        {
            if (State != GameFlowState.Tip) return;
            _session.SetPaused(false);
            SetState(GameFlowState.Playing);
        }

        /// <summary>Shows the cards of a level-up: gameplay pauses until <see cref="CloseCards"/>.</summary>
        public void OpenCards()
        {
            if (State != GameFlowState.Playing) return;
            _session.SetPaused(true);
            SetState(GameFlowState.Cards);
        }

        public void CloseCards()
        {
            if (State != GameFlowState.Cards) return;
            if (_ads != null && _ads.IsShowing) return; // the reroll's rewarded ad is still showing
            _session.SetPaused(false);
            SetState(GameFlowState.Playing);
        }

        /// <summary>The player died (stage 6d): gameplay pauses in the death window until <see cref="CloseDeath"/> or the end.</summary>
        public void OpenDeath()
        {
            if (State != GameFlowState.Playing) return;
            _session.SetPaused(true);
            SetState(GameFlowState.Death);
        }

        /// <summary>The player revived: the game goes on.</summary>
        public void CloseDeath()
        {
            if (State != GameFlowState.Death) return;
            if (_ads != null && _ads.IsShowing) return;
            _session.SetPaused(false);
            SetState(GameFlowState.Playing);
        }

        /// <summary>
        /// The result's ×2 (ТЗ: rewarded ×2 к заработку, при победе и при смерти): after the ad was watched the run's money
        /// is added once more. True when doubled.
        /// </summary>
        public async UniTask<bool> DoubleRewardAsync(CancellationToken cancellation)
        {
            if (!CanDoubleReward) return false;
            var money = LastResult.Value.Money;
            LastRewardDoubled = true; // no second offer while the ad is shown
            var watched = _ads == null || await _ads.ShowRewardedAsync(DoublePlacement, cancellation);
            if (!watched)
            {
                LastRewardDoubled = false;
                return false;
            }

            try
            {
                _progress.AddMoney(money);
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Save, e, "Saving the doubled money failed");
            }

            return true;
        }

        public void CompleteLevel() => EndLevel(GameFlowState.Completed);

        public void FailLevel() => EndLevel(GameFlowState.Failed);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _lifetime.Cancel();
            _transition?.Cancel();
            if (_session != null)
            {
                _session.Finished -= OnLevelFinished;
                _session.Dispose();
                _session = null;
            }

            _lifetime.Dispose();
            StateChanged = null;
        }

        private async UniTask StartLevelAsync(string levelId, LevelLaunchOptions options, CancellationToken cancellation)
        {
            var fromMenu = State == GameFlowState.MainMenu;
            // Retry from the pause: the left run's money stays (until the run save of stage 6e).
            if (_session != null && State != GameFlowState.Completed && State != GameFlowState.Failed) CreditLeftRun();
            SetState(GameFlowState.Loading);
            CurrentLevelId = levelId;
            _launchOptions = options;
            LastResult = null;
            LastWasCompletedBefore = false;
            LastRewardDoubled = false;
            GameLog.Info(LogChannel.LevelLoading, $"Loading level '{levelId}'.");

            // Stop current gameplay, dispose the old LevelScope, release its Addressables.
            await UnloadLevelAsync();

            // An interstitial before the level (retry, next, from the menu) when the ad policy allows; over the loading screen.
            if (_ads != null) await _ads.ShowBeforeLevelAsync(levelId, fromMenu, cancellation);

            var entry = _catalog.Find(levelId)
                        ?? throw new LevelLoadException($"Level '{levelId}' is not in the level catalog.");

            try
            {
                var levelAssets = _addressables.CreateOwner("Level " + levelId);
                LevelData level;
                try
                {
                    level = await levelAssets.LoadAsync<LevelData>(entry.Address, cancellation);
                    EnsurePlayable(level);
                }
                catch
                {
                    levelAssets.Dispose();
                    throw;
                }

                _session = await _sessionFactory.CreateAsync(level, levelAssets, options, cancellation);
                _session.Finished += OnLevelFinished;
                await _session.LoadAsync(cancellation);
                cancellation.ThrowIfCancellationRequested();
            }
            catch
            {
                // Never leave a partially initialized level (ТЗ §109).
                await UnloadLevelAsync();
                throw;
            }

            GameLog.Info(LogChannel.LevelLoading, $"Level '{levelId}' loaded.");
            StartGameplay();
        }

        private static void EnsurePlayable(LevelData level)
        {
            var report = LevelValidator.Validate(level);
            if (report.IsValid)
            {
                if (report.WarningCount > 0)
                    GameLog.Info(LogChannel.Validation, $"Level '{level.name}' has {report.WarningCount} validation warning(s).");
                return;
            }

            var message = new StringBuilder($"Level '{level.name}' has {report.ErrorCount} validation error(s):");
            var listed = 0;
            foreach (var issue in report.Issues)
            {
                if (issue.Severity != ValidationSeverity.Error) continue;
                message.Append("\n- ").Append(issue.Message);
                if (++listed == MaxReportedValidationErrors) break;
            }

            throw new LevelLoadException(message.ToString());
        }

        private async UniTask UnloadLevelAsync()
        {
            var session = _session;
            if (session == null) return;
            _session = null;

            session.Finished -= OnLevelFinished;
            try
            {
                session.StopGameplay();
                await session.UnloadAsync();
                GameLog.Info(LogChannel.LevelUnloading, $"Level '{CurrentLevelId}' unloaded.");
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.LevelUnloading, e, $"Unloading level '{CurrentLevelId}' failed");
            }
        }

        private void OnLevelFinished(LevelOutcome outcome)
        {
            if (outcome == LevelOutcome.Completed) CompleteLevel();
            else FailLevel();
        }

        /// <summary>
        /// ТЗ §88–89, stage 6d: stop gameplay → the run's result → save (money and kills always — при смерти собранное
        /// остаётся; the completion when won) → result window.
        /// </summary>
        private void EndLevel(GameFlowState result)
        {
            if (State != GameFlowState.Playing && State != GameFlowState.Paused && State != GameFlowState.Map &&
                State != GameFlowState.Tip && State != GameFlowState.Cards && State != GameFlowState.Death)
                return;

            _session.StopGameplay();
            var outcome = result == GameFlowState.Completed ? LevelOutcome.Completed : LevelOutcome.Failed;
            var levelResult = _session.GetResult(outcome);
            LastResult = levelResult;
            LastRewardDoubled = false;
            LastWasCompletedBefore = _progress.IsCompleted(CurrentLevelId);
            GameLog.Info(LogChannel.GameFlow, $"Level '{CurrentLevelId}' ended: {levelResult}.");

            try
            {
                _progress.RecordRun(CurrentLevelId, levelResult);
            }
            catch (Exception e)
            {
                // Losing a save must not take the result screen away.
                GameLog.Exception(LogChannel.Save, e, $"Recording the run of '{CurrentLevelId}' failed");
            }

            SetState(result);
        }

        /// <summary>The run is left from the pause (menu, retry): its money goes to the save, no result.</summary>
        private void CreditLeftRun()
        {
            try
            {
                var money = _session.GetResult(LevelOutcome.Failed).Money;
                if (money <= 0) return;
                GameLog.Info(LogChannel.GameFlow, $"Level '{CurrentLevelId}' left with {money} money.");
                _progress.AddMoney(money);
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Save, e, $"Saving the money of the left run of '{CurrentLevelId}' failed");
            }
        }

        private async UniTask RunTransition(string name, Func<CancellationToken, UniTask> body)
        {
            if (_disposed) return;

            _transition?.Cancel();
            var transition = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _transition = transition;

            var previous = _transitionDone;
            var done = new UniTaskCompletionSource();
            _transitionDone = done.Task;

            try
            {
                await previous;
                transition.Token.ThrowIfCancellationRequested();
                await body(transition.Token);
            }
            catch (OperationCanceledException) when (transition.IsCancellationRequested)
            {
                GameLog.Info(LogChannel.GameFlow, $"{name} cancelled.");
            }
            catch (Exception e)
            {
                await EnterErrorAsync(name, e);
            }
            finally
            {
                if (_transition == transition) _transition = null;
                transition.Dispose();
                done.TrySetResult();
            }
        }

        private async UniTask EnterErrorAsync(string operation, Exception exception)
        {
            GameLog.Exception(LogChannel.GameFlow, exception, $"{operation} failed");
            await UnloadLevelAsync();
            if (_disposed) return;

            ErrorMessage = exception.Message;
            SetState(GameFlowState.Error);
        }

        private void SetState(GameFlowState state)
        {
            if (State == state) return;
            GameLog.Info(LogChannel.GameFlow, $"{State} → {state}");
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
