using System;
using UnityEngine;

namespace Maze.Core.Lighting
{
    /// <summary>A kind of light the theme's auto placement chooses from (by weight), e.g. torch, cold lamp.</summary>
    [Serializable]
    public sealed class LightPreset
    {
        public string Id = "torch";

        [Min(0f)] public float Weight = 1f;

        public Color Color = new Color(1f, 0.62f, 0.3f);

        [Tooltip("Cells.")]
        [Range(1f, 12f)] public float Radius = 4f;

        [Range(0f, 4f)] public float Intensity = 1.2f;

        [Tooltip("0 = steady, 1 = strong flicker.")]
        [Range(0f, 1f)] public float Flicker = 0.6f;
    }
}
