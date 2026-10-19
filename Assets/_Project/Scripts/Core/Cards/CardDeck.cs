using System.Collections.Generic;
using Purge.Core.Content;
using UnityEngine;

namespace Purge.Core.Cards
{
    /// <summary>
    /// The cards of a run and how often they come (stage 6c): every <see cref="CardDefinition"/> of <c>Data/Cards</c>
    /// (Purge → Dev → Build Cards collects them), the experience curve of the run's levels, weights by rarity and how
    /// many cards a level-up offers. Shared by every level (address <see cref="Address"/>); numbers are drafts
    /// (balance — stage 9).
    /// </summary>
    [CreateAssetMenu(fileName = "CardDeck", menuName = "Purge/Definitions/Card Deck")]
    public sealed class CardDeck : ScriptableObject
    {
        public const string Address = "Cards/Deck";

        [SerializeField] private List<CardDefinition> _cards = new List<CardDefinition>();

        [Header("Experience")]
        [Tooltip("Experience from level 1 to level 2.")]
        [SerializeField, Min(1f)] private float _firstLevel = 10f;

        [Tooltip("Each next level needs this many times more (a card comes less often towards the end).")]
        [SerializeField, Min(1f)] private float _growth = 1.25f;

        [Header("Choice")]
        [SerializeField, Min(1)] private int _choices = 3;

        [Tooltip("Weight of a card in the choice by its rarity: gray, green, blue, purple, orange.")]
        [SerializeField] private float[] _rarityWeights = { 10f, 6f, 3f, 1.5f, 0.5f };

        public IReadOnlyList<CardDefinition> Cards => _cards;
        public int Choices => Mathf.Max(1, _choices);

        /// <summary>Experience from <paramref name="level"/> to the next one (level 1 → 2 = the first level's).</summary>
        public int ExperienceFor(int level) =>
            Mathf.Max(1, Mathf.RoundToInt(_firstLevel * Mathf.Pow(_growth, Mathf.Max(0, level - 1))));

        public float WeightOf(CardDefinition card) => card != null ? WeightOf(card.Rarity) : 0f;

        public float WeightOf(Rarity rarity)
        {
            var index = (int)rarity;
            return _rarityWeights != null && index < _rarityWeights.Length ? Mathf.Max(0f, _rarityWeights[index]) : 0f;
        }

        /// <summary>Tools and tests.</summary>
        internal void SetCards(IEnumerable<CardDefinition> cards) => _cards = new List<CardDefinition>(cards);

        /// <summary>Tests.</summary>
        internal void Configure(float firstLevel, float growth, int choices)
        {
            _firstLevel = Mathf.Max(1f, firstLevel);
            _growth = Mathf.Max(1f, growth);
            _choices = Mathf.Max(1, choices);
        }
    }
}
