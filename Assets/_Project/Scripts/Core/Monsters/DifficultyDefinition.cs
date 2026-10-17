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
    /// Stage 5b: perception only; monster stats by difficulty — stage 6. Assets — Data/Difficulty, one per level.
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

        public DifficultyLevel Level => _level;
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
    }
}
