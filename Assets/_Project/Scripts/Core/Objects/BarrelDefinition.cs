using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Effects;
using Purge.Core.Level;
using Purge.Core.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Core.Objects
{
    /// <summary>
    /// A kind of explosive barrel (ТЗ 7в): HP, and the effects of its blast (an explosion and a zone of its kind) — the
    /// player's own, they never hurt the player. One asset per <see cref="BarrelKind"/> (<c>Data/Objects</c>); a level's
    /// <see cref="BarrelData"/> names the kind. The model is display only.
    /// </summary>
    [CreateAssetMenu(fileName = "Barrel", menuName = "Purge/Definitions/Barrel")]
    public sealed class BarrelDefinition : ContentDefinition
    {
        [SerializeField] private BarrelKind _kind;

        [Tooltip("Damage it takes before it blows up.")]
        [SerializeField, Min(1f)] private float _health = 30f;

        [Tooltip("What happens where it blows up, in order (the player's own: monsters only).")]
        [SerializeReference, ModuleList] private List<EffectModule> _effects = new List<EffectModule>();

        [Tooltip("The model standing in the cell (display only).")]
        [SerializeField] private AssetReferenceGameObject _prefab;

        public BarrelKind Kind => _kind;
        public float Health => _health;
        public IReadOnlyList<EffectModule> Effects => _effects;
        public AssetReferenceGameObject Prefab => _prefab;

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, BarrelKind kind, float health, params EffectModule[] effects)
        {
            SetId(id);
            _kind = kind;
            _health = health;
            _effects = new List<EffectModule>(effects);
        }

        internal List<EffectModule> MutableEffects => _effects ??= new List<EffectModule>();
        internal void SetPrefab(AssetReferenceGameObject prefab) => _prefab = prefab;
    }
}
