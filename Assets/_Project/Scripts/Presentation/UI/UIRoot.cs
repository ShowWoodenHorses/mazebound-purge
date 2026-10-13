using System;
using Maze.Application.Services;
using Maze.Presentation.Localization;
using Maze.Presentation.UI.Touch;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Maze.Presentation.UI
{
    /// <summary>Application UI canvas in the Bootstrap scene: references to every screen, wired in the scene.</summary>
    public sealed class UIRoot : MonoBehaviour
    {
        [SerializeField] private MainMenuScreen _mainMenu;
        [SerializeField] private LevelSelectScreen _levelSelect;
        [SerializeField] private LoadingScreen _loading;
        [SerializeField] private ErrorScreen _error;
        [SerializeField] private HudScreen _hud;
        [SerializeField] private PauseScreen _pause;
        [SerializeField] private ResultScreen _result;
        [SerializeField] private ConfirmExitScreen _confirmExit;
        [SerializeField] private MapScreen _map;
        [SerializeField] private SettingsScreen _settings;

        [Tooltip("Tutorial layer over the HUD: reminder, arrow, highlight ring and the step window.")]
        [SerializeField] private TutorialScreen _tutorial;

        [Tooltip("On-screen controls: a separate canvas with physical sizes, shown by the HUD.")]
        [SerializeField] private TouchControls _touchControls;

        [Tooltip("Frames per second in the bottom-right corner, over every screen; shown when the setting is on.")]
        [SerializeField] private FpsCounter _fpsCounter;

        [Tooltip("\"Rotate your device\" over everything on a phone held in portrait.")]
        [SerializeField] private RotateDeviceScreen _rotateDevice;

        [Tooltip("The scene's EventSystem: tells whether the pointer is over the UI (clicks there are not attacks).")]
        [SerializeField] private EventSystem _eventSystem;

        [Tooltip("Every static caption of the Bootstrap scene (both canvases), filled by RuntimeScenesBuilder.")]
        [SerializeField] private LocalizedText[] _localizedTexts = Array.Empty<LocalizedText>();

        public MainMenuScreen MainMenu => _mainMenu;
        public LevelSelectScreen LevelSelect => _levelSelect;
        public LoadingScreen Loading => _loading;
        public ErrorScreen Error => _error;
        public HudScreen Hud => _hud;
        public PauseScreen Pause => _pause;
        public ResultScreen Result => _result;
        public ConfirmExitScreen ConfirmExit => _confirmExit;
        public MapScreen Map => _map;
        public SettingsScreen Settings => _settings;
        public TutorialScreen Tutorial => _tutorial;
        public TouchControls TouchControls => _touchControls;
        public FpsCounter FpsCounter => _fpsCounter;
        public RotateDeviceScreen RotateDevice => _rotateDevice;
        public EventSystem EventSystem => _eventSystem;

        /// <summary>A control asked for its sound (<see cref="UiSound"/>).</summary>
        public event Action<UiSoundKind> SoundRequested;

        public void RequestSound(UiSoundKind kind) => SoundRequested?.Invoke(kind);

        /// <summary>
        /// The default drag threshold (10 px) is under a millimetre on a phone: a tap that slides a little starts a
        /// drag (level pages swipe) and the button does not get its click. ~2 mm of travel, never less than the default.
        /// </summary>
        public void ScaleDragThreshold()
        {
            const float millimetres = 2f;
            const int minimum = 10;
            if (_eventSystem == null) return;
            var dpi = Screen.dpi;
            _eventSystem.pixelDragThreshold = dpi > 0f ? Mathf.Max(minimum, Mathf.RoundToInt(dpi / 25.4f * millimetres)) : minimum;
        }

        /// <summary>Static captions take the texts of the current language; screens get them for their own texts.</summary>
        public void ApplyLanguage(LocalizationService texts)
        {
            foreach (var text in _localizedTexts)
                if (text != null) text.Apply(texts);

            UIScreen[] screens = { _mainMenu, _levelSelect, _loading, _error, _hud, _pause, _result, _confirmExit, _map, _settings, _tutorial };
            foreach (var screen in screens)
                if (screen != null) screen.ApplyLanguage(texts);
            if (_touchControls != null && _touchControls.LayoutEditor != null)
                _touchControls.LayoutEditor.ApplyLanguage(texts);
        }
    }
}
