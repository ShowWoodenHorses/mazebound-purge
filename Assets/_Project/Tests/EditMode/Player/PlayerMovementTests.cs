using System.Collections.Generic;
using Maze.Core.Authoring;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Gameplay.Player;
using Maze.Tests.EditMode.Visual;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Player
{
    public class PlayerMovementTests
    {
        private const float Half = 0.3f;
        private const float Assist = 0.35f;

        /// <summary>
        /// Map rows are listed top (north) to bottom; '#' wall, '.' floor, 'Z' floor with zombies.
        /// </summary>
        private sealed class MapBlockers : IPlayerBlockers
        {
            private readonly string[] _rows;

            public MapBlockers(params string[] rows) => _rows = rows;

            private char At(GridPosition cell)
            {
                var row = _rows.Length - 1 - cell.Y;
                if (row < 0 || row >= _rows.Length || cell.X < 0 || cell.X >= _rows[row].Length) return '#';
                return _rows[row][cell.X];
            }

            public bool IsWalkable(GridPosition cell) => At(cell) != '#';
            public bool CanEnter(GridPosition cell) => At(cell) != 'Z';
        }

        private static Vector2 Run(Vector2 position, Vector2 direction, float seconds, IPlayerBlockers blockers,
            float speed = 3.5f, float dt = 1f / 60f)
        {
            for (var t = 0f; t < seconds; t += dt)
                position = PlayerMovement.Move(position, direction * speed * dt, Half, Assist, blockers);
            return position;
        }

        [Test]
        public void FreeMovement_TravelsSpeedTimesTime()
        {
            var map = new MapBlockers(".......", ".......", ".......");
            var end = PlayerMovement.Move(new Vector2(1f, 1f), new Vector2(2f, 0f), Half, Assist, map);
            Assert.AreEqual(3f, end.x, 1e-4f);
            Assert.AreEqual(1f, end.y, 1e-4f);
        }

        [Test]
        public void Wall_StopsFootprintAtCellBoundary()
        {
            //  y=1: . . #
            var map = new MapBlockers("..#");
            var end = Run(new Vector2(0f, 0f), Vector2.right, 2f, map);
            Assert.AreEqual(1.5f - Half, end.x, 0.01f);
        }

        [Test]
        public void HugeStep_DoesNotTunnelThroughWall()
        {
            var map = new MapBlockers("..#....");
            var end = PlayerMovement.Move(new Vector2(0f, 0f), new Vector2(10f, 0f), Half, Assist, map);
            Assert.Less(end.x, 1.5f);
        }

        [Test]
        public void DiagonalIntoWall_SlidesAlongIt()
        {
            // Wall row to the north; moving north-east slides east.
            var map = new MapBlockers("#####", ".....");
            var end = Run(new Vector2(0f, 0f), new Vector2(1f, 1f).normalized, 0.5f, map);
            Assert.AreEqual(0.5f - Half, end.y, 0.01f, "Stopped by the northern wall.");
            Assert.Greater(end.x, 1f, "Kept moving east.");
        }

        [Test]
        public void CornerAssist_EntersSidePassage_WhenSlightlyOffLane()
        {
            // Corridor along x at y=0 with a side passage north at x=2.
            var map = new MapBlockers(
                "##.##",
                ".....");
            var start = new Vector2(2.25f, 0f); // 0.25 east of the passage lane
            var end = Run(start, Vector2.up, 1f, map);
            Assert.Greater(end.y, 0.6f, "Entered the passage.");
            // Nudged just enough for the footprint to fit the one-cell passage.
            Assert.LessOrEqual(Mathf.Abs(end.x - 2f), 0.5f - Half + 0.01f);
        }

        [Test]
        public void CornerAssist_DoesNotPullFromFarAway()
        {
            var map = new MapBlockers(
                "##.##",
                ".....");
            var end = Run(new Vector2(3f, 0f), Vector2.up, 1f, map);
            Assert.AreEqual(3f, end.x, 1e-4f);
            Assert.AreEqual(0.5f - Half, end.y, 0.01f);
        }

        [Test]
        public void ZombieCell_CentreCannotEnter()
        {
            var map = new MapBlockers("..Z..");
            var end = Run(new Vector2(0f, 0f), Vector2.right, 2f, map);
            Assert.AreEqual(new GridPosition(1, 0), PlayerMovement.CellOf(end));
            Assert.Greater(end.x, 1.4f, "Walks up to the cell boundary.");
        }

        [Test]
        public void Fuzz_NeverOverlapsWalls_NeverSkipsCells()
        {
            var fixture = new VisualFixture(width: 21, height: 21, mazeSeed: 7, loopDensity: 0.3f);
            try
            {
                LevelAuthoring.GenerateNew(fixture.Level);
                var grid = new LevelGrid(fixture.Level.Geometry);
                var blockers = new GridBlockers(grid);
                var random = new DeterministicRandom(123);
                var start = fixture.Level.PlayerStarts[0].Position;
                var position = new Vector2(start.X, start.Y);
                var cell = start;
                var direction = Vector2.zero;

                for (var i = 0; i < 20000; i++)
                {
                    if (i % 20 == 0)
                    {
                        var angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
                        direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    }

                    var dt = random.NextInt(10) == 0 ? 0.25f : 1f / 60f; // occasional frame hitches
                    position = PlayerMovement.Move(position, direction * 3.5f * dt, Half, Assist, blockers);

                    Assert.IsFalse(PlayerMovement.Overlaps(position, Half, blockers), $"Overlap at {position} (step {i}).");
                    var next = PlayerMovement.CellOf(position);
                    Assert.LessOrEqual(Mathf.Abs(next.X - cell.X) + Mathf.Abs(next.Y - cell.Y), 2,
                        $"Jumped from {cell} to {next} (step {i}).");
                    Assert.AreNotEqual(CellType.Wall, grid.GetCellOrWall(next), $"Centre in a wall at {next} (step {i}).");
                    cell = next;
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private sealed class GridBlockers : IPlayerBlockers
        {
            private readonly LevelGrid _grid;
            public GridBlockers(LevelGrid grid) => _grid = grid;
            public bool IsWalkable(GridPosition cell) => _grid.GetCellOrWall(cell) == CellType.Floor;
            public bool CanEnter(GridPosition cell) => true;
        }
    }
}
