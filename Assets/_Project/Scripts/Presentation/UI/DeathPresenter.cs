using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Flow;
using Purge.Application.Platform;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The death window (stage 6d): when <see cref="PlayerRevival.Downed"/> asks for it the game pauses
    /// (<see cref="GameFlow.OpenDeath"/>) under <see cref="DeathScreen"/> with the revives left. The ad's revive asks for a
    /// rewarded ad (<see cref="AdsService"/>; placement <see cref="RevivePlacement"/>) and revives only when it was
    /// watched; the skill's revives at once; giving up fails the level (the result window follows).
    /// </summary>
    public sealed class DeathPresenter : ILevelLoadStep, IDisposable
    {
        public const string RevivePlacement = "revive";

        private readonly UIRoot _ui;
        private readonly PlayerRevival _revival;
        private readonly GameFlow _flow;
        private readonly AdsService _ads;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _bound;
        private bool _adShowing;

        public DeathPresenter(UIRoot ui, PlayerRevival revival, GameFlow flow, AdsService ads = null)
        {
            _ui = ui;
            _revival = revival;
            _flow = flow;
            _ads = ads;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        private DeathScreen Screen => _ui != null ? _ui.Death : null;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_bound) return UniTask.CompletedTask;
            _revival.Downed += OnDowned;
            if (Screen != null)
            {
                Screen.AdClicked += OnAdClicked;
                Screen.SkillClicked += OnSkillClicked;
                Screen.GiveUpClicked += OnGiveUpClicked;
            }

            _bound = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            if (!_bound) return;
            _revival.Downed -= OnDowned;
            if (Screen != null)
            {
                Screen.AdClicked -= OnAdClicked;
                Screen.SkillClicked -= OnSkillClicked;
                Screen.GiveUpClicked -= OnGiveUpClicked;
            }

            _bound = false;
        }

        private void OnDowned()
        {
            // Without the window (tests without the UI) the player cannot choose: dead for good.
            if (Screen == null)
            {
                _revival.GiveUp();
                return;
            }

            Screen.SetOptions(_revival.CanRevive(ReviveKind.Ad), _revival.CanRevive(ReviveKind.Skill));
            _flow.OpenDeath();
        }

        private void OnAdClicked()
        {
            if (_flow.State != GameFlowState.Death || _adShowing || !_revival.CanRevive(ReviveKind.Ad)) return;
            ReviveByAdAsync(_lifetime.Token).Forget();
        }

        private async UniTaskVoid ReviveByAdAsync(CancellationToken cancellation)
        {
            _adShowing = true;
            Screen.SetBusy(true);
            bool watched;
            try
            {
                // Without an ad service (tests, a site without ads) the revive is free.
                watched = _ads == null || await _ads.ShowRewardedAsync(RevivePlacement, cancellation);
            }
            finally
            {
                _adShowing = false;
            }

            if (cancellation.IsCancellationRequested || _flow.State != GameFlowState.Death) return;
            if (watched) Revive(ReviveKind.Ad);
            else Screen.SetBusy(false);
        }

        private void OnSkillClicked()
        {
            if (_flow.State != GameFlowState.Death || _adShowing) return;
            Revive(ReviveKind.Skill);
        }

        private void OnGiveUpClicked()
        {
            if (_flow.State != GameFlowState.Death || _adShowing) return;
            _revival.GiveUp();
        }

        private void Revive(ReviveKind kind)
        {
            if (!_revival.Revive(kind)) return;
            _flow.CloseDeath();
        }
    }
}
