using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Analytic two-bone IK (upper arm → forearm → hand) on final, already animated transforms (call after the
    /// Animator wrote the pose, e.g. in LateUpdate). No allocations. The elbow keeps its animated bend plane as far as
    /// possible: first the elbow angle is set so the hand can reach, then the whole arm swings onto the target.
    /// </summary>
    public static class TwoBoneIK
    {
        private const float Epsilon = 1e-4f;

        /// <summary>
        /// Moves <paramref name="end"/> to <paramref name="target"/> (blended by <paramref name="weight"/> from where
        /// it is now). A target out of reach stretches the arm toward it.
        /// </summary>
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, float weight)
        {
            if (weight <= 0f) return;

            var a = upper.position;
            var b = lower.position;
            var c = end.position;
            target = Vector3.Lerp(c, target, Mathf.Clamp01(weight));

            var upperLength = (b - a).magnitude;
            var lowerLength = (c - b).magnitude;
            if (upperLength < Epsilon || lowerLength < Epsilon) return;

            // Elbow angle for the wanted shoulder → hand distance (law of cosines).
            var reach = Mathf.Clamp((target - a).magnitude, Mathf.Abs(upperLength - lowerLength) + Epsilon,
                upperLength + lowerLength - Epsilon);
            var current = Vector3.Angle(a - b, c - b);
            var wanted = Mathf.Acos(Mathf.Clamp(
                (upperLength * upperLength + lowerLength * lowerLength - reach * reach) / (2f * upperLength * lowerLength),
                -1f, 1f)) * Mathf.Rad2Deg;

            var bendAxis = Vector3.Cross(a - b, c - b);
            if (bendAxis.sqrMagnitude < Epsilon * Epsilon)
            {
                // Arm straight in the pose: bend it the way the elbow joint bends (its own local axis).
                bendAxis = lower.rotation * Vector3.up;
            }

            lower.rotation = Quaternion.AngleAxis(wanted - current, bendAxis.normalized) * lower.rotation;

            // Swing the arm so the hand lands on the target.
            var toHand = end.position - a;
            var toTarget = target - a;
            if (toHand.sqrMagnitude > Epsilon * Epsilon && toTarget.sqrMagnitude > Epsilon * Epsilon)
                upper.rotation = Quaternion.FromToRotation(toHand, toTarget) * upper.rotation;
        }
    }
}
