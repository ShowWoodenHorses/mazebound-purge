using System;
using System.Collections.Generic;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;

namespace Purge.Core.Generation
{
    /// <summary>
    /// The level being generated (floor 0), shared by the steps of a <see cref="BiomeRecipe"/>: cells, surfaces and
    /// obstacles plus what the steps leave for the next ones — the start, the arena, spawn points, clearings and the
    /// cells that must stay open (<see cref="IsOpen"/>: start, arena, clearings, trails). Kept cells (edited by hand,
    /// locked areas) are never changed: every setter skips them.
    /// </summary>
    public sealed class LevelLayout
    {
        private readonly CellType[] _cells;
        private readonly CellSurface[] _surfaces;
        private readonly ObstacleDefinition[] _obstacles;
        private readonly bool[] _kept;
        private readonly bool[] _open;
        private readonly bool[] _trail;

        internal LevelLayout(int width, int height, int seed, bool[] kept = null)
        {
            Width = width;
            Height = height;
            Seed = seed;
            var count = width * height;
            _cells = new CellType[count];
            _surfaces = new CellSurface[count];
            _obstacles = new ObstacleDefinition[count];
            _kept = kept != null && kept.Length >= count ? kept : new bool[count];
            _open = new bool[count];
            _trail = new bool[count];
            for (var i = 0; i < count; i++)
                _cells[i] = CellType.Floor;
        }

        public int Width { get; }
        public int Height { get; }
        public int CellCount => Width * Height;
        public int Seed { get; }

        /// <summary>Wall width along the level's edge (set by the perimeter step).</summary>
        public int Perimeter { get; internal set; }

        public bool HasStart { get; private set; }

        /// <summary>The start stands on a kept cell (the level already has it; not generated again).</summary>
        public bool StartKept { get; internal set; }
        public GridPosition Start { get; private set; }

        /// <summary>Open ground around the start (set by the start step), cells.</summary>
        public float StartClearRadius { get; internal set; }

        /// <summary>The arena; null until the arena step (or kept from the level).</summary>
        public ArenaData Arena { get; internal set; }

        /// <summary>Where trails come to the arena: the cell just outside each entry.</summary>
        public List<GridPosition> ArenaApproaches { get; } = new List<GridPosition>();

        /// <summary>Spawn points: kept ones first, then the generated ones.</summary>
        public List<GridPosition> Spawns { get; } = new List<GridPosition>();

        /// <summary>How many of <see cref="Spawns"/> stand on kept cells (not generated).</summary>
        public int KeptSpawnCount { get; internal set; }

        public List<Clearing> Clearings { get; } = new List<Clearing>();

        /// <summary>Barrels (stage 7в): kept ones first (<see cref="KeptBarrelCount"/>), then the generated ones.</summary>
        public List<LayoutObject<BarrelKind>> Barrels { get; } = new List<LayoutObject<BarrelKind>>();
        public int KeptBarrelCount { get; internal set; }

        /// <summary>Chests: kept ones first (<see cref="KeptChestCount"/>), then the generated ones.</summary>
        public List<LayoutObject<ChestKind>> Chests { get; } = new List<LayoutObject<ChestKind>>();
        public int KeptChestCount { get; internal set; }

        public List<string> Problems { get; } = new List<string>();
        public DecorSettings Decor { get; internal set; }

        // ------------------------------------------------------------ Cells

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
        public int Index(int x, int y) => y * Width + x;
        public int Index(GridPosition p) => p.Y * Width + p.X;
        public GridPosition Position(int index) => new GridPosition(index % Width, index / Width);

        /// <summary>Distance from the cell to the nearest edge of the level (0 on the border).</summary>
        public int EdgeDistance(int x, int y) => Math.Min(Math.Min(x, y), Math.Min(Width - 1 - x, Height - 1 - y));

        public CellType Cell(int index) => _cells[index];
        public CellSurface Surface(int index) => _surfaces[index];
        public ObstacleDefinition Obstacle(int index) => _obstacles[index];
        public bool IsKept(int index) => _kept[index];
        public bool IsOpen(int index) => _open[index];
        public bool IsTrail(int index) => _trail[index];
        public bool IsWall(int index) => _cells[index] == CellType.Wall;

        /// <summary>The player and monsters can stand here (doors count as open).</summary>
        public bool IsWalkable(int index)
        {
            var type = _cells[index];
            if (type != CellType.Floor && type != CellType.Door) return false;
            var obstacle = _obstacles[index];
            return obstacle == null || !obstacle.BlocksMovement;
        }

        internal void SetWall(int index)
        {
            if (_kept[index]) return;
            _cells[index] = CellType.Wall;
            _surfaces[index] = CellSurface.None;
            _obstacles[index] = null;
            _open[index] = false;
        }

        /// <summary>Plain floor that later steps leave free (no walls, no obstacles).</summary>
        internal void MarkOpen(int index)
        {
            if (_kept[index]) return;
            _cells[index] = CellType.Floor;
            _obstacles[index] = null;
            _open[index] = true;
        }

        internal void MarkTrail(int index)
        {
            if (_kept[index]) return;
            MarkOpen(index);
            _trail[index] = true;
        }

        internal void SetObstacle(int index, ObstacleDefinition obstacle)
        {
            if (_kept[index] || _cells[index] != CellType.Floor) return;
            _obstacles[index] = obstacle;
        }

        internal void SetSurface(int index, CellSurface surface)
        {
            if (_kept[index] || _cells[index] != CellType.Floor) return;
            _surfaces[index] = surface;
        }

        /// <summary>Copies a cell of the existing level (kept cells start this way).</summary>
        internal void Load(int index, CellType type, CellSurface surface, ObstacleDefinition obstacle)
        {
            _cells[index] = type;
            _surfaces[index] = surface;
            _obstacles[index] = obstacle;
        }

        internal void SetStart(GridPosition start)
        {
            Start = new GridPosition(start.X, start.Y);
            HasStart = true;
        }

        /// <summary>Marks a disc of cells open (clipped to the level).</summary>
        internal void OpenDisc(GridPosition center, float radius)
        {
            var r = (int)Math.Ceiling(radius);
            for (var y = center.Y - r; y <= center.Y + r; y++)
            for (var x = center.X - r; x <= center.X + r; x++)
            {
                var dx = x - center.X;
                var dy = y - center.Y;
                if (InBounds(x, y) && dx * dx + dy * dy <= radius * radius)
                    MarkOpen(Index(x, y));
            }
        }

        // ------------------------------------------------------------ Connectivity

        /// <summary>Walkable cells reachable from the start (4 directions).</summary>
        public bool[] Reachable()
        {
            var reached = new bool[CellCount];
            if (!HasStart || !InBounds(Start.X, Start.Y)) return reached;
            var queue = new Queue<int>();
            var start = Index(Start);
            if (!IsWalkable(start)) return reached;
            reached[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                var x = cell % Width;
                var y = cell / Width;
                for (var d = 0; d < 4; d++)
                {
                    var nx = x + Dx[d];
                    var ny = y + Dy[d];
                    if (!InBounds(nx, ny)) continue;
                    var next = Index(nx, ny);
                    if (reached[next] || !IsWalkable(next)) continue;
                    reached[next] = true;
                    queue.Enqueue(next);
                }
            }

            return reached;
        }

        /// <summary>
        /// Every walkable cell becomes reachable from the start: obstacles cutting a pocket off are removed along the
        /// shortest way through them; pockets closed by walls alone become walls. Kept cells are not changed.
        /// </summary>
        internal void Connect()
        {
            if (!HasStart) return;
            for (var pass = 0; pass < 256; pass++)
            {
                var reached = Reachable();
                if (!OpenWayToPocket(reached))
                    break;
            }

            var final = Reachable();
            for (var i = 0; i < CellCount; i++)
                if (!final[i] && IsWalkable(i) && !_kept[i])
                    SetWall(i);
        }

        /// <summary>0-1 search from the reached cells through obstacles to the nearest unreached walkable cell.</summary>
        private bool OpenWayToPocket(bool[] reached)
        {
            var cost = new int[CellCount];
            var from = new int[CellCount];
            for (var i = 0; i < CellCount; i++)
            {
                cost[i] = int.MaxValue;
                from[i] = -1;
            }

            var deque = new LinkedList<int>();
            var any = false;
            for (var i = 0; i < CellCount; i++)
            {
                if (reached[i])
                {
                    cost[i] = 0;
                    deque.AddLast(i);
                }
                else if (IsWalkable(i)) any = true;
            }

            if (!any) return false;

            while (deque.Count > 0)
            {
                var cell = deque.First.Value;
                deque.RemoveFirst();
                if (!reached[cell] && IsWalkable(cell))
                {
                    var changed = false;
                    for (var c = cell; c >= 0 && !reached[c]; c = from[c])
                        if (_obstacles[c] != null && _obstacles[c].BlocksMovement && !_kept[c])
                        {
                            _obstacles[c] = null;
                            changed = true;
                        }
                    return changed;
                }

                var x = cell % Width;
                var y = cell / Width;
                for (var d = 0; d < 4; d++)
                {
                    var nx = x + Dx[d];
                    var ny = y + Dy[d];
                    if (!InBounds(nx, ny)) continue;
                    var next = Index(nx, ny);
                    int step;
                    if (IsWalkable(next)) step = 0;
                    else if (_cells[next] == CellType.Floor && !_kept[next]) step = 1; // a removable obstacle
                    else continue;
                    if (cost[cell] + step >= cost[next]) continue;
                    cost[next] = cost[cell] + step;
                    from[next] = cell;
                    if (step == 0) deque.AddFirst(next);
                    else deque.AddLast(next);
                }
            }

            return false;
        }

        internal static readonly int[] Dx = { 0, 1, 0, -1 };
        internal static readonly int[] Dy = { 1, 0, -1, 0 };
    }

    /// <summary>An open round patch made by the clearings step.</summary>
    public readonly struct Clearing
    {
        public Clearing(GridPosition center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public GridPosition Center { get; }
        public float Radius { get; }
    }

    /// <summary>A level object placed by a step (a barrel, a chest) and its kind.</summary>
    public readonly struct LayoutObject<TKind>
    {
        public LayoutObject(GridPosition cell, TKind kind)
        {
            Cell = cell;
            Kind = kind;
        }

        public GridPosition Cell { get; }
        public TKind Kind { get; }
    }
}
