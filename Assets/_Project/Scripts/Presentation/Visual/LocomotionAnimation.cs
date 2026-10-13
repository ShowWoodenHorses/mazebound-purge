using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>Playback speed of in-place walk/run clips for a character moving at a given speed.</summary>
    public static class LocomotionAnimation
    {
        /// <summary>
        /// Clips are not sped up beyond this: a frantic step rate looks worse from the top-down camera than feet
        /// sliding a little.
        /// </summary>
        public const float MaxPlayback = 1.4f;

        public const float MinPlayback = 0.5f;

        /// <summary>
        /// Playback multiplier so the clip's feet keep up with <paramref name="speed"/> (cells = metres per second);
        /// <paramref name="groundSpeed"/> is how fast the clip's planted foot moves at the model's scale.
        /// </summary>
        public static float Playback(float speed, float groundSpeed) =>
            groundSpeed > 0f ? Mathf.Clamp(speed / groundSpeed, MinPlayback, MaxPlayback) : 1f;
    }
}
