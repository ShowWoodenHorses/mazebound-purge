using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Weather of the theme (display only): frost on the characters (<c>Maze/Lit</c>: whitening of the surfaces facing
    /// up and of the edges), falling snow around the player, breath puffs of the player and the zombies and the look of
    /// the snowdrift edges (<c>Maze/Geometry</c>, keyword <c>MAZE_SNOWDRIFT</c>). Empty
    /// <see cref="SnowMaterial"/> = no snow, empty <see cref="BreathMaterial"/> = no breath, frost 0 = no frost.
    /// </summary>
    [Serializable]
    public sealed class ThemeWeather
    {
        [Header("Frost")]
        [Tooltip("Frost on the zombies, 0..1 (0 = none).")]
        [Range(0f, 1f)] public float ZombieFrost;

        [Tooltip("Frost on the player, 0..1 (0 = none).")]
        [Range(0f, 1f)] public float PlayerFrost;

        [Tooltip("Colour of the frost (sRGB).")]
        public Color FrostColor = new Color(0.9f, 0.95f, 1f, 1f);

        [Tooltip("How sharply the frost gathers on the surfaces facing up: higher — only the tops (shoulders, head).")]
        [Range(0.5f, 8f)] public float FrostSharpness = 2f;

        [Tooltip("Frost on the edges of the model (rim), as a share of the full frost.")]
        [Range(0f, 1f)] public float FrostRim = 0.35f;

        [Header("Snowfall")]
        [Tooltip("Snowflake particle (Maze/Particle, alpha). Maze → Dev → Build Weather. Empty = no snow.")]
        public Material SnowMaterial;

        [Tooltip("Snowflakes per second around the player.")]
        [Range(0f, 400f)] public float SnowRate = 70f;

        [Tooltip("Side of the square around the player where snow falls, cells.")]
        [Range(4f, 24f)] public float SnowArea = 12f;

        [Tooltip("Height the flakes start from, cells (below the camera).")]
        [Range(1f, 8f)] public float SnowHeight = 4.5f;

        [Tooltip("Falling speed, cells/s.")]
        [Range(0.2f, 5f)] public float SnowFallSpeed = 1.1f;

        [Tooltip("Wind: sideways drift, cells/s (x = East, y = North).")]
        public Vector2 SnowWind = new Vector2(0.35f, -0.15f);

        [Tooltip("Random sideways speed of each flake, cells/s.")]
        [Range(0f, 2f)] public float SnowFlutter = 0.25f;

        [Tooltip("Flake size, cells.")]
        [Range(0.01f, 0.3f)] public float SnowSize = 0.07f;

        public Color SnowColor = new Color(1f, 1f, 1f, 0.85f);

        [Header("Breath")]
        [Tooltip("Soft round puff (Maze/Particle, alpha). Maze → Dev → Build Weather. Empty = no breath.")]
        public Material BreathMaterial;

        [Tooltip("Seconds between two puffs of one character.")]
        [Range(0.5f, 8f)] public float BreathInterval = 2.4f;

        [Tooltip("Puff size, cells.")]
        [Range(0.05f, 1f)] public float BreathSize = 0.22f;

        [Tooltip("Seconds a puff lives.")]
        [Range(0.2f, 4f)] public float BreathLifetime = 1.1f;

        public Color BreathColor = new Color(0.92f, 0.96f, 1f, 0.45f);

        [Tooltip("From the head bone: forward and down to the mouth, cells.")]
        public Vector2 BreathMouthOffset = new Vector2(0.14f, -0.06f);

        [Tooltip("Speed a puff leaves the mouth with, cells/s.")]
        [Range(0f, 2f)] public float BreathSpeed = 0.35f;

        [Header("Snowdrifts")]
        [Tooltip("Colour of the drift snow (sRGB): the floor texture is lifted towards it, so its grain stays. Used where " +
                 "snowdrift cells show a drift floor of the theme.")]
        public Color DriftColor = new Color(0.86f, 0.91f, 1f, 1f);

        [Tooltip("Width of the soft edge between the drift and the ordinary snow, in mask units (0.5 ≈ the whole cell).")]
        [Range(0.02f, 0.5f)] public float DriftEdgeSoftness = 0.14f;

        [Tooltip("How far the noise pushes the edge in and out (uneven edge), mask units.")]
        [Range(0f, 0.6f)] public float DriftEdgeNoise = 0.3f;

        [Tooltip("Size of the edge bumps: noise frequency per cell.")]
        [Range(0.5f, 6f)] public float DriftNoiseScale = 2.2f;

        [Tooltip("Light shadow along the foot of the drift, 0..1.")]
        [Range(0f, 1f)] public float DriftShadow = 0.25f;

        [Header("Frozen doors")]
        [Tooltip("Ice put over a frozen door (pivot = the door's centre on the floor, door rotation 0 = passage " +
                 "South–North). Optional FrozenDoorIce component chips the pieces off. Maze → Dev → Build Weather.")]
        public AssetReferenceGameObject IceOverlay;

        public bool HasFrost => ZombieFrost > 0f || PlayerFrost > 0f;

        public bool HasIceOverlay => IceOverlay != null && IceOverlay.RuntimeKeyIsValid();
    }
}
