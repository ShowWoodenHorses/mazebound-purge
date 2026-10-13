using UnityEngine;

namespace Maze.Presentation.Visual
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
        [SerializeField, Min(1f)] private float _followHalfExtent = 4f;

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
            _camera.farClipPlane = Mathf.Max(_camera.farClipPlane, distance + radius * 2f);
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
                transform.position = desired;
                _velocity = Vector3.zero;
                return;
            }

            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _velocity, _followSmoothTime,
                Mathf.Infinity, deltaTime);
        }
    }
}
