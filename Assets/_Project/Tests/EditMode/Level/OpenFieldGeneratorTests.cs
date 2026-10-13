using System;
using System.Collections.Generic;
using Purge.Core.Generation;
using Purge.Core.Grid;
using Purge.Core.Level;
using NUnit.Framework;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Level
{
    public class OpenFieldGeneratorTests
    {
        private readonly OpenFieldGenerator _generator = new OpenFieldGenerator();

        private static LevelGenerationSettings Settings(int width, int height, int seed = 1, float obstacles = 0f) =>
            new LevelGenerationSettings { Width = width, Height = height, LayoutSeed = seed, ObstacleDensity = obstacles };

        [Test]
        public void OpenField_BorderWall_FloorInside_StartInTheCentre()
        {
            var result = _generator.Generate(Settings(12, 9));
            var geometry = result.Geometry;

            Assert.AreEqual(12, geometry.Width);
            Assert.AreEqual(9, geometry.Height);
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var p = geometry.ToPosition(i);
                var border = p.X == 0 || p.Y == 0 || p.X == geometry.Width - 1 || p.Y == geometry.Height - 1;
                Assert.AreEqual(border ? CellType.Wall : CellType.Floor, geometry.GetCell(p), p.ToString());
            }

            CollectionAssert.AreEqual(new[] { new GridPosition(6, 4) }, result.PlayerStarts);
        }

        [Test]
        public void Pillars_OnlyOnEvenCells_NeverAtTheStart_EveryFloorReachable()
        {
            for (var seed = 0; seed < 50; seed++)
            {
                var result = _generator.Generate(Settings(25, 21, seed, 0.7f));
                var geometry = result.Geometry;
                var start = result.PlayerStarts[0];
                Assert.AreEqual(CellType.Floor, geometry.GetCell(start));

                var floor = 0;
                for (var i = 0; i < geometry.CellCount; i++)
                {
                    var p = geometry.ToPosition(i);
                    var inner = p.X > 0 && p.Y > 0 && p.X < geometry.Width - 1 && p.Y < geometry.Height - 1;
                    if (geometry.GetCell(p) == CellType.Floor) floor++;
                    else if (inner)
                    {
                        Assert.IsTrue(p.X % 2 == 0 && p.Y % 2 == 0, $"seed {seed}: pillar at {p}");
                        Assert.IsTrue(Math.Abs(p.X - start.X) > 1 || Math.Abs(p.Y - start.Y) > 1, $"seed {seed}: next to the start");
                    }
                }

                Assert.AreEqual(floor, Reachable(geometry, start), $"seed {seed}");
            }
        }

        [Test]
        public void SameSettings_SameLayout_DensityAddsPillars()
        {
            var a = _generator.Generate(Settings(31, 31, 5, 0.5f)).Geometry;
            var b = _generator.Generate(Settings(31, 31, 5, 0.5f)).Geometry;
            CollectionAssert.AreEqual(a.CopyCells(), b.CopyCells());

            Assert.Less(Walls(_generator.Generate(Settings(31, 31, 5, 0.2f)).Geometry),
                Walls(_generator.Generate(Settings(31, 31, 5, 0.9f)).Geometry));
        }

        [Test]
        public void TooSmall_Throws()
        {
            Assert.Throws<ArgumentException>(() => _generator.Generate(Settings(OpenFieldGenerator.MinSize - 1, 10)));
        }

        [Test]
        public void ApplyGenerated_ReplacesDesignAndAssignsIds()
        {
            var level = UnityEngine.ScriptableObject.CreateInstance<LevelData>();
            try
            {
                level.MutableMedkits.Add(new MedkitData("medkit_1", new GridPosition(1, 1)));
                level.VisualData.SetObjectOverride("medkit_1", new Purge.Core.Visual.VisualChoice("x"));

                level.ApplyGenerated(_generator.Generate(Settings(9, 9)));

                Assert.IsEmpty(level.Medkits);
                Assert.IsEmpty(level.VisualData.ObjectOverrides);
                Assert.AreEqual(1, level.PlayerStarts.Count);
                Assert.AreEqual("start_1", level.PlayerStarts[0].Id);
                Assert.AreEqual(9, level.Geometry.Width);
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        private static int Walls(LevelGeometry geometry)
        {
            var count = 0;
            for (var i = 0; i < geometry.CellCount; i++)
                if (geometry.GetCell(geometry.ToPosition(i)) == CellType.Wall) count++;
            return count;
        }

        private static int Reachable(LevelGeometry geometry, GridPosition start)
        {
            var seen = new HashSet<GridPosition> { start };
            var queue = new Queue<GridPosition>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var direction in DirectionExtensions.All)
                {
                    var next = p.Neighbour(direction);
                    if (geometry.IsInside(next) && geometry.GetCell(next) == CellType.Floor && seen.Add(next))
                        queue.Enqueue(next);
                }
            }

            return seen.Count;
        }
    }
}
