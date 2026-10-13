using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Core.Visual
{
    /// <summary>
    /// One visual option inside a <see cref="VisualSet"/>. Weight matters only while assigning visuals
    /// at level creation time; runtime uses the saved VariantId and ignores weights.
    /// </summary>
    [Serializable]
    public sealed class VisualVariant
    {
        [SerializeField] private string _id;
        [SerializeField] private AssetReferenceGameObject _prefab;
        [SerializeField, Min(0)] private int _weight = 1;
        [SerializeField] private VisualCategory _category;

        [Tooltip("Optional. Restricts this variant to entities with this gameplay definition (e.g. a specific weapon or zombie type).")]
        [SerializeField] private ScriptableObject _definition;

        [Tooltip("Key/door pair colour (e.g. 'red'). Locked doors use coloured variants, unlocked doors uncoloured; " +
                 "a key always uses the colour of its door.")]
        [SerializeField] private string _colorTag;

        [Tooltip("Decor: height of the model in metres, measured from the prefab by the editor (Regenerate Visuals, " +
                 "Place Decor). Taller than the level's Max Auto Decor Height = placed only by hand.")]
        [SerializeField, Min(0f)] private float _height;

        public VisualVariant(string id, int weight = 1, VisualCategory category = VisualCategory.General,
            ScriptableObject definition = null, AssetReferenceGameObject prefab = null, string colorTag = null)
        {
            _id = id;
            _weight = weight;
            _category = category;
            _definition = definition;
            _prefab = prefab;
            _colorTag = colorTag;
        }

        public string ColorTag => _colorTag;
        public bool HasColor => !string.IsNullOrEmpty(_colorTag);

        public string Id => _id;
        public AssetReferenceGameObject Prefab => _prefab;
        public int Weight => _weight;
        public VisualCategory Category => _category;
        public ScriptableObject Definition => _definition;

        /// <summary>Model height, metres (decor; measured by the editor).</summary>
        public float Height { get => _height; internal set => _height = value; }

        public bool Matches(VisualCategory category, ScriptableObject definition) =>
            _category == category && (_definition == null || _definition == definition);
    }
}
