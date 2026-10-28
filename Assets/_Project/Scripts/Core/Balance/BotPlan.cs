using System.Collections.Generic;
using Purge.Core.Cards;
using Purge.Core.Level;
using Purge.Core.Monsters;
using UnityEngine;

namespace Purge.Core.Balance
{
    /// <summary>
    /// The balance bot's batch (stage 10a, ТЗ «Бот-автопилот»): which levels on which difficulties, how many runs of
    /// each, and how the bot plays — the order it takes cards in and its distances. Dev only (Data/Balance), run from
    /// Purge → Balance → Run Bot; the statistics go to CSV in Tools/Balance/Bot.
    /// </summary>
    [CreateAssetMenu(fileName = "BotPlan", menuName = "Purge/Definitions/Bot Plan")]
    public sealed class BotPlan : ScriptableObject
    {
        [Header("Batch")]
        [Tooltip("Levels of the batch (from the game's catalog), in order.")]
        [SerializeField] private List<LevelData> _levels = new List<LevelData>();
        [Tooltip("Difficulties every level is played on, in order.")]
        [SerializeField] private List<DifficultyDefinition> _difficulties = new List<DifficultyDefinition>();
        [Tooltip("Runs of each level on each difficulty.")]
        [SerializeField, Min(1)] private int _runs = 10;
        [Tooltip("Seed of the first run; each run adds its number (the same batch replays the same waves).")]
        [SerializeField] private int _seed = 1000;
        [Tooltip("A run longer than this (minutes) is stopped and recorded as a timeout.")]
        [SerializeField, Min(1f)] private float _maxMinutes = 40f;

        [Header("Cards")]
        [Tooltip("Cards in the order the bot takes them; an offer without any of them — the rarest card.")]
        [SerializeField] private List<CardDefinition> _cardPriority = new List<CardDefinition>();

        [Header("Fight")]
        [Tooltip("The bot backs away from monsters closer than this (cells).")]
        [SerializeField, Min(1f)] private float _keepDistance = 5f;
        [Tooltip("Monsters within this radius steer the bot (cells).")]
        [SerializeField, Min(2f)] private float _threatRadius = 9f;
        [Tooltip("Throws when at least this many monsters stand around one point.")]
        [SerializeField, Min(1)] private int _throwCrowd = 6;
        [Tooltip("Shoots a barrel with at least this many monsters around it.")]
        [SerializeField, Min(1)] private int _barrelCrowd = 4;
        [Tooltip("Radius of a crowd around a throw's or a barrel's point (cells).")]
        [SerializeField, Min(0.5f)] private float _crowdRadius = 2.5f;

        [Header("Errands")]
        [Tooltip("Goes for drop, chests and keys this close (cells, along the way)…")]
        [SerializeField, Min(1f)] private float _errandRadius = 12f;
        [Tooltip("…only while no monster is this close (cells).")]
        [SerializeField, Min(1f)] private float _safeDistance = 7f;

        public IReadOnlyList<LevelData> Levels => _levels;
        public IReadOnlyList<DifficultyDefinition> Difficulties => _difficulties;
        public int Runs => _runs;
        public int Seed => _seed;
        public float MaxMinutes => _maxMinutes;
        public IReadOnlyList<CardDefinition> CardPriority => _cardPriority;
        public float KeepDistance => _keepDistance;
        public float ThreatRadius => _threatRadius;
        public int ThrowCrowd => _throwCrowd;
        public int BarrelCrowd => _barrelCrowd;
        public float CrowdRadius => _crowdRadius;
        public float ErrandRadius => _errandRadius;
        public float SafeDistance => _safeDistance;

        /// <summary>Index of the card in <see cref="CardPriority"/>; int.MaxValue — not listed.</summary>
        public int PriorityOf(CardDefinition card)
        {
            var index = card != null ? _cardPriority.IndexOf(card) : -1;
            return index >= 0 ? index : int.MaxValue;
        }

        /// <summary>For tools and tests.</summary>
        internal void Configure(IEnumerable<LevelData> levels, IEnumerable<DifficultyDefinition> difficulties, int runs,
            IEnumerable<CardDefinition> cardPriority)
        {
            _levels = new List<LevelData>(levels);
            _difficulties = new List<DifficultyDefinition>(difficulties);
            _runs = Mathf.Max(1, runs);
            _cardPriority = new List<CardDefinition>(cardPriority);
        }
    }
}
