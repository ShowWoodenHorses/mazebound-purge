using System;
using System.Collections.Generic;
using Purge.Core.Grid;

namespace Purge.Core.Level
{
    /// <summary>
    /// The arena as the cells show it (stage 7б): its entries are the passages through the ring found in the geometry,
    /// not the generator's list — a ring edited by hand counts as it is. Used by the validator and the arena brush.
    /// </summary>
    public static class ArenaLayout
    {
        /// <summary>A walker can stand here: inside, and neither the structure nor an obstacle blocks movement (doors pass).</summary>
        public static bool IsWalkable(LevelGeometry geometry, GridPosition p) =>
            geometry.IsInside(p) && (geometry.GetFlags(p) & CellFlags.BlocksMovement) == 0;

        /// <summary>
        /// Passages through the ring, per side: runs of cells along the side whose whole line across the ring
        /// (<see cref="ArenaData.Ring"/> cells deep, at least 1) is walkable. Corners do not count.
        /// </summary>
        public static List<ArenaEntry> FindEntries(LevelGeometry geometry, ArenaData arena)
        {
            var entries = new List<ArenaEntry>();
            if (arena == null || arena.IsEmpty) return entries;

            var rect = arena.Rect;
            var depth = Math.Max(1, arena.Ring);
            foreach (var side in DirectionExtensions.All)
            {
                var horizontal = side == Direction.North || side == Direction.South;
                var span = horizontal ? rect.Width : rect.Height;
                var runStart = -1;
                for (var k = 0; k <= span; k++)
                {
                    var open = k < span && LineOpen(geometry, arena, side, k, depth);
                    if (open && runStart < 0) runStart = k;
                    if (open || runStart < 0) continue;
                    entries.Add(new ArenaEntry(Gap(rect, side, runStart, k - runStart, depth), side));
                    runStart = -1;
                }
            }

            return entries;
        }

        /// <summary>Side of the largest square of walkable cells inside the arena rectangle.</summary>
        public static int LargestFreeSquare(LevelGeometry geometry, ArenaData arena)
        {
            if (arena == null || arena.IsEmpty) return 0;
            var rect = arena.Rect;
            var previous = new int[rect.Width + 1];
            var current = new int[rect.Width + 1];
            var best = 0;
            for (var y = 0; y < rect.Height; y++)
            {
                for (var x = 0; x < rect.Width; x++)
                {
                    var p = new GridPosition(rect.X + x, rect.Y + y, arena.Floor);
                    current[x + 1] = IsWalkable(geometry, p)
                        ? 1 + Math.Min(Math.Min(previous[x + 1], current[x]), previous[x])
                        : 0;
                    best = Math.Max(best, current[x + 1]);
                }

                (previous, current) = (current, previous);
            }

            return best;
        }

        private static bool LineOpen(LevelGeometry geometry, ArenaData arena, Direction side, int along, int depth)
        {
            var rect = arena.Rect;
            for (var d = 0; d < depth; d++)
            {
                GridPosition p;
                switch (side)
                {
                    case Direction.North: p = new GridPosition(rect.X + along, rect.YMax + d, arena.Floor); break;
                    case Direction.South: p = new GridPosition(rect.X + along, rect.Y - 1 - d, arena.Floor); break;
                    case Direction.East: p = new GridPosition(rect.XMax + d, rect.Y + along, arena.Floor); break;
                    default: p = new GridPosition(rect.X - 1 - d, rect.Y + along, arena.Floor); break;
                }

                if (!IsWalkable(geometry, p)) return false;
            }

            return true;
        }

        private static GridRect Gap(GridRect rect, Direction side, int start, int width, int depth)
        {
            switch (side)
            {
                case Direction.North: return new GridRect(rect.X + start, rect.YMax, width, depth);
                case Direction.South: return new GridRect(rect.X + start, rect.Y - depth, width, depth);
                case Direction.East: return new GridRect(rect.XMax, rect.Y + start, depth, width);
                default: return new GridRect(rect.X - depth, rect.Y + start, depth, width);
            }
        }
    }
}
