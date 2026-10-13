using System;
using System.Collections.Generic;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Core.Level;

namespace Maze.Gameplay.Map
{
    /// <summary>
    /// Map fragments of the level (ТЗ §58): which ones the player has collected. A collected fragment reveals its
    /// region on the map. Fragments lie in cells as pickups (<c>PickupSystem</c> collects them on entering the cell).
    /// Runtime state only: it starts empty on every load and is never saved (ТЗ §86).
    /// </summary>
    public sealed class MapSystem : IDisposable
    {
        private readonly IReadOnlyList<MapFragmentData> _fragments;
        private readonly HashSet<string> _collected = new HashSet<string>(StringComparer.Ordinal);

        public MapSystem(LevelData level)
        {
            _fragments = level.MapFragments;
        }

        public IReadOnlyList<MapFragmentData> Fragments => _fragments;
        public int TotalCount => _fragments.Count;
        public int CollectedCount => _collected.Count;

        /// <summary>True when every fragment is collected; also true for a level without fragments.</summary>
        public bool AllCollected => _collected.Count == _fragments.Count;

        public event Action<MapFragmentData> FragmentCollected;

        public bool IsCollected(MapFragmentData fragment) => fragment != null && _collected.Contains(fragment.Id);

        /// <summary>True when <paramref name="cell"/> lies in the region of a collected fragment.</summary>
        public bool IsRevealed(GridPosition cell)
        {
            foreach (var fragment in _fragments)
                if (fragment.Region.Contains(cell) && _collected.Contains(fragment.Id))
                    return true;

            return false;
        }

        /// <summary>Marks the fragment collected. False when it was already collected.</summary>
        public bool Collect(MapFragmentData fragment)
        {
            if (fragment == null) throw new ArgumentNullException(nameof(fragment));
            if (!_collected.Add(fragment.Id))
                return false;

            GameLog.Info(LogChannel.Gameplay, $"Collected map fragment '{fragment.Id}' ({CollectedCount}/{TotalCount}).");
            FragmentCollected?.Invoke(fragment);
            return true;
        }

        public void Dispose() => FragmentCollected = null;
    }
}
