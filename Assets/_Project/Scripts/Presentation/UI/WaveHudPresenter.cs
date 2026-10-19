using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Flow;
using Purge.Application.Services;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Gameplay.Level;
using Purge.Gameplay.Run;
using Purge.Gameplay.Waves;
using Purge.Gameplay.Weapons;
using Purge.Presentation.Localization;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The waves on the HUD (stage 6b; ТЗ: волна/враги/деньги забега, пауза между волнами с отсчётом и «Пропустить +N»):
    /// the wave of how many, enemies left and money of the run under the time; in the pause between waves the countdown,
    /// the skip button (also Enter / Space / gamepad South — <see cref="IInputService.ConfirmRequested"/>; not in the
    /// pause's first <see cref="SkipLock"/> s, so the shots at the last monster do not skip it) and the ammo reserve by
    /// type; a big caption when a wave starts or is cleared. Texts change only when their numbers do. Hidden on a level
    /// without waves.
    /// </summary>
    public sealed class WaveHudPresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        /// <summary>Seconds of the pause the skip key is ignored (the button works at once).</summary>
        public const float SkipLock = 1f;

        private readonly UIRoot _ui;
        private readonly WaveSystem _waves;
        private readonly RunWallet _wallet;
        private readonly LocalizationService _texts;
        private readonly GameFlow _flow;
        private readonly AmmoStock _ammo;
        private readonly IInputService _input;
        private readonly ContentCatalog _content;
        private readonly List<AmmoDefinition> _ammoTypes = new List<AmmoDefinition>();
        private readonly List<string> _ammoEntries = new List<string>();
        private bool _bound;
        private bool _shown;
        private int _waveShown = -2;
        private int _countdownShown = -1;
        private int _bonusShown = -1;
        private WavePhase _phaseShown;
        private bool _ammoDirty;

        public WaveHudPresenter(UIRoot ui, WaveSystem waves, RunWallet wallet, LocalizationService texts, GameFlow flow,
            AmmoStock ammo, IInputService input = null, ContentCatalog content = null)
        {
            _ui = ui;
            _waves = waves;
            _wallet = wallet;
            _texts = texts;
            _flow = flow;
            _ammo = ammo;
            _input = input;
            _content = content;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        private HudScreen Hud => _ui != null && _ui.Hud != null ? _ui.Hud : null;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _ammoTypes.Clear();
            if (_content != null)
                foreach (var item in _content.Items)
                    if (item is AmmoDefinition ammo) _ammoTypes.Add(ammo);

            if (!_bound)
            {
                // The waves start at SpawnZombies, after this stage: the HUD follows them from the late tick.
                _waves.WaveStarted += OnWaveStarted;
                _waves.WaveCleared += OnWaveCleared;
                _ammo.Changed += OnAmmoChanged;
                _texts.LanguageChanged += OnLanguageChanged;
                if (Hud != null) Hud.SkipClicked += OnSkipClicked;
                if (_input != null) _input.ConfirmRequested += OnSkipKey;
                _bound = true;
            }

            _shown = false;
            Hud?.SetWaveShown(false);
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_bound) return;
            _waves.WaveStarted -= OnWaveStarted;
            _waves.WaveCleared -= OnWaveCleared;
            _ammo.Changed -= OnAmmoChanged;
            _texts.LanguageChanged -= OnLanguageChanged;
            if (Hud != null) Hud.SkipClicked -= OnSkipClicked;
            if (_input != null) _input.ConfirmRequested -= OnSkipKey;
            _bound = false;
            Hud?.SetWaveShown(false);
        }

        public void LateTick(float deltaTime)
        {
            var hud = Hud;
            if (hud == null) return;
            if (!_waves.IsActive)
            {
                if (_shown) hud.SetWaveShown(false);
                _shown = false;
                return;
            }

            if (!_shown)
            {
                _shown = true;
                Invalidate();
                hud.SetWaveShown(true);
            }

            var phase = _waves.Phase;
            var number = Math.Min(_waves.WaveIndex + 1, _waves.WaveCount);
            if (number != _waveShown)
            {
                _waveShown = number;
                hud.SetWave(_texts.Format(TextKeys.HudWave, Math.Max(1, number), _waves.WaveCount));
            }

            hud.SetEnemies(_waves.RemainingCount);
            hud.SetMoney(_wallet.Money);

            var waiting = phase == WavePhase.Delay || phase == WavePhase.Pause;
            if (!waiting)
            {
                if (phase != _phaseShown)
                {
                    hud.SetCountdown(false);
                    ShowAmmo(false);
                }

                _phaseShown = phase;
                return;
            }

            var seconds = Mathf.CeilToInt(_waves.TimeLeft);
            var bonus = phase == WavePhase.Pause ? _waves.SkipBonus : -1;
            if (phase != _phaseShown || seconds != _countdownShown || bonus != _bonusShown)
            {
                _countdownShown = seconds;
                _bonusShown = bonus;
                var text = _texts.Format(TextKeys.HudWaveCountdown, _waves.WaveIndex + 2, seconds);
                hud.SetCountdown(true, text, bonus >= 0 ? _texts.Format(TextKeys.HudWaveSkip, bonus) : null);
            }

            if (phase != _phaseShown || _ammoDirty) ShowAmmo(phase == WavePhase.Pause);
            _phaseShown = phase;
        }

        private void OnWaveStarted(int index) =>
            Hud?.ShowBanner(_texts.Format(TextKeys.HudWaveStart, index + 1));

        private void OnWaveCleared(int index)
        {
            // The last one is followed by the level's result.
            if (index + 1 < _waves.WaveCount) Hud?.ShowBanner(_texts.Get(TextKeys.HudWaveCleared));
        }

        private void OnSkipClicked() => TrySkip();

        private void OnSkipKey()
        {
            if (_waves.PauseElapsed >= SkipLock) TrySkip();
        }

        private void TrySkip()
        {
            if (_flow.State != GameFlowState.Playing) return;
            _waves.Skip();
        }

        private void OnAmmoChanged(string ammoId) => _ammoDirty = true;

        private void OnLanguageChanged() => Invalidate();

        private void Invalidate()
        {
            _waveShown = -2;
            _countdownShown = _bonusShown = -1;
            _phaseShown = WavePhase.Inactive;
            _ammoDirty = true;
        }

        /// <summary>The reserve of every ammo type the player has, in the catalog's order (ТЗ: полный список — в паузе между волнами).</summary>
        private void ShowAmmo(bool shown)
        {
            _ammoDirty = false;
            _ammoEntries.Clear();
            if (shown)
                foreach (var ammo in _ammoTypes)
                {
                    var count = _ammo.Get(ammo);
                    if (count > 0) _ammoEntries.Add(AmmoName(ammo) + "  " + count);
                }

            Hud?.SetAmmo(_ammoEntries);
        }

        private string AmmoName(AmmoDefinition ammo)
        {
            var key = TextKeys.AmmoPrefix + ammo.Id;
            return _texts.Has(key) ? _texts.Get(key) : ammo.Id;
        }
    }
}
