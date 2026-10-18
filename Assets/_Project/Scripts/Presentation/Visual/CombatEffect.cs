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

        /// <summary>
        /// Every particle system loops (an effect that follows a moving thing, stage Э3) or plays once as built. Called
        /// before <see cref="Play"/>.
        /// </summary>
        public void SetLoop(bool loop)
        {
            if (_loopSystems == null)
            {
                _loopSystems = GetComponentsInChildren<ParticleSystem>(true);
                _builtLoop = new bool[_loopSystems.Length];
                for (var i = 0; i < _loopSystems.Length; i++) _builtLoop[i] = _loopSystems[i].main.loop;
            }

            for (var i = 0; i < _loopSystems.Length; i++)
            {
                var main = _loopSystems[i].main;
                main.loop = loop || _builtLoop[i];
            }
        }

        private ParticleSystem[] _loopSystems;
        private bool[] _builtLoop;

        private static readonly int ClipPlaneId = Shader.PropertyToID("_ClipPlane");
        private static readonly Vector4 NoClip = new Vector4(0f, 0f, 0f, 1f);
        private Renderer[] _renderers;
        private MaterialPropertyBlock _clipBlock;
        private Vector4 _clip = NoClip;

        /// <summary>
        /// Cuts every pixel of the effect beyond the plane through <paramref name="point"/> facing
        /// <paramref name="normal"/> (stage Э3: a stream ends hard at the wall it hits; shader Purge/Particle).
        /// </summary>
        public void SetClipPlane(Vector3 normal, Vector3 point) =>
            ApplyClip(new Vector4(normal.x, normal.y, normal.z, Vector3.Dot(normal, point)));

        /// <summary>No cut (as built).</summary>
        public void ClearClipPlane() => ApplyClip(NoClip);

        private void ApplyClip(Vector4 plane)
        {
            if (plane == _clip) return;
            _clip = plane;
            if (_renderers == null)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
                _clipBlock = new MaterialPropertyBlock();
            }

            foreach (var target in _renderers)
            {
                target.GetPropertyBlock(_clipBlock);
                _clipBlock.SetVector(ClipPlaneId, plane);
                target.SetPropertyBlock(_clipBlock);
            }
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

        /// <summary>
        /// Sized to <paramref name="scale"/> × its own size by spread (stage Э3: big explosions looked like a few huge
        /// squares): the root is scaled — reach, shape and speed follow (systems scale with the hierarchy) — but each
        /// particle grows only by √scale, and there are scale times as many (at most <see cref="MaxCountFactor"/>).
        /// 1 = as built. Called before <see cref="Play"/>.
        /// </summary>
        public void SetSpread(float scale)
        {
            scale = Mathf.Max(0.05f, scale);
            // × the size the copy was built at (Build Pack Effects scales the root): setting the scale outright threw
            // that away — every pack effect showed at the pack's own size.
            if (!_baseScaleCached)
            {
                _baseScale = transform.localScale;
                _baseScaleCached = true;
            }

            transform.localScale = _baseScale * scale;
            if (_spreadSystems == null)
            {
                if (Mathf.Approximately(scale, 1f)) return;
                CacheSpread();
            }

            var size = 1f / Mathf.Sqrt(scale);
            var count = Mathf.Clamp(scale, 1f, MaxCountFactor);
            for (var i = 0; i < _spreadSystems.Length; i++)
            {
                var system = _spreadSystems[i];
                var main = system.main;
                var basis = _spreadBase[i];
                if (main.startSize3D)
                {
                    main.startSizeXMultiplier = basis.SizeX * size;
                    main.startSizeYMultiplier = basis.SizeY * size;
                    main.startSizeZMultiplier = basis.SizeZ * size;
                }
                else main.startSizeMultiplier = basis.SizeX * size;

                main.maxParticles = Mathf.CeilToInt(basis.MaxParticles * count);
                var emission = system.emission;
                emission.rateOverTimeMultiplier = basis.Rate * count;
                for (var b = 0; b < basis.Bursts.Length; b++)
                {
                    var burst = emission.GetBurst(b);
                    burst.count = new ParticleSystem.MinMaxCurve(Mathf.Round(basis.Bursts[b].x * count), Mathf.Round(basis.Bursts[b].y * count));
                    emission.SetBurst(b, burst);
                }
            }
        }

        /// <summary>Most extra particles a spread effect gets (phones).</summary>
        public const float MaxCountFactor = 2.5f;

        private ParticleSystem[] _spreadSystems;
        private SpreadBase[] _spreadBase;
        private Vector3 _baseScale = Vector3.one;
        private bool _baseScaleCached;

        private void CacheSpread()
        {
            _spreadSystems = GetComponentsInChildren<ParticleSystem>(true);
            _spreadBase = new SpreadBase[_spreadSystems.Length];
            for (var i = 0; i < _spreadSystems.Length; i++)
            {
                var main = _spreadSystems[i].main;
                var emission = _spreadSystems[i].emission;
                var basis = new SpreadBase
                {
                    SizeX = main.startSize3D ? main.startSizeXMultiplier : main.startSizeMultiplier,
                    SizeY = main.startSizeYMultiplier,
                    SizeZ = main.startSizeZMultiplier,
                    MaxParticles = main.maxParticles,
                    Rate = emission.rateOverTimeMultiplier,
                    Bursts = new Vector2[emission.burstCount],
                };
                for (var b = 0; b < basis.Bursts.Length; b++)
                {
                    var count = emission.GetBurst(b).count;
                    basis.Bursts[b] = count.mode == ParticleSystemCurveMode.TwoConstants
                        ? new Vector2(count.constantMin, count.constantMax)
                        : new Vector2(count.constant, count.constant);
                }

                _spreadBase[i] = basis;
            }
        }

        private struct SpreadBase
        {
            public float SizeX, SizeY, SizeZ, Rate;
            public int MaxParticles;
            public Vector2[] Bursts;
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
