using System;
using System.Collections.Generic;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Core.Level;

namespace Maze.Core.Generation
{
    /// <summary>
    /// Generates the logical maze for "Generate New" (ТЗ §12–14).
    ///
    /// Layout: the outer border is always wall. "Rooms" sit at odd coordinates (1, 1), (3, 1), ... up to
    /// (Width - 2, Height - 2); cells between two rooms are carvable walls; cells with both coordinates even are
    /// permanent pillars, so passages are always exactly one cell wide. Width and height are odd, so the layout
    /// fills the grid exactly: no redundant wall rows or columns.
    ///
    /// The result depends only on the settings: same settings, same maze, on every platform.
    /// </summary>
    public sealed class MazeGenerator
    {
        public const int MinSize = 5;

        public MazeGenerationResult Generate(LevelGenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var cols = (settings.Width - 1) / 2;
            var rows = (settings.Height - 1) / 2;
            ValidateSettings(settings, cols * rows);

            var random = new DeterministicRandom(settings.MazeSeed);
            var geometry = new LevelGeometry(settings.Width, settings.Height);

            switch (settings.MazeAlgorithm)
            {
                case MazeAlgorithm.RecursiveBacktracker:
                    CarveRecursiveBacktracker(geometry, cols, rows, random);
                    break;
                default:
                    throw new NotSupportedException($"Maze algorithm {settings.MazeAlgorithm} is not supported.");
            }

            AddLoops(geometry, cols, rows, settings.LoopDensity, random);

            var grid = new LevelGrid(geometry);
            var candidates = SelectSpawnCandidates(grid, cols, rows,
                settings.InitialPlayerStartCount + settings.InitialExitCount);

            var anchors = new List<GridPosition>();
            var starts = PickSpreadOut(grid, candidates, anchors, settings.InitialPlayerStartCount, random);
            var exits = PickSpreadOut(grid, candidates, anchors, settings.InitialExitCount, random);

            return new MazeGenerationResult(geometry, starts, exits);
        }

        private static void ValidateSettings(LevelGenerationSettings settings, int roomCount)
        {
            if (!settings.HasOddSize)
                throw new ArgumentException($"Level size must be odd (e.g. 21x21), got {settings.Width}x{settings.Height}.");

            if (settings.Width < MinSize || settings.Height < MinSize)
                throw new ArgumentException($"Level size must be at least {MinSize}x{MinSize}.");

            if (settings.InitialPlayerStartCount < 1 || settings.InitialExitCount < 1)
                throw new ArgumentException("At least one player start and one exit are required.");

            var required = settings.InitialPlayerStartCount + settings.InitialExitCount;
            if (required > roomCount)
                throw new ArgumentException(
                    $"Level {settings.Width}x{settings.Height} has room for {roomCount} starts/exits, {required} requested.");
        }

        private static GridPosition RoomToCell(int i, int j) => new GridPosition(2 * i + 1, 2 * j + 1);

        private static void CarveRecursiveBacktracker(LevelGeometry geometry, int cols, int rows, DeterministicRandom random)
        {
            var visited = new bool[cols * rows];
            var stack = new List<int>(cols * rows);
            var options = new List<Direction>(4);

            var start = random.NextInt(visited.Length);
            visited[start] = true;
            geometry.SetCell(RoomToCell(start % cols, start / cols), CellType.Floor);
            stack.Add(start);

            while (stack.Count > 0)
            {
                var current = stack[stack.Count - 1];
                var i = current % cols;
                var j = current / cols;

                options.Clear();
                foreach (var direction in DirectionExtensions.All)
                {
                    var offset = direction.ToOffset();
                    var ni = i + offset.X;
                    var nj = j + offset.Y;
                    if (ni >= 0 && ni < cols && nj >= 0 && nj < rows && !visited[nj * cols + ni])
                        options.Add(direction);
                }

                if (options.Count == 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                var step = options[random.NextInt(options.Count)].ToOffset();
                var next = (j + step.Y) * cols + (i + step.X);
                var room = RoomToCell(i, j);

                geometry.SetCell(room + step, CellType.Floor);
                geometry.SetCell(room + step + step, CellType.Floor);
                visited[next] = true;
                stack.Add(next);
            }
        }

        /// <summary>LoopDensity is the fraction of remaining walls between adjacent rooms that get removed.</summary>
        private static void AddLoops(LevelGeometry geometry, int cols, int rows, float loopDensity, DeterministicRandom random)
        {
            var density = Math.Max(0f, Math.Min(1f, loopDensity));
            if (density <= 0f)
                return;

            var walls = new List<GridPosition>();
            for (var j = 0; j < rows; j++)
            for (var i = 0; i < cols; i++)
            {
                var room = RoomToCell(i, j);
                if (i + 1 < cols && geometry.GetCell(room.Neighbour(Direction.East)) == CellType.Wall)
                    walls.Add(room.Neighbour(Direction.East));
                if (j + 1 < rows && geometry.GetCell(room.Neighbour(Direction.North)) == CellType.Wall)
                    walls.Add(room.Neighbour(Direction.North));
            }

            var count = (int)Math.Round(walls.Count * (double)density, MidpointRounding.AwayFromZero);
            for (var k = 0; k < count; k++)
            {
                var pick = k + random.NextInt(walls.Count - k);
                (walls[k], walls[pick]) = (walls[pick], walls[k]);
                geometry.SetCell(walls[k], CellType.Floor);
            }
        }

        /// <summary>Dead ends are preferred for starts and exits; falls back to all rooms if there are too few.</summary>
        private static List<GridPosition> SelectSpawnCandidates(LevelGrid grid, int cols, int rows, int required)
        {
            var rooms = new List<GridPosition>(cols * rows);
            var deadEnds = new List<GridPosition>();

            for (var j = 0; j < rows; j++)
            for (var i = 0; i < cols; i++)
            {
                var room = RoomToCell(i, j);
                rooms.Add(room);

                var openSides = 0;
                foreach (var direction in DirectionExtensions.All)
                    if (grid.GetCellOrWall(room.Neighbour(direction)) != CellType.Wall)
                        openSides++;

                if (openSides == 1)
                    deadEnds.Add(room);
            }

            return deadEnds.Count >= required ? deadEnds : rooms;
        }

        /// <summary>
        /// Farthest-point sampling: each pick maximizes path distance to every anchor chosen so far.
        /// The very first pick (no anchors yet) is random. Picks are appended to <paramref name="anchors"/>.
        /// </summary>
        private static List<GridPosition> PickSpreadOut(LevelGrid grid, List<GridPosition> candidates,
            List<GridPosition> anchors, int count, DeterministicRandom random)
        {
            var picked = new List<GridPosition>(count);
            for (var k = 0; k < count; k++)
            {
                GridPosition best;
                if (anchors.Count == 0)
                {
                    best = candidates[random.NextInt(candidates.Count)];
                }
                else
                {
                    var distances = PathDistances(grid, anchors);
                    best = default;
                    var bestDistance = -1;
                    foreach (var candidate in candidates)
                    {
                        var distance = distances[grid.ToIndex(candidate)];
                        if (distance > bestDistance && !anchors.Contains(candidate))
                        {
                            best = candidate;
                            bestDistance = distance;
                        }
                    }
                }

                picked.Add(best);
                anchors.Add(best);
            }

            return picked;
        }

        /// <summary>Multi-source BFS over non-wall cells. Unreachable cells get -1.</summary>
        private static int[] PathDistances(LevelGrid grid, List<GridPosition> sources)
        {
            var distances = new int[grid.CellCount];
            for (var i = 0; i < distances.Length; i++)
                distances[i] = -1;

            var queue = new Queue<GridPosition>();
            foreach (var source in sources)
            {
                distances[grid.ToIndex(source)] = 0;
                queue.Enqueue(source);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var next = distances[grid.ToIndex(current)] + 1;
                foreach (var direction in DirectionExtensions.All)
                {
                    var neighbour = current.Neighbour(direction);
                    if (grid.GetCellOrWall(neighbour) == CellType.Wall)
                        continue;

                    var index = grid.ToIndex(neighbour);
                    if (distances[index] >= 0)
                        continue;

                    distances[index] = next;
                    queue.Enqueue(neighbour);
                }
            }

            return distances;
        }
    }
}
