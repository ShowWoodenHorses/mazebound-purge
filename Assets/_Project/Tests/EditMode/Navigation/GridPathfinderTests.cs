using System.Collections.Generic;
using System.Linq;
using Maze.Core.Generation;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Navigation;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Navigation
{
    public class GridPathfinderTests
    {
        private static CellMaskPassability Mask(LevelGeometry geometry) =>
            new CellMaskPassability(geometry.Width, geometry.Height,
                Enumerable.Range(0, geometry.CellCount).Select(i => geometry.GetCell(geometry.ToPosition(i)) != CellType.Wall).ToArray());

        private static LevelGeometry Parse(params string[] rowsTopToBottom)
        {
            var height = rowsTopToBottom.Length;
            var geometry = new LevelGeometry(rowsTopToBottom[0].Length, height);
            for (var row = 0; row < height; row++)
            for (var x = 0; x < rowsTopToBottom[row].Length; x++)
                if (rowsTopToBottom[row][x] != '#')
                    geometry.SetCell(new GridPosition(x, height - 1 - row), CellType.Floor);

            return geometry;
        }

        private readonly struct CellCost : IGridCost
        {
            private readonly HashSet<GridPosition> _cells;
            private readonly int _cost;

            public CellCost(int cost, params GridPosition[] cells)
            {
                _cost = cost;
                _cells = new HashSet<GridPosition>(cells);
            }

            public int StepCost(GridPosition to) => _cells.Contains(to) ? _cost : 1;
        }

        [Test]
        public void WeightedCost_GoesAroundExpensiveCell_WhenDetourIsCheaper()
        {
            var geometry = Parse(
                "#######",
                "#.....#",
                "#.....#",
                "#######");
            var path = new List<GridPosition>();
            var pathfinder = new GridPathfinder(7, 4);
            var drift = new GridPosition(3, 1);

            // Straight: 4 steps, one of them into the drift (cost 4) = 7; around: 6 steps = 6.
            Assert.IsTrue(pathfinder.TryFindPath(new GridPosition(1, 1), new GridPosition(5, 1), Mask(geometry),
                new CellCost(4, drift), path));
            CollectionAssert.DoesNotContain(path, drift);
            Assert.AreEqual(7, path.Count);

            // Cost 2: straight = 5 < around = 6 — wades through.
            Assert.IsTrue(pathfinder.TryFindPath(new GridPosition(1, 1), new GridPosition(5, 1), Mask(geometry),
                new CellCost(2, drift), path));
            CollectionAssert.Contains(path, drift);
            Assert.AreEqual(5, path.Count);

            // A drift across the whole corridor: no way around, the path goes through it.
            Assert.IsTrue(pathfinder.TryFindPath(new GridPosition(1, 1), new GridPosition(5, 1), Mask(geometry),
                new CellCost(4, drift, new GridPosition(3, 2)), path));
            Assert.AreEqual(5, path.Count);
        }

        [Test]
        public void OpenGrid_PathLengthIsManhattan()
        {
            var geometry = new LevelGeometry(10, 10, CellType.Floor);
            var path = new List<GridPosition>();

            Assert.IsTrue(new GridPathfinder(10, 10).TryFindPath(new GridPosition(1, 2), new GridPosition(7, 9), Mask(geometry), path));
            Assert.AreEqual(6 + 7 + 1, path.Count);
            Assert.AreEqual(new GridPosition(1, 2), path[0]);
            Assert.AreEqual(new GridPosition(7, 9), path[path.Count - 1]);
        }

        [Test]
        public void Path_GoesAroundWalls_AndIsContiguous()
        {
            var geometry = Parse(
                "#######",
                "#.....#",
                "#.###.#",
                "#.#.#.#",
                "#...#.#",
                "#######");
            var mask = Mask(geometry);
            var path = new List<GridPosition>();

            Assert.IsTrue(new GridPathfinder(7, 6).TryFindPath(new GridPosition(3, 2), new GridPosition(5, 1), mask, path));
            Assert.AreEqual(14, path.Count);
            for (var i = 1; i < path.Count; i++)
            {
                Assert.AreEqual(1, path[i - 1].ManhattanDistance(path[i]));
                Assert.IsTrue(mask.IsPassable(path[i]));
            }
        }

        [Test]
        public void NoPath_OrImpassableGoal_ReturnsFalse()
        {
            var geometry = Parse(
                "#####",
                "#.#.#",
                "#####");
            var pathfinder = new GridPathfinder(5, 3);
            var path = new List<GridPosition>();

            Assert.IsFalse(pathfinder.TryFindPath(new GridPosition(1, 1), new GridPosition(3, 1), Mask(geometry), path));
            Assert.IsEmpty(path);
            Assert.IsFalse(pathfinder.TryFindPath(new GridPosition(1, 1), new GridPosition(2, 1), Mask(geometry), path));
            Assert.IsFalse(pathfinder.TryFindPath(new GridPosition(1, 1), new GridPosition(9, 9), Mask(geometry), path));
        }

        [Test]
        public void StartEqualsGoal_ReturnsSingleCell()
        {
            var geometry = new LevelGeometry(4, 4, CellType.Floor);
            var path = new List<GridPosition>();
            Assert.IsTrue(new GridPathfinder(4, 4).TryFindPath(new GridPosition(2, 2), new GridPosition(2, 2), Mask(geometry), path));
            CollectionAssert.AreEqual(new[] { new GridPosition(2, 2) }, path);
        }

        [Test]
        public void MatchesBfsDistance_OnGeneratedMazesWithLoops_ReusingOneInstance()
        {
            var settings = new LevelGenerationSettings { Width = 31, Height = 31, LoopDensity = 0.3f };
            var pathfinder = new GridPathfinder(31, 31);
            var path = new List<GridPosition>();

            for (var seed = 0; seed < 20; seed++)
            {
                settings.MazeSeed = seed;
                var geometry = new MazeGenerator().Generate(settings).Geometry;
                var mask = Mask(geometry);
                var start = new GridPosition(1, 1);
                var distances = Bfs(geometry, start);

                for (var y = 1; y < 30; y += 2)
                for (var x = 1; x < 30; x += 2)
                {
                    var goal = new GridPosition(x, y);
                    Assert.IsTrue(pathfinder.TryFindPath(start, goal, mask, path));
                    Assert.AreEqual(distances[geometry.ToIndex(goal)], path.Count - 1, $"seed {seed}, goal {goal}");
                }
            }
        }

        private static int[] Bfs(LevelGeometry geometry, GridPosition source)
        {
            var distances = Enumerable.Repeat(-1, geometry.CellCount).ToArray();
            var queue = new Queue<GridPosition>();
            distances[geometry.ToIndex(source)] = 0;
            queue.Enqueue(source);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var direction in DirectionExtensions.All)
                {
                    var next = current.Neighbour(direction);
                    if (!geometry.IsInside(next) || geometry.GetCell(next) == CellType.Wall || distances[geometry.ToIndex(next)] >= 0)
                        continue;

                    distances[geometry.ToIndex(next)] = distances[geometry.ToIndex(current)] + 1;
                    queue.Enqueue(next);
                }
            }

            return distances;
        }
    }
}
