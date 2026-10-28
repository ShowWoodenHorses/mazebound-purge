using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Generation
{
    /// <summary>
    /// Barrels and chests (stage 7в). Everything outside the start's view (Chebyshev &gt; <see cref="_startViewRadius"/>),
    /// off the trails, the arena (and a cell around it), spawn points and kept cells; no object cuts any walkable cell off
    /// from the start.
    /// <list type="bullet">
    /// <item>Barrels — <see cref="_minBarrels"/>…<see cref="_maxBarrels"/> of the kinds evenly, at the edges of groves and
    /// clearings (next to an obstacle or a wall), at least <see cref="_barrelStartDistance"/> from the start and
    /// <see cref="_barrelSpacing"/> from each other.</item>
    /// <item>Chests — common ones inside clearings, the locked one at least <see cref="_lockedStartDistance"/> from the
    /// start, the challenge one where the circle of <see cref="_challengeRadius"/> around it is the most open (the
    /// champions climb out there).</item>
    /// </list>
    /// Kept objects of the level count towards the numbers.
    /// </summary>
    [Serializable, ModuleInfo("Objects", Category = "Fill")]
    public sealed class ObjectsStep : GenerationStep
    {
        [Header("Barrels")]
        [SerializeField, Min(0)] private int _minBarrels = 3;
        [SerializeField, Min(0)] private int _maxBarrels = 5;
        [SerializeField] private List<BarrelKind> _barrelKinds = new List<BarrelKind> { BarrelKind.Gas, BarrelKind.Fire, BarrelKind.Acid };
        [SerializeField, Min(0)] private int _barrelStartDistance = 8;
        [SerializeField, Min(1)] private int _barrelSpacing = 4;

        [Header("Chests")]
        [SerializeField, Min(0)] private int _commonChests = 2;
        [SerializeField, Min(0)] private int _lockedChests = 1;
        [SerializeField, Min(0)] private int _challengeChests = 1;
        [SerializeField, Min(0)] private int _lockedStartDistance = 20;
        [Tooltip("Cells around a challenge chest that should be open: its champions climb out there.")]
        [SerializeField, Min(1)] private int _challengeRadius = 6;
        [Tooltip("Least distance between two chests, cells.")]
        [SerializeField, Min(1)] private int _chestSpacing = 8;

        [Header("All")]
        [Tooltip("Half size of the start's view (21×21 → 10): objects are outside it.")]
        [SerializeField, Min(0)] private int _startViewRadius = 10;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            if (!layout.HasStart) return;
            // Pockets are joined first (the generator does it after the steps too): a way made later could need a cell
            // an object stands in.
            layout.Connect();
            var taken = new HashSet<int>();
            foreach (var barrel in layout.Barrels) taken.Add(layout.Index(barrel.Cell));
            foreach (var chest in layout.Chests) taken.Add(layout.Index(chest.Cell));
            VehiclesStep.TakeKept(layout, taken);
            var baseline = CountReachable(layout, taken);

            PlaceChests(layout, random, taken, ref baseline);
            PlaceBarrels(layout, random, taken, ref baseline);
        }

        // ------------------------------------------------------------------ Chests

        private void PlaceChests(LevelLayout layout, DeterministicRandom random, HashSet<int> taken, ref int baseline)
        {
            var common = Need(layout, ChestKind.Common, _commonChests);
            var locked = Need(layout, ChestKind.Locked, _lockedChests);
            var challenge = Need(layout, ChestKind.Challenge, _challengeChests);
            var candidates = Candidates(layout, taken);
            ArenaStep.Shuffle(candidates, random);

            for (var n = 0; n < challenge; n++)
            {
                var best = -1;
                var bestOpen = -1;
                for (var i = 0; i < candidates.Count; i++)
                {
                    if (!FarFromChests(layout, candidates[i])) continue;
                    var open = OpenAround(layout, candidates[i], _challengeRadius);
                    if (open <= bestOpen) continue;
                    bestOpen = open;
                    best = i;
                }

                if (!TryPlaceChest(layout, candidates, best, ChestKind.Challenge, taken, ref baseline))
                    layout.Problems.Add("Objects: no place for the challenge chest.");
            }

            for (var n = 0; n < locked; n++)
            {
                var best = -1;
                for (var i = 0; i < candidates.Count && best < 0; i++)
                    if (Distance(candidates[i], layout.Start) >= _lockedStartDistance && FarFromChests(layout, candidates[i]))
                        best = i;
                if (!TryPlaceChest(layout, candidates, best, ChestKind.Locked, taken, ref baseline,
                        cell => Distance(cell, layout.Start) >= _lockedStartDistance))
                    layout.Problems.Add("Objects: no place for the locked chest.");
            }

            for (var n = 0; n < common; n++)
            {
                var best = -1;
                for (var i = 0; i < candidates.Count && best < 0; i++)
                    if (InClearing(layout, candidates[i]) && FarFromChests(layout, candidates[i]))
                        best = i;
                // No clearing has room: any place in the open.
                for (var i = 0; i < candidates.Count && best < 0; i++)
                    if (FarFromChests(layout, candidates[i]))
                        best = i;
                if (!TryPlaceChest(layout, candidates, best, ChestKind.Common, taken, ref baseline))
                    layout.Problems.Add("Objects: no place for a common chest.");
            }
        }

        private static int Need(LevelLayout layout, ChestKind kind, int wanted)
        {
            var have = 0;
            foreach (var chest in layout.Chests)
                if (chest.Kind == kind) have++;
            return Math.Max(0, wanted - have);
        }

        private bool TryPlaceChest(LevelLayout layout, List<GridPosition> candidates, int first, ChestKind kind,
            HashSet<int> taken, ref int baseline, Func<GridPosition, bool> fits = null)
        {
            if (first < 0) return false;
            // The chosen one, else the next fitting ones in order, if it would cut cells off.
            for (var i = first; i < candidates.Count; i++)
            {
                if (i != first && (!FarFromChests(layout, candidates[i]) || fits != null && !fits(candidates[i]))) continue;
                if (!TryTake(layout, candidates[i], taken, ref baseline)) continue;
                layout.Chests.Add(new LayoutObject<ChestKind>(candidates[i], kind));
                candidates.RemoveAt(i);
                return true;
            }

            return false;
        }

        private bool FarFromChests(LevelLayout layout, GridPosition cell)
        {
            foreach (var chest in layout.Chests)
                if (Distance(chest.Cell, cell) < _chestSpacing)
                    return false;
            return true;
        }

        private static bool InClearing(LevelLayout layout, GridPosition cell)
        {
            foreach (var clearing in layout.Clearings)
                if (Distance(cell, clearing.Center) <= clearing.Radius - 1f)
                    return true;
            return false;
        }

        /// <summary>Walkable cells within <paramref name="radius"/> of the cell.</summary>
        private static int OpenAround(LevelLayout layout, GridPosition cell, int radius)
        {
            var open = 0;
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                var x = cell.X + dx;
                var y = cell.Y + dy;
                if (layout.InBounds(x, y) && layout.IsWalkable(layout.Index(x, y))) open++;
            }

            return open;
        }

        // ------------------------------------------------------------------ Barrels

        private void PlaceBarrels(LevelLayout layout, DeterministicRandom random, HashSet<int> taken, ref int baseline)
        {
            var target = random.NextInt(_minBarrels, Math.Max(_minBarrels, _maxBarrels) + 1) - layout.KeptBarrelCount;
            if (target <= 0 || _barrelKinds.Count == 0) return;
            var candidates = Candidates(layout, taken);
            ArenaStep.Shuffle(candidates, random);
            var placed = 0;
            var firstKind = random.NextInt(_barrelKinds.Count);
            foreach (var cell in candidates)
            {
                if (placed >= target) break;
                if (Distance(cell, layout.Start) < _barrelStartDistance || !NextToCover(layout, cell)) continue;
                var far = true;
                foreach (var barrel in layout.Barrels)
                    if (Distance(barrel.Cell, cell) < _barrelSpacing)
                        far = false;
                foreach (var chest in layout.Chests)
                    if (Distance(chest.Cell, cell) < 2f)
                        far = false;
                if (!far || !TryTake(layout, cell, taken, ref baseline)) continue;
                var kind = _barrelKinds[(firstKind + placed) % _barrelKinds.Count]; // the kinds in turn
                layout.Barrels.Add(new LayoutObject<BarrelKind>(cell, kind));
                placed++;
            }

            if (placed + layout.KeptBarrelCount < _minBarrels)
                layout.Problems.Add($"Objects: {placed + layout.KeptBarrelCount} barrel(s) fit, {_minBarrels} wanted.");
        }

        /// <summary>An obstacle or a wall next to it (the edge of a grove or a clearing).</summary>
        private static bool NextToCover(LevelLayout layout, GridPosition cell)
        {
            for (var d = 0; d < 4; d++)
            {
                var x = cell.X + LevelLayout.Dx[d];
                var y = cell.Y + LevelLayout.Dy[d];
                if (layout.InBounds(x, y) && !layout.IsWalkable(layout.Index(x, y))) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ Common

        /// <summary>Walkable cells reachable from the start where an object may stand.</summary>
        private List<GridPosition> Candidates(LevelLayout layout, HashSet<int> taken)
        {
            var reached = layout.Reachable();
            var result = new List<GridPosition>();
            var arena = layout.Arena;
            for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var index = layout.Index(x, y);
                if (!reached[index] || taken.Contains(index) || layout.IsKept(index) || layout.IsTrail(index)) continue;
                if (layout.Cell(index) != CellType.Floor) continue;
                var cell = new GridPosition(x, y);
                if (Math.Max(Math.Abs(x - layout.Start.X), Math.Abs(y - layout.Start.Y)) <= _startViewRadius) continue;
                if (arena != null && !arena.IsEmpty && Expand(arena.Outer, 1).Contains(cell)) continue;
                var nearSpawn = false;
                foreach (var spawn in layout.Spawns)
                    if (Math.Max(Math.Abs(x - spawn.X), Math.Abs(y - spawn.Y)) <= 2)
                        nearSpawn = true;
                if (!nearSpawn) result.Add(cell);
            }

            return result;
        }

        /// <summary>Takes the cell when no walkable cell is cut off from the start by it.</summary>
        private static bool TryTake(LevelLayout layout, GridPosition cell, HashSet<int> taken, ref int baseline)
        {
            var index = layout.Index(cell);
            taken.Add(index);
            var reached = CountReachable(layout, taken);
            if (reached == baseline - 1)
            {
                baseline = reached;
                return true;
            }

            taken.Remove(index);
            return false;
        }

        /// <summary>Walkable cells reachable from the start (4 directions) when the taken cells block.</summary>
        internal static int CountReachable(LevelLayout layout, HashSet<int> taken)
        {
            var start = layout.Index(layout.Start);
            if (!layout.IsWalkable(start) || taken.Contains(start)) return 0;
            var reached = new bool[layout.CellCount];
            var queue = new Queue<int>();
            reached[start] = true;
            queue.Enqueue(start);
            var count = 1;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var x = current % layout.Width;
                var y = current / layout.Width;
                for (var d = 0; d < 4; d++)
                {
                    var nx = x + LevelLayout.Dx[d];
                    var ny = y + LevelLayout.Dy[d];
                    if (!layout.InBounds(nx, ny)) continue;
                    var next = layout.Index(nx, ny);
                    if (reached[next] || !layout.IsWalkable(next) || taken.Contains(next)) continue;
                    reached[next] = true;
                    queue.Enqueue(next);
                    count++;
                }
            }

            // Cells of taken objects that were reachable are not counted: the baseline counts without them.
            return count;
        }

        private static GridRect Expand(GridRect rect, int by) =>
            new GridRect(rect.X - by, rect.Y - by, rect.Width + 2 * by, rect.Height + 2 * by);

        private static float Distance(GridPosition a, GridPosition b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public override void Validate(List<string> problems)
        {
            if (_maxBarrels < _minBarrels) problems.Add("Objects: Max Barrels is less than Min Barrels.");
            if (_minBarrels > 0 && _barrelKinds.Count == 0) problems.Add("Objects: no barrel kinds.");
        }
    }
}
