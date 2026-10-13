using Maze.Presentation.UI.Shapes;
using TMPro;
using UnityEngine;

namespace Maze.Presentation.UI.Style
{
    /// <summary>
    /// The one place for the look of the UI (thin light lines over translucent dark fills, colour only where it
    /// matters): palette, line widths and the shared <c>Maze/UIShape</c> material. Screens are built from it by
    /// <c>RuntimeScenesBuilder</c>; runtime presenters use it for state colours (accent, danger, key colours come
    /// from <c>VisualColorTags</c>). Created by Maze → Dev → Build UI Style.
    /// </summary>
    [CreateAssetMenu(menuName = "Maze/UI/Style", fileName = "UiStyle")]
    public sealed class UiStyle : ScriptableObject
    {
        [Header("Palette (sRGB)")]
        [Tooltip("Lines, icons and main text.")]
        public Color Line = new Color(0.925f, 0.902f, 0.839f, 0.72f);

        [Tooltip("Main text and icons at full strength.")]
        public Color Text = new Color(0.925f, 0.902f, 0.839f, 1f);

        [Tooltip("Captions and secondary text.")]
        public Color MutedText = new Color(0.663f, 0.635f, 0.576f, 1f);

        [Tooltip("Translucent dark inside of buttons and panels.")]
        public Color Fill = new Color(0.047f, 0.047f, 0.043f, 0.45f);

        [Tooltip("Pressed buttons: the light fill.")]
        public Color PressedFill = new Color(0.925f, 0.902f, 0.839f, 0.2f);

        [Tooltip("What is active, available or next: active weapon, Use, the next level, earned stars.")]
        public Color Accent = new Color(0.914f, 0.714f, 0.31f, 1f);

        [Tooltip("Health.")]
        public Color Health = new Color(0.788f, 0.314f, 0.247f, 1f);

        [Tooltip("Low health and other warnings.")]
        public Color Danger = new Color(0.878f, 0.29f, 0.208f, 1f);

        [Tooltip("Alpha multiplier of disabled controls.")]
        [Range(0f, 1f)] public float DisabledAlpha = 0.35f;

        [Header("Lines (canvas units)")]
        [Min(0f)] public float Stroke = 1.5f;
        [Min(0f)] public float ThinStroke = 1f;

        [Header("Assets")]
        [Tooltip("Shared material with the Maze/UIShape shader (all UiShape use it, so they batch).")]
        public Material ShapeMaterial;

        [Tooltip("Text: Rajdhani SemiBold (static SDF atlas, ASCII only — see CLAUDE.md).")]
        public TMP_FontAsset Font;

        [Tooltip("Titles and buttons: Rajdhani Bold.")]
        public TMP_FontAsset BoldFont;

        /// <summary>Gives a shape the shared material and the standard look: dark fill, light (or accent) outline.</summary>
        public void Paint(UiShape shape, bool accent = false)
        {
            shape.material = ShapeMaterial;
            shape.Fill = Fill;
            shape.Stroke = accent ? Accent : Line;
            shape.StrokeWidth = Stroke;
        }
    }
}
