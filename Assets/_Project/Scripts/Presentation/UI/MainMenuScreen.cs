using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// Main menu (stage 11a, ТЗ А.10): the bottom bar of five tabs — Arsenal, Character, ▶ PLAY, Tasks, Shop — with
    /// «Продолжить» above PLAY while a run is saved; money, settings and the site's account at the top. The middle is
    /// transparent: the character on the podium (<see cref="Menu.MenuPodiumView"/>) shows through. Tabs that have no
    /// screen yet are disabled (Arsenal, Character — 11в, 11д) or hidden (Tasks — 16б, Shop — 17б). DEV (the level list)
    /// and "Debug: reset progress" — development builds only; the version at the bottom right.
    /// </summary>
    public sealed class MainMenuScreen : UIScreen
    {
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _arsenalButton;
        [SerializeField] private Button _characterButton;
        [SerializeField] private Button _tasksButton;
        [SerializeField] private Button _shopButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private TMP_Text _money;
        [SerializeField] private TMP_Text _version;

        [Header("Account (sites with accounts)")]
        [SerializeField] private GameObject _account;
        [SerializeField] private Button _signInButton;

        [Header("Development builds only")]
        [SerializeField] private Button _devButton;
        [SerializeField] private Button _debugResetProgressButton;

        public event Action ContinueClicked;
        public event Action PlayClicked;
        public event Action SettingsClicked;
        public event Action DevClicked;
        public event Action DebugResetProgressClicked;
        public event Action SignInClicked;

        private void Awake()
        {
            Bind(_continueButton, () => ContinueClicked?.Invoke());
            Bind(_playButton, () => PlayClicked?.Invoke());
            Bind(_settingsButton, () => SettingsClicked?.Invoke());
            Bind(_devButton, () => DevClicked?.Invoke());
            Bind(_debugResetProgressButton, () => DebugResetProgressClicked?.Invoke());
            Bind(_signInButton, () => SignInClicked?.Invoke());
            if (_devButton != null) _devButton.gameObject.SetActive(Debug.isDebugBuild);
            if (_debugResetProgressButton != null)
                _debugResetProgressButton.gameObject.SetActive(Debug.isDebugBuild);
            if (_version != null) _version.text = "v" + UnityEngine.Application.version;
            SetTabs(arsenal: false, character: false, tasks: false, shop: false);
        }

        /// <summary>The money kept between runs.</summary>
        public void SetMoney(int money)
        {
            if (_money != null) _money.text = money.ToString();
        }

        /// <summary>
        /// Tabs with a screen behind them: Arsenal and Character are shown always and enabled once they work, Tasks and
        /// Shop are hidden until then.
        /// </summary>
        public void SetTabs(bool arsenal, bool character, bool tasks, bool shop)
        {
            if (_arsenalButton != null) _arsenalButton.interactable = arsenal;
            if (_characterButton != null) _characterButton.interactable = character;
            if (_tasksButton != null) _tasksButton.gameObject.SetActive(tasks);
            if (_shopButton != null) _shopButton.gameObject.SetActive(shop);
        }

        /// <summary>«Продолжить» above Play while a run is saved (stage 6e); it is the main action then.</summary>
        public void SetContinueAvailable(bool available)
        {
            if (_continueButton != null) _continueButton.gameObject.SetActive(available);
            if (_playButton is Style.UiButton play) play.Accent = !available;
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
        }
    }
}
