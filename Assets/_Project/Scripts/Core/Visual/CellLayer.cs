using Maze.Core.Grid;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Visual layers of a cell. Every cell (wall, floor, door) has a Floor layer, so the floor is visually
    /// continuous under thin walls; Wall cells additionally have a Wall layer on top. Floor cells may have one
    /// optional Decor item (empty is normal, unlike the other layers).
    /// Purely visual: gameplay semantics come from <see cref="CellType"/> only.
    /// </summary>
    public enum CellLayer
    {
        Floor = 0,
        Wall = 1,
        Decor = 2,
    }

    public static class CellLayers
    {
        /// <summary>The layers every cell must have a visual for (where they exist). Decor is optional and separate.</summary>
        public static readonly CellLayer[] All = { CellLayer.Floor, CellLayer.Wall };

        public static bool Exists(CellType cellType, CellLayer layer)
        {
            switch (layer)
            {
                case CellLayer.Floor: return true;
                case CellLayer.Wall: return cellType == CellType.Wall;
                default: return cellType == CellType.Floor; // Decor: not in walls and doors.
            }
        }

        public static VisualKind Kind(CellLayer layer)
        {
            switch (layer)
            {
                case CellLayer.Floor: return VisualKind.Floor;
                case CellLayer.Wall: return VisualKind.Wall;
                default: return VisualKind.Decor;
            }
        }
    }
}
