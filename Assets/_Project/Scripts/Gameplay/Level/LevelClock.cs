using UnityEngine;

namespace Purge.Gameplay.Level
{
    /// <summary>
    /// The pace of the level's simulation (stage 8a): <see cref="LevelRuntime"/> ticks the gameplay with real seconds
    /// × <see cref="Scale"/>. A slow-down (a boss's death: × 0.3 for 0.5 s) runs out in real seconds. Never
    /// <c>Time.timeScale</c>: the views, the UI and the sound keep real time; the horde's views follow the horde's clock.
    /// </summary>
    public sealed class LevelClock
    {
        private float _slowFactor = 1f;
        private float _slowLeft;

        /// <summary>× real time of the simulation this frame (1 = normal).</summary>
        public float Scale => _slowLeft > 0f ? _slowFactor : 1f;

        public bool IsSlowed => _slowLeft > 0f;

        /// <summary>
        /// The simulation runs × <paramref name="factor"/> for <paramref name="seconds"/> real seconds (the slower of two
        /// at once wins, the longer stays).
        /// </summary>
        public void SlowDown(float factor, float seconds)
        {
            if (seconds <= 0f) return;
            factor = Mathf.Clamp(factor, 0.05f, 1f);
            _slowFactor = _slowLeft > 0f ? Mathf.Min(_slowFactor, factor) : factor;
            _slowLeft = Mathf.Max(_slowLeft, seconds);
        }

        /// <summary>The simulation's seconds for <paramref name="realSeconds"/> of this frame; the slow-down runs out.</summary>
        public float Advance(float realSeconds)
        {
            var scaled = realSeconds * Scale;
            if (_slowLeft > 0f) _slowLeft -= realSeconds;
            return scaled;
        }

        public void Reset() => _slowLeft = 0f;
    }
}
