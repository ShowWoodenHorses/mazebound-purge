using UnityEngine;

namespace Maze.Presentation.UI.Touch
{
    /// <summary>
    /// Turns a raw on-screen stick deflection (1 = edge of travel) into the Move value: a dead zone, then a curve with
    /// a soft start. Past the dead zone the output starts just above the dead zone of PlayerSystem (slowest walk)
    /// and reaches full speed slightly before the edge, so the thumb does not need to hit it exactly.
    /// </summary>
    public static class StickResponse
    {
        /// <summary>Output right past the dead zone; above the 0.1 dead zone of PlayerSystem and PlayerCombat.</summary>
        public const float MinOutput = 0.12f;

        /// <summary>Deflection that already gives full output.</summary>
        public const float FullAt = 0.9f;

        /// <summary>Sensitivity 0..1 to the curve exponent: 0 = soft start (2.5), 1 = quick (0.7).</summary>
        public static float ExponentFor(float sensitivity) => Mathf.Lerp(2.5f, 0.7f, Mathf.Clamp01(sensitivity));

        public static Vector2 Shape(Vector2 deflection, float deadZone, float exponent)
        {
            var magnitude = deflection.magnitude;
            deadZone = Mathf.Clamp(deadZone, 0f, FullAt - 0.05f);
            if (magnitude <= deadZone || magnitude <= 0f)
                return Vector2.zero;

            var t = Mathf.Clamp01((magnitude - deadZone) / (FullAt - deadZone));
            var output = Mathf.Lerp(MinOutput, 1f, Mathf.Pow(t, exponent));
            return deflection * (output / magnitude);
        }
    }
}
