using System;
using Maze.Core.Grid;
using UnityEngine;

namespace Maze.Core.Visibility
{
    /// <summary>
    /// Where a segment first enters an opaque cell (bullets, melee reach — ТЗ §70: Wall and closed Door block,
    /// open Door does not). Same traversal and corner rule as <see cref="GridLineOfSight"/>: a segment passing exactly
    /// through a grid corner is blocked only when both cells beside it are opaque. Allocation-free.
    /// </summary>
    public static class GridRaycast
    {
        private const float CornerEpsilon = 1e-5f;

        /// <summary>
        /// True when the segment is blocked. <paramref name="hitT"/> is the fraction 0..1 of the segment where it
        /// enters the blocking cell (0 when it starts inside one); 1 when not blocked.
        /// </summary>
        public static bool Cast<T>(Vector2 from, Vector2 to, in T opacity, out float hitT, out GridPosition hitCell)
            where T : IGridOpacity
        {
            var cell = GridLineOfSight.CellOf(from.x, from.y);
            hitT = 1f;
            hitCell = cell;
            if (opacity.IsOpaque(cell))
            {
                hitT = 0f;
                return true;
            }

            var end = GridLineOfSight.CellOf(to.x, to.y);
            int x = cell.X, y = cell.Y;
            var dx = to.x - from.x;
            var dy = to.y - from.y;
            var stepX = Math.Sign(dx);
            var stepY = Math.Sign(dy);
            var tDeltaX = stepX != 0 ? 1f / Math.Abs(dx) : float.PositiveInfinity;
            var tDeltaY = stepY != 0 ? 1f / Math.Abs(dy) : float.PositiveInfinity;
            var tMaxX = stepX != 0 ? (x + 0.5f * stepX - from.x) / dx : float.PositiveInfinity;
            var tMaxY = stepY != 0 ? (y + 0.5f * stepY - from.y) / dy : float.PositiveInfinity;

            for (var guard = Math.Abs(end.X - x) + Math.Abs(end.Y - y) + 2; guard > 0 && (x != end.X || y != end.Y); guard--)
            {
                float t;
                if (Math.Abs(tMaxX - tMaxY) < CornerEpsilon)
                {
                    t = tMaxX;
                    if (opacity.IsOpaque(new GridPosition(x + stepX, y)) && opacity.IsOpaque(new GridPosition(x, y + stepY)))
                    {
                        hitT = Mathf.Clamp01(t);
                        hitCell = new GridPosition(x + stepX, y + stepY);
                        return true;
                    }

                    x += stepX;
                    y += stepY;
                    tMaxX += tDeltaX;
                    tMaxY += tDeltaY;
                }
                else if (tMaxX < tMaxY)
                {
                    t = tMaxX;
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    t = tMaxY;
                    y += stepY;
                    tMaxY += tDeltaY;
                }

                if (t > 1f) break;
                var entered = new GridPosition(x, y);
                if (opacity.IsOpaque(entered))
                {
                    hitT = Mathf.Clamp01(t);
                    hitCell = entered;
                    return true;
                }
            }

            return false;
        }
    }
}
