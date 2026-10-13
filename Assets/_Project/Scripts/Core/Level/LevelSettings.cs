using System;
using UnityEngine;

namespace Purge.Core.Level
{
    /// <summary>Gameplay-facing settings of a level.</summary>
    [Serializable]
    public sealed class LevelSettings
    {
        [SerializeField] private string _displayName = "New Level";

        public string DisplayName { get => _displayName; internal set => _displayName = value; }
    }
}
