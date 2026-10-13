using Maze.Core.Level;
using Maze.Core.Visual;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>Colour of an object's saved visual (key/door pair colour, <see cref="VisualVariant.ColorTag"/>).</summary>
    internal static class VisualColorTags
    {
        /// <summary>Colour tag of the object's saved visual variant, or null.</summary>
        public static string TagOf(LevelData level, VisualKind kind, LevelEntityData entity)
        {
            var set = level.VisualTheme != null ? level.VisualTheme.GetSet(kind) : null;
            var variant = set?.FindVariant(VisualResolver.ResolveObject(level, entity).VariantId);
            return variant != null && variant.HasColor ? variant.ColorTag : null;
        }

        /// <summary>Unity colour for a tag like "red", "pink" or "#FF8800" (<see cref="VisualColors"/>).</summary>
        public static bool TryGetColor(string tag, out Color color)
        {
            return VisualColors.TryGetColor(tag, out color);
        }
    }
}
