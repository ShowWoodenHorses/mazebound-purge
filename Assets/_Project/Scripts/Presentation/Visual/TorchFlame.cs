using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Flame of a light fixture (on the root of a torch prefab; set up by Maze → Dev → Build Light Fixtures): looping
    /// particle systems tinted by the colour of their light. Display only, no logic of its own.
    /// </summary>
    public sealed class TorchFlame : MonoBehaviour
    {
        [Tooltip("Particle systems of the flame; their start colour is multiplied by the light's colour.")]
        [SerializeField] private ParticleSystem[] _systems = new ParticleSystem[0];

        [Tooltip("0 = flame keeps its own colours, 1 = fully the light's colour.")]
        [Range(0f, 1f)] [SerializeField] private float _tint = 0.6f;

        public ParticleSystem[] Systems { get => _systems; set => _systems = value; }

        public void SetColor(Color light)
        {
            var tint = Color.Lerp(Color.white, light, _tint);
            tint.a = 1f;
            foreach (var system in _systems)
            {
                if (system == null) continue;
                var main = system.main;
                var start = main.startColor;
                start.color *= tint;
                start.colorMin *= tint;
                start.colorMax *= tint;
                main.startColor = start;
            }
        }

        /// <summary>Restarts the flame (after the load warm-up cleared it). Prewarmed systems start fully lit.</summary>
        public void Play()
        {
            foreach (var system in _systems)
                if (system != null && system.gameObject.activeInHierarchy)
                    system.Play(false);
        }
    }
}
