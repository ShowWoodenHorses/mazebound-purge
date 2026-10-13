using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Purge.Core.Level
{
    /// <summary>
    /// Input for "Generate New". Used only at level creation time; runtime never reads it.
    /// LayoutSeed drives the logical layout, VisualSeed drives the initial visual variant selection, light and decor placement.
    /// </summary>
    [Serializable]
    public sealed class LevelGenerationSettings
    {
        [SerializeField] private int _width = 21;
        [SerializeField] private int _height = 21;
        [SerializeField, FormerlySerializedAs("_mazeSeed")] private int _layoutSeed;
        [SerializeField] private int _visualSeed;

        [Tooltip("Single-cell pillars inside the field: share of the cells with both coordinates even (0 = open field).")]
        [SerializeField, Range(0f, 1f)] private float _obstacleDensity;

        [Tooltip("Auto placed light sources: 0 = none, 1 = as many as fit (about every 3 cells along walls).")]
        [SerializeField, Range(0f, 1f)] private float _lightDensity = 0.5f;

        [Tooltip("Auto placed decor: share of floor cells with a prop (0 = none, 1 = every floor cell).")]
        [SerializeField, Range(0f, 1f)] private float _decorDensity = 0.15f;

        [Tooltip("Decor taller than this (metres) is never placed automatically, only with the Decor brush.")]
        [SerializeField, Min(0f)] private float _maxAutoDecorHeight = 0.3f;

        public int Width { get => _width; internal set => _width = value; }
        public int Height { get => _height; internal set => _height = value; }
        public int LayoutSeed { get => _layoutSeed; internal set => _layoutSeed = value; }
        public int VisualSeed { get => _visualSeed; internal set => _visualSeed = value; }
        public float ObstacleDensity { get => _obstacleDensity; internal set => _obstacleDensity = value; }
        public float LightDensity { get => _lightDensity; internal set => _lightDensity = value; }
        public float DecorDensity { get => _decorDensity; internal set => _decorDensity = value; }
        public float MaxAutoDecorHeight { get => _maxAutoDecorHeight; internal set => _maxAutoDecorHeight = value; }
    }
}
