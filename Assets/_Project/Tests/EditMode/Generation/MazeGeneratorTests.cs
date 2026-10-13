using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Maze.Core.Common;
using Maze.Core.Generation;
using Maze.Core.Grid;
using Maze.Core.Level;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Generation
{
    public class MazeGeneratorTests
    {
        private readonly MazeGenerator _generator = new MazeGenerator();

        private static LevelGenerationSettings Settings(int width = 21, int height = 21, int seed = 1,
            float loopDensity = 0f, int starts = 1, int exits = 1)
        {
            return new LevelGenerationSettings
            {
                Width = width,
                Height = height,
                MazeSeed = seed,
                LoopDensity = loopDensity,
                InitialPlayerStartCount = starts,
                InitialExitCount = exits,
            };
        }

        [Test]
        public void SameSettings_ProduceIdenticalResult()
        {
            var a = _generator.Generate(Settings(seed: 123, loopDensity: 0.2f, starts: 2, exits: 2));
            var b = _generator.Generate(Settings(seed: 123, loopDensity: 0.2f, starts: 2, exits: 2));

            CollectionAssert.AreEqual(a.Geometry.CopyCells(), b.Geometry.CopyCells());
            CollectionAssert.AreEqual(a.PlayerStarts, b.PlayerStarts);
            CollectionAssert.AreEqual(a.Exits, b.Exits);
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentMazes()
        {
            var a = _generator.Generate(Settings(seed: 1));
            var b = _generator.Generate(Settings(seed: 2));
            CollectionAssert.AreNotEqual(a.Geometry.CopyCells(), b.Geometry.CopyCells());
        }

        [Test]
        public void ZeroLoopDensity_ProducesPerfectMaze()
        {
            var geometry = _generator.Generate(Settings(width: 31, height: 25)).Geometry;
            var floorCount = geometry.CopyCells().Count(c => c == CellType.Floor);

            Assert.AreEqual(floorCount - 1, CountFloorAdjacencies(geometry), "A perfect maze is a tree: edges = nodes - 1.");
        }

        [Test]
        public void LoopDensity_AddsCycles_ProportionallyToDensity()
        {
            var tree = CountFloorAdjacencies(_generator.Generate(Settings(seed: 5)).Geometry);
            var some = CountFloorAdjacencies(_generator.Generate(Settings(seed: 5, loopDensity: 0.3f)).Geometry);
            var all = CountFloorAdjacencies(_generator.Generate(Settings(seed: 5, loopDensity: 1f)).Geometry);

            Assert.Greater(some, tree);
            Assert.Greater(all, some);
        }

        [Test]
        public void FullLoopDensity_OpensEveryWallBetweenRooms_UpToTheBorder()
        {
            // The whole interior (up to Width-2 / Height-2) is used: no redundant wall row or column.
            var geometry = _generator.Generate(Settings(width: 13, height: 11, loopDensity: 1f)).Geometry;
            for (var y = 1; y <= geometry.Height - 2; y++)
            for (var x = 1; x <= geometry.Width - 2; x++)
            {
                var expected = x % 2 == 0 && y % 2 == 0 ? CellType.Wall : CellType.Floor;
                Assert.AreEqual(expected, geometry.GetCell(new GridPosition(x, y)), $"Cell ({x}, {y})");
            }
        }

        [Test]
        public void SingleStartAndExit_ExitIsFarthestReachableCandidate()
        {
            var result = _generator.Generate(Settings(width: 25, height: 25, seed: 9));
            var grid = new LevelGrid(result.Geometry);
            var distances = BfsDistances(grid, result.PlayerStarts[0]);
            var exitDistance = distances[grid.ToIndex(result.Exits[0])];

            var maxRoomDistance = 0;
            for (var y = 1; y < grid.Height - 1; y += 2)
            for (var x = 1; x < grid.Width - 1; x += 2)
                maxRoomDistance = Math.Max(maxRoomDistance, distances[grid.ToIndex(new GridPosition(x, y))]);

            Assert.AreEqual(maxRoomDistance, exitDistance);
        }

        [TestCase(20, 21)]
        [TestCase(21, 20)]
        public void EvenSize_IsRejected(int width, int height)
        {
            Assert.Throws<ArgumentException>(() => _generator.Generate(Settings(width, height)));
        }

        [Test]
        public void TooSmallSize_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => _generator.Generate(Settings(3, 21)));
        }

        [Test]
        public void TooManyStartsAndExits_AreRejected()
        {
            // 5x5 has 2x2 = 4 rooms.
            Assert.DoesNotThrow(() => _generator.Generate(Settings(5, 5, starts: 2, exits: 2)));
            Assert.Throws<ArgumentException>(() => _generator.Generate(Settings(5, 5, starts: 3, exits: 2)));
        }

        [Test]
        public void ApplyGeneratedMaze_ReplacesDesignAndAssignsIds()
        {
            var level = UnityEngine.ScriptableObject.CreateInstance<LevelData>();
            try
            {
                level.MutableKeys.Add(new KeyData("key_1", new GridPosition(1, 1)));
                var result = _generator.Generate(Settings(starts: 2, exits: 1));

                level.ApplyGeneratedMaze(result);

                Assert.AreSame(result.Geometry, level.Geometry);
                Assert.IsEmpty(level.Keys);
                CollectionAssert.AreEqual(new[] { "start_1", "start_2" }, level.PlayerStarts.Select(s => s.Id));
                CollectionAssert.AreEqual(result.PlayerStarts, level.PlayerStarts.Select(s => s.Position));
                Assert.AreEqual("exit_1", level.Exits[0].Id);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        /// <summary>ТЗ §111: mass generation over 1000 seeds with error statistics.</summary>
        [Test]
        public void MassGeneration_1000Seeds_AllInvariantsHold()
        {
            var sizes = new DeterministicRandom(2024);
            var failures = new Dictionary<string, int>();
            var examples = new StringBuilder();

            for (var seed = 0; seed < 1000; seed++)
            {
                var settings = Settings(
                    width: sizes.NextInt(2, 26) * 2 + 1,
                    height: sizes.NextInt(2, 26) * 2 + 1,
                    seed: seed,
                    loopDensity: (float)sizes.NextDouble() * 0.5f,
                    starts: sizes.NextInt(1, 3),
                    exits: sizes.NextInt(1, 3));

                foreach (var error in CheckInvariants(_generator.Generate(settings)))
                {
                    failures.TryGetValue(error, out var count);
                    failures[error] = count + 1;
                    if (count == 0)
                        examples.AppendLine($"{error}: seed {seed}, {settings.Width}x{settings.Height}");
                }
            }

            Assert.IsEmpty(failures, "Invariant failures:\n" +
                string.Join("\n", failures.Select(f => $"{f.Key}: {f.Value}")) + "\nFirst examples:\n" + examples);
        }

        private static IEnumerable<string> CheckInvariants(MazeGenerationResult result)
        {
            var grid = new LevelGrid(result.Geometry);

            for (var x = 0; x < grid.Width; x++)
                if (grid.GetCell(new GridPosition(x, 0)) != CellType.Wall ||
                    grid.GetCell(new GridPosition(x, grid.Height - 1)) != CellType.Wall)
                {
                    yield return "Border is open";
                    break;
                }

            for (var y = 0; y < grid.Height; y++)
                if (grid.GetCell(new GridPosition(0, y)) != CellType.Wall ||
                    grid.GetCell(new GridPosition(grid.Width - 1, y)) != CellType.Wall)
                {
                    yield return "Border is open";
                    break;
                }

            // Every room cell right up to the border is used: no redundant double-wall row or column.
            var unusedRoom = false;
            for (var y = 1; y <= grid.Height - 2; y += 2)
            for (var x = 1; x <= grid.Width - 2; x += 2)
                unusedRoom |= grid.GetCell(new GridPosition(x, y)) != CellType.Floor;
            if (unusedRoom)
                yield return "Unused room cell (double wall at the edge)";

            for (var y = 0; y < grid.Height - 1; y++)
            for (var x = 0; x < grid.Width - 1; x++)
            {
                var p = new GridPosition(x, y);
                if (grid.GetCell(p) == CellType.Floor &&
                    grid.GetCell(p.Neighbour(Direction.East)) == CellType.Floor &&
                    grid.GetCell(p.Neighbour(Direction.North)) == CellType.Floor &&
                    grid.GetCell(new GridPosition(x + 1, y + 1)) == CellType.Floor)
                    yield return "Passage wider than 1 cell";
            }

            var spawns = result.PlayerStarts.Concat(result.Exits).ToList();
            if (spawns.Distinct().Count() != spawns.Count)
                yield return "Start/exit share a cell";

            if (spawns.Any(p => !grid.IsInside(p) || grid.GetCell(p) != CellType.Floor))
                yield return "Start/exit not on floor";

            var distances = BfsDistances(grid, result.PlayerStarts[0]);
            for (var i = 0; i < grid.CellCount; i++)
                if (grid.GetCell(grid.ToPosition(i)) == CellType.Floor && distances[i] < 0)
                {
                    yield return "Floor not fully connected";
                    break;
                }
        }

        private static int CountFloorAdjacencies(LevelGeometry geometry)
        {
            var edges = 0;
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var p = geometry.ToPosition(i);
                if (geometry.GetCell(p) != CellType.Floor)
                    continue;

                var east = p.Neighbour(Direction.East);
                var north = p.Neighbour(Direction.North);
                if (geometry.IsInside(east) && geometry.GetCell(east) == CellType.Floor) edges++;
                if (geometry.IsInside(north) && geometry.GetCell(north) == CellType.Floor) edges++;
            }

            return edges;
        }

        private static int[] BfsDistances(LevelGrid grid, GridPosition source)
        {
            var distances = Enumerable.Repeat(-1, grid.CellCount).ToArray();
            var queue = new Queue<GridPosition>();
            distances[grid.ToIndex(source)] = 0;
            queue.Enqueue(source);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var direction in DirectionExtensions.All)
                {
                    var next = current.Neighbour(direction);
                    if (grid.GetCellOrWall(next) == CellType.Wall || distances[grid.ToIndex(next)] >= 0)
                        continue;

                    distances[grid.ToIndex(next)] = distances[grid.ToIndex(current)] + 1;
                    queue.Enqueue(next);
                }
            }

            return distances;
        }
    }
}
