using System;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>"Finish level?" (ТЗ §62). No closes the window and gameplay continues.</summary>
    public sealed class ConfirmExitScreen : UIScreen
    {
        [SerializeField] private Button _yesButton;
        [SerializeField] private Button _noButton;

        public event Action YesClicked;
        public event Action NoClicked;

        private void Awake()
        {
            Bind(_yesButton, () => YesClicked?.Invoke());
            Bind(_noButton, () => NoClicked?.Invoke());
        }
    }
}
