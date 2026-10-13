using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// A pooled one-shot effect (display only): restarts its particle systems on every use and says how long it lasts.
    /// Optional on effect prefabs — without it <see cref="CombatViewPresenter"/> restarts the particle systems itself and
    /// uses the definition's duration. A <see cref="SwingArc"/> on the same object is animated over the duration.
    /// Particle systems must not use the Collision module (no physics in the project).
    /// </summary>
    public sealed class CombatEffect : MonoBehaviour
    {
        [Tooltip("Seconds until the effect returns to the pool: the longest particle lifetime.")]
        [SerializeField, Min(0.02f)] private float _duration = 0.3f;

        private ParticleSystem[] _particles;
        private SwingArc _swing;
        private bool _cached;

        public float Duration { get => _duration; set => _duration = value; }

        /// <summary>Melee swing driven by this effect, or null.</summary>
        public SwingArc Swing
        {
            get
            {
                Cache();
                return _swing;
            }
        }

        /// <summary>Starts the effect from the beginning (after it was placed).</summary>
        public void Play()
        {
            Cache();
            Restart(_particles);
            if (_swing != null) _swing.Animate(0f);
        }

        /// <summary>Progress of the effect, 0..1 (called every frame while it is shown).</summary>
        public void Animate(float progress)
        {
            if (_swing != null) _swing.Animate(progress);
        }

        /// <summary>Restarts every root particle system (with children) of a prefab without a <see cref="CombatEffect"/>.</summary>
        public static void Restart(ParticleSystem[] particles)
        {
            if (particles == null) return;
            foreach (var system in particles)
            {
                system.Clear(true);
                system.Play(true);
            }
        }

        /// <summary>Particle systems of <paramref name="root"/> that are not children of another one (they play with it).</summary>
        public static ParticleSystem[] RootSystems(GameObject root)
        {
            var all = root.GetComponentsInChildren<ParticleSystem>(true);
            var count = 0;
            foreach (var system in all)
                if (IsRoot(system, root.transform)) count++;

            var roots = new ParticleSystem[count];
            count = 0;
            foreach (var system in all)
                if (IsRoot(system, root.transform)) roots[count++] = system;
            return roots;
        }

        private static bool IsRoot(ParticleSystem system, Transform root)
        {
            for (var parent = system.transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.GetComponent<ParticleSystem>() != null) return false;
                if (parent == root) break;
            }
            return true;
        }

        private void Cache()
        {
            if (_cached) return;
            _particles = RootSystems(gameObject);
            _swing = GetComponentInChildren<SwingArc>(true);
            _cached = true;
        }
    }
}
