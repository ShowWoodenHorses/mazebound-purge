using System.Collections.Generic;
using Purge.Core.Effects;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Definitions
{
    /// <summary>
    /// Gameplay parameters of the player. Shared by all levels, Addressable at <see cref="Address"/>.
    /// Visual representation is defined separately (<see cref="Visual.PlayerVisualDefinition"/>), never here.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerDefinition", menuName = "Purge/Definitions/Player")]
    public sealed class PlayerDefinition : ScriptableObject
    {
        public const string Address = "Player/Definition";

        [Tooltip("Cells (metres) per second at full stick deflection.")]
        [SerializeField, Min(0.1f)] private float _moveSpeed = 3.5f;

        [Tooltip("Share of the speed kept while the player's centre is in a snowdrift cell.")]
        [SerializeField, Range(0.1f, 1f)] private float _snowdriftSpeed = 0.6f;

        [Tooltip("Half size of the player's square footprint used for collisions with walls and closed doors. " +
                 "Must be below 0.5 so the player fits a one-cell corridor.")]
        [SerializeField, Range(0.05f, 0.45f)] private float _bodyHalfSize = 0.3f;

        [Tooltip("How far (in cells) the player is nudged toward a side passage when sliding along a wall.")]
        [SerializeField, Range(0f, 0.5f)] private float _cornerAssist = 0.35f;

        [SerializeField, Min(1)] private int _maxHealth = 100;

        [Tooltip("Body mass against the monsters' (stage 5d): bumping into one, the lighter gives way more. Rank-and-file 0.15, Brute 6.")]
        [SerializeField, Min(0.01f)] private float _mass = 1f;

        [Tooltip("The weapon of an empty slot (ТЗ: базовый пистолет, бесконечные патроны): a picked weapon replaces it " +
                 "and is never dropped for it; a dropped weapon brings it back.")]
        [SerializeField] private WeaponDefinition _baseWeapon;

        [Header("Death and revive (stage 6d)")]
        [Tooltip("Seconds the player lies (the death clip) before the death window.")]
        [SerializeField, Min(0f)] private float _deathDelay = 1.3f;
        [Tooltip("Seconds no damage reaches the revived player.")]
        [SerializeField, Min(0f)] private float _reviveInvulnerability = 2f;
        [Tooltip("The character's revive skill is open (ТЗ: once a run besides the rewarded one). Until the character tree " +
                 "(stage 10) — this flag.")]
        [SerializeField] private bool _reviveSkill = true;
        [Tooltip("What happens around the revived player, in order: a wave pushing back and stunning the monsters, its look.")]
        [SerializeReference, ModuleList] private List<EffectModule> _reviveEffects = new List<EffectModule>();

        [Header("Sounds (ТЗ §71), radii in cells")]
        [Tooltip("A step sound is emitted every this many cells walked.")]
        [SerializeField, Min(0.1f)] private float _stepDistance = 0.8f;
        [SerializeField, Min(0f)] private float _stepSoundRadius = 2f;
        [Tooltip("Doors and pickups.")]
        [SerializeField, Min(0f)] private float _interactionSoundRadius = 3f;

        public float MoveSpeed => _moveSpeed;
        public float SnowdriftSpeed => _snowdriftSpeed;
        public float BodyHalfSize => _bodyHalfSize;
        public float CornerAssist => _cornerAssist;
        public int MaxHealth => _maxHealth;
        public float Mass => _mass > 0f ? _mass : 1f;
        public WeaponDefinition BaseWeapon { get => _baseWeapon; internal set => _baseWeapon = value; }
        public float StepDistance => _stepDistance;
        public float StepSoundRadius => _stepSoundRadius;
        public float InteractionSoundRadius => _interactionSoundRadius;
        public float DeathDelay => _deathDelay;
        public float ReviveInvulnerability => _reviveInvulnerability;
        public bool ReviveSkill { get => _reviveSkill; internal set => _reviveSkill = value; }
        public IReadOnlyList<EffectModule> ReviveEffects => _reviveEffects;

        /// <summary>Tools: the revive's effects after editing them in place.</summary>
        internal List<EffectModule> MutableReviveEffects => _reviveEffects ??= new List<EffectModule>();

        /// <summary>For tests.</summary>
        internal void ConfigureDeath(float deathDelay, float reviveInvulnerability)
        {
            _deathDelay = deathDelay;
            _reviveInvulnerability = reviveInvulnerability;
        }

        /// <summary>For tests and tools.</summary>
        internal void Configure(float moveSpeed, float bodyHalfSize, float cornerAssist)
        {
            _moveSpeed = moveSpeed;
            _bodyHalfSize = bodyHalfSize;
            _cornerAssist = cornerAssist;
        }
    }
}
