using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Services;
using Purge.Gameplay.Level;
using Purge.Gameplay.Objects;
using Purge.Gameplay.Player;
using Purge.Presentation.Localization;
using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The level objects on the HUD (stage 7в): the safe dome's seconds left under the time (only while the player is
    /// inside during a wave; red in the last 3), the bar over a chest being opened, and the messages — no chest key, the
    /// key picked up, a challenge begun and done, the dome out until the pause.
    /// </summary>
    public sealed class LevelObjectsHudPresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        private const float BarHeight = 1.2f;
        private const float WarnTime = 3f;

        private readonly UIRoot _ui;
        private readonly ChestSystem _chests;
        private readonly RefugeSystem _refuge;
        private readonly PlayerInteraction _interaction;
        private readonly PlayerInventory _inventory;
        private readonly LocalizationService _texts;
        private readonly TopDownCamera _camera;
        private readonly LevelViewRoot _root;
        private int _secondsShown = -1;
        private int _chestKeys;
        private bool _subscribed;

        public LevelObjectsHudPresenter(UIRoot ui, ChestSystem chests, RefugeSystem refuge, PlayerInteraction interaction,
            PlayerInventory inventory, LocalizationService texts, TopDownCamera camera, LevelViewRoot root)
        {
            _ui = ui;
            _chests = chests;
            _refuge = refuge;
            _interaction = interaction;
            _inventory = inventory;
            _texts = texts;
            _camera = camera;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed) return UniTask.CompletedTask;
            _chestKeys = _inventory.ChestKeys;
            _interaction.Interacted += OnInteracted;
            _inventory.KeysChanged += OnKeysChanged;
            _chests.ChallengeStarted += OnChallengeStarted;
            _chests.ChallengeDone += OnChallengeDone;
            _refuge.PushStarted += OnPushStarted;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _interaction.Interacted -= OnInteracted;
            _inventory.KeysChanged -= OnKeysChanged;
            _chests.ChallengeStarted -= OnChallengeStarted;
            _chests.ChallengeDone -= OnChallengeDone;
            _refuge.PushStarted -= OnPushStarted;
            _subscribed = false;
        }

        public void LateTick(float deltaTime)
        {
            var hud = _ui != null ? _ui.Hud : null;
            if (hud == null) return;

            var inside = _refuge.Exists && _refuge.IsLimited && _refuge.PlayerInside && !_refuge.IsPushing;
            if (inside)
            {
                var seconds = Mathf.CeilToInt(_refuge.TimeLeft);
                string text = null;
                if (seconds != _secondsShown)
                {
                    _secondsShown = seconds;
                    text = _texts.Format(TextKeys.HudRefuge, seconds);
                }

                hud.SetRefuge(true, text, _refuge.TimeLeft < WarnTime);
            }
            else
            {
                _secondsShown = -1;
                hud.SetRefuge(false);
            }

            var bar = hud.ChestBar;
            if (bar == null) return;
            var chest = _chests.Opening;
            bar.SetVisible(chest != null);
            if (chest == null) return;
            bar.SetProgress(chest.Progress);
            var world = _root.transform.TransformPoint(new Vector3(chest.Position.x, BarHeight, chest.Position.y));
            bar.Place(_camera.Camera.WorldToScreenPoint(world));
        }

        private void OnInteracted(InteractionResult result, Core.Level.DoorData door)
        {
            if (result == InteractionResult.ChestLocked) _ui.Hud?.ShowMessage(_texts.Get(TextKeys.HudChestLocked));
        }

        private void OnKeysChanged()
        {
            var keys = _inventory.ChestKeys;
            if (keys > _chestKeys) _ui.Hud?.ShowMessage(_texts.Get(TextKeys.HudChestKey));
            _chestKeys = keys;
        }

        private void OnChallengeStarted(Chest chest, int champions) => _ui.Hud?.ShowMessage(_texts.Get(TextKeys.HudChallenge));

        private void OnChallengeDone(Chest chest) => _ui.Hud?.ShowMessage(_texts.Get(TextKeys.HudChallengeDone));

        private void OnPushStarted() => _ui.Hud?.ShowMessage(_texts.Get(TextKeys.HudRefugeOver));
    }
}
