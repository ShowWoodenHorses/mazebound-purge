using System.Collections.Generic;
using UnityEngine;

namespace Maze.Core.Visual
{
    /// <summary>Reusable set of weighted visual variants for one <see cref="VisualKind"/> (ТЗ §22–23).</summary>
    [CreateAssetMenu(fileName = "VisualSet", menuName = "Maze/Visual/Visual Set")]
    public sealed class VisualSet : ScriptableObject
    {
        [SerializeField] private VisualKind _kind;

        [Tooltip("Explicit fallback when no variant matches the required category. Empty = no fallback (validation error).")]
        [SerializeField] private string _defaultVariantId;

        [SerializeField] private List<VisualVariant> _variants = new List<VisualVariant>();

        public VisualKind Kind { get => _kind; internal set => _kind = value; }
        public string DefaultVariantId { get => _defaultVariantId; internal set => _defaultVariantId = value; }
        public IReadOnlyList<VisualVariant> Variants => _variants;

        internal List<VisualVariant> MutableVariants => _variants;

        public VisualVariant FindVariant(string variantId)
        {
            if (string.IsNullOrEmpty(variantId))
                return null;

            foreach (var variant in _variants)
                if (variant.Id == variantId)
                    return variant;

            return null;
        }
    }
}
