using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>Parent of everything the level shows (geometry chunks, entity views). Lives in the Game scene.</summary>
    public sealed class LevelViewRoot : MonoBehaviour
    {
        [Tooltip("Directional light of the Game scene; LevelLighting sets it from the theme (no shadows).")]
        [SerializeField] private Light _moon;

        public Light Moon => _moon;
    }
}
