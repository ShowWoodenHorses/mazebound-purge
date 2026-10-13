using System;
using Maze.Application.Services;

namespace Maze.Application.Flow
{
    /// <summary>
    /// Toggles pause from the Pause input action (Escape / Start / Android Back) and the map from the OpenMap action.
    /// Pause while the map is open closes the map; while a tutorial window is open — closes the window.
    /// </summary>
    public sealed class PauseController : IDisposable
    {
        private readonly IInputService _input;
        private readonly GameFlow _flow;
        private bool _initialized;

        public PauseController(IInputService input, GameFlow flow)
        {
            _input = input;
            _flow = flow;
        }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _input.PauseRequested += OnPauseRequested;
            _input.MapRequested += OnMapRequested;
        }

        public void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;
            _input.PauseRequested -= OnPauseRequested;
            _input.MapRequested -= OnMapRequested;
        }

        private void OnPauseRequested()
        {
            if (_flow.State == GameFlowState.Playing) _flow.PauseGameplay();
            else if (_flow.State == GameFlowState.Paused) _flow.ResumeGameplay();
            else if (_flow.State == GameFlowState.Map) _flow.CloseMap();
            else if (_flow.State == GameFlowState.Tip) _flow.CloseTip();
        }

        private void OnMapRequested()
        {
            if (_flow.State == GameFlowState.Playing) _flow.OpenMap();
            else if (_flow.State == GameFlowState.Map) _flow.CloseMap();
        }
    }
}
