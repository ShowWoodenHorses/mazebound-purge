using UnityEngine;

namespace Purge.Core.Monsters
{
    /// <summary>ТЗ: лёгкая / средняя / сложная / нереальная (the save keeps the number).</summary>
    public enum DifficultyLevel
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        Unreal = 3,
    }

    /// <summary>
    /// What a difficulty changes (ТЗ: «сложность меняет и статы, и поведение — дальность слуха/зрения, радиус крика»).
    /// Perception (stage 5b) and the factors over the level's one wave scenario (stage 6: ТЗ «HP, количество, доля старших
    /// рангов, редкость дропа»; drafts, numbers — stage 9). Assets — Data/Difficulty, one per level.
    /// </summary>
    [CreateAssetMenu(fileName = "Difficulty", menuName = "Purge/Definitions/Difficulty")]
    public sealed class DifficultyDefinition : ScriptableObject
    {
        [SerializeField] private DifficultyLevel _level = DifficultyLevel.Normal;

        [Header("Perception")]
        [Tooltip("× the radius of every sound the monsters hear (shots, steps, doors…).")]
        [SerializeField, Range(0.25f, 3f)] private float _hearing = 1f;
        [Tooltip("× the monsters' sight range.")]
        [SerializeField, Range(0.25f, 3f)] private float _sight = 1f;
        [Tooltip("× how far a caller's call carries.")]
        [SerializeField, Range(0.25f, 3f)] private float _call = 1f;

        [Header("Waves (stage 6)")]
        [Tooltip("× every monster's HP.")]
        [SerializeField, Range(0.25f, 4f)] private float _health = 1f;
        [Tooltip("× each wave's budget and its groups' counts.")]
        [SerializeField, Range(0.25f, 4f)] private float _count = 1f;
        [Tooltip("× the weights of officers, elite and bosses in a wave (their share).")]
        [SerializeField, Range(0.25f, 4f)] private float _seniorShare = 1f;
        [Tooltip("× the weights of the rarer drops (above grey).")]
        [SerializeField, Range(0.25f, 4f)] private float _dropRarity = 1f;
        [Tooltip("× the money of kills and waves.")]
        [SerializeField, Range(0.25f, 5f)] private float _money = 1f;

        public DifficultyLevel Level => _level;
        public float Health => _health;
        public float Count => _count;
        public float SeniorShare => _seniorShare;
        public float DropRarity => _dropRarity;
        public float Money => _money;
        public float Hearing => _hearing;
        public float Sight => _sight;
        public float Call => _call;

        /// <summary>For tests and tools.</summary>
        internal void Configure(DifficultyLevel level, float hearing, float sight, float call)
        {
            _level = level;
            _hearing = hearing;
            _sight = sight;
            _call = call;
        }

        /// <summary>For tests and tools.</summary>
        internal void ConfigureWaves(float health, float count, float seniorShare, float dropRarity, float money)
        {
            _health = health;
            _count = count;
            _seniorShare = seniorShare;
            _dropRarity = dropRarity;
            _money = money;
        }
    }
}
