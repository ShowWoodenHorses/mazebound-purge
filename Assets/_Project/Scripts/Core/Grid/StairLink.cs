using System;
using UnityEngine;

namespace Purge.Core.Grid
{
    /// <summary>A two-cell stair footprint shared by two consecutive floors.</summary>
    [Serializable]
    public struct StairLink
    {
        [SerializeField] private GridPosition _lower;
        [SerializeField] private bool _alongX;

        public StairLink(GridPosition lower, bool alongX)
        {
            _lower = lower;
            _alongX = alongX;
        }

        public int LowerFloor => _lower.Floor;
        public int UpperFloor => LowerFloor + 1;
        public GridPosition First => _lower;
        public GridPosition Second => new GridPosition(_lower.X + (_alongX ? 1 : 0),
            _lower.Y + (_alongX ? 0 : 1), LowerFloor);

        private GridPosition Axis => Second - First;
        public GridPosition LowerLanding => First - Axis;
        public GridPosition UpperLanding => (Second + Axis).OnFloor(UpperFloor);
        public Vector3 Bottom => First.ToWorld() - Axis.ToWorld() * 0.5f;
        public Vector3 Top => Second.OnFloor(UpperFloor).ToWorld() + Axis.ToWorld() * 0.5f;

        public GridPosition ExitFrom(GridPosition entrance) => entrance == LowerLanding ? UpperLanding : LowerLanding;
        public bool Uses(GridPosition cell) => Contains(cell) || cell == LowerLanding || cell == UpperLanding;

        internal bool Overlaps(StairLink other) =>
            other.Uses(First) || other.Uses(Second) || other.Uses(First.OnFloor(UpperFloor)) ||
            other.Uses(Second.OnFloor(UpperFloor)) || other.Uses(LowerLanding) || other.Uses(UpperLanding);

        public bool Contains(GridPosition cell) =>
            (cell.Floor == LowerFloor || cell.Floor == UpperFloor) &&
            (cell.OnFloor(LowerFloor) == First || cell.OnFloor(LowerFloor) == Second);

        internal bool Fits(LevelGeometry geometry) =>
            IsLanding(geometry, LowerLanding) && IsLanding(geometry, UpperLanding) &&
            IsLanding(geometry, First) && IsLanding(geometry, Second) &&
            IsLanding(geometry, First.OnFloor(UpperFloor)) && IsLanding(geometry, Second.OnFloor(UpperFloor));

        private static bool IsLanding(LevelGeometry geometry, GridPosition cell) =>
            geometry.IsInside(cell) && geometry.GetCell(cell) == CellType.Floor &&
            (geometry.GetFlags(cell) & CellFlags.BlocksMovement) == 0;
    }
}
