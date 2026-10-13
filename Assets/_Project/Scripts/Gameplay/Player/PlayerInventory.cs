using System;
using System.Collections.Generic;
using Maze.Core.Level;

namespace Maze.Gameplay.Player
{
    /// <summary>Keys the player carries (ТЗ §63 Inventory). Weapons live in <see cref="Weapons.WeaponSystem"/>.</summary>
    public sealed class PlayerInventory
    {
        private readonly List<KeyData> _keys = new List<KeyData>();

        public IReadOnlyList<KeyData> Keys => _keys;

        /// <summary>Raised after the set of keys changed (picked up or used).</summary>
        public event Action KeysChanged;

        public bool HasKey(string keyId)
        {
            foreach (var key in _keys)
                if (key.Id == keyId)
                    return true;
            return false;
        }

        public void AddKey(KeyData key)
        {
            if (HasKey(key.Id)) return;
            _keys.Add(key);
            KeysChanged?.Invoke();
        }

        /// <summary>Removes a used key (1 key = 1 door). False when the player does not have it.</summary>
        public bool UseKey(string keyId)
        {
            for (var i = 0; i < _keys.Count; i++)
                if (_keys[i].Id == keyId)
                {
                    _keys.RemoveAt(i);
                    KeysChanged?.Invoke();
                    return true;
                }

            return false;
        }
    }
}
