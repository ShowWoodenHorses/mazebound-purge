using System;
using System.Collections.Generic;
using System.Linq;
using Purge.Core.Generation;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Vehicles;

namespace Purge.Core.Validation
{
    /// <summary>
    /// Layout checks of a location (stage 7б, ТЗ карточка 7б): everything walkable is reachable from the start; the arena
    /// is big enough and has enough wide entries; spawn points are out of the start's view, reachable, and a 3×3 body
    /// gets from each of them to the arena; obstacles stay within the chunk budget. Arena and spawn checks run only when
    /// the level has them.
    /// </summary>
    public static partial class LevelValidator
    {
        /// <summary>Least side of the free square inside the arena.</summary>
        public const int MinArenaFreeSide = 14;

        public const int MinArenaEntries = 3;
        public const int MinArenaEntryWidth = 3;

        /// <summary>Spawn points stay farther than this from every start (Chebyshev: the start's view is 21×21).</summary>
        public const int StartViewRadius = 10;

        /// <summary>Side of the body that must get from every spawn point to the arena (big monsters).</summary>
        public const int BodySize = 3;

        /// <summary>Obstacle share of an 8×8 chunk when the recipe has no Obstacles step.</summary>
        public const float DefaultMaxChunkShare = 0.35f;

        private static void ValidateLayout(LevelData level, List<PlayerStartData> starts, ValidationReport report)
        {
            var reached = ValidateConnectivity(level, starts, report);
            ValidateArena(level, report);
            ValidateSpawnLayout(level, starts, reached, report);
            ValidateObstacleBudget(level, report);
        }

        /// <summary>Walkable cells reached from the starts (4 directions, doors pass); every other walkable region is an error.</summary>
        private static bool[] ValidateConnectivity(LevelData level, List<PlayerStartData> starts, ValidationReport report)
        {
            var geometry = level.Geometry;
            var reached = new bool[geometry.CellCount];
            var queue = new Queue<int>();
            // Barrels and chests stand in their cells (stage 7в): the way goes around them.
            var blocked = new HashSet<int>();
            foreach (var barrel in level.Barrels)
                if (geometry.IsInside(barrel.Position)) blocked.Add(geometry.ToIndex(barrel.Position));
            foreach (var chest in level.Chests)
                if (geometry.IsInside(chest.Position)) blocked.Add(geometry.ToIndex(chest.Position));
            // Parked vehicles (stage 9в): their bodies too.
            var cells = new List<GridPosition>();
            foreach (var vehicle in level.Vehicles)
            {
                VehicleFootprint.Cells(vehicle, cells);
                foreach (var cell in cells)
                    if (geometry.IsInside(cell)) blocked.Add(geometry.ToIndex(cell));
            }

            foreach (var start in starts)
                if (ArenaLayout.IsWalkable(geometry, start.Position))
                    Flood(geometry, geometry.ToIndex(start.Position), reached, queue, blocked);

            var floors = new HashSet<int>(starts.Select(s => s.Position.Floor));
            var region = new bool[geometry.CellCount];
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var p = geometry.ToPosition(i);
                if (reached[i] || region[i] || blocked.Contains(i) || !floors.Contains(p.Floor) || !ArenaLayout.IsWalkable(geometry, p))
                    continue;
                var size = Flood(geometry, i, region, queue, blocked);
                report.Add(Error, Gameplay, ValidationCodes.LevelDisconnected,
                    $"{size} walkable cell(s) around {p} cannot be reached from the start.", p);
            }

            return reached;
        }

        /// <summary>Marks the walkable region of <paramref name="from"/> in <paramref name="mask"/>; returns its size.</summary>
        private static int Flood(LevelGeometry geometry, int from, bool[] mask, Queue<int> queue, HashSet<int> blocked)
        {
            if (mask[from]) return 0;
            mask[from] = true;
            queue.Enqueue(from);
            var size = 0;
            while (queue.Count > 0)
            {
                var p = geometry.ToPosition(queue.Dequeue());
                size++;
                foreach (var direction in DirectionExtensions.All)
                {
                    var next = p.Neighbour(direction);
                    if (!ArenaLayout.IsWalkable(geometry, next)) continue;
                    var index = geometry.ToIndex(next);
                    if (mask[index] || blocked.Contains(index)) continue;
                    mask[index] = true;
                    queue.Enqueue(index);
                }
            }

            return size;
        }

        private static void ValidateArena(LevelData level, ValidationReport report)
        {
            var arena = level.Arena;
            if (arena.IsEmpty) return;

            var geometry = level.Geometry;
            var center = arena.Center;
            var free = ArenaLayout.LargestFreeSquare(geometry, arena);
            if (free < MinArenaFreeSide)
                report.Add(Error, Gameplay, ValidationCodes.ArenaTooSmall,
                    $"Arena {arena.Rect}: the largest free square is {free}x{free}, at least " +
                    $"{MinArenaFreeSide}x{MinArenaFreeSide} needed.", center);

            var entries = ArenaLayout.FindEntries(geometry, arena);
            var wide = entries.Count(e => e.Width >= MinArenaEntryWidth);
            if (wide < MinArenaEntries)
                report.Add(Error, Gameplay, ValidationCodes.ArenaFewEntries,
                    $"Arena {arena.Rect} has {wide} entr{(wide == 1 ? "y" : "ies")} at least {MinArenaEntryWidth} wide through " +
                    $"its ring, {MinArenaEntries} needed.", center);
        }

        /// <summary>
        /// Spawn points: out of every start's view, reachable from the start, and a 3×3 body gets from each to the arena
        /// (centres whose 3×3 is walkable, 4 directions; one search from the arena).
        /// </summary>
        private static void ValidateSpawnLayout(LevelData level, List<PlayerStartData> starts, bool[] reached,
            ValidationReport report)
        {
            var geometry = level.Geometry;
            var arena = level.Arena;
            bool[] bodyReached = null;
            if (!arena.IsEmpty && level.MonsterSpawns.Count > 0)
                bodyReached = BodyReach(geometry, arena);

            foreach (var spawn in level.MonsterSpawns)
            {
                var p = spawn.Position;
                if (!ArenaLayout.IsWalkable(geometry, p)) continue; // MonsterSpawnBlocked

                var seen = starts.FirstOrDefault(s => s.Position.Floor == p.Floor &&
                                                      Math.Max(Math.Abs(s.Position.X - p.X), Math.Abs(s.Position.Y - p.Y)) <= StartViewRadius);
                if (seen != null)
                    report.Add(Error, Gameplay, ValidationCodes.SpawnInStartView,
                        $"Spawn point '{spawn.Id}' at {p} is within {StartViewRadius} cells of start '{seen.Id}': " +
                        "monsters would appear in view.", p, spawn.Id);

                if (!reached[geometry.ToIndex(p)])
                    report.Add(Error, Gameplay, ValidationCodes.SpawnUnreachable,
                        $"Spawn point '{spawn.Id}' at {p} cannot be reached from the start.", p, spawn.Id);

                if (bodyReached == null || p.Floor != arena.Floor) continue;
                if (!BodyFits(geometry, p))
                    report.Add(Error, Gameplay, ValidationCodes.SpawnNoBodyPath,
                        $"Spawn point '{spawn.Id}' at {p} has no free {BodySize}x{BodySize} ground around it.", p, spawn.Id);
                else if (!bodyReached[geometry.ToIndex(p)])
                    report.Add(Error, Gameplay, ValidationCodes.SpawnNoBodyPath,
                        $"A {BodySize}x{BodySize} body cannot get from spawn point '{spawn.Id}' at {p} to the arena.", p, spawn.Id);
            }
        }

        /// <summary>Body centres that reach a centre inside the arena.</summary>
        private static bool[] BodyReach(LevelGeometry geometry, ArenaData arena)
        {
            var reached = new bool[geometry.CellCount];
            var queue = new Queue<GridPosition>();
            var rect = arena.Rect;
            for (var y = rect.Y; y < rect.YMax; y++)
            for (var x = rect.X; x < rect.XMax; x++)
            {
                var p = new GridPosition(x, y, arena.Floor);
                if (!BodyFits(geometry, p)) continue;
                reached[geometry.ToIndex(p)] = true;
                queue.Enqueue(p);
            }

            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var direction in DirectionExtensions.All)
                {
                    var next = p.Neighbour(direction);
                    if (!geometry.IsInside(next) || reached[geometry.ToIndex(next)] || !BodyFits(geometry, next)) continue;
                    reached[geometry.ToIndex(next)] = true;
                    queue.Enqueue(next);
                }
            }

            return reached;
        }

        private static bool BodyFits(LevelGeometry geometry, GridPosition center)
        {
            const int half = BodySize / 2;
            for (var dy = -half; dy <= half; dy++)
            for (var dx = -half; dx <= half; dx++)
                if (!ArenaLayout.IsWalkable(geometry, new GridPosition(center.X + dx, center.Y + dy, center.Floor)))
                    return false;
            return true;
        }

        /// <summary>
        /// No 8×8 chunk holds more obstacles than the recipe's Obstacles step allows (the generator's limit; hand-placed
        /// groves can break it — a warning).
        /// </summary>
        private static void ValidateObstacleBudget(LevelData level, ValidationReport report)
        {
            var geometry = level.Geometry;
            if (!geometry.ObstaclesConsistent) return;

            const int size = ObstaclesStep.ChunkSize;
            var share = MaxChunkShare(level.Generation.Recipe);
            var limit = (int)Math.Floor(share * size * size);
            for (var floor = 0; floor < geometry.FloorCount; floor++)
            for (var cy = 0; cy < geometry.Height; cy += size)
            for (var cx = 0; cx < geometry.Width; cx += size)
            {
                var count = 0;
                for (var y = cy; y < Math.Min(cy + size, geometry.Height); y++)
                for (var x = cx; x < Math.Min(cx + size, geometry.Width); x++)
                    if (geometry.GetObstacle(new GridPosition(x, y, floor)) != null)
                        count++;

                if (count > limit)
                    report.Add(Warning, Gameplay, ValidationCodes.ObstacleBudget,
                        $"Chunk {size}x{size} at ({cx}, {cy}) has {count} obstacles, the budget is {limit} ({Math.Round(share * 100f)}%).",
                        new GridPosition(Math.Min(cx + size / 2, geometry.Width - 1), Math.Min(cy + size / 2, geometry.Height - 1), floor));
            }
        }

        private static float MaxChunkShare(BiomeRecipe recipe)
        {
            if (recipe != null)
                foreach (var step in recipe.Steps)
                    if (step is ObstaclesStep obstacles)
                        return obstacles.MaxChunkShare;
            return DefaultMaxChunkShare;
        }
    }
}
