using System;
using UnityEngine;

namespace Purge.Core.Visual
{
    /// <summary>
    /// Bare ground of the theme (display only, stage 7г): on cells with <see cref="Grid.CellSurface.Ground"/>
    /// <c>Purge/Geometry</c> (keyword <c>PURGE_GROUND</c>) draws this texture in world space over the low up-facing
    /// floor, with a soft noisy edge between cells. Empty <see cref="Albedo"/> = no ground blending (the floor variant
    /// of the cell still shows). Purge → Dev → Build Biome Themes fills it.
    /// </summary>
    [Serializable]
    public sealed class ThemeGround
    {
        [Tooltip("Ground texture (seamless), repeated every Tile Cells cells. Empty = no ground blending.")]
        public Texture2D Albedo;

        [Tooltip("Cells covered by one repeat of the texture.")]
        [Range(0.25f, 8f)] public float TileCells = 1f;

        [Tooltip("Width of the soft edge between ground and the other floor, in mask units (0.5 ≈ the whole cell).")]
        [Range(0.02f, 0.5f)] public float EdgeSoftness = 0.12f;

        [Tooltip("How far the noise pushes the edge in and out (uneven edge), mask units.")]
        [Range(0f, 0.6f)] public float EdgeNoise = 0.55f;

        [Tooltip("Size of the edge bumps: noise frequency per cell.")]
        [Range(0.5f, 6f)] public float NoiseScale = 1.3f;
    }
}
