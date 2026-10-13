using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Level;

namespace Maze.Core.Navigation
{
    /// <summary>What the player can reach from one start, collecting every key on the way.</summary>
    public sealed class ReachabilityResult
    {
        private readonly bool[] _reachable;

        internal ReachabilityResult(int width, int height, bool[] reachable, bool[] passable, HashSet<string> keys)
        {
            Width = width;
            Height = height;
            _reachable = reachable;
            Passable = new CellMaskPassability(width, height, passable);
            CollectedKeyIds = keys;
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Final passability with all reachable keys collected. Usable with <see cref="GridPathfinder"/>.</summary>
        public CellMaskPassability Passable { get; }

        public IReadOnlyCollection<string> CollectedKeyIds { get; }

        public bool IsReachable(GridPosition position) =>
            position.X >= 0 && position.X < Width && position.Y >= 0 && position.Y < Height &&
            _reachable[position.Y * Width + position.X];
    }

    /// <summary>
    /// Key/Door reachability (ТЗ §90, §110).
    /// Game rule: one key opens exactly one door and is then removed from the inventory; an unlocked door
    /// stays unlocked. Since a key is only ever useful at its own door, consumption cannot block anything else,
    /// so "reach the key, then its door opens" is exact. The validator enforces the one-key-one-door rule
    /// (KeySharedByDoors); for levels breaking it this solver is optimistic.
    /// Doors without a key or initially open are passable. Repeats a flood fill until no new key is found.
    /// </summary>
    public static class KeyDoorSolver
    {
        public static ReachabilityResult Solve(LevelGeometry geometry, IReadOnlyList<DoorData> doors,
            IReadOnlyList<KeyData> keys, GridPosition start)
        {
            var width = geometry.Width;
            var height = geometry.Height;
            var count = geometry.CellCount;

            var requiredKey = new string[count];
            foreach (var door in doors)
                if (geometry.IsInside(door.Position) && door.RequiresKey && !door.IsInitiallyOpen)
                    requiredKey[geometry.ToIndex(door.Position)] = door.KeyId;

            var keysAt = new Dictionary<int, List<string>>();
            foreach (var key in keys)
            {
                if (!geometry.IsInside(key.Position))
                    continue;

                var index = geometry.ToIndex(key.Position);
                if (!keysAt.TryGetValue(index, out var list))
                    keysAt[index] = list = new List<string>();
                list.Add(key.Id);
            }

            var collected = new HashSet<string>();
            var passable = new bool[count];
            var reachable = new bool[count];

            while (true)
            {
                for (var i = 0; i < count; i++)
                {
                    var lockKey = requiredKey[i];
                    passable[i] = geometry.GetCell(geometry.ToPosition(i)) != CellType.Wall &&
                                  (lockKey == null || collected.Contains(lockKey));
                }

                FloodFill(geometry, passable, start, reachable);

                var foundNew = false;
                foreach (var pair in keysAt)
                    if (reachable[pair.Key])
                        foreach (var keyId in pair.Value)
                            foundNew |= collected.Add(keyId);

                if (!foundNew)
                    return new ReachabilityResult(width, height, reachable, passable, collected);
            }
        }

        private static void FloodFill(LevelGeometry geometry, bool[] passable, GridPosition start, bool[] reachable)
        {
            System.Array.Clear(reachable, 0, reachable.Length);
            if (!geometry.IsInside(start) || !passable[geometry.ToIndex(start)])
                return;

            var queue = new Queue<GridPosition>();
            reachable[geometry.ToIndex(start)] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var direction in DirectionExtensions.All)
                {
                    var next = current.Neighbour(direction);
                    if (!geometry.IsInside(next))
                        continue;

                    var index = geometry.ToIndex(next);
                    if (reachable[index] || !passable[index])
                        continue;

                    reachable[index] = true;
                    queue.Enqueue(next);
                }
            }
        }
    }
}
