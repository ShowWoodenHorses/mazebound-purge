using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Level;
using Maze.Gameplay.Navigation;
using Maze.Gameplay.Player;
using Maze.Gameplay.Sound;
using Maze.Gameplay.Spatial;

namespace Maze.Gameplay.Zombies
{
    /// <summary>
    /// Zombies of the level (ТЗ §73–77): spawns them (load stage SpawnZombies), ticks their AI, forwards sounds to
    /// hearing, keeps occupancy and spatial queries in sync and removes the dead. Zombies are damageable targets of
    /// <see cref="ISpatialQueryService"/>. Visibility never stops them (ТЗ §57).
    /// </summary>
    public sealed class ZombieSystem : ILevelLoadStep, ILevelTickable, IDisposable
    {
        private readonly LevelData _level;
        private readonly NavigationSystem _navigation;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _playerHealth;
        private readonly OccupancyMap _occupancy;
        private readonly SpatialQueryService _spatial;
        private readonly SoundEventBus _sounds;
        private readonly List<ZombieController> _controllers = new List<ZombieController>();
        private readonly List<ZombieRuntime> _zombies = new List<ZombieRuntime>();
        private bool _subscribed;

        public ZombieSystem(LevelData level, NavigationSystem navigation, PlayerSystem player, PlayerHealth playerHealth,
            OccupancyMap occupancy, SpatialQueryService spatial, SoundEventBus sounds)
        {
            _level = level;
            _navigation = navigation;
            _player = player;
            _playerHealth = playerHealth;
            _occupancy = occupancy;
            _spatial = spatial;
            _sounds = sounds;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>Every zombie of the level, dead ones included.</summary>
        public IReadOnlyList<ZombieRuntime> Zombies => _zombies;

        public int TotalCount => _zombies.Count;
        public int KilledCount { get; private set; }
        public bool AllKilled => KilledCount == _zombies.Count;

        public event Action<ZombieRuntime> Spawned;

        /// <summary>(zombie) after it hit the player.</summary>
        public event Action<ZombieRuntime> Attacked;

        public event Action<ZombieRuntime> Died;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var patrols = new Dictionary<string, IReadOnlyList<GridPosition>>();
            foreach (var patrol in _level.Patrols)
                patrols[patrol.Id] = patrol.Points;

            for (var i = 0; i < _level.ZombieSpawns.Count; i++)
            {
                var spawn = _level.ZombieSpawns[i];
                patrols.TryGetValue(spawn.PatrolId ?? string.Empty, out var points);
                var zombie = new ZombieRuntime(spawn, points);
                _occupancy.AddZombie(zombie.Cell);
                _spatial.Add(zombie);
                zombie.Died += OnDied;

                // Spread detection samples over frames.
                var controller = new ZombieController(zombie, _navigation, _player, _playerHealth, _occupancy, _spatial,
                    ZombieController.DetectionInterval * i / Math.Max(1, _level.ZombieSpawns.Count));
                controller.Attacked += OnAttacked;
                _zombies.Add(zombie);
                _controllers.Add(controller);
                Spawned?.Invoke(zombie);
            }

            if (!_subscribed)
            {
                _sounds.Emitted += OnSound;
                _subscribed = true;
            }

            if (_zombies.Count > 0)
                GameLog.Info(LogChannel.Gameplay, $"Spawned {_zombies.Count} zombie(s).");
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            for (var i = 0; i < _controllers.Count; i++)
                _controllers[i].Tick(deltaTime);
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _sounds.Emitted -= OnSound;
                _subscribed = false;
            }

            foreach (var controller in _controllers)
                controller.Attacked -= OnAttacked;
            foreach (var zombie in _zombies)
                zombie.Died -= OnDied;
        }

        private void OnSound(SoundEvent sound)
        {
            for (var i = 0; i < _controllers.Count; i++)
                _controllers[i].Hear(sound);
        }

        private void OnAttacked(ZombieRuntime zombie) => Attacked?.Invoke(zombie);

        private void OnDied(ZombieRuntime zombie)
        {
            _occupancy.RemoveZombie(zombie.Cell);
            _spatial.Remove(zombie);
            KilledCount++;
            GameLog.Info(LogChannel.Gameplay, $"Zombie '{zombie.Id}' killed ({KilledCount}/{_zombies.Count}).");
            Died?.Invoke(zombie);
        }
    }
}
