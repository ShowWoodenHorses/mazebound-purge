using UnityEngine;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Colour of a <see cref="VisualVariant.ColorTag"/> (key/door pair): our own names first (bright enough for the
    /// HUD, map and key lights on a dark maze, and names HTML lacks, like "pink"), then any HTML name or #hex.
    /// The original red/blue/green/yellow are plain HTML names.
    /// </summary>
    public static class VisualColors
    {
        private static readonly (string Tag, Color Color)[] Named =
        {
            ("orange", new Color(1f, 0.5f, 0.05f)),
            ("cyan", new Color(0.1f, 0.85f, 0.85f)),
            ("purple", new Color(0.65f, 0.2f, 0.9f)),
            ("pink", new Color(1f, 0.4f, 0.7f)),
        };

        public static bool TryGetColor(string tag, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(tag))
                return false;

            foreach (var (name, value) in Named)
                if (name == tag)
                {
                    color = value;
                    return true;
                }

            return ColorUtility.TryParseHtmlString(tag, out color);
        }
    }
}
