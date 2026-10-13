using System;
using System.Collections.Generic;
using Maze.Core.Grid;

namespace Maze.Core.Visibility
{
    /// <summary>
    /// Cells the player can see (ТЗ §53–55): a square window of <c>2·radius+1</c> cells around the player's cell,
    /// limited by line of sight. Reused between computations, so recalculation does not allocate.
    /// <para>
    /// Rules. The player stands somewhere in their cell, so rays are cast from sample points of the origin cell
    /// (centre and four inset corners) to the same sample points of every cell of the window. Every cell a ray
    /// passes through before it is blocked is visible, including the opaque cell that blocks it (the wall is seen) —
    /// so a far cell is never seen while a nearer cell on the same line is not.
    /// An opaque cell (wall, closed door) is also visible when it shares a side with a visible transparent cell
    /// (the walls enclosing visible floor), or when it closes a corner of visible floor: touches it diagonally with
    /// both cells in between being such visible walls. A wall touching visible floor only diagonally, across hidden
    /// floor, stays hidden — it is the far wall of a space the player does not see.
    /// Cells outside the grid are never visible.
    /// </para>
    /// <para>
    /// Revealed cells (<see cref="IsRevealed"/>) are for static geometry only: the visible cells plus single hidden
    /// cells of the window that have visible transparent cells (floor, open door) on two opposite sides (W+E or
    /// S+N): such a cell would otherwise be a one-cell hole in the shown floor. Cells boxed in by walls or closed
    /// doors are not filled — what is behind them stays hidden.
    /// Objects (zombies, pickups) use the strict <see cref="IsVisible"/> (ТЗ §54).
    /// </para>
    /// </summary>
    public sealed class FieldOfView
    {
        /// <summary>5 cells in every direction → 11×11 (ТЗ §53).</summary>
        public const int DefaultRadius = 5;

        /// <summary>Sample points are this far from the cell centre: inside the cell, off its exact corners.</summary>
        private const float Inset = 0.45f;

        private static readonly float[] SampleX = { 0f, -Inset, Inset, -Inset, Inset };
        private static readonly float[] SampleY = { 0f, -Inset, -Inset, Inset, Inset };

        private readonly bool[] _visible;
        private readonly List<GridPosition> _cells = new List<GridPosition>();
        private readonly List<GridPosition> _opaqueCandidates = new List<GridPosition>();
        private readonly List<GridPosition> _corners = new List<GridPosition>();
        private readonly bool[] _revealed;
        private readonly List<GridPosition> _revealedCells = new List<GridPosition>();

        public FieldOfView(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), $"Invalid grid size {width}x{height}.");

            Width = width;
            Height = height;
            _visible = new bool[width * height];
            _revealed = new bool[width * height];
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>False until the first <see cref="Compute{T}"/>.</summary>
        public bool HasResult { get; private set; }

        public GridPosition Origin { get; private set; }
        public int Radius { get; private set; }

        /// <summary>Visible cells of the last computation, in no particular order.</summary>
        public IReadOnlyList<GridPosition> VisibleCells => _cells;

        public bool IsVisible(GridPosition cell) =>
            cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height && _visible[cell.Y * Width + cell.X];

        /// <summary>Cells whose static geometry is shown: visible cells plus filled one-cell gaps.</summary>
        public IReadOnlyList<GridPosition> RevealedCells => _revealedCells;

        public bool IsRevealed(GridPosition cell) =>
            cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height && _revealed[cell.Y * Width + cell.X];

        /// <summary>Square window around <paramref name="origin"/>, clipped to the grid.</summary>
        public static GridRect Window(GridPosition origin, int radius, int width, int height)
        {
            var minX = Math.Max(0, origin.X - radius);
            var minY = Math.Max(0, origin.Y - radius);
            var maxX = Math.Min(width - 1, origin.X + radius);
            var maxY = Math.Min(height - 1, origin.Y + radius);
            return maxX < minX || maxY < minY ? new GridRect(0, 0, 0, 0) : new GridRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        public void Compute<T>(GridPosition origin, int radius, in T opacity) where T : IGridOpacity
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, null);

            foreach (var cell in _cells)
                _visible[cell.Y * Width + cell.X] = false;
            _cells.Clear();
            foreach (var cell in _revealedCells)
                _revealed[cell.Y * Width + cell.X] = false;
            _revealedCells.Clear();
            _opaqueCandidates.Clear();

            Origin = origin;
            Radius = radius;
            HasResult = true;

            var window = Window(origin, radius, Width, Height);
            var marker = new WindowMarker(this, window);
            marker.Visit(origin);

            // Rays to sample points of every cell of the window; every cell a ray passes through is visible, so
            // visibility has no holes along a line (a far cell seen while a nearer cell on the same line is not).
            for (var y = window.Y; y < window.YMax; y++)
            for (var x = window.X; x < window.XMax; x++)
            {
                if (x == origin.X && y == origin.Y) continue;
                CastRays(origin, new GridPosition(x, y), opacity, ref marker);
            }

            // Walls bordering visible floor: checked against the transparent cells found by rays only.
            for (var y = window.Y; y < window.YMax; y++)
            for (var x = window.X; x < window.XMax; x++)
            {
                var cell = new GridPosition(x, y);
                if (!IsVisible(cell) && opacity.IsOpaque(cell))
                    _opaqueCandidates.Add(cell);
            }

            for (var i = _opaqueCandidates.Count - 1; i >= 0; i--)
                if (BordersVisibleTransparent(_opaqueCandidates[i], opacity))
                {
                    MarkVisible(_opaqueCandidates[i]);
                    _opaqueCandidates.RemoveAt(i);
                }

            // Corner pieces: decided on the walls found so far, then applied, so corners never chain.
            _corners.Clear();
            foreach (var cell in _opaqueCandidates)
                if (IsCornerOfVisibleFloor(cell, opacity))
                    _corners.Add(cell);
            foreach (var cell in _corners)
                MarkVisible(cell);

            // Geometry: visible cells plus one-cell gaps between them (judged by strict visibility, no chains).
            foreach (var cell in _cells)
                MarkRevealed(cell);
            for (var y = window.Y; y < window.YMax; y++)
            for (var x = window.X; x < window.XMax; x++)
            {
                var cell = new GridPosition(x, y);
                if (!IsVisible(cell) && IsGap(cell, opacity))
                    MarkRevealed(cell);
            }
        }

        /// <summary>
        /// A hidden cell between two visible transparent cells (W+E or S+N). Walls do not count: a room boxed in by
        /// walls and a closed door seen from outside stays hidden (ТЗ §55).
        /// </summary>
        private bool IsGap<T>(GridPosition cell, in T opacity) where T : IGridOpacity
        {
            return IsSeenThrough(new GridPosition(cell.X - 1, cell.Y), opacity) && IsSeenThrough(new GridPosition(cell.X + 1, cell.Y), opacity) ||
                   IsSeenThrough(new GridPosition(cell.X, cell.Y - 1), opacity) && IsSeenThrough(new GridPosition(cell.X, cell.Y + 1), opacity);
        }

        private bool IsSeenThrough<T>(GridPosition cell, in T opacity) where T : IGridOpacity =>
            IsVisible(cell) && !opacity.IsOpaque(cell);

        private void MarkRevealed(GridPosition cell)
        {
            _revealed[cell.Y * Width + cell.X] = true;
            _revealedCells.Add(cell);
        }

        private void MarkVisible(GridPosition cell)
        {
            var index = cell.Y * Width + cell.X;
            if (_visible[index]) return;
            _visible[index] = true;
            _cells.Add(cell);
        }

        /// <summary>The wall shares a side with a visible transparent cell (it encloses that floor).</summary>
        private bool BordersVisibleTransparent<T>(GridPosition cell, in T opacity) where T : IGridOpacity =>
            IsSeenThrough(new GridPosition(cell.X - 1, cell.Y), opacity) ||
            IsSeenThrough(new GridPosition(cell.X + 1, cell.Y), opacity) ||
            IsSeenThrough(new GridPosition(cell.X, cell.Y - 1), opacity) ||
            IsSeenThrough(new GridPosition(cell.X, cell.Y + 1), opacity);

        /// <summary>
        /// The wall closes the corner of visible floor: it touches a visible transparent cell diagonally and both cells
        /// between them are visible walls. A wall touching floor only diagonally across hidden floor is not a corner —
        /// it belongs to a space the player does not see (e.g. the far wall of a room behind a closed door).
        /// </summary>
        private bool IsCornerOfVisibleFloor<T>(GridPosition cell, in T opacity) where T : IGridOpacity
        {
            for (var dy = -1; dy <= 1; dy += 2)
            for (var dx = -1; dx <= 1; dx += 2)
            {
                var sideX = new GridPosition(cell.X + dx, cell.Y);
                var sideY = new GridPosition(cell.X, cell.Y + dy);
                if (IsSeenThrough(new GridPosition(cell.X + dx, cell.Y + dy), opacity) &&
                    IsVisible(sideX) && opacity.IsOpaque(sideX) &&
                    IsVisible(sideY) && opacity.IsOpaque(sideY))
                    return true;
            }

            return false;
        }

        private static void CastRays<T>(GridPosition from, GridPosition to, in T opacity, ref WindowMarker marker)
            where T : IGridOpacity
        {
            for (var i = 0; i < SampleX.Length; i++)
            for (var j = 0; j < SampleX.Length; j++)
                GridLineOfSight.Trace(
                    from.X + SampleX[i], from.Y + SampleY[i],
                    to.X + SampleX[j], to.Y + SampleY[j], opacity, ref marker);
        }

        /// <summary>Marks cells crossed by rays as visible, ignoring anything outside the window.</summary>
        private struct WindowMarker : IGridCellVisitor
        {
            private readonly FieldOfView _owner;
            private readonly GridRect _window;

            public WindowMarker(FieldOfView owner, GridRect window)
            {
                _owner = owner;
                _window = window;
            }

            public void Visit(GridPosition cell)
            {
                if (_window.Contains(cell))
                    _owner.MarkVisible(cell);
            }
        }
    }
}
