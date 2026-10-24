using System;
using System.Collections.Generic;
using Purge.Core.Grid;
using UnityEngine;

namespace Purge.Core.Level
{
    /// <summary>
    /// What the biome generator must not touch when it runs again ("Generate", stage 7): cells edited by hand in the
    /// Level Designer (every cell edit marks its cell) and locked areas (rectangles). Such cells keep
    /// their structure, surface, obstacle, objects and visual overrides. Editor data only; runtime never reads it.
    /// Locked areas are drawn with the Lock Area brush of the Level Designer (stage 7б).
    /// </summary>
    [Serializable]
    public sealed class AuthoringMarks
    {
        [Tooltip("1 = the cell was edited by hand, same indexing as the cells; empty = none.")]
        [SerializeField] private byte[] _manual = Array.Empty<byte>();

        [SerializeField] private List<LockedArea> _lockedAreas = new List<LockedArea>();

        public IReadOnlyList<LockedArea> LockedAreas => _lockedAreas;

        public int ManualCount
        {
            get
            {
                var count = 0;
                if (_manual != null)
                    foreach (var mark in _manual)
                        if (mark != 0) count++;
                return count;
            }
        }

        public bool IsManual(LevelGeometry geometry, GridPosition position)
        {
            if (!geometry.IsInside(position) || _manual == null || _manual.Length != geometry.CellCount) return false;
            return _manual[geometry.ToIndex(position)] != 0;
        }

        public bool IsLocked(GridPosition position) => FindLockedArea(position) != null;

        /// <summary>The last added locked area holding the cell; null when none does.</summary>
        public LockedArea FindLockedArea(GridPosition position)
        {
            for (var i = _lockedAreas.Count - 1; i >= 0; i--)
                if (_lockedAreas[i].Floor == position.Floor && _lockedAreas[i].Rect.Contains(position))
                    return _lockedAreas[i];
            return null;
        }

        /// <summary>The generator keeps the cell: edited by hand or in a locked area.</summary>
        public bool IsKept(LevelGeometry geometry, GridPosition position) =>
            IsManual(geometry, position) || IsLocked(position);

        /// <summary>Kept flag of every cell of <paramref name="geometry"/>.</summary>
        public bool[] KeptMask(LevelGeometry geometry)
        {
            var mask = new bool[geometry.CellCount];
            for (var i = 0; i < mask.Length; i++)
                mask[i] = IsKept(geometry, geometry.ToPosition(i));
            return mask;
        }

        internal void MarkManual(LevelGeometry geometry, GridPosition position)
        {
            if (!geometry.IsInside(position)) return;
            if (_manual == null || _manual.Length != geometry.CellCount)
                Resize(geometry.CellCount);
            _manual[geometry.ToIndex(position)] = 1;
        }

        /// <summary>Keeps the marks of the cells that still exist (floors added or removed at the top).</summary>
        internal void Resize(int cellCount)
        {
            var old = _manual ?? Array.Empty<byte>();
            _manual = new byte[cellCount];
            Array.Copy(old, _manual, Math.Min(old.Length, cellCount));
        }

        internal void ClearManual() => _manual = Array.Empty<byte>();

        internal void AddLockedArea(GridRect rect, int floor = 0) => _lockedAreas.Add(new LockedArea(rect, floor));

        internal bool RemoveLockedArea(LockedArea area) => _lockedAreas.Remove(area);

        internal void Clear()
        {
            ClearManual();
            _lockedAreas.Clear();
        }
    }

    /// <summary>A rectangle of cells on one floor the generator leaves as it is.</summary>
    [Serializable]
    public sealed class LockedArea
    {
        [SerializeField] private GridRect _rect;
        [SerializeField] private int _floor;

        public LockedArea(GridRect rect, int floor)
        {
            _rect = rect;
            _floor = floor;
        }

        public GridRect Rect => _rect;
        public int Floor => _floor;
    }
}
