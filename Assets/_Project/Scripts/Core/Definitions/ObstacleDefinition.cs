using Purge.Core.Content;
using Purge.Core.Grid;
using UnityEngine;

namespace Purge.Core.Definitions
{
    /// <summary>
    /// A thing standing in a floor cell (tree, bush, rock, barrel…) and what it blocks (ТЗ: дерево — движение и пули,
    /// не обзор; куст — только движение; цветок — ничего; стена — всё). Gameplay only: its look is a Decor variant of
    /// the theme bound to this definition (<see cref="Visual.VisualVariant.Definition"/>), picked like decor.
    /// Content: a new obstacle is an asset in Data/Obstacles, no code.
    /// </summary>
    [CreateAssetMenu(fileName = "Obstacle", menuName = "Purge/Definitions/Obstacle")]
    public sealed class ObstacleDefinition : ContentDefinition
    {
        [SerializeField] private bool _blocksMovement = true;
        [SerializeField] private bool _blocksBullets = true;
        [SerializeField] private bool _blocksSight;

        [Tooltip("Fire spreads over it (bushes, dry grass).")]
        [SerializeField] private bool _flammable;

        [Tooltip("Share of the speed kept by walkers in the cell; 1 = no slowdown. Only for obstacles that can be walked.")]
        [SerializeField, Range(0.1f, 1f)] private float _speedFactor = 1f;

        public bool BlocksMovement => _blocksMovement;
        public bool BlocksBullets => _blocksBullets;
        public bool BlocksSight => _blocksSight;
        public bool Flammable => _flammable;

        /// <summary>Share of the speed kept in the cell (1 when it does not slow or cannot be walked).</summary>
        public float SpeedFactor => _blocksMovement ? 1f : _speedFactor;

        public CellFlags Flags =>
            (_blocksMovement ? CellFlags.BlocksMovement : CellFlags.None) |
            (_blocksBullets ? CellFlags.BlocksBullets : CellFlags.None) |
            (_blocksSight ? CellFlags.BlocksSight : CellFlags.None) |
            (_flammable ? CellFlags.Flammable : CellFlags.None) |
            (SpeedFactor < 1f ? CellFlags.Slows : CellFlags.None);

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, bool movement, bool bullets, bool sight, bool flammable = false, float speedFactor = 1f)
        {
            SetId(id);
            _blocksMovement = movement;
            _blocksBullets = bullets;
            _blocksSight = sight;
            _flammable = flammable;
            _speedFactor = speedFactor;
        }
    }
}
