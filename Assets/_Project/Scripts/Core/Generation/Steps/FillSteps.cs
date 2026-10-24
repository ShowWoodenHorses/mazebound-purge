using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Generation
{
    /// <summary>An obstacle kind and its weight in <see cref="ObstaclesStep"/>.</summary>
    [Serializable]
    public sealed class WeightedObstacle
    {
        [SerializeField] private ObstacleDefinition _obstacle;
        [SerializeField, Min(0)] private int _weight = 1;

        public WeightedObstacle(ObstacleDefinition obstacle, int weight)
        {
            _obstacle = obstacle;
            _weight = weight;
        }

        public ObstacleDefinition Obstacle => _obstacle;
        public int Weight => _weight;
    }

    /// <summary>
    /// Obstacles (trees, bushes, stones) on the floor left free by the layout steps, in groves (noise), kinds by weight;
    /// no 8×8 chunk gets more than <see cref="MaxChunkShare"/> of its cells. Never on open cells (start, arena,
    /// clearings, trails, spawn points).
    /// </summary>
    [Serializable, ModuleInfo("Obstacles", Category = "Fill")]
    public sealed class ObstaclesStep : GenerationStep
    {
        public const int ChunkSize = 8;

        [SerializeField] private List<WeightedObstacle> _obstacles = new List<WeightedObstacle>();

        [Tooltip("Average share of the free cells that get an obstacle (inside groves up to twice as much).")]
        [SerializeField, Range(0f, 1f)] private float _density = 0.4f;

        [Tooltip("Most of an 8×8 chunk's cells that may hold obstacles.")]
        [SerializeField, Range(0f, 1f)] private float _maxChunkShare = 0.35f;

        [Tooltip("Lattice cells per grid cell of the grove noise: smaller = bigger groves.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _groveScale = 0.15f;

        public float MaxChunkShare { get => _maxChunkShare; internal set => _maxChunkShare = value; }
        public float Density { get => _density; internal set => _density = value; }
        internal List<WeightedObstacle> MutableObstacles => _obstacles;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            var total = 0;
            foreach (var entry in _obstacles)
                if (entry.Obstacle != null) total += entry.Weight;
            if (total <= 0) return;

            var chunksX = (layout.Width + ChunkSize - 1) / ChunkSize;
            var chunksY = (layout.Height + ChunkSize - 1) / ChunkSize;
            var perChunk = new int[chunksX * chunksY];
            var cells = new List<int>();
            for (var i = 0; i < layout.CellCount; i++)
            {
                if (layout.Obstacle(i) != null) perChunk[Chunk(layout, i, chunksX)]++;
                else if (layout.Cell(i) == CellType.Floor && !layout.IsOpen(i) && !layout.IsKept(i)) cells.Add(i);
            }

            ArenaStep.Shuffle(cells, random);
            var limit = (int)Math.Floor(_maxChunkShare * ChunkSize * ChunkSize);
            var noiseSeed = (int)random.NextULong();
            foreach (var cell in cells)
            {
                var chunk = Chunk(layout, cell, chunksX);
                if (perChunk[chunk] >= limit) continue;
                var noise = LayoutNoise.Fractal(noiseSeed, cell % layout.Width, cell / layout.Width, _groveScale);
                var chance = Mathf.Clamp01(2f * _density * Mathf.SmoothStep(0f, 1f, (noise - 0.3f) / 0.4f)); // groves and gaps
                if (random.NextDouble() >= chance) continue;
                layout.SetObstacle(cell, Pick(random, total));
                perChunk[chunk]++;
            }
        }

        private static int Chunk(LevelLayout layout, int cell, int chunksX) =>
            cell / layout.Width / ChunkSize * chunksX + cell % layout.Width / ChunkSize;

        private ObstacleDefinition Pick(DeterministicRandom random, int total)
        {
            var roll = random.NextInt(total);
            foreach (var entry in _obstacles)
            {
                if (entry.Obstacle == null) continue;
                if (roll < entry.Weight) return entry.Obstacle;
                roll -= entry.Weight;
            }

            return null;
        }

        public override void Validate(List<string> problems)
        {
            if (_obstacles.Count == 0) problems.Add("Obstacles: no obstacle kinds.");
            foreach (var entry in _obstacles)
                if (entry.Obstacle == null) problems.Add("Obstacles: an empty obstacle kind.");
        }
    }

    /// <summary>
    /// Grass (<see cref="CellSurface.Grass"/>: burns, tall grass decor) in noise patches over free floor; not on trails
    /// and not under obstacles.
    /// </summary>
    [Serializable, ModuleInfo("Grass", Category = "Fill")]
    public sealed class GrassStep : GenerationStep
    {
        [Tooltip("Share of the free floor cells that get grass.")]
        [SerializeField, Range(0f, 1f)] private float _coverage = 0.35f;

        [Tooltip("Lattice cells per grid cell of the patch noise: smaller = bigger patches.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _patchScale = 0.12f;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            var noiseSeed = (int)random.NextULong();
            var cells = new List<(float, int)>();
            for (var i = 0; i < layout.CellCount; i++)
                if (layout.Cell(i) == CellType.Floor && layout.Obstacle(i) == null && !layout.IsTrail(i) && !layout.IsKept(i))
                    cells.Add((LayoutNoise.Fractal(noiseSeed, i % layout.Width, i / layout.Width, _patchScale), i));

            cells.Sort((a, b) => b.Item1.CompareTo(a.Item1));
            var count = (int)Math.Round(cells.Count * _coverage);
            for (var k = 0; k < count; k++)
                layout.SetSurface(cells[k].Item2, CellSurface.Grass);
        }
    }

    /// <summary>Auto decor settings of the level (no gameplay): density, height limit, tall grass on grass cells.</summary>
    [Serializable, ModuleInfo("Decor", Category = "Fill")]
    public sealed class DecorStep : GenerationStep
    {
        [Tooltip("Share of the plain floor cells with a prop.")]
        [SerializeField, Range(0f, 1f)] private float _density = 0.15f;

        [Tooltip("Props taller than this (metres) are placed only by hand.")]
        [SerializeField, Min(0f)] private float _maxHeight = 0.3f;

        [Tooltip("Share of the grass cells with grass decor.")]
        [SerializeField, Range(0f, 1f)] private float _grassDensity = 0.7f;

        public override void Apply(LevelLayout layout, DeterministicRandom random) =>
            layout.Decor = new DecorSettings { Density = _density, MaxHeight = _maxHeight, GrassDensity = _grassDensity };
    }
}
