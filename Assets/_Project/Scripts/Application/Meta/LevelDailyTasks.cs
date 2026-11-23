using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Flow;
using Purge.Application.Levels;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Vehicles;
using Purge.Gameplay.Waves;
using UnityEngine;

namespace Purge.Application.Meta
{
    public sealed class LevelDailyTasks : ILevelLoadStep, IDisposable
    {
        private readonly DailyTasks _tasks;
        private readonly GameFlow _flow;
        private readonly ILevelCatalog _catalog;
        private readonly HordeSystem _horde;
        private readonly WaveSystem _waves;
        private readonly VehicleSystem _vehicles;
        private bool _subscribed;

        public LevelDailyTasks(DailyTasks tasks, GameFlow flow, ILevelCatalog catalog, HordeSystem horde,
            WaveSystem waves, VehicleSystem vehicles)
        {
            _tasks = tasks;
            _flow = flow;
            _catalog = catalog;
            _horde = horde;
            _waves = waves;
            _vehicles = vehicles;
        }

        public LevelLoadStage Stage => LevelLoadStage.Warmup;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed || _flow.CurrentLaunch.Bot != null) return UniTask.CompletedTask;
            foreach (var level in _catalog.Levels)
            {
                if (level.LevelId != _flow.CurrentLevelId) continue;
                _horde.Killed += OnKilled;
                _waves.WaveCleared += OnWaveCleared;
                _vehicles.Rammed += OnRammed;
                _subscribed = true;
                break;
            }
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _horde.Killed -= OnKilled;
            _waves.WaveCleared -= OnWaveCleared;
            _vehicles.Rammed -= OnRammed;
            _subscribed = false;
            _tasks.Flush();
        }

        private bool Counts => _flow.State == GameFlowState.Playing && !_flow.IsTestRun;

        private void OnKilled(int agent, Vector2 position)
        {
            if (Counts) _tasks.Record(DailyTasks.LocalDay, DailyCondition.Kills, 1);
        }

        private void OnWaveCleared(int index)
        {
            if (!Counts) return;
            var day = DailyTasks.LocalDay;
            _tasks.Record(day, DailyCondition.ClearedWaves, 1);
            if (!_waves.IsSurvival)
                _tasks.Record(day, DailyCondition.CampaignWave, index + 1, _flow.CurrentDifficulty);
        }

        private void OnRammed(Vehicle vehicle, int hits, int kills)
        {
            if (Counts) _tasks.Record(DailyTasks.LocalDay, DailyCondition.RamKills, kills);
        }
    }
}
