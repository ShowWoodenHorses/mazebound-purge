using System;
using Purge.Core.Cards;
using Purge.Core.Definitions;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Objects;
using Purge.Gameplay.Player;
using Purge.Gameplay.Run;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Waves;
using Purge.Gameplay.Weapons;
using UnityEngine;

namespace Purge.Gameplay.Balance
{
    /// <summary>
    /// Writes the balance bot's run into its <see cref="BotReport"/> (stage 10a): kills by rank, money (and of kills
    /// alone), experience, damage and healing, cards, throws, chests, weapons taken — over the run and by wave. Registered
    /// only for a bot's run (<see cref="LevelLaunchOptions.Bot"/>), ticks only while the level runs (pauses not counted).
    /// </summary>
    public sealed class BotRecorder : ILevelTickable, IDisposable
    {
        private readonly BotReport _report;
        private readonly HordeSystem _horde;
        private readonly WaveSystem _waves;
        private readonly RunWallet _wallet;
        private readonly RunExperience _experience;
        private readonly CardSystem _cards;
        private readonly PlayerHealth _health;
        private readonly PlayerRevival _revival;
        private readonly PlayerThrowing _throwing;
        private readonly ChestSystem _chests;
        private readonly WeaponSystem _weapons;
        private int _lastHealth;
        private bool _disposed;

        public BotRecorder(LevelLaunchOptions options, HordeSystem horde, WaveSystem waves, RunWallet wallet,
            RunExperience experience, CardSystem cards, PlayerHealth health, PlayerRevival revival, PlayerThrowing throwing,
            ChestSystem chests, WeaponSystem weapons)
        {
            _report = options.Bot.Report;
            _horde = horde;
            _waves = waves;
            _wallet = wallet;
            _experience = experience;
            _cards = cards;
            _health = health;
            _revival = revival;
            _throwing = throwing;
            _chests = chests;
            _weapons = weapons;
            _lastHealth = health.Current;

            _horde.Killed += OnKilled;
            _waves.WaveStarted += OnWaveStarted;
            _waves.WaveCleared += OnWaveCleared;
            _wallet.Changed += OnMoney;
            _cards.Chosen += OnCardChosen;
            _health.Changed += OnHealthChanged;
            _revival.Died += OnDied;
            _throwing.Thrown += OnThrown;
            _chests.Opened += OnChestOpened;
            _weapons.Changed += OnWeaponsChanged;
        }

        public void Tick(float deltaTime)
        {
            _report.Duration += deltaTime;
            _report.WaveCount = _waves.WaveCount;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close(_report.CurrentWave);
            _horde.Killed -= OnKilled;
            _waves.WaveStarted -= OnWaveStarted;
            _waves.WaveCleared -= OnWaveCleared;
            _wallet.Changed -= OnMoney;
            _cards.Chosen -= OnCardChosen;
            _health.Changed -= OnHealthChanged;
            _revival.Died -= OnDied;
            _throwing.Thrown -= OnThrown;
            _chests.Opened -= OnChestOpened;
            _weapons.Changed -= OnWeaponsChanged;
        }

        private void OnKilled(int agent, Vector2 position)
        {
            var definition = _horde.DefinitionOf(agent);
            if (definition == null) return;
            var rank = Mathf.Clamp((int)definition.Rank, 0, BotReport.Ranks - 1);
            var experience = _experience.IsActive ? definition.Experience : 0f;
            _report.Kills[rank]++;
            _report.KillMoney += definition.Money * _wallet.Factor;
            _report.Experience += experience;
            var wave = _report.CurrentWave;
            if (wave == null) return;
            wave.Kills[rank]++;
            wave.Experience += experience;
        }

        private void OnWaveStarted(int index)
        {
            Close(_report.CurrentWave);
            _report.Waves.Add(new BotWave(index, _report.Duration));
        }

        private void OnWaveCleared(int index)
        {
            var wave = _report.CurrentWave;
            if (wave == null || wave.Index != index) return;
            wave.Duration = _report.Duration - wave.Start;
            _report.WavesCleared++;
            Close(wave);
        }

        /// <summary>The wave's end state: the level and HP when it was cleared or the run stopped.</summary>
        private void Close(BotWave wave)
        {
            if (wave == null) return;
            wave.XpLevel = _experience.Level;
            wave.Health = _health.Current;
        }

        private void OnMoney(int money, int added)
        {
            _report.Money = money;
            var wave = _report.CurrentWave;
            if (wave != null) wave.Money += added;
        }

        private void OnCardChosen(CardDefinition card, int level)
        {
            _report.XpLevel = _experience.Level;
            _report.Cards.Add($"{card.Id}@{Mathf.RoundToInt(_report.Duration)}");
        }

        /// <summary>Every change of HP: blows, zones and states alike (only blows have <see cref="PlayerHealth.Hurt"/>).</summary>
        private void OnHealthChanged(int current, int max)
        {
            if (current > _lastHealth) _report.Healed += current - _lastHealth;
            else
            {
                var damage = _lastHealth - current;
                _report.DamageTaken += damage;
                var wave = _report.CurrentWave;
                if (wave != null) wave.DamageTaken += damage;
            }

            _lastHealth = current;
            _report.XpLevel = _experience.Level;
        }

        private void OnDied()
        {
            _report.Died = true;
            Close(_report.CurrentWave);
        }

        private void OnThrown(ThrowableDefinition throwable, Vector2 direction) => _report.Throws++;

        private void OnChestOpened(Chest chest) => _report.ChestsOpened++;

        private void OnWeaponsChanged()
        {
            for (var slot = 0; slot < _report.Weapons.Length; slot++)
            {
                var weapon = _weapons.Get((WeaponSlot)slot);
                var id = weapon == null || weapon.IsBase ? "" : weapon.Definition.Id;
                if (id.Length > 0 && id != _report.Weapons[slot] && _report.Duration > 0f) _report.WeaponsTaken++;
                _report.Weapons[slot] = id;
            }
        }
    }
}
