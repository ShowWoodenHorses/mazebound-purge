using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Flow;
using Purge.Application.Platform;
using Purge.Application.Services;
using Purge.Core.Cards;
using Purge.Gameplay.Level;
using Purge.Gameplay.Run;
using Purge.Presentation.Localization;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The run's experience and cards on screen (stage 6c): the level and the bar under the HP; at a level-up (while
    /// playing) the offer of <see cref="CardSystem"/> in <see cref="CardsScreen"/> with the game paused
    /// (<see cref="GameFlow.OpenCards"/>). A pick applies the card; another level-up waiting shows its cards at once,
    /// otherwise the game goes on. The reroll asks for a rewarded ad (<see cref="AdsService"/>; placement
    /// <see cref="RerollPlacement"/>) and rerolls only when it was watched. Hidden on a level without cards.
    /// </summary>
    public sealed class CardsPresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        public const string RerollPlacement = "cards_reroll";

        private readonly UIRoot _ui;
        private readonly CardSystem _cards;
        private readonly RunExperience _experience;
        private readonly GameFlow _flow;
        private readonly LocalizationService _texts;
        private readonly AdsService _ads;
        private readonly Gameplay.Level.LevelClock _clock;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _bound;
        private bool _experienceDirty = true;
        private bool _adShowing;

        public CardsPresenter(UIRoot ui, CardSystem cards, RunExperience experience, GameFlow flow, LocalizationService texts,
            AdsService ads = null, Gameplay.Level.LevelClock clock = null)
        {
            _clock = clock;
            _ui = ui;
            _cards = cards;
            _experience = experience;
            _flow = flow;
            _texts = texts;
            _ads = ads;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        private CardsScreen Screen => _ui != null ? _ui.Cards : null;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_bound)
            {
                _experience.Changed += OnExperienceChanged;
                _texts.LanguageChanged += OnLanguageChanged;
                if (Screen != null)
                {
                    Screen.CardClicked += OnCardClicked;
                    Screen.RerollClicked += OnRerollClicked;
                }

                _bound = true;
            }

            _experienceDirty = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            if (!_bound) return;
            _experience.Changed -= OnExperienceChanged;
            _texts.LanguageChanged -= OnLanguageChanged;
            if (Screen != null)
            {
                Screen.CardClicked -= OnCardClicked;
                Screen.RerollClicked -= OnRerollClicked;
            }

            _bound = false;
            _ui?.Hud?.SetExperience(false, null, 0f);
        }

        public void LateTick(float deltaTime)
        {
            if (_experienceDirty)
            {
                _experienceDirty = false;
                _ui?.Hud?.SetExperience(_cards.IsActive, _texts.Format(TextKeys.HudRunLevel, _experience.Level),
                    _experience.Progress);
            }

            // A level-up shows its cards when the game is played (not over the map, the pause or a tutorial window) and
            // not in the slow-down of a boss's death (stage 8a: the boss falls first).
            if (_flow.State != GameFlowState.Playing || _experience.Pending <= 0 || Screen == null) return;
            if (_clock != null && _clock.IsSlowed) return;
            if (!_cards.OpenNext()) return;
            Fill();
            _flow.OpenCards();
            Screen.Refresh();
            _ui.RequestSound(UiSoundKind.CardsShown);
        }

        private void OnCardClicked(int index)
        {
            if (_flow.State != GameFlowState.Cards || _adShowing || index >= _cards.Offer.Count) return;
            _cards.Choose(index);
            _ui.RequestSound(UiSoundKind.CardChosen);
            if (_cards.OpenNext())
            {
                Fill();
                Screen.Refresh();
                _ui.RequestSound(UiSoundKind.CardsShown);
                return;
            }

            _flow.CloseCards();
        }

        private void OnRerollClicked()
        {
            if (_flow.State != GameFlowState.Cards || _adShowing || _cards.RerollUsed) return;
            RerollAsync(_lifetime.Token).Forget();
        }

        private async UniTaskVoid RerollAsync(CancellationToken cancellation)
        {
            _adShowing = true;
            Screen.SetReroll(false);
            bool watched;
            try
            {
                // Without an ad service (tests, a site without ads) the reroll is free.
                watched = _ads == null || await _ads.ShowRewardedAsync(RerollPlacement, cancellation);
            }
            finally
            {
                _adShowing = false;
            }

            if (cancellation.IsCancellationRequested || _flow.State != GameFlowState.Cards) return;
            if (watched)
            {
                _cards.Reroll();
                _ui.RequestSound(UiSoundKind.CardReroll);
            }
            Fill();
            Screen.Refresh();
        }

        private void Fill()
        {
            var screen = Screen;
            var offer = _cards.Offer;
            // The level this offer is for: several level-ups at once show theirs in turn.
            screen.SetSubtitle(_texts.Format(TextKeys.CardsSubtitle, _experience.Level - _experience.Pending + 1));
            for (var i = 0; i < screen.SlotCount; i++)
            {
                if (i >= offer.Count)
                {
                    screen.SetCard(i, false, default, null, null, null, null, null);
                    continue;
                }

                var card = offer[i];
                var level = _cards.LevelOf(card);
                screen.SetCard(i, true, card.Rarity,
                    _texts.Get(TextKeys.CardRarityPrefix + card.Rarity.ToString().ToLowerInvariant()), card.Icon,
                    NameOf(card), TextOf(card),
                    level <= 0 ? _texts.Get(TextKeys.CardsNew) : _texts.Format(TextKeys.CardsLevel, level + 1, card.MaxLevel));
            }

            screen.SetReroll(!_cards.RerollUsed);
        }

        private string NameOf(CardDefinition card)
        {
            var key = TextKeys.CardPrefix + card.Id;
            return _texts.Has(key) ? _texts.Get(key) : card.Id;
        }

        private string TextOf(CardDefinition card)
        {
            var key = TextKeys.CardPrefix + card.Id + TextKeys.CardTextSuffix;
            if (!_texts.Has(key)) return string.Empty;
            var modules = card.Modules;
            var values = new object[modules.Count];
            for (var i = 0; i < values.Length; i++)
                values[i] = modules[i] != null ? modules[i].ShownValue : 0f;
            return _texts.Format(key, values);
        }

        private void OnExperienceChanged() => _experienceDirty = true;

        private void OnLanguageChanged()
        {
            _experienceDirty = true;
            if (_flow.State == GameFlowState.Cards && _cards.HasOffer) Fill();
        }
    }
}
