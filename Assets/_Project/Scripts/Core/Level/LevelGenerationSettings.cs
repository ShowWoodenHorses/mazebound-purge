using System;
using UnityEngine;

namespace Maze.Core.Level
{
    public enum MazeAlgorithm
    {
        RecursiveBacktracker = 0,
    }

    /// <summary>
    /// Input for "Generate New". Used only at level creation time; runtime never reads it.
    /// MazeSeed drives the logical maze, VisualSeed drives the initial visual variant selection, light and decor placement.
    /// </summary>
    [Serializable]
    public sealed class LevelGenerationSettings
    {
        [SerializeField] private int _width = 21;
        [SerializeField] private int _height = 21;
        [SerializeField] private int _mazeSeed;
        [SerializeField] private int _visualSeed;
        [SerializeField] private MazeAlgorithm _mazeAlgorithm = MazeAlgorithm.RecursiveBacktracker;
        [SerializeField, Range(0f, 1f)] private float _loopDensity = 0.1f;
        [SerializeField, Min(1)] private int _initialPlayerStartCount = 1;
        [SerializeField, Min(1)] private int _initialExitCount = 1;

        [Tooltip("Auto placed light sources: 0 = none, 1 = as many as fit (about every 3 cells along walls).")]
        [SerializeField, Range(0f, 1f)] private float _lightDensity = 0.5f;

        [Tooltip("Auto placed decor: share of floor cells with a prop (0 = none, 1 = every floor cell).")]
        [SerializeField, Range(0f, 1f)] private float _decorDensity = 0.15f;

        [Tooltip("Decor taller than this (metres) is never placed automatically, only with the Decor brush.")]
        [SerializeField, Min(0f)] private float _maxAutoDecorHeight = 0.3f;

        public int Width { get => _width; internal set => _width = value; }
        public int Height { get => _height; internal set => _height = value; }
        public int MazeSeed { get => _mazeSeed; internal set => _mazeSeed = value; }
        public int VisualSeed { get => _visualSeed; internal set => _visualSeed = value; }
        public MazeAlgorithm MazeAlgorithm { get => _mazeAlgorithm; internal set => _mazeAlgorithm = value; }
        public float LoopDensity { get => _loopDensity; internal set => _loopDensity = value; }
        public int InitialPlayerStartCount { get => _initialPlayerStartCount; internal set => _initialPlayerStartCount = value; }
        public int InitialExitCount { get => _initialExitCount; internal set => _initialExitCount = value; }
        public float LightDensity { get => _lightDensity; internal set => _lightDensity = value; }
        public float DecorDensity { get => _decorDensity; internal set => _decorDensity = value; }
        public float MaxAutoDecorHeight { get => _maxAutoDecorHeight; internal set => _maxAutoDecorHeight = value; }

        /// <summary>
        /// The generator needs odd width and height: a border wall plus alternating passage/wall cells, all one
        /// cell thick, fit exactly into an odd size (21 = wall + 10 x (passage, wall)). Project decision that
        /// replaces the "even size" rule of ТЗ §12, which left a redundant double wall at the top/right.
        /// </summary>
        public bool HasOddSize => _width % 2 == 1 && _height % 2 == 1;
    }
}
