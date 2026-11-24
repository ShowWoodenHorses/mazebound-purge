using UnityEngine;

namespace Purge.Core.Visual
{
    /// <summary>
    /// How a monster's equipment model (weapon, shield, staff…) sits in the bone that carries it — set once on the
    /// model's prefab and shared by every monster that carries it (ТЗ: сдвиг/поворот модели в слоте — один раз в
    /// данных этой модели). Look only: what the monster's attack does is its modules'. Build Monsters adds it with
    /// zero offsets when missing; rank-and-file get the model glued into the baked mesh at this offset.
    /// </summary>
    public sealed class EquipmentModel : MonoBehaviour
    {
        [Tooltip("Use the palm rather than the hand bone's origin and axes. +Y points toward the index finger, +Z toward the knuckles.")]
        [SerializeField] private bool _usePalmGrip;
        [Tooltip("Offset in metres in the selected attachment space (bone or palm).")]
        [SerializeField] private Vector3 _position;
        [Tooltip("Rotation in the selected attachment space, degrees.")]
        [SerializeField] private Vector3 _rotation;
        [SerializeField, Min(0.01f)] private float _scale = 1f;

        public bool UsePalmGrip => _usePalmGrip;
        public Vector3 Position => _position;
        public Quaternion Rotation => Quaternion.Euler(_rotation);
        public float Scale => _scale;

        /// <summary>Model space → the selected attachment space.</summary>
        public Matrix4x4 Offset => Matrix4x4.TRS(_position, Rotation, Vector3.one * _scale);

        /// <summary>Editor tools.</summary>
        internal void Set(Vector3 position, Vector3 rotation, float scale)
        {
            _position = position;
            _rotation = rotation;
            _scale = scale;
        }
    }
}
