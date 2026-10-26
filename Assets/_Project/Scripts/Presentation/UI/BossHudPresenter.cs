using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Services;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Presentation.Localization;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The boss on the HUD (stage 8a): its bar (<see cref="BossBarView"/>: the name «monster.&lt;id&gt;», HP, the notches
    /// of its phases) from its coming to its death; at its death a white flash over the screen and the banner. The arrows
    /// to it and to the arena — <see cref="MonsterArrowsPresenter"/>; its point on the map — the map.
    /// </summary>
    public sealed class BossHudPresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        private static readonly Color DeathFlash = new Color(1f, 1f, 1f, 0.55f);
        private const float DeathFlashTime = 0.45f;

        private readonly UIRoot _ui;
        private readonly LocalizationService _texts;
        private readonly BossSystem _bosses;
        private bool _subscribed;

        public BossHudPresenter(UIRoot ui, LocalizationService texts, BossSystem bosses = null)
        {
            _ui = ui;
            _texts = texts;
            _bosses = bosses;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        private BossBarView Bar => _ui.Hud != null ? _ui.Hud.BossBar : null;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            Bar?.Hide();
            if (_bosses == null || _subscribed) return UniTask.CompletedTask;
            _bosses.Appeared += OnAppeared;
            _bosses.Defeated += OnDefeated;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _bosses.Appeared -= OnAppeared;
            _bosses.Defeated -= OnDefeated;
            _subscribed = false;
        }

        public void LateTick(float deltaTime)
        {
            var bar = Bar;
            if (bar == null || _bosses == null) return;
            if (!_bosses.IsBossAlive)
            {
                if (bar.IsShown) bar.Hide();
                return;
            }

            bar.SetHealth(_bosses.HealthShare, deltaTime);
        }

        private void OnAppeared(int slot)
        {
            var bar = Bar;
            if (bar == null) return;
            var definition = _bosses.Definition;
            var id = definition != null ? definition.Id : "boss";
            var key = TextKeys.MonsterPrefix + id;
            bar.Show(_texts.Has(key) ? _texts.Get(key) : id.Replace('_', ' '), _bosses.Thresholds);
        }

        private void OnDefeated(int slot, Vector2 position)
        {
            Bar?.Hide();
            var hud = _ui.Hud;
            if (hud == null) return;
            hud.Flash(DeathFlash, DeathFlashTime);
            hud.ShowBanner(_texts.Get(TextKeys.HudBossDefeated));
        }
    }
}
