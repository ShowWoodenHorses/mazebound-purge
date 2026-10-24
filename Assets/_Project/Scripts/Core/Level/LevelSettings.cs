using System;
using UnityEngine;

namespace Purge.Core.Level
{
    /// <summary>Gameplay-facing settings of a level.</summary>
    [Serializable]
    public sealed class LevelSettings
    {
        [SerializeField] private string _displayName = "New Level";

        [Tooltip("The safe dome at the player's start (stage 7в): monsters stay out, it heals, limited time in a wave.")]
        [SerializeField] private bool _refuge;

        [Tooltip("Radius of the dome, cells from the start cell.")]
        [SerializeField, Min(1)] private int _refugeRadius = 2;

        public string DisplayName { get => _displayName; internal set => _displayName = value; }

        /// <summary>The level has the safe dome at its start.</summary>
        public bool Refuge { get => _refuge; internal set => _refuge = value; }

        /// <summary>Cells from the start cell the dome covers.</summary>
        public int RefugeRadius { get => _refugeRadius; internal set => _refugeRadius = Mathf.Max(1, value); }
    }
}
