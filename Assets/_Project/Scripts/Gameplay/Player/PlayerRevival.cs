using System;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Level;

namespace Purge.Gameplay.Player
{
    /// <summary>How the player comes back (ТЗ: воскрешение за rewarded и отдельно навыком — stage 6d).</summary>
    public enum ReviveKind
    {
        Ad = 0,
        Skill = 1,
    }

    /// <summary>
    /// The player's death and revive (ТЗ §88, stage 6d). HP 0: the player falls (<see cref="PlayerSystem.Down"/>, the death
    /// clip), the world goes on for <see cref="PlayerDefinition.DeathDelay"/>, then — while a revive is left —
    /// <see cref="Downed"/> asks for the death window (the flow pauses the game); with none left <see cref="Died"/> at once.
    /// <see cref="Revive"/> (once by an ad, once by the character's skill a run) brings the player back with full HP,
    /// states cleared, <see cref="PlayerDefinition.ReviveInvulnerability"/> seconds of invulnerability and
    /// <see cref="PlayerDefinition.ReviveEffects"/> around (the wave pushing back and stunning the monsters);
    /// <see cref="GiveUp"/> — <see cref="Died"/>. The level's failure is <see cref="PlayerDeathRule"/>'s.
    /// </summary>
    public sealed class PlayerRevival : ILevelTickable, IDisposable
    {
        private readonly PlayerHealth _health;
        private readonly PlayerSystem _player;
        private readonly EffectSystem _effects;
        private readonly PlayerStatusSystem _statuses;
        private float _downTime;
        private float _invulnerableLeft;
        private bool _dead;

        public PlayerRevival(PlayerHealth health, PlayerSystem player = null, EffectSystem effects = null,
            PlayerStatusSystem statuses = null)
        {
            _health = health;
            _player = player;
            _effects = effects;
            _statuses = statuses;
            _health.Changed += OnHealthChanged;
        }

        /// <summary>Dead, waiting for the window or for its choice.</summary>
        public bool IsDown { get; private set; }

        /// <summary>The death window is due: the delay passed and a revive is left.</summary>
        public bool AwaitsChoice { get; private set; }

        public bool AdReviveUsed { get; private set; }
        public bool SkillReviveUsed { get; private set; }

        /// <summary>Revives of the run so far (both kinds).</summary>
        public int RevivesUsed => (AdReviveUsed ? 1 : 0) + (SkillReviveUsed ? 1 : 0);

        /// <summary>The character's revive skill is open.</summary>
        public bool HasSkill => Definition != null && Definition.ReviveSkill;

        public bool CanRevive(ReviveKind kind) => kind == ReviveKind.Ad ? !AdReviveUsed : HasSkill && !SkillReviveUsed;

        public bool CanReviveAtAll => CanRevive(ReviveKind.Ad) || CanRevive(ReviveKind.Skill);

        /// <summary>The death window is due (once per death, after the delay).</summary>
        public event Action Downed;

        /// <summary>The player came back.</summary>
        public event Action<ReviveKind> Revived;

        /// <summary>Dead for good: no revive left, or the player gave up.</summary>
        public event Action Died;

        private PlayerDefinition Definition => _player != null ? _player.Definition : null;

        public void Dispose() => _health.Changed -= OnHealthChanged;

        public void Tick(float deltaTime)
        {
            if (_invulnerableLeft > 0f)
            {
                _invulnerableLeft -= deltaTime;
                if (_invulnerableLeft <= 0f) _health.Invulnerable = false;
            }

            if (!IsDown || AwaitsChoice || _dead) return;
            _downTime += deltaTime;
            if (_downTime < (Definition != null ? Definition.DeathDelay : 0f)) return;

            if (!CanReviveAtAll)
            {
                GameLog.Info(LogChannel.Gameplay, "No revive left.");
                Die();
                return;
            }

            AwaitsChoice = true;
            Downed?.Invoke();
        }

        /// <summary>Brings the dead player back (false when that kind is used up or the player is alive).</summary>
        public bool Revive(ReviveKind kind)
        {
            if (!IsDown || _dead || !CanRevive(kind)) return false;
            if (kind == ReviveKind.Ad) AdReviveUsed = true;
            else SkillReviveUsed = true;

            IsDown = false;
            AwaitsChoice = false;
            _statuses?.Clear();
            _health.Revive();
            var definition = Definition;
            _invulnerableLeft = definition != null ? definition.ReviveInvulnerability : 0f;
            _health.Invulnerable = _invulnerableLeft > 0f;
            if (_player != null)
            {
                _player.Down = false;
                if (definition != null && _effects != null) _effects.Apply(definition.ReviveEffects, _player.Position, _player.Floor);
            }

            GameLog.Info(LogChannel.Gameplay, $"The player revived ({kind}).");
            Revived?.Invoke(kind);
            return true;
        }

        /// <summary>No revive wanted: dead for good.</summary>
        public void GiveUp()
        {
            if (!IsDown || _dead) return;
            GameLog.Info(LogChannel.Gameplay, "The player gave up.");
            Die();
        }

        private void Die()
        {
            _dead = true;
            AwaitsChoice = false;
            Died?.Invoke();
        }

        private void OnHealthChanged(int current, int max)
        {
            if (current > 0 || IsDown)
                return;

            GameLog.Info(LogChannel.Gameplay, "The player died.");
            IsDown = true;
            AwaitsChoice = false;
            _downTime = 0f;
            if (_player != null) _player.Down = true;
        }
    }
}
