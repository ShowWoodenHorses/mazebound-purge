using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Cards;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using UnityEngine;

namespace Purge.Gameplay.Run
{
    /// <summary>
    /// Experience and level of the run (stage 6c; ТЗ «Прогрессия»): every horde monster killed (by anything) gives its
    /// <see cref="Core.Monsters.MonsterDefinition.Experience"/>; the curve is the deck's (<see cref="CardDeck.ExperienceFor"/>).
    /// A level-up adds to <see cref="Pending"/> — the cards to choose (<see cref="CardSystem"/>); several at once wait in
    /// turn. Without a deck or waves (sandbox, stress test) nothing is counted.
    /// </summary>
    public sealed class RunExperience : ILevelLoadStep, IDisposable
    {
        private readonly HordeSystem _horde;
        private readonly CardDeck _deck;
        private float _fraction;
        private bool _subscribed;

        public RunExperience(HordeSystem horde, CardDeck deck = null)
        {
            _horde = horde;
            _deck = deck;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>A run: the deck is there and the level has waves (not the sandbox or the stress test).</summary>
        public bool IsActive => _deck != null && _horde.UsesWaves;

        /// <summary>Level of the run, from 1.</summary>
        public int Level { get; private set; } = 1;

        /// <summary>Experience gathered towards the next level.</summary>
        public int Experience { get; private set; }

        /// <summary>Experience from this level to the next one.</summary>
        public int Next => _deck != null ? _deck.ExperienceFor(Level) : 1;

        /// <summary>0..1 of the way to the next level.</summary>
        public float Progress => Mathf.Clamp01(Experience / (float)Mathf.Max(1, Next));

        /// <summary>Level-ups whose cards are not chosen yet.</summary>
        public int Pending { get; private set; }

        /// <summary>After the experience changed.</summary>
        public event Action Changed;

        /// <summary>(new level) after a level-up.</summary>
        public event Action<int> LeveledUp;

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

        /// <summary>Adds experience; the fraction is kept for the next one.</summary>
        public void Add(float amount)
        {
            if (!IsActive || amount <= 0f) return;
            _fraction += amount;
            var whole = Mathf.FloorToInt(_fraction + 1e-4f);
            if (whole <= 0) return;
            _fraction -= whole;
            Experience += whole;
            while (Experience >= Next)
            {
                Experience -= Next;
                Level++;
                Pending++;
                LeveledUp?.Invoke(Level);
            }

            Changed?.Invoke();
        }

        /// <summary>A continued run's level and experience (stage 6e): its cards are restored apart, nothing is pending.</summary>
        public void Restore(int level, int experience)
        {
            Level = Mathf.Max(1, level);
            Experience = Mathf.Max(0, experience);
            Pending = 0;
            _fraction = 0f;
            Changed?.Invoke();
        }

        /// <summary>One level-up's cards were chosen (or there was nothing to choose).</summary>
        public void ConsumePending()
        {
            if (Pending > 0) Pending--;
        }

        private void OnKilled(int agent, Vector2 position)
        {
            var definition = _horde.DefinitionOf(agent);
            if (definition != null) Add(definition.Experience);
        }
    }
}
