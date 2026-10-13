using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// Main menu (minimal by the user's choice): the title, Play (starts the next level at once), Levels (the level
    /// select), Tutorial (when the game has one) and Settings; total stars and
    /// zombies killed at the bottom left; "Debug: reset progress" (development builds) and the version at the bottom
    /// right; at the top right — signing in to the site's account (Yandex Games) while the player is not signed in.
    /// </summary>
    public sealed class MainMenuScreen : UIScreen
    {
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _levelsButton;
        [SerializeField] private Button _tutorialButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private GameObject _summary;
        [SerializeField] private TMP_Text _stars;
        [SerializeField] private TMP_Text _kills;
        [SerializeField] private TMP_Text _version;

        [Header("Account (sites with accounts)")]
        [SerializeField] private GameObject _account;
        [SerializeField] private Button _signInButton;

        [Header("Debug (development builds only)")]
        [SerializeField] private Button _debugResetProgressButton;

        public event Action PlayClicked;
        public event Action LevelsClicked;
        public event Action TutorialClicked;
        public event Action SettingsClicked;
        public event Action DebugResetProgressClicked;
        public event Action SignInClicked;

        private void Awake()
        {
            Bind(_playButton, () => PlayClicked?.Invoke());
            Bind(_levelsButton, () => LevelsClicked?.Invoke());
            Bind(_tutorialButton, () => TutorialClicked?.Invoke());
            Bind(_settingsButton, () => SettingsClicked?.Invoke());
            Bind(_debugResetProgressButton, () => DebugResetProgressClicked?.Invoke());
            Bind(_signInButton, () => SignInClicked?.Invoke());
            if (_debugResetProgressButton != null)
                _debugResetProgressButton.gameObject.SetActive(Debug.isDebugBuild);
            if (_version != null) _version.text = "v" + UnityEngine.Application.version;
        }

        /// <summary>Overall progress; hidden while the catalog is empty.</summary>
        public void SetSummary(int stars, int maxStars, int kills)
        {
            if (_summary != null) _summary.SetActive(maxStars > 0);
            if (_stars != null) _stars.text = stars + " / " + maxStars;
            if (_kills != null) _kills.text = kills.ToString();
        }

        /// <summary>The Tutorial button is there only when the catalog has a tutorial level.</summary>
        public void SetTutorialAvailable(bool available)
        {
            if (_tutorialButton != null) _tutorialButton.gameObject.SetActive(available);
        }

        /// <summary>The sign-in offer: shown while the player can sign in, inactive while a sign-in runs.</summary>
        public void SetSignIn(bool shown, bool interactable)
        {
            if (_account != null) _account.SetActive(shown);
            if (_signInButton != null) _signInButton.interactable = interactable;
        }

        /// <summary>Play is unavailable without levels (run Build / Sync in the Level Designer).</summary>
        public void SetPlayable(bool playable)
        {
            if (_playButton != null) _playButton.interactable = playable;
            if (_levelsButton != null) _levelsButton.interactable = playable;
        }
    }
}
