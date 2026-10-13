using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Maze.Application.Flow;
using Maze.Application.Platform;
using Maze.Application.Save;
using Maze.Application.Services;
using Maze.Presentation.UI.Touch;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// Applies <see cref="SettingsService.Controls"/> to the on-screen controls and <see cref="SettingsService.ShowFps"/>
    /// to the FPS counter, and runs the settings screen (volumes are previewed while a slider moves, saved on close): it is an overlay over the main menu or the pause screen, not a
    /// GameFlow state, and closes (saving) on any state change. "Edit button layout" hides the screen and runs
    /// <see cref="TouchLayoutEditor"/>; its changes are previewed like the other controls and saved when it is done.
    /// </summary>
    public sealed class SettingsPresenter : IDisposable
    {
        private readonly SettingsService _settings;
        private readonly GameFlow _flow;
        private readonly UIRoot _ui;
        private readonly LocalizationService _texts;
        private readonly PlatformService _platform;
        private readonly List<string> _names = new List<string>();
        private bool _initialized;

        private TouchLayoutEditor LayoutEditor => _ui.TouchControls != null ? _ui.TouchControls.LayoutEditor : null;

        public SettingsPresenter(SettingsService settings, GameFlow flow, UIRoot ui, LocalizationService texts,
            PlatformService platform = null)
        {
            _platform = platform;
            _texts = texts;
            _settings = settings;
            _flow = flow;
            _ui = ui;
        }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _settings.ControlsChanged += ApplyControls;
            _settings.ShowFpsChanged += ApplyShowFps;
            _ui.Settings.ShowFpsChanged += OnShowFpsEdited;
            _ui.Settings.VolumesChanged += OnVolumesEdited;
            _flow.StateChanged += OnStateChanged;
            _ui.MainMenu.SettingsClicked += Open;
            _ui.Pause.SettingsClicked += Open;
            _ui.Settings.Changed += OnEdited;
            _ui.Settings.ResetClicked += OnReset;
            _ui.Settings.BackClicked += Close;
            _ui.Settings.EditLayoutClicked += BeginLayoutEdit;
            _ui.Settings.LanguageListRequested += OnLanguageListRequested;
            _ui.Settings.PrivacyPolicyRequested += OnPrivacyPolicyRequested;
            if (_ui.Settings.LanguagePicker != null) _ui.Settings.LanguagePicker.Picked += OnLanguagePicked;
            _texts.LanguageChanged += ShowLanguage;
            if (LayoutEditor != null)
            {
                LayoutEditor.Changed += OnLayoutEdited;
                LayoutEditor.Done += EndLayoutEdit;
            }

            ApplyControls();
            ApplyShowFps();
        }

        public void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            _settings.SavePreviewed();
            _settings.ControlsChanged -= ApplyControls;
            _settings.ShowFpsChanged -= ApplyShowFps;
            _texts.LanguageChanged -= ShowLanguage;
            _flow.StateChanged -= OnStateChanged;
            if (_ui == null) return; // Scene already destroyed on application quit.

            _ui.MainMenu.SettingsClicked -= Open;
            _ui.Pause.SettingsClicked -= Open;
            _ui.Settings.Changed -= OnEdited;
            _ui.Settings.ResetClicked -= OnReset;
            _ui.Settings.BackClicked -= Close;
            _ui.Settings.ShowFpsChanged -= OnShowFpsEdited;
            _ui.Settings.VolumesChanged -= OnVolumesEdited;
            _ui.Settings.EditLayoutClicked -= BeginLayoutEdit;
            _ui.Settings.LanguageListRequested -= OnLanguageListRequested;
            _ui.Settings.PrivacyPolicyRequested -= OnPrivacyPolicyRequested;
            if (_ui.Settings.LanguagePicker != null) _ui.Settings.LanguagePicker.Picked -= OnLanguagePicked;
            if (LayoutEditor != null)
            {
                LayoutEditor.Changed -= OnLayoutEdited;
                LayoutEditor.Done -= EndLayoutEdit;
            }
        }

        private void ApplyControls() => _ui.TouchControls.Apply(_settings.Controls);

        private void ApplyShowFps()
        {
            if (_ui.FpsCounter != null) _ui.FpsCounter.SetVisible(_settings.ShowFps);
        }

        /// <summary>The policy opens in the browser (the game goes to the background and pauses as usual).</summary>
        private void OnPrivacyPolicyRequested()
        {
            var url = _platform?.Traits.PrivacyPolicyUrl;
            if (!string.IsNullOrEmpty(url)) UnityEngine.Application.OpenURL(url);
        }

        private void OnShowFpsEdited(bool show) => _settings.ShowFps = show;

        private void OnVolumesEdited(float music, float sfx) => _settings.PreviewVolumes(music, sfx);

        private void Open()
        {
            _ui.Settings.SetValues(_settings.Controls);
            _ui.Settings.SetVolumes(_settings.MusicVolume, _settings.SfxVolume);
            _ui.Settings.SetShowFps(_settings.ShowFps);
            _ui.Settings.SetSupport(_platform?.Traits.SupportEmail); // the site's traits are known only after its start
            _ui.Settings.SetPrivacyPolicy(!string.IsNullOrEmpty(_platform?.Traits.PrivacyPolicyUrl));
            ShowLanguage();
            _ui.Settings.SetVisible(true);
        }

        private void ShowLanguage()
        {
            var index = _texts.LanguageIndex;
            _ui.Settings.SetLanguage(index >= 0 ? _texts.Languages[index].NativeName : string.Empty);
        }

        /// <summary>The list of languages by their own names, the shown one marked.</summary>
        private void OnLanguageListRequested()
        {
            var picker = _ui.Settings.LanguagePicker;
            if (picker == null || _texts.Languages.Count == 0) return;
            _names.Clear();
            foreach (var language in _texts.Languages)
                _names.Add(language.NativeName);
            picker.Show(_names, _texts.LanguageIndex);
        }

        /// <summary>The screen shows the new name once its texts are loaded.</summary>
        private void OnLanguagePicked(int index)
        {
            if (index != _texts.LanguageIndex) _texts.SetLanguageAsync(index).Forget();
        }

        private void Close()
        {
            _settings.SavePreviewed();
            _ui.Settings.SetVisible(false);
        }

        private void OnStateChanged(GameFlowState state)
        {
            if (LayoutEditor != null && LayoutEditor.IsEditing)
            {
                LayoutEditor.End();
                _settings.SavePreviewed();
            }

            if (_ui.Settings.IsVisible) Close();
        }

        private void BeginLayoutEdit()
        {
            if (LayoutEditor == null) return;
            _ui.Settings.SetVisible(false);
            LayoutEditor.Begin(_settings.Controls.Layout);
        }

        private void OnLayoutEdited(TouchLayout layout)
        {
            var controls = _settings.Controls;
            controls.Layout = layout;
            _settings.PreviewControls(controls);
        }

        private void EndLayoutEdit()
        {
            LayoutEditor.End();
            _settings.SavePreviewed();
            Open();
        }

        private void OnEdited(ControlsSettings controls) => _settings.PreviewControls(controls);

        private void OnReset(SettingsTab tab)
        {
            switch (tab)
            {
                case SettingsTab.Controls: _settings.ResetTouchControls(); break;
                case SettingsTab.Shooting: _settings.ResetAim(); break;
                default: _settings.ResetOther(); break;
            }

            _ui.Settings.SetValues(_settings.Controls);
            _ui.Settings.SetVolumes(_settings.MusicVolume, _settings.SfxVolume);
            _ui.Settings.SetShowFps(_settings.ShowFps);
        }
    }
}
