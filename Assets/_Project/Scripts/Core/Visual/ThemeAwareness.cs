using System;
using UnityEngine;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Display of what zombies notice (theme settings, display only — gameplay never reads them):
    /// <list type="bullet">
    /// <item>vision zones of zombies that see (vision cone, or the all-around radius of vision + hearing), cut by walls
    /// and closed doors, shown while the zombie is calm or roaring, hidden while it chases or attacks;</item>
    /// <item>noise waves: a ring growing from the player to the radius of every sound it makes; steps and automatic
    /// fire give one ring when walking / firing starts, not one per sound.</item>
    /// </list>
    /// Empty material = that part is off.
    /// </summary>
    [Serializable]
    public sealed class ThemeAwareness
    {
        [Header("Vision zones")]
        [Tooltip("Unlit, alpha blended, vertex colour × tint (Maze/Particle), drawn over the fog. Maze → Dev → Build Awareness Visuals.")]
        public Material ZoneMaterial;

        [Tooltip("Idle, patrol, return.")]
        public Color CalmColor = new Color(1f, 0.82f, 0.3f, 1f);

        [Tooltip("Roaring before a chase (Alert).")]
        public Color AlertColor = new Color(1f, 0.18f, 0.12f, 1f);

        [Range(0f, 1f)] public float FillAlpha = 0.05f;
        [Range(0f, 1f)] public float EdgeAlpha = 0.35f;

        [Tooltip("Width of the bright edge, cells.")]
        [Range(0.01f, 0.5f)] public float EdgeWidth = 0.04f;

        [Tooltip("Angle between two rays of the zone, degrees (smaller = smoother walls, more rays).")]
        [Range(1f, 15f)] public float RayStep = 4f;

        [Tooltip("Seconds to appear / disappear.")]
        [Range(0.01f, 1f)] public float ZoneFade = 0.2f;

        [Header("Noise waves")]
        [Tooltip("Ring with a faint fill (shader Maze/Ring), drawn over everything but the UI. Empty = no waves.")]
        public Material NoiseMaterial;

        public Color NoiseColor = new Color(0.75f, 0.75f, 0.78f, 0.2f);

        [Tooltip("Width of the ring's line, cells (the same at any radius).")]
        [Range(0.005f, 0.3f)] public float RingWidth = 0.035f;

        [Tooltip("Seconds a single wave takes to reach its radius and fade.")]
        [Range(0.1f, 3f)] public float WaveTime = 0.7f;

        [Tooltip("Steps and automatic fire: seconds the ring takes to grow from the player to its radius when walking / " +
                 "firing starts.")]
        [Range(0.05f, 2f)] public float RingGrow = 0.3f;

        [Tooltip("Steps and automatic fire: seconds the ring then takes to fade (walking / firing goes on without it).")]
        [Range(0.01f, 2f)] public float RingFade = 0.4f;

        [Tooltip("Steps and automatic fire: seconds of quiet (standing, not firing) before the next start shows a ring " +
                 "again — short stops do not flicker.")]
        [Range(0f, 2f)] public float RingRearm = 0.3f;
    }
}
