using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Save;
using Maze.Gameplay.Combat;
using UnityEngine;

namespace Maze.Application.Services
{
    /// <summary>
    /// Player settings (ТЗ §85: part of SaveData). Every change is saved at once (a persistent event).
    /// Must be initialized after <see cref="SaveService"/>. Controls and volumes are previewed while a slider moves and
    /// saved once with <see cref="SavePreviewed"/> (not on every frame of a drag).
    /// </summary>
    public sealed class SettingsService : IApplicationService, IAimSettings
    {
        private readonly SaveService _save;
        private bool _previewDirty;

        public SettingsService(SaveService save)
        {
            _save = save;
        }

        public string Name => "Settings";

        /// <summary>Raised when <see cref="MusicVolume"/> or <see cref="SfxVolume"/> changes (also once after loading).</summary>
        public event Action VolumeChanged;

        /// <summary>Music volume, 0..1. Saved at once.</summary>
        public float MusicVolume
        {
            get => _save.Data.Settings.MusicVolume;
            set
            {
                if (SetVolume(ref _save.Data.Settings.MusicVolume, value)) _save.Save();
            }
        }

        /// <summary>Volume of sounds (game and interface), 0..1. Saved at once.</summary>
        public float SfxVolume
        {
            get => _save.Data.Settings.SfxVolume;
            set
            {
                if (SetVolume(ref _save.Data.Settings.SfxVolume, value)) _save.Save();
            }
        }

        /// <summary>Applies the volumes at once; they are written by <see cref="SavePreviewed"/>.</summary>
        public void PreviewVolumes(float music, float sfx)
        {
            var changed = SetVolume(ref _save.Data.Settings.MusicVolume, music, false);
            changed |= SetVolume(ref _save.Data.Settings.SfxVolume, sfx, false);
            if (!changed) return;
            _previewDirty = true;
            VolumeChanged?.Invoke();
        }

        /// <summary>Raised when <see cref="ShowFps"/> changes (also once after loading).</summary>
        public event Action ShowFpsChanged;

        /// <summary>Frames-per-second counter on screen. Saved at once.</summary>
        public bool ShowFps
        {
            get => _save.Data.Settings.ShowFps;
            set
            {
                if (_save.Data.Settings.ShowFps == value) return;
                _save.Data.Settings.ShowFps = value;
                _save.Save();
                ShowFpsChanged?.Invoke();
            }
        }

        /// <summary>Map screen shows the player icon. Saved at once.</summary>
        public bool MapShowPlayer
        {
            get => !_save.Data.Settings.MapHidePlayer;
            set => SetFlag(ref _save.Data.Settings.MapHidePlayer, !value);
        }

        /// <summary>Map screen shows icons of map fragments not collected yet. Saved at once.</summary>
        public bool MapShowFragments
        {
            get => !_save.Data.Settings.MapHideFragments;
            set => SetFlag(ref _save.Data.Settings.MapHideFragments, !value);
        }

        /// <summary>Map screen shows living zombies (also out of sight). Saved at once.</summary>
        public bool MapShowZombies
        {
            get => !_save.Data.Settings.MapHideZombies;
            set => SetFlag(ref _save.Data.Settings.MapHideZombies, !value);
        }

        /// <summary>Map screen shows weapons lying in the level. Saved at once.</summary>
        public bool MapShowWeapons
        {
            get => !_save.Data.Settings.MapHideWeapons;
            set => SetFlag(ref _save.Data.Settings.MapHideWeapons, !value);
        }

        /// <summary>Map screen shows medkits not used yet. Saved at once.</summary>
        public bool MapShowMedkits
        {
            get => !_save.Data.Settings.MapHideMedkits;
            set => SetFlag(ref _save.Data.Settings.MapHideMedkits, !value);
        }

        /// <summary>Map screen shows keys not picked up yet. Saved at once.</summary>
        public bool MapShowKeys
        {
            get => !_save.Data.Settings.MapHideKeys;
            set => SetFlag(ref _save.Data.Settings.MapHideKeys, !value);
        }

        /// <summary>Language code chosen by the player or on the first start; empty before that. Saved at once.</summary>
        public string Language
        {
            get => _save.Data.Settings.Language;
            set
            {
                value ??= string.Empty;
                if (_save.Data.Settings.Language == value) return;
                _save.Data.Settings.Language = value;
                _save.Save();
            }
        }

        /// <summary>Raised when <see cref="Controls"/> change (also once after loading).</summary>
        public event Action ControlsChanged;

        public ControlsSettings Controls => _save.Data.Settings.Controls;

        AimMode IAimSettings.AimMode => Controls.AimMode;
        bool IAimSettings.AimAssist => Controls.AimAssist;

        /// <summary>Applies the controls at once; they are written by <see cref="SavePreviewed"/>.</summary>
        public void PreviewControls(ControlsSettings controls)
        {
            controls = controls.Clamped();
            if (controls.Equals(Controls)) return;
            _save.Data.Settings.Controls = controls;
            _previewDirty = true;
            ControlsChanged?.Invoke();
        }

        /// <summary>Applies and saves the controls.</summary>
        public void SetControls(ControlsSettings controls)
        {
            PreviewControls(controls);
            SavePreviewed();
        }

        public void ResetControls() => SetControls(ControlsSettings.Default);

        /// <summary>Defaults for the on-screen controls (stick, size, opacity, layout); aiming stays.</summary>
        public void ResetTouchControls()
        {
            var controls = ControlsSettings.Default;
            controls.AimMode = Controls.AimMode;
            controls.AimAssist = Controls.AimAssist;
            SetControls(controls);
        }

        /// <summary>Default aiming; the on-screen controls stay.</summary>
        public void ResetAim()
        {
            var controls = Controls;
            controls.AimMode = ControlsSettings.Default.AimMode;
            controls.AimAssist = ControlsSettings.Default.AimAssist;
            SetControls(controls);
        }

        /// <summary>Defaults of the "Other" settings page: full volumes, no FPS counter.</summary>
        public void ResetOther()
        {
            var defaults = new SettingsData();
            PreviewVolumes(defaults.MusicVolume, defaults.SfxVolume);
            SavePreviewed();
            ShowFps = defaults.ShowFps;
        }

        /// <summary>Saves previewed controls and volumes, if any.</summary>
        public void SavePreviewed()
        {
            if (!_previewDirty) return;
            _previewDirty = false;
            _save.Save();
        }

        public UniTask InitializeAsync(CancellationToken cancellation)
        {
            var settings = _save.Data.Settings;
            settings.MusicVolume = Mathf.Clamp01(settings.MusicVolume);
            settings.SfxVolume = Mathf.Clamp01(settings.SfxVolume);
            settings.Controls = settings.Controls.Clamped();
            ControlsChanged?.Invoke();
            ShowFpsChanged?.Invoke();
            VolumeChanged?.Invoke();
            return UniTask.CompletedTask;
        }

        private void SetFlag(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            _save.Save();
        }

        /// <summary>True when the value changed; raises <see cref="VolumeChanged"/> when <paramref name="notify"/>.</summary>
        private bool SetVolume(ref float field, float value, bool notify = true)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value)) return false;
            field = value;
            if (notify) VolumeChanged?.Invoke();
            return true;
        }
    }
}
