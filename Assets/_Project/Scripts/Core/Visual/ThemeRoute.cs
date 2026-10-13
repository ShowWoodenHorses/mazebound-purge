using System;
using UnityEngine;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Look of the route hint line on the floor (display only): a ribbon from the player to the hint target with an
    /// outline, drawn over the fog. Kept quiet on purpose — it shows the way, it must not shout.
    /// </summary>
    [Serializable]
    public sealed class ThemeRoute
    {
        [Tooltip("Shader Maze/RouteLine (colours below are set from code). Empty = no line. Maze → Dev → Build Route Line.")]
        public Material LineMaterial;

        [Tooltip("Colour of the line (sRGB).")]
        public Color Color = new Color(0.62f, 0.86f, 1f, 1f);

        [Tooltip("Multiplier of the colour: below 1 dims it, above 1 makes it glow brighter.")]
        [Range(0f, 2f)] public float Brightness = 0.85f;

        [Tooltip("Opacity of the whole line.")]
        [Range(0f, 1f)] public float Opacity = 0.6f;

        [Tooltip("Width of the whole strip, outline included, cells.")]
        [Range(0.02f, 0.8f)] public float Width = 0.18f;

        [Tooltip("Colour of the outline along both edges (sRGB); its alpha is the outline's opacity.")]
        public Color OutlineColor = new Color(0.04f, 0.06f, 0.08f, 0.9f);

        [Tooltip("Width of the outline on each edge, cells. 0 = no outline.")]
        [Range(0f, 0.2f)] public float OutlineWidth = 0.03f;

        [Tooltip("Radius of the rounded turns, cells.")]
        [Range(0f, 0.5f)] public float CornerRadius = 0.35f;

        [Tooltip("The line fades in over this distance from the player, cells.")]
        [Range(0f, 3f)] public float StartFade = 0.6f;

        [Tooltip("Brighter pulses flowing towards the target: their strength (0 = a plain line).")]
        [Range(0f, 1f)] public float Pulse = 0.35f;

        [Tooltip("Distance between two pulses, cells.")]
        [Range(0.3f, 5f)] public float PulseSpacing = 1.6f;

        [Tooltip("Speed of the pulses, cells per second.")]
        [Range(0f, 5f)] public float PulseSpeed = 1.2f;

        [Tooltip("Height above the floor, metres.")]
        [Range(0f, 0.3f)] public float Height = 0.05f;
    }
}
