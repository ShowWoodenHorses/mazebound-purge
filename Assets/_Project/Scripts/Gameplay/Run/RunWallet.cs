using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using UnityEngine;

namespace Purge.Gameplay.Run
{
    /// <summary>
    /// Money of the run (stage 6b; ТЗ: одна валюта): every horde monster killed (by anything) pays its
    /// <see cref="Core.Monsters.MonsterDefinition.Money"/> × the run's difficulty money factor, the pause's «Пропустить»
    /// pays its bonus (<see cref="Waves.WaveSystem.Skip"/>), so does a cleared wave (stage 6d). Fractions add up (a factor
    /// of 0.8 still pays 4 for 5 rank-and-file). Goes to the save when the run ends or is left (<c>GameFlow</c>, stage 6d);
    /// the coin flying to the HUD — stage 6e.
    /// </summary>
    public sealed class RunWallet : ILevelLoadStep, IDisposable
    {
        private readonly HordeSystem _horde;
        private float _fraction;
        private bool _subscribed;

        public RunWallet(HordeSystem horde)
        {
            _horde = horde;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public int Money { get; private set; }

        /// <summary>(money now, added) after it grew.</summary>
        public event Action<int, int> Changed;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed || !_horde.IsActive) return UniTask.CompletedTask;
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

        /// <summary>The run's difficulty money factor (1 without a difficulty).</summary>
        public float Factor => _horde.Difficulty != null ? _horde.Difficulty.Money : 1f;

        /// <summary>A continued run's money (stage 6e), without <see cref="Changed"/>.</summary>
        public void Restore(int money)
        {
            Money = Mathf.Max(0, money);
            _fraction = 0f;
        }

        /// <summary>Adds <paramref name="amount"/> (already × the factor); the fraction is kept for the next one.</summary>
        public void Add(float amount)
        {
            if (amount <= 0f) return;
            _fraction += amount;
            var whole = Mathf.FloorToInt(_fraction + 1e-4f);
            if (whole <= 0) return;
            _fraction -= whole;
            Money += whole;
            Changed?.Invoke(Money, whole);
        }

        private void OnKilled(int agent, Vector2 position)
        {
            var definition = _horde.DefinitionOf(agent);
            if (definition != null) Add(definition.Money * Factor);
        }
    }
}
