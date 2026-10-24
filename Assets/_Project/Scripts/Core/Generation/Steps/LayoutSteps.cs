using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Generation
{
    /// <summary>A wall band along the level's edge (the forest around the level).</summary>
    [Serializable, ModuleInfo("Perimeter", Category = "Layout")]
    public sealed class PerimeterStep : GenerationStep
    {
        [Tooltip("Wall width along the edge, cells.")]
        [SerializeField, Min(1)] private int _thickness = 2;

        public int Thickness { get => _thickness; internal set => _thickness = value; }

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            layout.Perimeter = _thickness;
            for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
                if (layout.EdgeDistance(x, y) < _thickness)
                    layout.SetWall(layout.Index(x, y));
        }
    }

    /// <summary>The player start (a start kept on an edited cell stays) with open ground around it.</summary>
    [Serializable, ModuleInfo("Start", Category = "Layout")]
    public sealed class StartStep : GenerationStep
    {
        [Tooltip("Where the start is, share of the level's width and height (0.5, 0.5 = the centre).")]
        [SerializeField] private Vector2 _position = new Vector2(0.5f, 0.5f);

        [Tooltip("Open ground around the start, cells.")]
        [SerializeField, Min(0f)] private float _clearRadius = 3f;

        public float ClearRadius => _clearRadius;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            if (!layout.HasStart)
            {
                var x = Mathf.Clamp(Mathf.FloorToInt(layout.Width * _position.x), 0, layout.Width - 1);
                var y = Mathf.Clamp(Mathf.FloorToInt(layout.Height * _position.y), 0, layout.Height - 1);
                layout.SetStart(new GridPosition(x, y));
            }

            layout.OpenDisc(layout.Start, _clearRadius);
            layout.StartClearRadius = _clearRadius;
        }
    }

    /// <summary>
    /// The boss arena: an open square ringed by walls with a few wide entries, far enough from the start. A kept arena
    /// (on edited cells) stays where it is and is only rebuilt.
    /// </summary>
    [Serializable, ModuleInfo("Arena", Category = "Layout")]
    public sealed class ArenaStep : GenerationStep
    {
        [Tooltip("Open cells of the arena, side.")]
        [SerializeField, Min(4)] private int _size = 16;

        [Tooltip("Wall ring around the arena, cells.")]
        [SerializeField, Min(1)] private int _ring = 2;

        [Tooltip("Least distance from the start to the arena's centre, cells.")]
        [SerializeField, Min(0)] private int _minDistance = 20;

        [SerializeField, Min(1)] private int _minEntries = 3;
        [SerializeField, Min(1)] private int _maxEntries = 4;

        [Tooltip("Width of an entry, cells.")]
        [SerializeField, Min(1)] private int _entryWidth = 3;

        public int Size => _size;
        public int MinDistance => _minDistance;
        public int EntryWidth => _entryWidth;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            var arena = layout.Arena;
            if (arena == null || arena.IsEmpty)
            {
                arena = Place(layout, random);
                if (arena == null)
                {
                    layout.Problems.Add($"Arena {_size}x{_size} does not fit at least {_minDistance} cells from the start.");
                    return;
                }

                layout.Arena = arena;
            }

            Build(layout, arena);
        }

        private ArenaData Place(LevelLayout layout, DeterministicRandom random)
        {
            var outerSize = _size + 2 * _ring;
            var margin = layout.Perimeter + 2; // trails need room between the ring and the perimeter
            var candidates = new List<GridRect>();
            for (var y = margin; y + outerSize <= layout.Height - margin; y++)
            for (var x = margin; x + outerSize <= layout.Width - margin; x++)
            {
                var rect = new GridRect(x + _ring, y + _ring, _size, _size);
                var cx = rect.X + rect.Width / 2f - 0.5f;
                var cy = rect.Y + rect.Height / 2f - 0.5f;
                var dx = cx - layout.Start.X;
                var dy = cy - layout.Start.Y;
                if (dx * dx + dy * dy < (float)_minDistance * _minDistance) continue;
                if (OuterTouchesStart(layout, new GridRect(x, y, outerSize, outerSize))) continue;
                candidates.Add(rect);
            }

            if (candidates.Count == 0) return null;
            var arena = new ArenaData(candidates[random.NextInt(candidates.Count)], _ring);

            var sides = new List<Direction>();
            foreach (var side in DirectionExtensions.All)
                if (HasRoomOutside(layout, arena, side))
                    sides.Add(side);
            Shuffle(sides, random);
            var count = Math.Min(sides.Count, random.NextInt(_minEntries, _maxEntries + 1));
            if (count < _minEntries)
                layout.Problems.Add($"Arena: only {count} entries fit, {_minEntries} wanted.");

            for (var i = 0; i < count; i++)
                arena.MutableEntries.Add(Entry(arena, sides[i], random));
            return arena;
        }

        private static bool OuterTouchesStart(LevelLayout layout, GridRect outer)
        {
            var r = (int)Math.Ceiling(layout.StartClearRadius) + 1;
            return outer.Overlaps(new GridRect(layout.Start.X - r, layout.Start.Y - r, 2 * r + 1, 2 * r + 1));
        }

        /// <summary>Two rows outside the ring on that side are inside the perimeter (a trail of width 3 fits).</summary>
        private static bool HasRoomOutside(LevelLayout layout, ArenaData arena, Direction side)
        {
            var outer = arena.Outer;
            var inner = layout.Perimeter;
            switch (side)
            {
                case Direction.North: return outer.YMax + 1 < layout.Height - inner;
                case Direction.South: return outer.Y - 2 >= inner;
                case Direction.East: return outer.XMax + 1 < layout.Width - inner;
                default: return outer.X - 2 >= inner;
            }
        }

        private ArenaEntry Entry(ArenaData arena, Direction side, DeterministicRandom random)
        {
            var rect = arena.Rect;
            var horizontal = side == Direction.North || side == Direction.South;
            var span = horizontal ? rect.Width : rect.Height;
            var width = Math.Min(_entryWidth, span);
            var low = Math.Min(2, span - width);
            var high = Math.Max(low, span - width - 2);
            var along = (horizontal ? rect.X : rect.Y) + random.NextInt(low, high + 1);
            switch (side)
            {
                case Direction.North: return new ArenaEntry(new GridRect(along, rect.YMax, width, arena.Ring), side);
                case Direction.South: return new ArenaEntry(new GridRect(along, rect.Y - arena.Ring, width, arena.Ring), side);
                case Direction.East: return new ArenaEntry(new GridRect(rect.XMax, along, arena.Ring, width), side);
                default: return new ArenaEntry(new GridRect(rect.X - arena.Ring, along, arena.Ring, width), side);
            }
        }

        private static void Build(LevelLayout layout, ArenaData arena)
        {
            var outer = arena.Outer;
            for (var y = outer.Y; y < outer.YMax; y++)
            for (var x = outer.X; x < outer.XMax; x++)
            {
                if (!layout.InBounds(x, y)) continue;
                var cell = new GridPosition(x, y);
                var index = layout.Index(x, y);
                if (arena.Rect.Contains(cell) || InEntry(arena, cell)) layout.MarkOpen(index);
                else layout.SetWall(index);
            }

            layout.ArenaApproaches.Clear();
            foreach (var entry in arena.Entries)
            {
                var gap = entry.Gap;
                var offset = entry.Side.ToOffset();
                var approach = entry.Side == Direction.North || entry.Side == Direction.South
                    ? new GridPosition(gap.X + gap.Width / 2, entry.Side == Direction.North ? gap.YMax : gap.Y - 1)
                    : new GridPosition(entry.Side == Direction.East ? gap.XMax : gap.X - 1, gap.Y + gap.Height / 2);
                if (!layout.InBounds(approach.X, approach.Y)) continue;
                layout.ArenaApproaches.Add(approach);
                // The approach and one more row outward stay open, so a trail of width 3 can reach the gap.
                for (var step = 0; step < 2; step++)
                {
                    var c = new GridPosition(approach.X + offset.X * step, approach.Y + offset.Y * step);
                    for (var k = -1; k <= 1; k++)
                    {
                        var x = c.X + (offset.X == 0 ? k : 0);
                        var y = c.Y + (offset.Y == 0 ? k : 0);
                        if (layout.InBounds(x, y)) layout.MarkOpen(layout.Index(x, y));
                    }
                }
            }
        }

        private static bool InEntry(ArenaData arena, GridPosition cell)
        {
            foreach (var entry in arena.Entries)
                if (entry.Gap.Contains(cell))
                    return true;
            return false;
        }

        internal static void Shuffle<T>(IList<T> list, DeterministicRandom random)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = random.NextInt(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public override void Validate(List<string> problems)
        {
            if (_maxEntries < _minEntries) problems.Add("Arena: Max Entries is less than Min Entries.");
            if (_entryWidth > _size) problems.Add("Arena: an entry is wider than the arena.");
        }
    }

    /// <summary>
    /// Where the waves' monsters appear: near the edges, far from the start and outside its view, spread apart; each
    /// with open 3×3 ground (a big body fits). Spawns kept on edited cells count.
    /// </summary>
    [Serializable, ModuleInfo("Spawn Points", Category = "Layout")]
    public sealed class SpawnPointsStep : GenerationStep
    {
        [SerializeField, Min(1)] private int _minCount = 6;
        [SerializeField, Min(1)] private int _maxCount = 8;

        [Tooltip("How far inward from the perimeter a spawn point may be, cells.")]
        [SerializeField, Min(1)] private int _edgeBand = 6;

        [Tooltip("Least distance from the start, cells.")]
        [SerializeField, Min(0)] private int _minDistance = 12;

        [Tooltip("Half size of the start's view (21×21 → 10): spawn points are outside it.")]
        [SerializeField, Min(0)] private int _startViewRadius = 10;

        [Tooltip("Least distance between two spawn points, cells.")]
        [SerializeField, Min(1)] private int _minSpacing = 10;

        public int MinDistance => _minDistance;
        public int StartViewRadius => _startViewRadius;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            var target = random.NextInt(_minCount, Math.Max(_minCount, _maxCount) + 1);
            var candidates = new List<GridPosition>();
            for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
                if (IsCandidate(layout, x, y))
                    candidates.Add(new GridPosition(x, y));
            ArenaStep.Shuffle(candidates, random);

            var chosen = new List<GridPosition>(layout.Spawns);
            while (chosen.Count < target && candidates.Count > 0)
            {
                var best = -1;
                var bestDistance = -1f;
                for (var i = 0; i < candidates.Count; i++)
                {
                    var distance = chosen.Count == 0 ? 0f : NearestSquared(chosen, candidates[i]);
                    if (distance > bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }

                if (chosen.Count > 0 && bestDistance < _minSpacing * _minSpacing) break;
                chosen.Add(candidates[best]);
                candidates.RemoveAt(best);
            }

            if (chosen.Count < _minCount)
                layout.Problems.Add($"Spawn points: {chosen.Count} fit, {_minCount} wanted.");

            for (var i = layout.Spawns.Count; i < chosen.Count; i++)
            {
                var spawn = chosen[i];
                layout.Spawns.Add(spawn);
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    layout.MarkOpen(layout.Index(spawn.X + dx, spawn.Y + dy));
            }
        }

        private bool IsCandidate(LevelLayout layout, int x, int y)
        {
            var edge = layout.EdgeDistance(x, y);
            if (edge < layout.Perimeter + 1 || edge > layout.Perimeter + _edgeBand) return false;
            var dx = x - layout.Start.X;
            var dy = y - layout.Start.Y;
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= _startViewRadius) return false;
            if (dx * dx + dy * dy < _minDistance * _minDistance) return false;
            var arena = layout.Arena;
            for (var oy = -1; oy <= 1; oy++)
            for (var ox = -1; ox <= 1; ox++)
            {
                var cx = x + ox;
                var cy = y + oy;
                var index = layout.Index(cx, cy);
                if (layout.IsWall(index) && !layout.IsOpen(index) && layout.IsKept(index)) return false;
                if (arena != null && !arena.IsEmpty && Expand(arena.Outer, 1).Contains(new GridPosition(cx, cy))) return false;
            }

            return true;
        }

        private static GridRect Expand(GridRect rect, int by) =>
            new GridRect(rect.X - by, rect.Y - by, rect.Width + 2 * by, rect.Height + 2 * by);

        private static float NearestSquared(List<GridPosition> points, GridPosition p)
        {
            var best = float.MaxValue;
            foreach (var q in points)
            {
                float dx = p.X - q.X, dy = p.Y - q.Y;
                best = Math.Min(best, dx * dx + dy * dy);
            }

            return best;
        }

        public override void Validate(List<string> problems)
        {
            if (_maxCount < _minCount) problems.Add("Spawn points: Max Count is less than Min Count.");
        }
    }

    /// <summary>Open round patches in the forest, away from the start, the arena and each other.</summary>
    [Serializable, ModuleInfo("Clearings", Category = "Layout")]
    public sealed class ClearingsStep : GenerationStep
    {
        [SerializeField, Min(0)] private int _minCount = 3;
        [SerializeField, Min(0)] private int _maxCount = 5;

        [Tooltip("Diameter range, cells.")]
        [SerializeField, Min(2)] private int _minSize = 6;
        [SerializeField, Min(2)] private int _maxSize = 10;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            var target = random.NextInt(_minCount, Math.Max(_minCount, _maxCount) + 1);
            for (var attempt = 0; attempt < 200 && layout.Clearings.Count < target; attempt++)
            {
                var radius = random.NextInt(_minSize, Math.Max(_minSize, _maxSize) + 1) / 2f;
                var margin = layout.Perimeter + (int)Math.Ceiling(radius) + 1;
                if (layout.Width - 2 * margin <= 0 || layout.Height - 2 * margin <= 0) break;
                var center = new GridPosition(random.NextInt(margin, layout.Width - margin), random.NextInt(margin, layout.Height - margin));
                if (!Fits(layout, center, radius)) continue;
                layout.OpenDisc(center, radius);
                layout.Clearings.Add(new Clearing(center, radius));
            }

            if (layout.Clearings.Count < _minCount)
                layout.Problems.Add($"Clearings: {layout.Clearings.Count} fit, {_minCount} wanted.");
        }

        private static bool Fits(LevelLayout layout, GridPosition center, float radius)
        {
            if (Distance(center, layout.Start) < layout.StartClearRadius + radius + 3f) return false;
            foreach (var clearing in layout.Clearings)
                if (Distance(center, clearing.Center) < clearing.Radius + radius + 2f)
                    return false;
            foreach (var spawn in layout.Spawns)
                if (Distance(center, spawn) < radius + 2f)
                    return false;

            var arena = layout.Arena;
            if (arena == null || arena.IsEmpty) return true;
            var outer = arena.Outer;
            var nearestX = Math.Max(outer.X, Math.Min(center.X, outer.XMax - 1));
            var nearestY = Math.Max(outer.Y, Math.Min(center.Y, outer.YMax - 1));
            return Distance(center, new GridPosition(nearestX, nearestY)) >= radius + 2f;
        }

        private static float Distance(GridPosition a, GridPosition b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public override void Validate(List<string> problems)
        {
            if (_maxCount < _minCount) problems.Add("Clearings: Max Count is less than Min Count.");
            if (_maxSize < _minSize) problems.Add("Clearings: Max Size is less than Min Size.");
        }
    }

    /// <summary>
    /// Winding trails of a fixed width joining the start, the clearings, the arena entries and the spawn points (a
    /// spanning tree, shortest links first): every key place is connected, and vehicles (stage 9) have room to drive.
    /// </summary>
    [Serializable, ModuleInfo("Trails", Category = "Layout")]
    public sealed class TrailsStep : GenerationStep
    {
        [Tooltip("Trail width, cells (odd).")]
        [SerializeField, Min(1)] private int _width = 3;

        [Tooltip("How much the trails wind: 0 = straight.")]
        [SerializeField, Range(0f, 4f)] private float _wander = 1.5f;

        [Tooltip("Lattice cells per grid cell of the winding noise.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _wanderScale = 0.15f;

        public int Width => _width;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            var nodes = new List<GridPosition> { layout.Start };
            foreach (var clearing in layout.Clearings) nodes.Add(clearing.Center);
            nodes.AddRange(layout.ArenaApproaches);
            nodes.AddRange(layout.Spawns);

            var clearance = Math.Max(0, (_width - 1) / 2);
            var noiseSeed = (int)random.NextULong();
            foreach (var (a, b) in SpanningTree(nodes))
            {
                var path = Route(layout, nodes[a], nodes[b], clearance, noiseSeed);
                if (path == null)
                {
                    layout.Problems.Add($"Trails: no way from {nodes[a]} to {nodes[b]}.");
                    continue;
                }

                foreach (var cell in path)
                {
                    var x = cell % layout.Width;
                    var y = cell / layout.Width;
                    for (var dy = -clearance; dy <= clearance; dy++)
                    for (var dx = -clearance; dx <= clearance; dx++)
                        if (layout.InBounds(x + dx, y + dy))
                            layout.MarkTrail(layout.Index(x + dx, y + dy));
                }
            }
        }

        /// <summary>Prim's tree over straight distances: (from, to) node pairs.</summary>
        private static List<(int, int)> SpanningTree(List<GridPosition> nodes)
        {
            var edges = new List<(int, int)>();
            if (nodes.Count < 2) return edges;
            var inTree = new bool[nodes.Count];
            var best = new float[nodes.Count];
            var parent = new int[nodes.Count];
            for (var i = 0; i < nodes.Count; i++)
            {
                best[i] = float.MaxValue;
                parent[i] = -1;
            }

            best[0] = 0f;
            for (var added = 0; added < nodes.Count; added++)
            {
                var next = -1;
                for (var i = 0; i < nodes.Count; i++)
                    if (!inTree[i] && (next < 0 || best[i] < best[next]))
                        next = i;
                inTree[next] = true;
                if (parent[next] >= 0) edges.Add((parent[next], next));
                for (var i = 0; i < nodes.Count; i++)
                {
                    if (inTree[i]) continue;
                    float dx = nodes[i].X - nodes[next].X, dy = nodes[i].Y - nodes[next].Y;
                    var d = dx * dx + dy * dy;
                    if (d < best[i])
                    {
                        best[i] = d;
                        parent[i] = next;
                    }
                }
            }

            return edges;
        }

        /// <summary>
        /// Cheapest way (Dijkstra, 4 directions) whose cells all have <paramref name="clearance"/> cells free of walls
        /// around them. Noise makes it wind; cells of earlier trails are cheaper (trails merge).
        /// </summary>
        private List<int> Route(LevelLayout layout, GridPosition from, GridPosition to, int clearance, int noiseSeed)
        {
            var count = layout.CellCount;
            var cost = new float[count];
            var previous = new int[count];
            for (var i = 0; i < count; i++)
            {
                cost[i] = float.MaxValue;
                previous[i] = -1;
            }

            var start = layout.Index(from);
            var goal = layout.Index(to);
            var queue = new SortedSet<(float, int)>();
            cost[start] = 0f;
            queue.Add((0f, start));
            while (queue.Count > 0)
            {
                var (c, cell) = queue.Min;
                queue.Remove(queue.Min);
                if (cell == goal) break;
                if (c > cost[cell]) continue;
                var x = cell % layout.Width;
                var y = cell / layout.Width;
                for (var d = 0; d < 4; d++)
                {
                    var nx = x + LevelLayout.Dx[d];
                    var ny = y + LevelLayout.Dy[d];
                    if (!layout.InBounds(nx, ny)) continue;
                    var next = layout.Index(nx, ny);
                    if (next != goal && !Clear(layout, nx, ny, clearance)) continue;
                    var step = 1f + _wander * LayoutNoise.Fractal(noiseSeed, nx, ny, _wanderScale);
                    if (layout.IsTrail(next)) step *= 0.5f;
                    var total = c + step;
                    if (total >= cost[next]) continue;
                    if (cost[next] < float.MaxValue) queue.Remove((cost[next], next));
                    cost[next] = total;
                    previous[next] = cell;
                    queue.Add((total, next));
                }
            }

            if (previous[goal] < 0 && goal != start) return null;
            var path = new List<int>();
            for (var cell = goal; cell >= 0; cell = previous[cell])
                path.Add(cell);
            return path;
        }

        private static bool Clear(LevelLayout layout, int x, int y, int clearance)
        {
            for (var dy = -clearance; dy <= clearance; dy++)
            for (var dx = -clearance; dx <= clearance; dx++)
            {
                var cx = x + dx;
                var cy = y + dy;
                if (!layout.InBounds(cx, cy)) return false;
                var index = layout.Index(cx, cy);
                if (layout.IsWall(index) || (layout.IsKept(index) && !layout.IsWalkable(index))) return false;
            }

            return true;
        }
    }
}
