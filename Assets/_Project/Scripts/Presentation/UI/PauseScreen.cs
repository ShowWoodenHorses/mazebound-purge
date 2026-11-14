using System;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    public sealed class PauseScreen : UIScreen
    {
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _exitButton;

        [Header("Debug (development builds only)")]
        [SerializeField] private GameObject _debugGroup;
        [SerializeField] private Button _debugCompleteButton;
        [SerializeField] private Button _debugFailButton;
        [SerializeField] private Button _cheatsButton;

        public event Action ResumeClicked;
        public event Action RetryClicked;
        public event Action SettingsClicked;
        public event Action ExitClicked;
        public event Action DebugCompleteClicked;
        public event Action DebugFailClicked;

        public event Action CheatsClicked;

        public void SetCheatsAvailable(bool available)
        {
            if (_cheatsButton != null) _cheatsButton.gameObject.SetActive(available);
        }

        private void Awake()
        {
            Bind(_resumeButton, () => ResumeClicked?.Invoke());
            Bind(_retryButton, () => RetryClicked?.Invoke());
            Bind(_settingsButton, () => SettingsClicked?.Invoke());
            Bind(_exitButton, () => ExitClicked?.Invoke());
            Bind(_debugCompleteButton, () => DebugCompleteClicked?.Invoke());
            Bind(_debugFailButton, () => DebugFailClicked?.Invoke());
            Bind(_cheatsButton, () => CheatsClicked?.Invoke());
            if (_debugGroup != null)
                _debugGroup.SetActive(Debug.isDebugBuild);
        }
    }
}
