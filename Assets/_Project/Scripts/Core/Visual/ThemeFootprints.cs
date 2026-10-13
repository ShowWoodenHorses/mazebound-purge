using System;
using UnityEngine;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Footprints and step dust of the theme (display only). Prints are left by the player and by zombies as they
    /// move — also where the player does not see, so a fresh trail shows that a zombie came this way. A print fades as
    /// its maker walks away (<see cref="FootprintKind.FadeFrom"/> → <see cref="FootprintKind.FadeTo"/> cells) and with
    /// age (<see cref="FootprintKind.Lifetime"/>). Empty <see cref="PrintMaterial"/> = no footprints, empty
    /// <see cref="DustMaterial"/> = no dust.
    /// </summary>
    [Serializable]
    public sealed class ThemeFootprints
    {
        [Tooltip("Print texture: left half — the player's boot, right half — a zombie's bare foot; toes / tip up (+v). " +
                 "Unlit, alpha blended, drawn right after opaque geometry (under the fog). Maze → Dev → Build Footprints.")]
        public Material PrintMaterial;

        [Tooltip("Prints alive at once on the level; the oldest is reused when there are more.")]
        [Range(8, 256)] public int MaxPrints = 96;

        public FootprintKind Player = new FootprintKind
        {
            StepLength = 0.7f, Length = 0.26f, Width = 0.13f, Spacing = 0.09f,
            Color = new Color(0.08f, 0.06f, 0.05f, 0.5f), FadeFrom = 1f, FadeTo = 2f, Lifetime = 2f,
        };

        public FootprintKind Zombie = new FootprintKind
        {
            StepLength = 0.55f, Length = 0.25f, Width = 0.13f, Spacing = 0.1f,
            Color = new Color(0.07f, 0.09f, 0.04f, 0.55f), FadeFrom = 3f, FadeTo = 4f, Lifetime = 8f,
        };

        [Header("Dust")]
        [Tooltip("Soft round particle (e.g. the dust of the combat effects). Empty = no dust.")]
        public Material DustMaterial;

        public Color DustColor = new Color(0.7f, 0.66f, 0.58f, 0.35f);

        [Tooltip("Size of a dust puff, cells.")]
        [Range(0.05f, 1f)] public float DustSize = 0.28f;

        [Tooltip("Seconds a dust puff lives.")]
        [Range(0.05f, 2f)] public float DustLifetime = 0.4f;

        [Tooltip("Speed (cells/s) from which a step raises a second, bigger puff (running).")]
        [Range(0f, 10f)] public float DustRunSpeed = 1.5f;
    }

    /// <summary>Prints of one kind of walker.</summary>
    [Serializable]
    public sealed class FootprintKind
    {
        [Tooltip("Distance walked between two prints (left, right, left...), cells.")]
        [Range(0.1f, 2f)] public float StepLength = 0.7f;

        [Tooltip("Print size, cells.")]
        [Range(0.05f, 1f)] public float Length = 0.26f;

        [Range(0.05f, 1f)] public float Width = 0.13f;

        [Tooltip("Sideways distance of a print from the walker's path, cells.")]
        [Range(0f, 0.5f)] public float Spacing = 0.09f;

        [Tooltip("Colour of a fresh print; alpha = its opacity.")]
        public Color Color = new Color(0.08f, 0.06f, 0.05f, 0.5f);

        [Tooltip("Cells between the walker and the print where the print starts to fade.")]
        [Range(0f, 20f)] public float FadeFrom = 1f;

        [Tooltip("Cells between the walker and the print where it is gone.")]
        [Range(0.1f, 20f)] public float FadeTo = 2f;

        [Tooltip("Seconds after which a print is gone anyway (a walker standing still).")]
        [Range(0.1f, 60f)] public float Lifetime = 2f;
    }
}
