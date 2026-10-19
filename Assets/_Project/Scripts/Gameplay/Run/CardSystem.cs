using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Cards;
using Purge.Core.Common;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Throwing;
using UnityEngine;

namespace Purge.Gameplay.Run
{
    /// <summary>
    /// Cards of the run (stage 6c; ТЗ «Прогрессия»): a level-up of <see cref="RunExperience"/> offers
    /// <see cref="CardDeck.Choices"/> cards of the deck by the weights of their rarity (no card twice; one at its last
    /// level never), the player picks one (<see cref="Choose"/>) and its modules act until the run's end: stats into
    /// <see cref="RunStats"/> (and from there into the player — speed, max HP, the magnet), throwables into the slot.
    /// One reroll per offer (<see cref="Reroll"/>: a rewarded ad, asked for by the presentation) brings other cards when the
    /// deck has them. When every card is at its last level, a level-up offers nothing. Deterministic by the run's seed.
    /// The cards' heal for a kill is dealt here too.
    /// </summary>
    public sealed class CardSystem : ILevelLoadStep, IDisposable
    {
        private readonly RunExperience _experience;
        private readonly RunStats _stats;
        private readonly CardDeck _deck;
        private readonly PlayerHealth _health;
        private readonly PlayerSystem _player;
        private readonly ThrowableSlot _throwables;
        private readonly PickupSystem _pickups;
        private readonly HordeSystem _horde;
        private readonly DeterministicRandom _random;
        private readonly Dictionary<CardDefinition, int> _levels = new Dictionary<CardDefinition, int>();
        private readonly List<CardDefinition> _offer = new List<CardDefinition>();
        private readonly List<CardDefinition> _candidates = new List<CardDefinition>();
        private readonly List<CardDefinition> _previous = new List<CardDefinition>();
        private bool _subscribed;

        public CardSystem(RunExperience experience, RunStats stats, PlayerHealth health, PlayerSystem player,
            ThrowableSlot throwables, PickupSystem pickups, HordeSystem horde, LevelLaunchOptions options = null,
            CardDeck deck = null)
        {
            _experience = experience;
            _stats = stats;
            _deck = deck;
            _health = health;
            _player = player;
            _throwables = throwables;
            _pickups = pickups;
            _horde = horde;
            _random = new DeterministicRandom((int)StableHash.Of($"cards/{options?.Seed ?? Environment.TickCount}"));
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool IsActive => _deck != null && _experience.IsActive;

        public CardDeck Deck => _deck;

        /// <summary>The cards to choose from now; empty when nothing is offered.</summary>
        public IReadOnlyList<CardDefinition> Offer => _offer;

        public bool HasOffer => _offer.Count > 0;

        /// <summary>The reroll of this offer is spent.</summary>
        public bool RerollUsed { get; private set; }

        /// <summary>Cards picked in the run (each with its level).</summary>
        public IReadOnlyDictionary<CardDefinition, int> Levels => _levels;

        /// <summary>After the offer appeared, changed (reroll) or went (chosen).</summary>
        public event Action OfferChanged;

        /// <summary>(card, its level now) after the player picked it.</summary>
        public event Action<CardDefinition, int> Chosen;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed || !IsActive || !_horde.IsActive) return UniTask.CompletedTask;
            _horde.Killed += OnKilled;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _horde.Killed -= OnKilled;
            _subscribed = false;
        }

        public int LevelOf(CardDefinition card) => card != null && _levels.TryGetValue(card, out var level) ? level : 0;

        /// <summary>
        /// Makes the offer of the next level-up waiting (<see cref="RunExperience.Pending"/>); level-ups with nothing to
        /// offer are spent. True when there is an offer to show.
        /// </summary>
        public bool OpenNext()
        {
            if (!IsActive) return false;
            if (HasOffer) return true;
            while (_experience.Pending > 0)
            {
                RerollUsed = false;
                _previous.Clear();
                Roll();
                if (HasOffer)
                {
                    OfferChanged?.Invoke();
                    return true;
                }

                _experience.ConsumePending();
            }

            return false;
        }

        /// <summary>Picks a card of the offer; its modules act at once, the offer goes.</summary>
        public void Choose(int index)
        {
            if ((uint)index >= (uint)_offer.Count) return;
            var card = _offer[index];
            _offer.Clear();
            _experience.ConsumePending();
            Apply(card);
            OfferChanged?.Invoke();
        }

        /// <summary>Other cards instead of the offer (once per offer); false when spent or nothing is offered.</summary>
        public bool Reroll()
        {
            if (!HasOffer || RerollUsed) return false;
            RerollUsed = true;
            _previous.Clear();
            _previous.AddRange(_offer);
            Roll();
            OfferChanged?.Invoke();
            return true;
        }

        /// <summary>A card's level and modules once more (a pick; also bonus cards later).</summary>
        public void Apply(CardDefinition card)
        {
            if (card == null) return;
            var level = LevelOf(card) + 1;
            _levels[card] = level;
            foreach (var module in card.Modules)
            {
                switch (module)
                {
                    case StatCard stat:
                        _stats.Add(stat.Stat, stat.PerLevel);
                        if (stat.Stat == RunStat.MaxHealth) AddMaxHealth(Mathf.RoundToInt(stat.PerLevel));
                        break;
                    case StatusDamageCard status:
                        _stats.AddStatusDamage(status.Status, status.PerLevel);
                        break;
                    case ThrowablesCard throwables when _throwables != null:
                        var kind = _throwables.IsEmpty ? throwables.Fallback : _throwables.Definition;
                        if (kind != null) _throwables.Add(kind, throwables.Count);
                        break;
                }
            }

            PushToPlayer();
            GameLog.Info(LogChannel.Gameplay, $"Card '{card.Id}' level {level}.");
            Chosen?.Invoke(card, level);
        }

        /// <summary>The stats the player's systems hold themselves.</summary>
        private void PushToPlayer()
        {
            if (_player != null) _player.BonusSpeed = _stats.MoveSpeedFactor;
            if (_pickups != null) _pickups.MagnetRadius = _stats.PickupRadius;
        }

        private void AddMaxHealth(int amount)
        {
            if (_health == null || amount <= 0) return;
            _health.SetMax(_health.Max + amount);
            if (!_health.IsDead) _health.Heal(amount);
        }

        /// <summary><see cref="CardDeck.Choices"/> cards by weight, those of the last offer only when no others are left.</summary>
        private void Roll()
        {
            _offer.Clear();
            _candidates.Clear();
            foreach (var card in _deck.Cards)
                if (card != null && LevelOf(card) < card.MaxLevel && _deck.WeightOf(card) > 0f && !_candidates.Contains(card))
                    _candidates.Add(card);

            var choices = _deck.Choices;
            Pick(choices, fresh: true);
            if (_offer.Count < choices) Pick(choices, fresh: false);
        }

        private void Pick(int choices, bool fresh)
        {
            while (_offer.Count < choices)
            {
                var total = 0f;
                foreach (var card in _candidates)
                    if (Allowed(card, fresh)) total += _deck.WeightOf(card);
                if (total <= 0f) return;

                var roll = (float)_random.NextDouble() * total;
                CardDefinition picked = null;
                foreach (var card in _candidates)
                {
                    if (!Allowed(card, fresh)) continue;
                    picked = card;
                    roll -= _deck.WeightOf(card);
                    if (roll < 0f) break;
                }

                _offer.Add(picked);
            }
        }

        private bool Allowed(CardDefinition card, bool fresh) =>
            !_offer.Contains(card) && (!fresh || !_previous.Contains(card));

        private void OnKilled(int agent, Vector2 position)
        {
            var heal = _stats.HealOnKill;
            if (heal > 0 && _health != null && !_health.IsDead && !_health.IsFull) _health.Heal(heal);
        }
    }
}
