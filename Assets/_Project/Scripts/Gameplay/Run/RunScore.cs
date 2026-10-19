using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Waves;
using UnityEngine;

namespace Purge.Gameplay.Run
{
    /// <summary>
    /// Kills, cleared waves and score of the run (stage 6d; ТЗ «Очки»): every horde monster killed (by anything) adds its
    /// <see cref="Core.Monsters.MonsterDefinition.Score"/>, a cleared wave <see cref="Core.Waves.WaveScenario.ScoreBonusOf"/>,
    /// both × the difficulty's score factor. The score is for survival records; on levels it is only shown in the result.
    /// </summary>
    public sealed class RunScore : ILevelLoadStep, IDisposable
    {
        private readonly HordeSystem _horde;
        private readonly WaveSystem _waves;
        private double _score;
        private bool _subscribed;

        public RunScore(HordeSystem horde, WaveSystem waves = null)
        {
            _horde = horde;
            _waves = waves;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>Horde monsters killed.</summary>
        public int Kills { get; private set; }

        public int WavesCleared { get; private set; }

        public long Score => (long)Math.Floor(_score + 1e-6);

        /// <summary>The run's difficulty score factor (1 without a difficulty).</summary>
        public float Factor => _horde.Difficulty != null ? _horde.Difficulty.Score : 1f;

        /// <summary>A continued run's numbers (stage 6e).</summary>
        public void Restore(int kills, int wavesCleared, long score)
        {
            Kills = Math.Max(0, kills);
            WavesCleared = Math.Max(0, wavesCleared);
            _score = Math.Max(0L, score);
        }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed || !_horde.IsActive) return UniTask.CompletedTask;
            _horde.Killed += OnKilled;
            if (_waves != null) _waves.WaveCleared += OnWaveCleared;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _horde.Killed -= OnKilled;
            if (_waves != null) _waves.WaveCleared -= OnWaveCleared;
            _subscribed = false;
        }

        private void OnKilled(int agent, Vector2 position)
        {
            Kills++;
            var definition = _horde.DefinitionOf(agent);
            if (definition != null) _score += definition.Score * Factor;
        }

        private void OnWaveCleared(int index)
        {
            WavesCleared++;
            if (_waves.Scenario != null) _score += _waves.Scenario.ScoreBonusOf(index) * Factor;
        }
    }
}
