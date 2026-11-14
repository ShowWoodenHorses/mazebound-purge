using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Boosters;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Monsters;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Visibility;
using Purge.Gameplay.Waves;
using UnityEngine;

namespace Purge.Gameplay.Run
{
    /// <summary>Character effects tied to the life of a wave.</summary>
    public sealed class CharacterRunEffects : ILevelLoadStep, ILevelTickable, IDisposable
    {
        private readonly LevelLaunchOptions _options;
        private readonly WaveSystem _waves;
        private readonly RunStats _stats;
        private readonly PlayerHealth _health;
        private readonly SoundEventBus _sounds;
        private readonly HordeSystem _horde;
        private readonly MonsterAbilities _abilities;
        private readonly PlayerSystem _player;
        private readonly VisibilitySystem _visibility;
        private float _waveTime;
        private float _warningCooldown;

        public CharacterRunEffects(LevelLaunchOptions options, WaveSystem waves, RunStats stats, PlayerHealth health,
            SoundEventBus sounds, HordeSystem horde, MonsterAbilities abilities, PlayerSystem player,
            VisibilitySystem visibility)
        {
            _options = options;
            _waves = waves;
            _stats = stats;
            _health = health;
            _sounds = sounds;
            _horde = horde;
            _abilities = abilities;
            _player = player;
            _visibility = visibility;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public event Action<Vector2> ThreatDetected;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _waves.WaveStarted += OnWaveStarted;
            _waves.WaveCleared += OnWaveCleared;
            if (_options.HasSkill(CharacterSkills.QuietSteps)) _sounds.FootstepNoiseFactor = 0.5f;
            if (_options.HasSkill(CharacterSkills.Instinct))
            {
                _horde.PlayerAttackStarted += OnThreat;
                _abilities.CastStarted += OnCastStarted;
            }
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            _waves.WaveStarted -= OnWaveStarted;
            _waves.WaveCleared -= OnWaveCleared;
            _horde.PlayerAttackStarted -= OnThreat;
            _abilities.CastStarted -= OnCastStarted;
        }

        public void Tick(float deltaTime)
        {
            _warningCooldown = Mathf.Max(0f, _warningCooldown - deltaTime);
            if (_waves.Phase != WavePhase.Running) return;
            _waveTime += deltaTime;
            if (_waveTime >= 12f) _stats.TacticianActive = false;
            if (_waveTime >= 30f) _sounds.PlayerNoiseFactor = 1f;
        }

        private void OnWaveStarted(int index)
        {
            _waveTime = 0f;
            _stats.TacticianActive = _options.HasSkill(CharacterSkills.Tactician);
            if (_options.HasSkill(CharacterSkills.Barrier)) _health.RefillBarrier();
            if (index == 0 && _options.HasBoost(SpecialBoosterKind.QuietStart)) _sounds.PlayerNoiseFactor = 0.4f;
        }

        private void OnWaveCleared(int index)
        {
            _stats.TacticianActive = false;
            _sounds.PlayerNoiseFactor = 1f;
        }

        private void OnCastStarted(int agent, int ability)
        {
            if (_abilities.AbilityOf(agent, ability) is ProjectileAbility or StrikeAbility or ChargeAbility or
                PushAbility or AreaStrikeAbility or PullAbility or GazeAbility)
                OnThreat(_horde.Positions[agent]);
        }

        private void OnThreat(Vector2 position)
        {
            if (_warningCooldown > 0f || !_player.IsSpawned) return;
            var delta = position - _player.Position;
            if (delta.sqrMagnitude > 36f ||
                _visibility.IsVisible(new GridPosition(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y), _player.Floor)))
                return;
            _warningCooldown = 3f;
            ThreatDetected?.Invoke(delta);
        }
    }
}
