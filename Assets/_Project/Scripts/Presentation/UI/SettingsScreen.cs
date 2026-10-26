using System;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Gameplay.Combat;
using Purge.Presentation.Localization;
using Purge.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    /// <summary>Pages of the settings screen.</summary>
    public enum SettingsTab
    {
        /// <summary>On-screen controls: scheme, stick, size, opacity, layout.</summary>
        Controls,

        /// <summary>Aim directions, auto-aim.</summary>
        Shooting,

        /// <summary>Camera distance and shake, render resolution (Android), comparison at a weapon (stage 6e2).</summary>
        Game,

        /// <summary>Everything else: language, music and sound volumes, FPS counter.</summary>
        Other,
    }

    /// <summary>
    /// Settings, opened from the main menu and the pause screen: tabs (<see cref="SettingsTab"/>; the open one in the
    /// accent, underlined), one page shown at a time, rows "caption — control" with the value on the right; aim
    /// directions are three segments. "Reset tab" resets the shown page. Changes apply at once.
    /// </summary>
    public sealed class SettingsScreen : UIScreen
    {
        [SerializeField] private UiStyle _style;

        [Tooltip("In SettingsTab order.")]
        [SerializeField] private Button[] _tabButtons;
        [Tooltip("Tab captions, in SettingsTab order.")]
        [SerializeField] private TMP_Text[] _tabLabels;
        [Tooltip("Underlines of the open tab, in SettingsTab order.")]
        [SerializeField] private GameObject[] _tabUnderlines;
        [Tooltip("In SettingsTab order.")]
        [SerializeField] private GameObject[] _pages;

        [Tooltip("Scheme segments: Two sticks, Auto (TouchScheme order); the chosen one in the accent.")]
        [SerializeField] private UiButton[] _schemeButtons;
        [SerializeField] private Slider _deadZone;
        [SerializeField] private TMP_Text _deadZoneLabel;
        [SerializeField] private Slider _sensitivity;
        [SerializeField] private TMP_Text _sensitivityLabel;
        [SerializeField] private Slider _size;
        [SerializeField] private TMP_Text _sizeLabel;
        [SerializeField] private Slider _opacity;
        [SerializeField] private TMP_Text _opacityLabel;
        [SerializeField] private Toggle _floatingStick;
        [SerializeField] private Toggle _leftHanded;
        [SerializeField] private Toggle _vibration;
        [SerializeField] private Button _editLayoutButton;
        [Tooltip("Aim direction segments: Free, Eight, Four (AimMode order); the chosen one in the accent.")]
        [SerializeField] private UiButton[] _aimButtons;
        [SerializeField] private Toggle _aimAssist;
        [SerializeField] private Slider _cameraDistance;
        [SerializeField] private TMP_Text _cameraDistanceLabel;
        [SerializeField] private Toggle _cameraShake;
        [SerializeField] private Toggle _monsterArrows;
        [Tooltip("Render resolution row: shown only where it applies (Android; the editor for testing).")]
        [SerializeField] private GameObject _resolutionRow;
        [SerializeField] private Slider _resolution;
        [SerializeField] private TMP_Text _resolutionLabel;
        [Tooltip("Comparison segments: Arrows, Colour, Off (PickupCompare order); the chosen one in the accent.")]
        [SerializeField] private UiButton[] _compareButtons;
        [SerializeField] private Slider _musicVolume;
        [SerializeField] private TMP_Text _musicVolumeLabel;
        [SerializeField] private Slider _sfxVolume;
        [SerializeField] private TMP_Text _sfxVolumeLabel;
        [SerializeField] private Toggle _showFps;
        [Tooltip("Language: a button with the language's own name; it opens the list.")]
        [SerializeField] private TMP_Text _languageName;
        [SerializeField] private Button _languageButton;
        [SerializeField] private LanguagePicker _languagePicker;
        [Tooltip("Support contact row (only on sites that want one, PlatformTraits.SupportEmail).")]
        [SerializeField] private GameObject _supportRow;
        [SerializeField] private TMP_Text _supportValue;
        [Tooltip("Privacy policy row with an Open button (only where the platform has one, PlatformTraits.PrivacyPolicyUrl).")]
        [SerializeField] private GameObject _privacyRow;
        [SerializeField] private Button _privacyButton;
        [SerializeField] private Button _resetButton;
        [SerializeField] private Button _backButton;

        public event Action<ControlsSettings> Changed;

        /// <summary>The "Game" page was edited.</summary>
        public event Action<GameSettings> GameChanged;
        /// <summary>"Reset to defaults" on the shown page.</summary>
        public event Action<SettingsTab> ResetClicked;

        /// <summary>"Edit button layout": drag the on-screen controls.</summary>
        public event Action EditLayoutClicked;

        /// <summary>(music, sounds) volume sliders moved, 0..1 (not part of the controls).</summary>
        public event Action<float, float> VolumesChanged;

        /// <summary>The "Show FPS counter" toggle was switched (not part of the controls).</summary>
        public event Action<bool> ShowFpsChanged;

        /// <summary>The language button: open the list (<see cref="LanguagePicker"/>).</summary>
        public event Action LanguageListRequested;

        /// <summary>The privacy policy's Open button.</summary>
        public event Action PrivacyPolicyRequested;

        public LanguagePicker LanguagePicker => _languagePicker;
        public event Action BackClicked;

        private AimMode _aimMode;
        private GameSettings _game = GameSettings.Default;
        private TouchScheme _scheme;
        private LocalizationService _texts;

        public SettingsTab Tab { get; private set; }

        private void Awake()
        {
            Ranges();
            foreach (var slider in new[] { _deadZone, _sensitivity, _size, _opacity })
                slider.onValueChanged.AddListener(_ => OnEdited());
            _floatingStick.onValueChanged.AddListener(_ => OnEdited());
            _leftHanded.onValueChanged.AddListener(_ => OnEdited());
            _aimAssist.onValueChanged.AddListener(_ => OnEdited());
            if (_vibration != null) _vibration.onValueChanged.AddListener(_ => OnEdited());
            if (_cameraDistance != null) _cameraDistance.onValueChanged.AddListener(_ => OnGameEdited());
            if (_resolution != null) _resolution.onValueChanged.AddListener(_ => OnGameEdited());

            if (_cameraShake != null) _cameraShake.onValueChanged.AddListener(_ => OnGameEdited());
            if (_monsterArrows != null) _monsterArrows.onValueChanged.AddListener(_ => OnGameEdited());
            if (_compareButtons != null)
                for (var i = 0; i < _compareButtons.Length; i++)
                {
                    var compare = (PickupCompare)i;
                    Bind(_compareButtons[i], () => OnCompareClicked(compare));
                }

            if (_resolutionRow != null) _resolutionRow.SetActive(ResolutionShown);
            if (_showFps != null) _showFps.onValueChanged.AddListener(value => ShowFpsChanged?.Invoke(value));
            foreach (var slider in new[] { _musicVolume, _sfxVolume })
            {
                if (slider == null) continue;
                slider.onValueChanged.AddListener(_ => OnVolumesEdited());
            }
            for (var i = 0; i < _aimButtons.Length; i++)
            {
                var mode = (AimMode)i;
                Bind(_aimButtons[i], () => OnAimModeClicked(mode));
            }
            if (_schemeButtons != null)
                for (var i = 0; i < _schemeButtons.Length; i++)
                {
                    var scheme = (TouchScheme)i;
                    Bind(_schemeButtons[i], () => OnSchemeClicked(scheme));
                }
            Bind(_resetButton, () => ResetClicked?.Invoke(Tab));
            Bind(_editLayoutButton, () => EditLayoutClicked?.Invoke());
            Bind(_backButton, () => BackClicked?.Invoke());
            Bind(_languageButton, () => LanguageListRequested?.Invoke());
            Bind(_privacyButton, () => PrivacyPolicyRequested?.Invoke());
            for (var i = 0; i < _tabButtons.Length; i++)
            {
                var tab = (SettingsTab)i;
                Bind(_tabButtons[i], () => ShowTab(tab));
            }

            ShowTab(SettingsTab.Controls);
        }

        /// <summary>
        /// The Controls tab is all about the on-screen controls (stick, size, layout): release builds without them
        /// (desktop, no touch seen) do not show it. The editor and development builds keep it for testing.
        /// </summary>
        private static bool ControlsTabShown => HudScreen.TouchShown || Debug.isDebugBuild;

        /// <summary>Render resolution is set on Android only (the web follows the page); the editor shows it for testing.</summary>
        private static bool ResolutionShown =>
            UnityEngine.Application.platform == RuntimePlatform.Android || UnityEngine.Application.isEditor;

        private void OnEnable()
        {
            var controls = (int)SettingsTab.Controls;
            if (controls < _tabButtons.Length && _tabButtons[controls] != null)
                _tabButtons[controls].gameObject.SetActive(ControlsTabShown);
            if (Tab == SettingsTab.Controls && !ControlsTabShown) ShowTab(SettingsTab.Shooting);
        }

        public void ShowTab(SettingsTab tab)
        {
            if (tab == SettingsTab.Controls && !ControlsTabShown) tab = SettingsTab.Shooting;
            Tab = tab;
            for (var i = 0; i < _pages.Length; i++)
                _pages[i].SetActive(i == (int)tab);
            for (var i = 0; i < _tabLabels.Length; i++)
                _tabLabels[i].color = i == (int)tab ? _style.Accent : _style.MutedText;
            for (var i = 0; i < _tabUnderlines.Length; i++)
                _tabUnderlines[i].SetActive(i == (int)tab);
        }

        public override void ApplyLanguage(LocalizationService texts)
        {
            _texts = texts;
            UpdateLabels(_values);
            UpdateGameLabels();
            if (_musicVolume != null && _sfxVolume != null) UpdateVolumeLabels();
        }

        /// <summary>The shown language's own name.</summary>
        public void SetLanguage(string nativeName)
        {
            if (_languageName != null) _languageName.text = nativeName;
        }

        /// <summary>The support contact; empty hides the row.</summary>
        public void SetSupport(string email)
        {
            if (_supportRow != null) _supportRow.SetActive(!string.IsNullOrEmpty(email));
            if (_supportValue != null) _supportValue.text = email ?? "";
        }

        /// <summary>Shows the privacy policy row (false hides it).</summary>
        public void SetPrivacyPolicy(bool shown)
        {
            if (_privacyRow != null) _privacyRow.SetActive(shown);
        }

        public void SetShowFps(bool show)
        {
            if (_showFps != null) _showFps.SetIsOnWithoutNotify(show);
        }

        public void SetVolumes(float music, float sfx)
        {
            if (_musicVolume == null || _sfxVolume == null) return;
            _musicVolume.SetValueWithoutNotify(music);
            _sfxVolume.SetValueWithoutNotify(sfx);
            UpdateVolumeLabels();
        }

        public void SetValues(ControlsSettings settings)
        {
            Ranges();
            _values = settings;
            _deadZone.SetValueWithoutNotify(settings.StickDeadZone);
            _sensitivity.SetValueWithoutNotify(settings.StickSensitivity);
            _size.SetValueWithoutNotify(settings.Size);
            _opacity.SetValueWithoutNotify(settings.Opacity);
            _floatingStick.SetIsOnWithoutNotify(settings.FloatingStick);
            _leftHanded.SetIsOnWithoutNotify(settings.LeftHanded);
            _aimAssist.SetIsOnWithoutNotify(settings.AimAssist);
            if (_vibration != null) _vibration.SetIsOnWithoutNotify(settings.Vibration);
            _aimMode = settings.AimMode;
            _scheme = settings.Scheme;
            UpdateLabels(settings);
        }

        private ControlsSettings _values;

        private ControlsSettings Read()
        {
            var settings = _values; // Keeps what the screen does not edit (the layout).
            settings.StickDeadZone = _deadZone.value;
            settings.StickSensitivity = _sensitivity.value;
            settings.FloatingStick = _floatingStick.isOn;
            settings.Size = _size.value;
            settings.Opacity = _opacity.value;
            settings.LeftHanded = _leftHanded.isOn;
            settings.AimMode = _aimMode;
            settings.AimAssist = _aimAssist.isOn;
            settings.Scheme = _scheme;
            if (_vibration != null) settings.NoVibration = !_vibration.isOn;
            return settings;
        }

        /// <summary>
        /// Ranges of all sliders. Also before every Set*: the screen is filled before its first Awake (it opens hidden),
        /// and a value set into the default 0..1 range would be cut (a size of 1.2 read back as 1 and saved).
        /// </summary>
        private void Ranges()
        {
            Range(_deadZone, ControlsSettings.MinDeadZone, ControlsSettings.MaxDeadZone);
            Range(_sensitivity, 0f, 1f);
            Range(_size, ControlsSettings.MinSize, ControlsSettings.MaxSize);
            Range(_opacity, ControlsSettings.MinOpacity, ControlsSettings.MaxOpacity);
            if (_musicVolume != null) Range(_musicVolume, 0f, 1f);
            if (_sfxVolume != null) Range(_sfxVolume, 0f, 1f);

            if (_cameraDistance != null)
            {
                _cameraDistance.minValue = GameSettings.MinCameraDistance;
                _cameraDistance.maxValue = GameSettings.MaxCameraDistance;
                _cameraDistance.wholeNumbers = true;
            }

            if (_resolution != null)
            {
                // Whole steps of ResolutionScaleStep: the slider runs over step numbers.
                _resolution.minValue = Mathf.Round(GameSettings.MinResolutionScale / GameSettings.ResolutionScaleStep);
                _resolution.maxValue = Mathf.Round(GameSettings.MaxResolutionScale / GameSettings.ResolutionScaleStep);
                _resolution.wholeNumbers = true;
            }
        }

        public void SetGame(GameSettings game)
        {
            Ranges();
            _game = game;
            if (_cameraDistance != null) _cameraDistance.SetValueWithoutNotify(game.CameraDistance);
            if (_cameraShake != null) _cameraShake.SetIsOnWithoutNotify(game.CameraShake);
            if (_monsterArrows != null) _monsterArrows.SetIsOnWithoutNotify(game.MonsterArrows);
            if (_resolution != null) _resolution.SetValueWithoutNotify(Mathf.Round(game.ResolutionScale / GameSettings.ResolutionScaleStep));
            UpdateGameLabels();
        }

        private void OnCompareClicked(PickupCompare compare)
        {
            if (_game.Compare == compare) return;
            _game.Compare = compare;
            OnGameEdited();
        }

        private void OnGameEdited()
        {
            if (_cameraDistance != null) _game.CameraDistance = Mathf.RoundToInt(_cameraDistance.value);
            if (_cameraShake != null) _game.NoCameraShake = !_cameraShake.isOn;
            if (_monsterArrows != null) _game.NoMonsterArrows = !_monsterArrows.isOn;
            if (_resolution != null) _game.ResolutionScale = _resolution.value * GameSettings.ResolutionScaleStep;
            _game = _game.Clamped();
            UpdateGameLabels();
            GameChanged?.Invoke(_game);
        }

        private void UpdateGameLabels()
        {
            if (_cameraDistanceLabel != null)
                _cameraDistanceLabel.text = _texts != null
                    ? _texts.Format(TextKeys.SettingsCameraDistanceValue, _game.CameraDistance)
                    : _game.CameraDistance.ToString();
            if (_resolutionLabel != null) _resolutionLabel.text = Percent(_game.ResolutionScale);
            if (_compareButtons != null)
                for (var i = 0; i < _compareButtons.Length; i++)
                    _compareButtons[i].Accent = i == (int)_game.Compare;
        }

        private void OnAimModeClicked(AimMode mode)
        {
            if (_aimMode == mode) return;
            _aimMode = mode;
            OnEdited();
        }

        private void OnSchemeClicked(TouchScheme scheme)
        {
            if (_scheme == scheme) return;
            _scheme = scheme;
            OnEdited();
        }

        private void OnEdited()
        {
            var settings = Read();
            _values = settings;
            UpdateLabels(settings);
            Changed?.Invoke(settings);
        }

        private void UpdateLabels(ControlsSettings settings)
        {
            _deadZoneLabel.text = Percent(settings.StickDeadZone);
            _sensitivityLabel.text = Percent(settings.StickSensitivity);
            _sizeLabel.text = Percent(settings.Size);
            _opacityLabel.text = Percent(settings.Opacity);
            for (var i = 0; i < _aimButtons.Length; i++)
                _aimButtons[i].Accent = i == (int)settings.AimMode;
            if (_schemeButtons != null)
                for (var i = 0; i < _schemeButtons.Length; i++)
                    _schemeButtons[i].Accent = i == (int)settings.Scheme;
        }

        private void OnVolumesEdited()
        {
            UpdateVolumeLabels();
            VolumesChanged?.Invoke(_musicVolume.value, _sfxVolume.value);
        }

        private void UpdateVolumeLabels()
        {
            _musicVolumeLabel.text = Percent(_musicVolume.value);
            _sfxVolumeLabel.text = Percent(_sfxVolume.value);
        }

        private string Percent(float value)
        {
            var percent = Mathf.RoundToInt(value * 100f);
            return _texts != null ? _texts.Format(TextKeys.Percent, percent) : percent + "%";
        }

        private static void Range(Slider slider, float min, float max)
        {
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
        }
    }
}
