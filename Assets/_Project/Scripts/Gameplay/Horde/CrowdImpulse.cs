using UnityEngine;

namespace Purge.Gameplay.Horde
{
    /// <summary>
    /// The crowd impulse (stage 5d; the Brute's charge now, the ramming vehicle later — Progress «Машина-таран»):
    /// something heavy moving along a segment shoves the living monsters it touches aside — sideways from its path, to
    /// the side they already stand on — with no damage. Works on the horde's arrays through <see cref="HordeSystem.Push"/>
    /// (knockbacks fade and walls stop them, as any push). No physics: the horde's cell buckets of this tick.
    /// Allocation-free.
    /// </summary>
    public static class CrowdImpulse
    {
        /// <summary>
        /// Shoves aside every living monster (but <paramref name="ignore"/>) whose body is within
        /// <paramref name="halfWidth"/> of the segment <paramref name="from"/> → <paramref name="to"/>:
        /// <paramref name="speed"/> cells per second sideways, plus a little along the way. Returns how many it shoved.
        /// </summary>
        public static int Shove(HordeSystem horde, Vector2 from, Vector2 to, float halfWidth, float speed, int ignore = -1)
        {
            if (!horde.IsActive || speed <= 0f) return 0;
            var delta = to - from;
            var length = delta.magnitude;
            var along = length > 1e-5f ? delta / length : Vector2.zero;
            var reach = halfWidth + horde.MaxRadius + 0.5f;
            var minX = Mathf.FloorToInt(Mathf.Min(from.x, to.x) - reach + 0.5f);
            var maxX = Mathf.FloorToInt(Mathf.Max(from.x, to.x) + reach + 0.5f);
            var minY = Mathf.FloorToInt(Mathf.Min(from.y, to.y) - reach + 0.5f);
            var maxY = Mathf.FloorToInt(Mathf.Max(from.y, to.y) + reach + 0.5f);
            var positions = horde.Positions;
            var states = horde.States;
            var shoved = 0;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            for (var agent = horde.FirstInCell(x, y); agent >= 0; agent = horde.NextInCell(agent))
            {
                if (agent == ignore || !HordeSystem.IsAlive(states[agent])) continue;
                var position = positions[agent];
                var t = length > 1e-5f ? Mathf.Clamp(Vector2.Dot(position - from, along), 0f, length) : 0f;
                var offset = position - (from + along * t);
                var distance = offset.magnitude;
                if (distance > halfWidth + horde.RadiusOf(agent)) continue;

                // Sideways, to the side it already stands on (dead centre: by slot, so a column splits both ways).
                Vector2 side;
                if (along.sqrMagnitude > 0f)
                {
                    side = new Vector2(-along.y, along.x);
                    var cross = Vector2.Dot(offset, side);
                    if (Mathf.Abs(cross) < 1e-3f ? (agent & 1) == 1 : cross < 0f) side = -side;
                }
                else
                {
                    side = distance > 1e-4f ? offset / distance : Vector2.up;
                }

                horde.Push(agent, (side + along * 0.35f).normalized * speed);
                shoved++;
            }

            return shoved;
        }
    }
}
