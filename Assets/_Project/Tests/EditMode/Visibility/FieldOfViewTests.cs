using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Visibility;
using NUnit.Framework;

namespace Maze.Tests.EditMode.Visibility
{
    public class FieldOfViewTests
    {
        /// <summary>
        /// Map rows from top (highest y) to bottom. '#' wall, '.' floor, 'D' closed door, 'O' open door, '@' player.
        /// </summary>
        private sealed class Map
        {
            public readonly int Width;
            public readonly int Height;
            public readonly bool[] Opaque;
            public GridPosition Player;

            public Map(params string[] rows)
            {
                Height = rows.Length;
                Width = rows[0].Length;
                Opaque = new bool[Width * Height];
                for (var row = 0; row < Height; row++)
                {
                    var y = Height - 1 - row;
                    for (var x = 0; x < Width; x++)
                    {
                        var c = rows[row][x];
                        Opaque[y * Width + x] = c == '#' || c == 'D';
                        if (c == '@') Player = new GridPosition(x, y);
                    }
                }
            }

            public CellMaskOpacity Opacity => new CellMaskOpacity(Width, Height, Opaque);

            public FieldOfView Compute(int radius = FieldOfView.DefaultRadius)
            {
                var fov = new FieldOfView(Width, Height);
                fov.Compute(Player, radius, Opacity);
                return fov;
            }
        }

        private static Map OpenField(int size, GridPosition player)
        {
            var rows = new string[size];
            for (var i = 0; i < size; i++)
                rows[i] = new string('.', size);
            return new Map(rows) { Player = player };
        }

        [Test]
        public void OpenField_SeesExactly11x11_ClippedToGrid()
        {
            var fov = OpenField(21, new GridPosition(10, 10)).Compute();
            Assert.AreEqual(121, fov.VisibleCells.Count);
            Assert.IsTrue(fov.IsVisible(new GridPosition(5, 15)));
            Assert.IsFalse(fov.IsVisible(new GridPosition(4, 10)), "6 cells away is outside the window.");
            Assert.IsFalse(fov.IsVisible(new GridPosition(10, 16)));

            fov = OpenField(21, new GridPosition(0, 0)).Compute();
            Assert.AreEqual(36, fov.VisibleCells.Count, "Window is clipped by the grid border.");
        }

        [Test]
        public void Wall_BlocksSight_ButIsVisibleItself()
        {
            var fov = new Map(
                "#########",
                "#...#...#",
                "#.@.#...#",
                "#...#...#",
                "#########").Compute();

            Assert.IsTrue(fov.IsVisible(new GridPosition(4, 2)), "The blocking wall is seen.");
            Assert.IsTrue(fov.IsVisible(new GridPosition(0, 0)), "Room corner wall is seen.");
            for (var y = 1; y <= 3; y++)
            for (var x = 5; x <= 7; x++)
                Assert.IsFalse(fov.IsVisible(new GridPosition(x, y)), $"({x}, {y}) is behind the wall.");
        }

        [Test]
        public void ClosedDoor_BlocksSight_OpenDoorDoesNot()
        {
            var closed = new Map(
                "#######",
                "#.@D..#",
                "#######").Compute();
            Assert.IsTrue(closed.IsVisible(new GridPosition(3, 1)), "The closed door itself is seen.");
            Assert.IsFalse(closed.IsVisible(new GridPosition(4, 1)));
            Assert.IsFalse(closed.IsVisible(new GridPosition(5, 1)));

            var open = new Map(
                "#######",
                "#.@O..#",
                "#######").Compute();
            Assert.IsTrue(open.IsVisible(new GridPosition(4, 1)));
            Assert.IsTrue(open.IsVisible(new GridPosition(5, 1)));
            Assert.IsTrue(open.IsVisible(new GridPosition(6, 1)), "Wall at the end of the corridor.");
        }

        [Test]
        public void StraightCorridor_ShowsBothSideWallsUpToTheWindow()
        {
            var fov = new Map(
                "###",
                "#.#",
                "#.#",
                "#.#",
                "#.#",
                "#.#",
                "#.#",
                "#.#",
                "#@#",
                "###").Compute();

            for (var y = 0; y <= 6; y++)
            {
                Assert.IsTrue(fov.IsVisible(new GridPosition(0, y)), $"West wall y={y}.");
                Assert.IsTrue(fov.IsVisible(new GridPosition(1, y)), $"Corridor y={y}.");
                Assert.IsTrue(fov.IsVisible(new GridPosition(2, y)), $"East wall y={y}.");
            }

            Assert.IsFalse(fov.IsVisible(new GridPosition(1, 7)), "Beyond 5 cells.");
        }

        [Test]
        public void SideOpening_EntranceVisible_CorridorBehindItHidden()
        {
            var fov = new Map(
                "#.###",
                "#.###",
                "#....",
                "#.###",
                "#.###",
                "#@###",
                "#####").Compute();

            Assert.IsTrue(fov.IsVisible(new GridPosition(2, 4)), "Entrance of the side corridor.");
            Assert.IsFalse(fov.IsVisible(new GridPosition(3, 4)), "Deeper cells of the side corridor are around the corner.");
            Assert.IsFalse(fov.IsVisible(new GridPosition(4, 4)));
        }

        [Test]
        public void DiagonalGapBetweenWalls_BlocksSight()
        {
            var fov = new Map(
                "#.",
                "@#").Compute();

            Assert.IsFalse(fov.IsVisible(new GridPosition(1, 1)), "Walls touching at a corner close the gap.");
            Assert.IsTrue(fov.IsVisible(new GridPosition(1, 0)));
            Assert.IsTrue(fov.IsVisible(new GridPosition(0, 1)));
        }

        [Test]
        public void LineThroughCorner_BlockedOnlyWhenBothSidesOpaque()
        {
            var bothWalls = new Map("#.", "@#");
            Assert.IsFalse(GridLineOfSight.IsClear(0f, 0f, 1f, 1f, bothWalls.Opacity));

            var oneWall = new Map("..", "@#");
            Assert.IsTrue(GridLineOfSight.IsClear(0f, 0f, 1f, 1f, oneWall.Opacity));
        }

        [Test]
        public void FloorBehindPillar_IsRevealedForGeometry_ButNotVisible()
        {
            var fov = new Map(
                ".......",
                ".......",
                "...#...",
                "...@...").Compute();
            var shadow = new GridPosition(3, 2);

            Assert.IsFalse(fov.IsVisible(shadow), "Behind the pillar: objects there stay hidden.");
            Assert.IsTrue(fov.IsVisible(new GridPosition(2, 2)));
            Assert.IsTrue(fov.IsVisible(new GridPosition(4, 2)));
            Assert.IsTrue(fov.IsRevealed(shadow), "Floor between seen floor is drawn: no one-cell hole.");
            foreach (var cell in fov.VisibleCells)
                Assert.IsTrue(fov.IsRevealed(cell), "Every visible cell is revealed.");
        }

        [Test]
        public void RoomBehindClosedDoor_StaysHidden_EvenWhenItsWallsAreSeen()
        {
            // Bottom of Level_Dev: (3,3) is a room closed by walls and the door (2,3); all of them are seen.
            var fov = new Map(
                "#.....#",
                "#.###.#",
                "#.D.#.#",
                "#.###.#",
                "#@....#",
                "#######").Compute();
            var room = new GridPosition(3, 3);

            Assert.IsTrue(fov.IsVisible(new GridPosition(2, 3)), "The closed door is seen.");
            Assert.IsFalse(fov.IsVisible(room));
            Assert.IsFalse(fov.IsRevealed(room), "Nothing behind a closed door is drawn (ТЗ §55).");
            Assert.IsTrue(fov.IsVisible(new GridPosition(5, 2)), "The corridor beyond the room is seen…");
            Assert.IsFalse(fov.IsVisible(new GridPosition(5, 3)));
            Assert.IsFalse(fov.IsRevealed(new GridPosition(4, 3)),
                "…but the room's far wall touches it only diagonally, across hidden floor: it stays hidden.");
            Assert.IsTrue(fov.IsVisible(new GridPosition(0, 0)), "A real corner of visible floor is still closed.");
        }

        [Test]
        public void Revealed_FillsOnlySingleGaps_NotChains()
        {
            var fov = new Map(
                "#######",
                "#.....#",
                "#######",
                "#@....#",
                "#######").Compute();

            for (var x = 1; x <= 5; x++)
                Assert.IsFalse(fov.IsVisible(new GridPosition(x, 3)), "The upper corridor is behind the wall.");
            Assert.IsFalse(fov.IsRevealed(new GridPosition(3, 3)), "A hidden corridor is not a one-cell gap.");
        }

        [Test]
        public void GeneratedMazes_VisibleFloorIsConnected_NoIslandsAfterHiddenCells()
        {
            var generator = new Maze.Core.Generation.MazeGenerator();
            for (var seed = 1; seed <= 20; seed++)
            {
                var geometry = generator.Generate(new Maze.Core.Level.LevelGenerationSettings
                {
                    Width = 21, Height = 21, MazeSeed = seed, LoopDensity = seed % 2 == 0 ? 0.3f : 0f,
                    InitialPlayerStartCount = 1, InitialExitCount = 1,
                }).Geometry;
                var opaque = new bool[geometry.CellCount];
                for (var i = 0; i < opaque.Length; i++)
                    opaque[i] = geometry.GetCell(geometry.ToPosition(i)) != CellType.Floor;
                var opacity = new CellMaskOpacity(geometry.Width, geometry.Height, opaque);
                var fov = new FieldOfView(geometry.Width, geometry.Height);

                for (var i = 0; i < opaque.Length; i++)
                {
                    if (opaque[i]) continue;
                    var origin = geometry.ToPosition(i);
                    fov.Compute(origin, FieldOfView.DefaultRadius, opacity);
                    AssertVisibleFloorConnected(fov, origin, opaque, geometry.Width, $"seed {seed}, from {origin}");
                }
            }
        }

        /// <summary>
        /// Every visible floor cell is reachable from the origin through visible floor (8 directions): a ray only
        /// sees a cell after passing the cells before it, so a far cell is never seen past a hidden one.
        /// </summary>
        private static void AssertVisibleFloorConnected(FieldOfView fov, GridPosition origin, bool[] opaque, int width, string context)
        {
            var reached = new HashSet<GridPosition> { origin };
            var queue = new Queue<GridPosition>();
            queue.Enqueue(origin);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var next = new GridPosition(cell.X + dx, cell.Y + dy);
                    if (fov.IsVisible(next) && !opaque[next.Y * width + next.X] && reached.Add(next))
                        queue.Enqueue(next);
                }
            }

            foreach (var cell in fov.VisibleCells)
                if (!opaque[cell.Y * width + cell.X])
                    Assert.IsTrue(reached.Contains(cell), $"{context}: visible floor {cell} is cut off from the player.");
        }

        [Test]
        public void Recompute_ClearsPreviousResult()
        {
            var map = OpenField(31, new GridPosition(5, 5));
            var fov = new FieldOfView(map.Width, map.Height);
            fov.Compute(map.Player, 5, map.Opacity);
            Assert.IsTrue(fov.IsVisible(new GridPosition(0, 0)));

            fov.Compute(new GridPosition(25, 25), 5, map.Opacity);
            Assert.IsFalse(fov.IsVisible(new GridPosition(0, 0)));
            Assert.AreEqual(121, fov.VisibleCells.Count);
            Assert.AreEqual(new GridPosition(25, 25), fov.Origin);
        }
    }
}
