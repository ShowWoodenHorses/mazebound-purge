using System.Collections.Generic;
using System.Linq;
using System.Text;
using Maze.Core.Authoring;
using Maze.Core.Common;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Editor.LevelDesigner;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Temporary content helper (not a tool): builds catalog levels from a spec through the regular authoring API.
    /// Call <see cref="BuildAll"/> or <see cref="Build"/> via reflection (MCP execute_code). Delete when no longer needed.
    /// </summary>
    internal static class TempLevelBuilder
    {
        private const string LevelFolder = "Assets/_Project/Data/Levels";

        internal sealed class Spec
        {
            public string Name;
            public int Width, Height, MazeSeed, VisualSeed;
            public float LoopDensity = 0.12f;
            public int Zombies, Hunters, Listeners;
            public int LockedDoors;
            public int Medkits = 1;
            public int FragmentCols = 2, FragmentRows = 2;
            public string[] Weapons = { "Crowbar", "SubMachineGun", "HuntingRifle" };
        }

        public static string BuildAll(bool sync)
        {
            var specs = new[]
            {
                new Spec { Name = "Level_07", Width = 23, Height = 25, LoopDensity = 0.1f, MazeSeed = 70707, VisualSeed = 7071, Zombies = 10, Hunters = 3, Listeners = 2, LockedDoors = 2,
                    Weapons = new[] { "Bat", "SubMachineGun", "HuntingRifle" } },
                new Spec { Name = "Level_08", Width = 25, Height = 27, LoopDensity = 0.08f, MazeSeed = 80808, VisualSeed = 8081, Zombies = 12, Hunters = 4, Listeners = 2, LockedDoors = 3, Medkits = 2,
                    Weapons = new[] { "Crowbar", "SubMachineGun", "AssaultRifle01" } },
                new Spec { Name = "Level_09", Width = 27, Height = 29, LoopDensity = 0.07f, MazeSeed = 90909, VisualSeed = 9091, Zombies = 14, Hunters = 5, Listeners = 3, LockedDoors = 4, Medkits = 2,
                    FragmentCols = 3, Weapons = new[] { "Bat", "SubMachineGun", "AssaultRifle03", "SniperRifle" } },
                new Spec { Name = "Level_10", Width = 29, Height = 31, LoopDensity = 0.06f, MazeSeed = 101010, VisualSeed = 10101, Zombies = 16, Hunters = 6, Listeners = 3, LockedDoors = 4, Medkits = 2,
                    FragmentCols = 3, Weapons = new[] { "FireAxe", "SubMachineGun", "AssaultRifle01", "RevolverRifle" } },
            };

            var log = new StringBuilder();
            foreach (var spec in specs)
                log.AppendLine(Build(spec, sync));
            return log.ToString();
        }

        public static string Build(Spec spec, bool sync)
        {
            var log = new StringBuilder($"=== {spec.Name}\n");
            var path = $"{LevelFolder}/{spec.Name}.asset";
            var level = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (level == null)
            {
                level = ScriptableObject.CreateInstance<LevelData>();
                AssetDatabase.CreateAsset(level, path);
            }

            level.VisualTheme = AssetDatabase.LoadAssetAtPath<VisualTheme>("Assets/_Project/Data/Themes/CastleBlockTheme.asset");
            var g = level.Generation;
            g.Width = spec.Width;
            g.Height = spec.Height;
            g.VisualSeed = spec.VisualSeed;
            g.MazeAlgorithm = MazeAlgorithm.RecursiveBacktracker;
            g.LoopDensity = spec.LoopDensity;
            g.InitialPlayerStartCount = 1;
            g.InitialExitCount = 1;
            g.LightDensity = 0.5f;
            g.DecorDensity = 0.15f;
            g.MaxAutoDecorHeight = 0.3f;

            // Seeds are tried in turn until the doors and keys make a real progression.
            // The plan with the most doors on the way to the exit wins (all of them there — stop early).
            var bestSeed = 0;
            var bestMain = 0;
            DecorHeights.Refresh(level.VisualTheme);
            for (var attempt = 0; attempt < 300 && bestMain < spec.LockedDoors; attempt++)
            {
                g.MazeSeed = spec.MazeSeed + attempt * 131;
                LevelAuthoring.GenerateNew(level);
                var candidate = MakePlan(level, spec);
                if (candidate != null && candidate.MainDoors.Count > bestMain)
                {
                    bestMain = candidate.MainDoors.Count;
                    bestSeed = g.MazeSeed;
                }
            }

            if (bestMain == 0)
                return log.AppendLine("no maze seed gave the wanted doors").ToString();

            g.MazeSeed = bestSeed;
            LevelAuthoring.GenerateNew(level);
            var plan = MakePlan(level, spec);

            log.AppendLine($"maze seed {g.MazeSeed}");
            var geo = level.Geometry;
            var start = level.PlayerStarts[0].Position;
            var exit = level.Exits[0].Position;
            var random = new DeterministicRandom(g.MazeSeed ^ 0x5eed);
            var used = new HashSet<GridPosition> { start, exit };
            var openDist = Bfs(geo, start, null);
            var route = plan.Route;
            var mainDoors = plan.MainDoors;
            var branchDoors = plan.BranchDoors;
            var branchRegions = plan.BranchRegions;
            var segments = plan.Segments;
            var blockedAll = new HashSet<GridPosition>(mainDoors.Concat(branchDoors));

            foreach (var d in blockedAll)
            {
                LevelEditing.SetCellType(level, d, CellType.Door);
                used.Add(d);
            }

            var deadEnds = Floors(geo).Where(p => OpenNeighbours(geo, p) == 1).ToList();

            // --- Keys: main door i — in segment i, far from where the segment begins; branch doors — in their door's segment.
            var doors = level.Doors.ToDictionary(d => d.Position);
            for (var i = 0; i < mainDoors.Count; i++)
            {
                LevelEditing.AddKey(level, plan.MainKeys[i], doors[mainDoors[i]]);
                used.Add(plan.MainKeys[i]);
                log.AppendLine($"main door {mainDoors[i]} (route {route.IndexOf(mainDoors[i])}/{route.Count}) key {plan.MainKeys[i]}, segment {segments[i].Count} cells");
            }

            for (var b = 0; b < branchDoors.Count; b++)
            {
                LevelEditing.AddKey(level, plan.BranchKeys[b], doors[branchDoors[b]]);
                used.Add(plan.BranchKeys[b]);
                log.AppendLine($"branch door {branchDoors[b]} pocket {branchRegions[b].Count} key {plan.BranchKeys[b]}");
            }

            // --- Weapons: first one (melee) near the start, before any door; the rest spread along the progression.
            for (var w = 0; w < spec.Weapons.Length; w++)
            {
                var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>($"Assets/_Project/Data/Weapons/{spec.Weapons[w]}.asset");
                GridPosition cell;
                if (w == 0)
                    cell = PickByDistance(geo, deadEnds, used, openDist, segments[0], 3, 9);
                else
                {
                    // Last weapon goes into a branch pocket if there is one (a reward for the extra key).
                    var pocket = w == spec.Weapons.Length - 1 && branchRegions.Count > 0 ? branchRegions[0] : null;
                    var lo = route.Count * w / (spec.Weapons.Length + 1);
                    cell = pocket != null
                        ? FarCell(geo, pocket, deadEnds, used, branchDoors[0])
                        : PickByDistance(geo, deadEnds, used, openDist, null, lo, lo + 10);
                }
                LevelEditing.AddWeapon(level, cell, def);
                used.Add(cell);
                log.AppendLine($"weapon {spec.Weapons[w]} {cell} dist {openDist[cell]}");
            }

            for (var m = 0; m < spec.Medkits; m++)
            {
                var lo = route.Count * (m + 1) / (spec.Medkits + 1) - 4;
                var cell = PickByDistance(geo, deadEnds, used, openDist, null, lo, lo + 10);
                LevelEditing.AddMedkit(level, cell);
                used.Add(cell);
                log.AppendLine($"medkit {cell} dist {openDist[cell]}");
            }

            // --- Map fragments: a grid of regions, each fragment in its region's farthest free dead end
            // (second branch pocket, if any, gets the fragment of its region).
            for (var cy = 0; cy < spec.FragmentRows; cy++)
            for (var cx = 0; cx < spec.FragmentCols; cx++)
            {
                var x0 = geo.Width * cx / spec.FragmentCols;
                var x1 = geo.Width * (cx + 1) / spec.FragmentCols;
                var y0 = geo.Height * cy / spec.FragmentRows;
                var y1 = geo.Height * (cy + 1) / spec.FragmentRows;
                var region = new GridRect(x0, y0, x1 - x0, y1 - y0);
                var free = Floors(geo).Where(p => !used.Contains(p) && Inside(region, p)).ToList();
                var pocketCells = branchRegions.Skip(1).SelectMany(r => r).Where(free.Contains).ToList();
                var pool = pocketCells.Count > 0 ? pocketCells : free.Where(deadEnds.Contains).ToList();
                if (pool.Count == 0) pool = free;
                var cell = pool.OrderByDescending(p => openDist[p]).First();
                LevelEditing.AddMapFragment(level, cell, region);
                used.Add(cell);
                log.AppendLine($"fragment {cell} region {x0},{y0} {region.Width}x{region.Height}");
            }

            // --- Zombies: all patrol; spread out, never near the start; routes stay on their side of the locked doors.
            var types = new List<ZombieDefinition>();
            var walker = LoadZombie("ZombieWalker");
            var hunter = LoadZombie("ZombieHunter");
            var listener = LoadZombie("ZombieListener");
            for (var z = 0; z < spec.Zombies; z++)
                types.Add(z < spec.Hunters ? hunter : z < spec.Hunters + spec.Listeners ? listener : walker);
            types = types.OrderBy(_ => random.NextInt(1000)).ToList();

            var zombies = new List<GridPosition>();
            foreach (var type in types)
            {
                var candidates = Floors(geo).Where(p => !used.Contains(p) && openDist[p] >= 9 &&
                    Mathf.Max(Mathf.Abs(p.X - start.X), Mathf.Abs(p.Y - start.Y)) > 6).ToList();
                var spawn = candidates
                    .OrderByDescending(p => (zombies.Count == 0 ? 0 : zombies.Min(z => z.ManhattanDistance(p))) * 100 + random.NextInt(100))
                    .First();

                var local = Bfs(geo, spawn, blockedAll);
                var far = local.Where(kv => kv.Value >= 5 && kv.Value <= 9 && openDist[kv.Key] >= 8).Select(kv => kv.Key).ToList();
                var target = far.Count > 0 ? far[random.NextInt(far.Count)] : local.OrderByDescending(kv => kv.Value).First().Key;
                var walk = PathTo(local, spawn, target);

                var zombie = LevelEditing.AddZombie(level, spawn, type, DirectionTo(spawn, walk.Count > 1 ? walk[1] : spawn));
                LevelEditing.AddPatrolPoint(level, zombie, spawn);
                var mid = walk[walk.Count / 2];
                if (walk.Count >= 7 && mid != spawn && mid != target)
                    LevelEditing.AddPatrolPoint(level, zombie, mid);
                LevelEditing.AddPatrolPoint(level, zombie, target);
                zombies.Add(spawn);
                used.Add(spawn);
            }

            DecorHeights.Refresh(level.VisualTheme);
            LevelAuthoring.RegenerateVisuals(level, clearOverrides: false);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();

            var report = EditorLevelValidator.Validate(level);
            log.AppendLine($"size {spec.Width}x{spec.Height}; start {start} exit {exit} route {route.Count}; doors {mainDoors.Count}+{branchDoors.Count}; " +
                           $"zombies {level.ZombieSpawns.Count}; lights {level.Lights.Count}; errors {report.ErrorCount}, warnings {report.WarningCount}");
            foreach (var issue in report.Issues.Where(i => i.Code != "WallCategoryWithoutVariants"))
                log.AppendLine(issue.ToString());
            log.Append(Ascii(level));

            if (sync && report.IsValid)
                LevelSync.Sync(level);
            return log.ToString();
        }

        private sealed class Plan
        {
            public List<GridPosition> Route;
            public readonly List<GridPosition> MainDoors = new List<GridPosition>();
            public readonly List<GridPosition> MainKeys = new List<GridPosition>();
            public readonly List<GridPosition> BranchDoors = new List<GridPosition>();
            public readonly List<HashSet<GridPosition>> BranchRegions = new List<HashSet<GridPosition>>();
            public readonly List<GridPosition> BranchKeys = new List<GridPosition>();
            public readonly List<HashSet<GridPosition>> Segments = new List<HashSet<GridPosition>>();
        }

        private const int MinKeyWalk = 8;

        /// <summary>
        /// Locked doors for a freshly generated maze, null if it cannot hold them well: main doors are cuts of the
        /// start -> exit route (8+ steps apart, not in its first fifth); each key lies 8+ steps inside the part its
        /// previous door opens. Missing doors close side pockets (6..40 cells, not nested, no exit) with loot.
        /// </summary>
        private static Plan MakePlan(LevelData level, Spec spec)
        {
            var geo = level.Geometry;
            var start = level.PlayerStarts[0].Position;
            var exit = level.Exits[0].Position;
            var openDist = Bfs(geo, start, null);
            var plan = new Plan { Route = PathTo(openDist, start, exit) };
            var route = plan.Route;
            var random = new DeterministicRandom(level.Generation.MazeSeed ^ 0x0d00);

            var cuts = new List<int>();
            for (var i = route.Count / 5; i < route.Count - 3; i++)
                if (IsCorridor(geo, route[i]) && !Bfs(geo, start, new HashSet<GridPosition> { route[i] }).ContainsKey(exit))
                    cuts.Add(i);

            var mainIdx = new List<int>();
            for (var d = 0; d < spec.LockedDoors; d++)
            {
                var ideal = route.Count * (d + 1) / (spec.LockedDoors + 1);
                var pick = cuts.Where(c => mainIdx.All(m => Mathf.Abs(m - c) >= 8))
                    .OrderBy(c => Mathf.Abs(c - ideal)).Cast<int?>().FirstOrDefault();
                if (pick == null) break;
                mainIdx.Add(pick.Value);
            }
            mainIdx.Sort();
            plan.MainDoors.AddRange(mainIdx.Select(i => route[i]));
            var blockedAll = new HashSet<GridPosition>(plan.MainDoors);

            // Side pockets for the doors the route could not take.
            var corridors = Floors(geo).Where(p => IsCorridor(geo, p) && !route.Contains(p) && openDist[p] >= 6)
                .OrderBy(_ => random.NextInt(1000)).ToList();
            foreach (var c in corridors)
            {
                if (plan.MainDoors.Count + plan.BranchDoors.Count >= spec.LockedDoors) break;
                if (blockedAll.Any(b => b.ManhattanDistance(c) < 4)) continue;
                var blocked = new HashSet<GridPosition>(blockedAll) { c };
                var reach = Bfs(geo, start, blocked);
                var inside = Neighbours(geo, c).Where(n => !reach.ContainsKey(n) && !blocked.Contains(n)).ToList();
                if (inside.Count == 0) continue; // Not a cut: the corridor closes nothing.
                var behind = Bfs(geo, inside[0], blocked);
                if (behind.Count < 6 || behind.Count > 40 || behind.ContainsKey(exit) ||
                    blockedAll.Any(b => Neighbours(geo, b).Any(behind.ContainsKey)) ||
                    plan.BranchRegions.Any(r => r.Contains(c)))
                    continue; // No nested pockets: every door opens from the main progression.
                plan.BranchDoors.Add(c);
                plan.BranchRegions.Add(behind.Keys.ToHashSet());
                blockedAll.Add(c);
            }

            if (plan.MainDoors.Count == 0 || plan.MainDoors.Count + plan.BranchDoors.Count < spec.LockedDoors)
                return null;

            // Progression segments: segment i = what opening main door i-1 adds (segment 0 = around the start).
            var seen = new HashSet<GridPosition>();
            for (var i = 0; i <= plan.MainDoors.Count; i++)
            {
                var blocked = new HashSet<GridPosition>(plan.MainDoors.Skip(i).Concat(plan.BranchDoors));
                var reach = Bfs(geo, start, blocked).Keys.Where(p => !seen.Contains(p)).ToHashSet();
                plan.Segments.Add(reach);
                seen.UnionWith(reach);
            }

            var deadEnds = Floors(geo).Where(p => OpenNeighbours(geo, p) == 1).ToList();
            var used = new HashSet<GridPosition>(blockedAll) { start, exit };
            var offRoute = MultiBfs(geo, route);
            for (var i = 0; i < plan.MainDoors.Count; i++)
            {
                if (i > 0 && plan.Segments[i].Count < 20)
                    return null;
                var entry = i == 0 ? start : plan.MainDoors[i - 1];
                var fromEntry = Bfs(geo, entry, null);
                var fromDoor = Bfs(geo, plan.MainDoors[i], null);
                var key = deadEnds.Where(p => plan.Segments[i].Contains(p) && !used.Contains(p) && offRoute[p] >= 3 &&
                                              Mathf.Min(fromEntry[p], fromDoor[p]) >= MinKeyWalk)
                    .OrderByDescending(p => Mathf.Min(fromEntry[p], fromDoor[p]) + offRoute[p])
                    .Cast<GridPosition?>().FirstOrDefault();
                if (key == null)
                    return null;
                plan.MainKeys.Add(key.Value);
                used.Add(key.Value);
            }

            foreach (var door in plan.BranchDoors)
            {
                var segment = plan.Segments.FindIndex(seg => Neighbours(geo, door).Any(seg.Contains));
                var key = FarCell(geo, plan.Segments[segment], deadEnds, used, door);
                if (Bfs(geo, door, null)[key] < MinKeyWalk)
                    return null;
                plan.BranchKeys.Add(key);
                used.Add(key);
            }

            return plan;
        }

        private static ZombieDefinition LoadZombie(string name) =>
            AssetDatabase.LoadAssetAtPath<ZombieDefinition>($"Assets/_Project/Data/Zombies/{name}.asset");

        /// <summary>Free dead end of the area farthest (walking) from <paramref name="from"/>; any free cell if no dead end.</summary>
        private static GridPosition FarCell(LevelGeometry geo, ICollection<GridPosition> area, List<GridPosition> deadEnds,
            HashSet<GridPosition> used, GridPosition from)
        {
            var dist = Bfs(geo, from, null);
            var pool = deadEnds.Where(p => area.Contains(p) && !used.Contains(p)).ToList();
            if (pool.Count == 0) pool = area.Where(p => !used.Contains(p) && geo.GetCell(p) == CellType.Floor).ToList();
            return pool.OrderByDescending(p => dist[p]).First();
        }

        private static GridPosition PickByDistance(LevelGeometry geo, List<GridPosition> deadEnds, HashSet<GridPosition> used,
            Dictionary<GridPosition, int> dist, ICollection<GridPosition> area, int min, int max)
        {
            bool Ok(GridPosition p) => !used.Contains(p) && (area == null || area.Contains(p));
            var cell = deadEnds.Where(p => Ok(p) && dist[p] >= min && dist[p] <= max).OrderBy(p => dist[p]).Cast<GridPosition?>().FirstOrDefault()
                       ?? Floors(geo).Where(p => Ok(p) && dist[p] >= min).OrderBy(p => dist[p]).Cast<GridPosition?>().FirstOrDefault()
                       ?? Floors(geo).Where(Ok).OrderByDescending(p => dist[p]).First();
            return cell;
        }

        private static string Ascii(LevelData level)
        {
            var geo = level.Geometry;
            var sb = new StringBuilder();
            for (var y = geo.Height - 1; y >= 0; y--)
            {
                for (var x = 0; x < geo.Width; x++)
                {
                    var p = new GridPosition(x, y);
                    var c = geo.GetCell(p) == CellType.Wall ? '#' : geo.GetCell(p) == CellType.Door ? 'D' : '.';
                    if (level.PlayerStarts.Any(e => e.Position == p)) c = 'S';
                    else if (level.Exits.Any(e => e.Position == p)) c = 'E';
                    else if (level.Keys.Any(e => e.Position == p)) c = 'K';
                    else if (level.Weapons.Any(e => e.Position == p)) c = 'W';
                    else if (level.Medkits.Any(e => e.Position == p)) c = '+';
                    else if (level.MapFragments.Any(e => e.Position == p)) c = 'F';
                    else if (level.ZombieSpawns.Any(e => e.Position == p)) c = 'Z';
                    sb.Append(c);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static bool Inside(GridRect r, GridPosition p) =>
            p.X >= r.X && p.X < r.X + r.Width && p.Y >= r.Y && p.Y < r.Y + r.Height;

        private static IEnumerable<GridPosition> Floors(LevelGeometry geo)
        {
            for (var i = 0; i < geo.CellCount; i++)
            {
                var p = geo.ToPosition(i);
                if (geo.GetCell(p) == CellType.Floor)
                    yield return p;
            }
        }

        private static readonly Direction[] Dirs = { Direction.North, Direction.East, Direction.South, Direction.West };

        private static bool Open(LevelGeometry geo, GridPosition p) => geo.IsInside(p) && geo.GetCell(p) != CellType.Wall;

        private static IEnumerable<GridPosition> Neighbours(LevelGeometry geo, GridPosition p) =>
            Dirs.Select(d => p.Neighbour(d)).Where(n => Open(geo, n));

        private static int OpenNeighbours(LevelGeometry geo, GridPosition p) => Neighbours(geo, p).Count();

        private static bool IsCorridor(LevelGeometry geo, GridPosition p)
        {
            var n = Open(geo, p.Neighbour(Direction.North));
            var s = Open(geo, p.Neighbour(Direction.South));
            var e = Open(geo, p.Neighbour(Direction.East));
            var w = Open(geo, p.Neighbour(Direction.West));
            return (n && s && !e && !w) || (e && w && !n && !s);
        }

        private static Dictionary<GridPosition, int> Bfs(LevelGeometry geo, GridPosition from, HashSet<GridPosition> blocked)
        {
            var dist = new Dictionary<GridPosition, int> { [from] = 0 };
            var queue = new Queue<GridPosition>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var n in Neighbours(geo, p))
                {
                    if ((blocked != null && blocked.Contains(n)) || dist.ContainsKey(n))
                        continue;
                    dist[n] = dist[p] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        private static Dictionary<GridPosition, int> MultiBfs(LevelGeometry geo, IEnumerable<GridPosition> sources)
        {
            var dist = new Dictionary<GridPosition, int>();
            var queue = new Queue<GridPosition>();
            foreach (var source in sources)
            {
                dist[source] = 0;
                queue.Enqueue(source);
            }
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var n in Neighbours(geo, p))
                {
                    if (dist.ContainsKey(n)) continue;
                    dist[n] = dist[p] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        private static List<GridPosition> PathTo(Dictionary<GridPosition, int> dist, GridPosition from, GridPosition to)
        {
            var path = new List<GridPosition> { to };
            var p = to;
            while (p != from)
            {
                var current = p;
                p = Dirs.Select(d => current.Neighbour(d)).First(n => dist.TryGetValue(n, out var v) && v == dist[current] - 1);
                path.Add(p);
            }
            path.Reverse();
            return path;
        }

        private static Direction DirectionTo(GridPosition from, GridPosition to)
        {
            if (to.Y > from.Y) return Direction.North;
            if (to.X > from.X) return Direction.East;
            if (to.Y < from.Y) return Direction.South;
            return to.X < from.X ? Direction.West : Direction.South;
        }
    }
}
