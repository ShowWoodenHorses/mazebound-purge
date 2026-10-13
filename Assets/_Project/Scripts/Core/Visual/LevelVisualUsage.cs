using System;
using System.Collections.Generic;
using Maze.Core.Common;
using Maze.Core.Level;

namespace Maze.Core.Visual
{
    /// <summary>Identifies one visual variant of a theme: the set (kind) and the variant inside it.</summary>
    public readonly struct VisualKey : IEquatable<VisualKey>
    {
        public VisualKey(VisualKind kind, string variantId)
        {
            Kind = kind;
            VariantId = variantId;
        }

        public VisualKind Kind { get; }
        public string VariantId { get; }

        public bool Equals(VisualKey other) => Kind == other.Kind && string.Equals(VariantId, other.VariantId, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is VisualKey other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ StableHash.Of(VariantId ?? string.Empty).GetHashCode();
        public override string ToString() => $"{Kind}/{VariantId}";
    }

    /// <summary>
    /// Which visual variants a level actually shows (resolved with <see cref="VisualResolver"/>), so runtime loads
    /// only those prefabs instead of whole themes (ТЗ §44).
    /// </summary>
    public static class LevelVisualUsage
    {
        /// <summary>Distinct variants of the cell layers (decor included), of every placed object that has a visual and of light fixtures, in stable order.</summary>
        public static List<VisualKey> Collect(LevelData level)
        {
            var seen = new HashSet<VisualKey>();
            var result = new List<VisualKey>();

            void Add(VisualKind kind, VisualChoice choice)
            {
                if (choice.IsEmpty) return;
                var key = new VisualKey(kind, choice.VariantId);
                if (seen.Add(key)) result.Add(key);
            }

            var geometry = level.Geometry;
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var position = geometry.ToPosition(i);
                foreach (var layer in CellLayers.All)
                    Add(CellLayers.Kind(layer), VisualResolver.ResolveCell(level, position, layer));
                Add(VisualKind.Decor, VisualResolver.ResolveDecor(level, position));
            }

            foreach (var entity in level.AllEntities())
                if (VisualKinds.TryGetForEntity(entity, out var kind, out _))
                    Add(kind, VisualResolver.ResolveObject(level, entity));

            foreach (var light in level.Lights)
                if (LightFixtures.TryGetWallSide(level.Geometry, light, out _))
                    Add(VisualKind.Light, LightFixtures.Resolve(level, light));

            return result;
        }
    }
}
