using UnityEngine;

namespace Maze.Core.Definitions
{
    public enum ZombieDetectionType
    {
        VisionOnly = 0,
        HearingOnly = 1,
        VisionAndHearing = 2,
    }

    /// <summary>Gameplay behaviour of a zombie type. Appearance is defined by ZombieVisualSet, never here.</summary>
    [CreateAssetMenu(fileName = "ZombieDefinition", menuName = "Maze/Definitions/Zombie")]
    public sealed class ZombieDefinition : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private ZombieDetectionType _detectionType;

        [Header("Vision (VisionOnly)")]
        [SerializeField, Range(1f, 360f)] private float _visionAngle = 45f;
        [SerializeField, Min(0f)] private float _visionRange = 5f;

        [Header("Hearing (HearingOnly, VisionAndHearing)")]
        [SerializeField, Min(0f)] private float _hearingRadius = 5f;

        [Header("Vision + Hearing")]
        [Tooltip("Sees all around within this radius; walls and closed doors block the view. Hears by Hearing Radius.")]
        [SerializeField, Min(0f)] private float _detectionRadius = 4f;

        [Header("Combat & Movement")]
        [SerializeField, Min(1f)] private float _maxHp = 30f;
        [SerializeField, Min(0f)] private float _damage = 10f;
        [Tooltip("Seconds between attacks.")]
        [SerializeField, Min(0.01f)] private float _attackInterval = 1f;
        [Tooltip("Cells per second while patrolling and returning (walk).")]
        [SerializeField, Min(0.01f)] private float _moveSpeed = 1.5f;
        [Tooltip("Cells per second while chasing the player (run).")]
        [SerializeField, Min(0.01f)] private float _chaseSpeed = 2.5f;
        [Tooltip("Share of the speed kept while the zombie is in a snowdrift cell.")]
        [SerializeField, Range(0.1f, 1f)] private float _snowdriftSpeed = 0.6f;

        public string Id => _id;
        public ZombieDetectionType DetectionType => _detectionType;
        public float VisionAngle => _visionAngle;
        public float VisionRange => _visionRange;
        public float HearingRadius => _hearingRadius;
        public float DetectionRadius => _detectionRadius;
        public float MaxHp => _maxHp;
        public float Damage => _damage;
        public float AttackInterval => _attackInterval;
        public float MoveSpeed => _moveSpeed;
        public float ChaseSpeed => _chaseSpeed;
        public float SnowdriftSpeed => _snowdriftSpeed;

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, ZombieDetectionType detection, float visionAngle = 45f, float visionRange = 5f,
            float hearingRadius = 5f, float detectionRadius = 4f, float maxHp = 30f, float damage = 10f,
            float attackInterval = 1f, float moveSpeed = 1.5f, float chaseSpeed = 0f)
        {
            _id = id;
            _detectionType = detection;
            _visionAngle = visionAngle;
            _visionRange = visionRange;
            _hearingRadius = hearingRadius;
            _detectionRadius = detectionRadius;
            _maxHp = maxHp;
            _damage = damage;
            _attackInterval = attackInterval;
            _moveSpeed = moveSpeed;
            _chaseSpeed = chaseSpeed > 0f ? chaseSpeed : moveSpeed; // 0: chase as fast as patrol
        }
    }
}
