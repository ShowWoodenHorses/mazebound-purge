using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Top-down game camera in the Game scene. Looks almost straight down and never rotates around Y, so screen up
    /// is always grid North (input relies on it). Frames the whole level while it loads, then follows the player.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class TopDownCamera : MonoBehaviour
    {
        [Tooltip("Angle below the horizon. 90 = straight down; slightly less keeps a hint of wall sides.")]
        [SerializeField, Range(30f, 89.5f)] private float _pitch = 88f;
        [SerializeField, Min(1f)] private float _margin = 1.05f;

        [Tooltip("Half-size (cells) of the square around the player that always fits the screen, on any aspect. " +
                 "6.5 shows the whole 11x11 visible area with a cell of margin; 4 (the user's choice) is closer, the " +
                 "edges of the visible area are off screen.")]
        [SerializeField, Min(1f)] private float _followHalfExtent = 7f;

        [SerializeField, Min(0f)] private float _followSmoothTime = 0.12f;
        [SerializeField] private Camera _camera;

        private Vector3 _velocity;

        public Camera Camera => _camera;

        /// <summary>Places the camera so the whole <paramref name="bounds"/> fits the view.</summary>
        public void Frame(Bounds bounds)
        {
            transform.rotation = Quaternion.Euler(_pitch, 0f, 0f);

            var radius = bounds.extents.magnitude * _margin;
            var distance = radius / Mathf.Sin(NarrowHalfFov());

            transform.position = bounds.center - transform.forward * distance;
            _base = transform.position;
            _kick = Vector3.zero;
            _shake = 0f;
            _camera.farClipPlane = Mathf.Max(_camera.farClipPlane, distance + radius * 2f);
        }

        /// <summary>Half-size (cells) of the square around the player that always fits the screen.</summary>
        public float FollowHalfExtent
        {
            get => _followHalfExtent;
            set => _followHalfExtent = Mathf.Max(1f, value);
        }

        /// <summary>Distance at which a square of ±<see cref="_followHalfExtent"/> around the target fits the view.</summary>
        public float FollowDistance => _followHalfExtent / Mathf.Tan(NarrowHalfFov());

        /// <summary>The smaller of the vertical and horizontal half field of view, in radians.</summary>
        private float NarrowHalfFov()
        {
            var halfVertical = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * _camera.aspect);
            return Mathf.Min(halfVertical, halfHorizontal);
        }

        /// <summary>Keeps <paramref name="target"/> in the centre of the view; smoothed unless <paramref name="snap"/>.</summary>
        public void Follow(Vector3 target, float deltaTime, bool snap)
        {
            transform.rotation = Quaternion.Euler(_pitch, 0f, 0f);
            var desired = target - transform.forward * FollowDistance;
            if (snap || deltaTime <= 0f || _followSmoothTime <= 0f)
            {
                _base = desired;
                _velocity = Vector3.zero;
            }
            else
            {
                _base = Vector3.SmoothDamp(_base, desired, ref _velocity, _followSmoothTime, Mathf.Infinity, deltaTime);
            }

            transform.position = _base + Feel(deltaTime);
        }

        // ------------------------------------------------------------------ Feel: kick and shake

        /// <summary>1/s: how fast a kick returns (a jump, then ~0.1 s back).</summary>
        private const float KickReturn = 22f;

        /// <summary>1/s: how fast a shake dies out.</summary>
        private const float ShakeDamping = 10f;

        /// <summary>Shakes per second.</summary>
        private const float ShakeFrequency = 28f;

        /// <summary>Largest kick and shake, cells (many shots at once never throw the view away).</summary>
        private const float MaxOffset = 0.6f;

        private Vector3 _base;
        private Vector3 _kick;
        private float _shake;
        private float _shakeTime;

        /// <summary>Kicks and shakes are shown (the player's setting «Camera shake»); off — the view stays still.</summary>
        public bool FeelEnabled
        {
            get => _feelEnabled;
            set
            {
                _feelEnabled = value;
                if (value) return;
                _kick = Vector3.zero;
                _shake = 0f;
            }
        }

        private bool _feelEnabled = true;

        /// <summary>The view jumps <paramref name="cells"/> against a shot along <paramref name="direction"/> (grid x, y) and springs back.</summary>
        public void Kick(Vector2 direction, float cells)
        {
            if (!_feelEnabled || cells <= 0f || direction.sqrMagnitude <= 0f) return;
            var d = direction.normalized;
            _kick = Vector3.ClampMagnitude(_kick - new Vector3(d.x, 0f, d.y) * cells, MaxOffset);
        }

        /// <summary>Shakes the view by up to <paramref name="cells"/>; a stronger shake replaces a weaker one.</summary>
        public void Shake(float cells)
        {
            if (_feelEnabled) _shake = Mathf.Min(MaxOffset, Mathf.Max(_shake, cells));
        }

        private Vector3 Feel(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                _kick *= Mathf.Exp(-KickReturn * deltaTime);
                _shake *= Mathf.Exp(-ShakeDamping * deltaTime);
                _shakeTime += deltaTime;
            }

            if (_kick.sqrMagnitude < 1e-8f) _kick = Vector3.zero;
            if (_shake < 1e-4f) _shake = 0f;
            if (_shake <= 0f) return _kick;

            // Smooth noise on two axes, not a jitter of random jumps.
            var t = _shakeTime * ShakeFrequency;
            var x = Mathf.PerlinNoise(t, 0.37f) * 2f - 1f;
            var z = Mathf.PerlinNoise(0.71f, t) * 2f - 1f;
            return _kick + new Vector3(x, 0f, z) * _shake;
        }
    }
}
