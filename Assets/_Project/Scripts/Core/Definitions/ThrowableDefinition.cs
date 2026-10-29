using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Effects;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Definitions
{
    /// <summary>
    /// A throwable kind (ТЗ: гранаты, молотовы…): carried as a stack in the throwable slot, thrown along an arc to a
    /// cell (it flies over monsters, walls and whatever stops bullets cut it short), and its
    /// <see cref="EffectModule"/>s happen where it lands (explosion, zone, state). The look is defined separately
    /// (ItemVisualCatalog), never here.
    /// </summary>
    [CreateAssetMenu(fileName = "Throwable", menuName = "Purge/Definitions/Throwable")]
    public sealed class ThrowableDefinition : ContentDefinition
    {
        [SerializeField] private Rarity _rarity;

        [Tooltip("Open from the start without buying (stage 11б: the grenade): for the run's start and the drop.")]
        [SerializeField] private bool _startPool;

        [Tooltip("Most of this kind the slot holds.")]
        [SerializeField, Min(1)] private int _maxStack = 3;

        [Tooltip("Farthest cell it can be thrown to, cells from the player.")]
        [SerializeField, Min(1f)] private float _range = 8f;

        [Tooltip("Cells per second along the ground.")]
        [SerializeField, Min(1f)] private float _speed = 12f;

        [Tooltip("Top of the arc, × the distance (display only).")]
        [SerializeField, Range(0f, 1f)] private float _arcHeight = 0.3f;

        [Tooltip("Seconds between two throws.")]
        [SerializeField, Min(0f)] private float _cooldown = 0.6f;

        [Tooltip("How far the throw itself is heard by monsters, cells.")]
        [SerializeField, Min(0f)] private float _soundRadius = 3f;

        [Tooltip("What happens where it lands, in order.")]
        [SerializeReference, ModuleList] private List<EffectModule> _effects = new List<EffectModule>();

        public Rarity Rarity => _rarity;

        /// <summary>Open from the start without buying (stage 11б).</summary>
        public bool StartPool { get => _startPool; internal set => _startPool = value; }

        public int MaxStack => _maxStack;
        public float Range => _range;
        public float Speed => _speed;
        public float ArcHeight => _arcHeight;
        public float Cooldown => _cooldown;
        public float SoundRadius => _soundRadius;
        public IReadOnlyList<EffectModule> Effects => _effects;

        /// <summary>The widest effect's reach, cells (the aim's ring).</summary>
        public float EffectRadius
        {
            get
            {
                var radius = 0f;
                foreach (var effect in _effects)
                    if (effect != null && effect.Radius > radius) radius = effect.Radius;
                return radius;
            }
        }

        public void Validate(List<string> problems)
        {
            var owner = $"Throwable '{name}'";
            Purge.Core.Modules.Modules.Validate(_effects, owner, problems);
            if (_effects.Count == 0) problems.Add($"{owner}: has no effects (does nothing where it lands).");
        }

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, int maxStack, float range, params EffectModule[] effects)
        {
            SetId(id);
            _maxStack = maxStack;
            _range = range;
            _effects = new List<EffectModule>(effects);
        }

        internal void ConfigureFlight(float speed, float cooldown)
        {
            _speed = speed;
            _cooldown = cooldown;
        }

        /// <summary>Tools: the effects after editing them in place.</summary>
        internal List<EffectModule> MutableEffects => _effects;

        internal void SetRarity(Rarity rarity) => _rarity = rarity;
    }
}
