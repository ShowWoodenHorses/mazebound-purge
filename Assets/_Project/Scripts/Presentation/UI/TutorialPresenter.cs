using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Flow;
using Maze.Application.Services;
using Maze.Core.Level;
using Maze.Gameplay.Level;
using Maze.Gameplay.Tutorial;
using Maze.Presentation.Localization;
using Maze.Presentation.Visual;
using UnityEngine;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// Shows the tutorial (<see cref="TutorialSystem"/>) on <see cref="UIRoot.Tutorial"/>: a step with a window opens it
    /// a moment after the step began (<see cref="DialogDelay"/> of play time: the previous action finishes first) and
    /// pauses the game (<see cref="GameFlow.OpenTip"/>); the reminder line, the arrow over the target and the ring around
    /// the control stay while the step is on. Touch screens get the "<key>.touch" texts when the table has them.
    /// Reports the map being opened to the tutorial. Inactive outside the tutorial level.
    /// </summary>
    public sealed class TutorialPresenter : ILevelLoadStep, ILevelTickable, ILevelLateTickable, IDisposable
    {
        /// <summary>Play time from a step's start to its window (the first step: from the level start).</summary>
        private const float DialogDelay = 0.7f;

        private const float FirstDialogDelay = 0.35f;

        /// <summary>Height of the arrow's target over the floor, metres (about the middle of a body).</summary>
        private const float TargetHeight = 0.9f;

        private readonly TutorialSystem _tutorial;
        private readonly UIRoot _ui;
        private readonly GameFlow _flow;
        private readonly LocalizationService _texts;
        private readonly TopDownCamera _camera;
        private float _dialogIn = -1f;
        private bool _bound;

        public TutorialPresenter(TutorialSystem tutorial, UIRoot ui, GameFlow flow, LocalizationService texts,
            TopDownCamera camera)
        {
            _tutorial = tutorial;
            _ui = ui;
            _flow = flow;
            _texts = texts;
            _camera = camera;
        }

        /// <summary>Before the tutorial starts its first step (stage SpawnZombies).</summary>
        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        /// <summary>The screen, or a real null (also when destroyed on quit: <c>?.</c> must not reach a dead object).</summary>
        private TutorialScreen Screen
        {
            get
            {
                var screen = _ui != null ? _ui.Tutorial : null;
                return screen != null ? screen : null;
            }
        }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            Screen?.Clear();
            if (!_tutorial.IsActive || _bound) return UniTask.CompletedTask;

            _tutorial.StepStarted += OnStepStarted;
            _tutorial.Finished += OnFinished;
            _flow.StateChanged += OnFlowStateChanged;
            _texts.LanguageChanged += ShowTexts;
            _bound = true;
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            if (_dialogIn < 0f) return;
            _dialogIn -= deltaTime;
            if (_dialogIn > 0f || _flow.State != GameFlowState.Playing) return;

            _dialogIn = -1f;
            ShowTexts();
            _flow.OpenTip();
        }

        public void LateTick(float deltaTime)
        {
            var screen = Screen;
            if (screen == null || !_bound) return;

            var camera = _camera != null ? _camera.Camera : null;
            if (camera == null || !_tutorial.TryGetTarget(out var target))
            {
                screen.SetPointer(false, default);
                return;
            }

            var point = camera.WorldToScreenPoint(new Vector3(target.x, TargetHeight, target.y));
            screen.SetPointer(point.z > 0f, point);
        }

        public void Dispose()
        {
            if (_bound)
            {
                _tutorial.StepStarted -= OnStepStarted;
                _tutorial.Finished -= OnFinished;
                _flow.StateChanged -= OnFlowStateChanged;
                _texts.LanguageChanged -= ShowTexts;
                _bound = false;
            }

            Screen?.Clear();
        }

        private void OnStepStarted(int index)
        {
            var step = _tutorial.Current;
            _dialogIn = step != null && step.Dialog ? (index == 0 ? FirstDialogDelay : DialogDelay) : -1f;
            Screen?.SetHighlight(step?.Highlight ?? TutorialHighlight.None);
            ShowTexts();
        }

        private void OnFinished()
        {
            _dialogIn = -1f;
            var screen = Screen;
            if (screen == null) return;
            screen.SetHint(null);
            screen.SetHighlight(TutorialHighlight.None);
        }

        private void OnFlowStateChanged(GameFlowState state)
        {
            if (state == GameFlowState.Map) _tutorial.ReportMapOpened();
        }

        /// <summary>The step's window text and its reminder, in the current language and for the current controls.</summary>
        private void ShowTexts()
        {
            var screen = Screen;
            var step = _tutorial.Current;
            if (screen == null) return;
            if (step == null)
            {
                screen.SetHint(null);
                return;
            }

            screen.SetDialog(_texts.Get(TextKeys.HudTutorial), Text(step.Text));
            var hintKey = step.Text + TextKeys.HintSuffix;
            screen.SetHint(_texts.Has(hintKey) ? Text(hintKey) : null);
        }

        private string Text(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            var touchKey = key + TextKeys.TouchSuffix;
            return _texts.Get(HudScreen.TouchShown && _texts.Has(touchKey) ? touchKey : key);
        }
    }
}
