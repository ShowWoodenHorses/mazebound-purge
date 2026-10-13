using Maze.Core.Grid;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Neighbourhood of a cell used to pick its visual shape (ТЗ §33). Cells outside the grid read as Wall.
    /// Describes existing geometry only; it never changes it.
    /// </summary>
    public readonly struct CellVisualContext
    {
        public CellVisualContext(LevelGeometry geometry, GridPosition position)
        {
            Position = position;
            CellType = geometry.GetCell(position);
            North = Read(geometry, position, 0, 1);
            South = Read(geometry, position, 0, -1);
            East = Read(geometry, position, 1, 0);
            West = Read(geometry, position, -1, 0);
            NorthEast = Read(geometry, position, 1, 1);
            NorthWest = Read(geometry, position, -1, 1);
            SouthEast = Read(geometry, position, 1, -1);
            SouthWest = Read(geometry, position, -1, -1);
            WallConnections = ConnectionMask(geometry, position);
        }

        public GridPosition Position { get; }
        public CellType CellType { get; }
        public CellType North { get; }
        public CellType South { get; }
        public CellType East { get; }
        public CellType West { get; }
        public CellType NorthEast { get; }
        public CellType NorthWest { get; }
        public CellType SouthEast { get; }
        public CellType SouthWest { get; }

        /// <summary>
        /// Bit mask of cardinal neighbours that a wall visually connects to (Wall or Door; outside does not
        /// connect, so border walls read as straight). Bits: North = 1, East = 2, South = 4, West = 8.
        /// </summary>
        public int WallConnections { get; }

        public CellType Get(Direction direction)
        {
            switch (direction)
            {
                case Direction.North: return North;
                case Direction.East: return East;
                case Direction.South: return South;
                default: return West;
            }
        }

        private static CellType Read(LevelGeometry geometry, GridPosition position, int dx, int dy)
        {
            var p = new GridPosition(position.X + dx, position.Y + dy);
            return geometry.IsInside(p) ? geometry.GetCell(p) : CellType.Wall;
        }

        private static int ConnectionMask(LevelGeometry geometry, GridPosition position)
        {
            var mask = 0;
            foreach (var direction in DirectionExtensions.All)
            {
                var p = position.Neighbour(direction);
                if (geometry.IsInside(p) && geometry.GetCell(p) != CellType.Floor)
                    mask |= 1 << (int)direction;
            }

            return mask;
        }
    }

    /// <summary>
    /// Maps a wall connection mask to a category and rotation. Prefabs are authored in canonical orientation:
    /// End connects North; Straight connects North+South; Corner connects North+East;
    /// TJunction connects North+East+South. Rotation is clockwise quarter turns from canonical.
    /// </summary>
    public static class WallShapes
    {
        private static readonly (VisualCategory Category, int Rotation)[] Table = BuildTable();

        public static (VisualCategory Category, int Rotation) Classify(int connectionMask) => Table[connectionMask & 15];

        public static int RotateClockwise(int mask, int quarterTurns)
        {
            for (var i = 0; i < quarterTurns; i++)
                mask = ((mask << 1) | (mask >> 3)) & 15;

            return mask;
        }

        private static (VisualCategory, int)[] BuildTable()
        {
            var table = new (VisualCategory, int)[16];
            var filled = new bool[16];

            void Add(VisualCategory category, int canonicalMask)
            {
                for (var r = 0; r < 4; r++)
                {
                    var mask = RotateClockwise(canonicalMask, r);
                    if (filled[mask])
                        continue;

                    table[mask] = (category, r);
                    filled[mask] = true;
                }
            }

            Add(VisualCategory.Isolated, 0);
            Add(VisualCategory.End, 1);
            Add(VisualCategory.Straight, 1 | 4);
            Add(VisualCategory.Corner, 1 | 2);
            Add(VisualCategory.TJunction, 1 | 2 | 4);
            Add(VisualCategory.Cross, 15);
            return table;
        }
    }
}
