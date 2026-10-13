using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Ice over a frozen door (display only, on the root of the theme's ice prefab). The ice is made of pieces: hits
    /// chip them away in order (<see cref="SetDamage"/>), the last hit breaks all of it. Without pieces the whole ice
    /// stays until broken.
    /// </summary>
    public sealed class FrozenDoorIce : MonoBehaviour
    {
        [Tooltip("Pieces in the order they fall off (the last ones stay until the ice breaks).")]
        [SerializeField] private GameObject[] _pieces = new GameObject[0];

        /// <summary>For the builder.</summary>
        public GameObject[] Pieces { get => _pieces; set => _pieces = value; }

        /// <summary>0 = whole ice, 1 = broken (everything hidden).</summary>
        public void SetDamage(float damage)
        {
            damage = Mathf.Clamp01(damage);
            if (damage >= 1f)
            {
                gameObject.SetActive(false);
                return;
            }

            // Never every piece before the ice actually breaks.
            var hidden = Mathf.Min(Mathf.FloorToInt(damage * _pieces.Length), _pieces.Length - 1);
            for (var i = 0; i < _pieces.Length; i++)
                if (_pieces[i] != null)
                    _pieces[i].SetActive(i >= hidden);
        }
    }
}
