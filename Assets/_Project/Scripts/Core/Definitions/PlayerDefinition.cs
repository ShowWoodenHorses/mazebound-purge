using UnityEngine;

namespace Maze.Core.Definitions
{
    /// <summary>
    /// Gameplay parameters of the player. Shared by all levels, Addressable at <see cref="Address"/>.
    /// Visual representation is defined separately (<see cref="Visual.PlayerVisualDefinition"/>), never here.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerDefinition", menuName = "Maze/Definitions/Player")]
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
        public float StepDistance => _stepDistance;
        public float StepSoundRadius => _stepSoundRadius;
        public float InteractionSoundRadius => _interactionSoundRadius;

        /// <summary>For tests and tools.</summary>
        internal void Configure(float moveSpeed, float bodyHalfSize, float cornerAssist)
        {
            _moveSpeed = moveSpeed;
            _bodyHalfSize = bodyHalfSize;
            _cornerAssist = cornerAssist;
        }
    }
}
