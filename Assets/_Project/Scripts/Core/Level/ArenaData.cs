using System;
using System.Collections.Generic;
using Purge.Core.Grid;
using UnityEngine;

namespace Purge.Core.Level
{
    /// <summary>
    /// The boss arena of a level (ТЗ А.7, stage 7): an open rectangle surrounded by a ring of walls with a few wide
    /// entries. The generator makes it; bosses (stage 8) appear in its centre and close the entries with gates.
    /// </summary>
    [Serializable]
    public sealed class ArenaData
    {
        [Tooltip("Open cells of the arena (without the ring around it).")]
        [SerializeField] private GridRect _rect;

        [SerializeField] private int _floor;

        [Tooltip("Width of the wall ring around the arena, cells.")]
        [SerializeField, Min(0)] private int _ring;

        [SerializeField] private List<ArenaEntry> _entries = new List<ArenaEntry>();

        public ArenaData(GridRect rect, int ring, int floor = 0)
        {
            _rect = rect;
            _ring = ring;
            _floor = floor;
        }

        public GridRect Rect => _rect;
        public int Floor => _floor;
        public int Ring => _ring;
        public bool IsEmpty => _rect.IsEmpty;
        public IReadOnlyList<ArenaEntry> Entries => _entries;

        /// <summary>The arena with its ring.</summary>
        public GridRect Outer => new GridRect(_rect.X - _ring, _rect.Y - _ring, _rect.Width + 2 * _ring, _rect.Height + 2 * _ring);

        public GridPosition Center => new GridPosition(_rect.X + _rect.Width / 2, _rect.Y + _rect.Height / 2, _floor);

        internal List<ArenaEntry> MutableEntries => _entries;
    }

    /// <summary>A gap in the arena ring: the cells of the gap (across the ring) and the side of the arena it is on.</summary>
    [Serializable]
    public sealed class ArenaEntry
    {
        [SerializeField] private GridRect _gap;
        [SerializeField] private Direction _side;

        public ArenaEntry(GridRect gap, Direction side)
        {
            _gap = gap;
            _side = side;
        }

        public GridRect Gap => _gap;
        public Direction Side => _side;

        /// <summary>Width of the passage (cells along the ring).</summary>
        public int Width => _side == Direction.North || _side == Direction.South ? _gap.Width : _gap.Height;
    }
}
