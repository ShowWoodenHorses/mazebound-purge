using UnityEngine;

namespace Purge.Presentation.Visual
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

        [Tooltip("Cells the effect covers at its own size: an explosion is scaled to its radius by this (0 = never scaled). " +
                 "A looping stream: cells it reaches (its particles live longer or shorter to reach the weapon's range).")]
        [SerializeField, Min(0f)] private float _radius;

        [Tooltip("Continuous (a stream): loops while held, then stops emitting and fades out over the duration.")]
        [SerializeField] private bool _looping;

        private ParticleSystem[] _particles;
        private SwingArc _swing;
        private ParticleSystem[] _all;
        private float[] _lifetimes;
        private bool _cached;

        public float Duration { get => _duration; set => _duration = value; }
        public float Radius { get => _radius; set => _radius = value; }
        public bool Looping { get => _looping; set => _looping = value; }

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

        /// <summary>A stopped looping effect emits again, keeping what is still out (no restart).</summary>
        public void Resume()
        {
            Cache();
            foreach (var system in _particles)
                system.Play(true);
        }

        /// <summary>A looping effect stops emitting; what is out fades on its own.</summary>
        public void Stop()
        {
            Cache();
            foreach (var system in _particles)
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// Particles live <paramref name="factor"/> times their own lifetime (a stream reaches farther or nearer at the
        /// same speed). Called before <see cref="Play"/>; 1 = as built.
        /// </summary>
        public void SetLifetimeFactor(float factor)
        {
            Cache();
            if (_lifetimes == null)
            {
                _all = GetComponentsInChildren<ParticleSystem>(true);
                _lifetimes = new float[_all.Length];
                for (var i = 0; i < _all.Length; i++) _lifetimes[i] = _all[i].main.startLifetimeMultiplier;
            }

            for (var i = 0; i < _all.Length; i++)
            {
                var main = _all[i].main;
                main.startLifetimeMultiplier = _lifetimes[i] * factor;
            }
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
