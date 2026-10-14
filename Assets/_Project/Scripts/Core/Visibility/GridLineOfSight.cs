using System;
using Purge.Core.Grid;

namespace Purge.Core.Visibility
{
    /// <summary>
    /// Answers "does this cell block sight?" (ТЗ §55: Wall and closed Door; outside the grid counts as Wall).
    /// Implemented by structs and passed as a generic argument, so line-of-sight checks neither box nor allocate.
    /// </summary>
    public interface IGridOpacity
    {
        bool IsOpaque(GridPosition position);
    }

    /// <summary>Receives cells crossed by a line (<see cref="GridLineOfSight.Trace{TOpacity,TVisitor}"/>). Use a struct.</summary>
    public interface IGridCellVisitor
    {
        void Visit(GridPosition cell);
    }

    /// <summary>
    /// Opacity backed by a per-cell mask with the grid's indexing ((floor · height + y) · width + x; the length says
    /// how many floors it covers). Outside is opaque.
    /// </summary>
    public readonly struct CellMaskOpacity : IGridOpacity
    {
        private readonly bool[] _opaque;
        private readonly int _width;
        private readonly int _height;

        public CellMaskOpacity(int width, int height, bool[] opaque)
        {
            _width = width;
            _height = height;
            _opaque = opaque;
        }

        public bool IsOpaque(GridPosition position) =>
            position.X < 0 || position.X >= _width || position.Y < 0 || position.Y >= _height || position.Floor < 0 ||
            (position.Floor * _height + position.Y) * _width + position.X >= _opaque.Length ||
            _opaque[(position.Floor * _height + position.Y) * _width + position.X];
    }

    /// <summary>
    /// Straight segment through the grid (Amanatides–Woo traversal). Points are in grid units: cell (x, y) spans
    /// x−0.5..x+0.5, y−0.5..y+0.5, on one <c>floor</c> (lines never leave their floor). Shared by visibility now and by
    /// bullets later (same blockers, ТЗ §55, §72).
    /// </summary>
    public static class GridLineOfSight
    {
        private const float CornerEpsilon = 1e-5f;

        public static GridPosition CellOf(float x, float y, int floor = 0) =>
            new GridPosition((int)Math.Floor(x + 0.5f), (int)Math.Floor(y + 0.5f), floor);

        /// <summary>
        /// True when no opaque cell lies strictly between the start cell and the end cell: the end cell itself may
        /// be opaque (a wall is seen when the line reaches it). A line passing exactly through a grid corner is
        /// blocked only when both cells beside that corner are opaque (no seeing through diagonal gaps).
        /// </summary>
        public static bool IsClear<T>(float fromX, float fromY, float toX, float toY, in T opacity, int floor = 0)
            where T : IGridOpacity
        {
            var none = default(NoVisitor);
            return Trace(fromX, fromY, toX, toY, opacity, ref none, floor);
        }

        /// <summary>
        /// Same as <see cref="IsClear{T}"/>, and reports to <paramref name="visitor"/> every cell the segment enters
        /// after the start cell, in order, up to and including the end cell or the opaque cell that blocks it
        /// (a wall the line runs into is seen). Cells only touched at a corner are not reported.
        /// </summary>
        public static bool Trace<TOpacity, TVisitor>(float fromX, float fromY, float toX, float toY, in TOpacity opacity,
            ref TVisitor visitor, int floor = 0)
            where TOpacity : IGridOpacity
            where TVisitor : IGridCellVisitor
        {
            var cell = CellOf(fromX, fromY, floor);
            var end = CellOf(toX, toY, floor);
            int x = cell.X, y = cell.Y;
            if (x == end.X && y == end.Y)
                return true;

            var dx = toX - fromX;
            var dy = toY - fromY;
            var stepX = Math.Sign(dx);
            var stepY = Math.Sign(dy);
            var tDeltaX = stepX != 0 ? 1f / Math.Abs(dx) : float.PositiveInfinity;
            var tDeltaY = stepY != 0 ? 1f / Math.Abs(dy) : float.PositiveInfinity;
            var tMaxX = stepX != 0 ? (x + 0.5f * stepX - fromX) / dx : float.PositiveInfinity;
            var tMaxY = stepY != 0 ? (y + 0.5f * stepY - fromY) / dy : float.PositiveInfinity;

            // Each step moves one cell closer to the end; the guard only protects against float edge cases.
            for (var guard = Math.Abs(end.X - x) + Math.Abs(end.Y - y) + 2; guard > 0; guard--)
            {
                if (Math.Abs(tMaxX - tMaxY) < CornerEpsilon)
                {
                    if (opacity.IsOpaque(new GridPosition(x + stepX, y, floor)) && opacity.IsOpaque(new GridPosition(x, y + stepY, floor)))
                        return false;
                    x += stepX;
                    y += stepY;
                    tMaxX += tDeltaX;
                    tMaxY += tDeltaY;
                }
                else if (tMaxX < tMaxY)
                {
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    y += stepY;
                    tMaxY += tDeltaY;
                }

                var entered = new GridPosition(x, y, floor);
                visitor.Visit(entered);
                if (x == end.X && y == end.Y)
                    return true;
                if (opacity.IsOpaque(entered))
                    return false;
            }

            return false;
        }

        private struct NoVisitor : IGridCellVisitor
        {
            public void Visit(GridPosition cell) { }
        }
    }
}
