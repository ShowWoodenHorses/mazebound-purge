using System;
using System.Collections.Generic;
using Maze.Core.Lighting;
using UnityEngine;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Lighting mood of a theme: no realtime shadows; a gloomy ambient, a weak shadowless "moon" (directional light,
    /// gives volume to walls and characters), a soft lantern around the player (computed in the shaders, not a Unity
    /// light) and soft round shadows under characters.
    /// </summary>
    [Serializable]
    public sealed class ThemeLighting
    {
        [Tooltip("Flat ambient light: the base brightness of everything.")]
        public Color Ambient = new Color(0.2f, 0.25f, 0.34f);

        [Tooltip("Directional light without shadows.")]
        public Color MoonColor = new Color(0.62f, 0.72f, 0.95f);

        [Range(0f, 2f)] public float MoonIntensity = 0.35f;

        [Tooltip("Light around the player (shaders Maze/Geometry and Maze/Lit).")]
        public Color LanternColor = new Color(1f, 0.82f, 0.58f);

        [Range(0f, 4f)] public float LanternIntensity = 0.9f;

        [Tooltip("Cells: the light fades to nothing at this distance.")]
        [Range(0.5f, 12f)] public float LanternRadius = 4.5f;

        [Tooltip("Height of the lantern above the floor (cells).")]
        [Range(0f, 3f)] public float LanternHeight = 1.3f;

        [Tooltip("Floor and walls only: height (cells) where the light of the sources starts to fade upwards. " +
                 "Walls are 1.5 high; characters and objects are always fully lit.")]
        [Range(0f, 3f)] public float LightFadeStart = 0.8f;

        [Tooltip("Height range (cells) over which the sources' light fades to the top share.")]
        [Range(0.05f, 3f)] public float LightFadeLength = 0.6f;

        [Tooltip("Share of the sources' light left above the fade (wall tops): 0 = dark tops.")]
        [Range(0f, 1f)] public float LightTopShare = 0.15f;

        [Tooltip("Soft round shadow under the player and zombies (shader Maze/BlobShadow). Empty = none.")]
        public Material BlobShadowMaterial;

        [Tooltip("Diameter of the blob shadow, cells.")]
        [Range(0.2f, 2f)] public float BlobShadowSize = 0.9f;

        [Tooltip("Light sources auto placement picks from (by weight). Density is a level generation setting.")]
        public List<LightPreset> LightPresets = new List<LightPreset>();
    }
}
