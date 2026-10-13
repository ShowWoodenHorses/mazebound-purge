using Maze.Core.Common;
using Maze.Core.Grid;
using UnityEngine;

namespace Maze.Presentation.Map
{
    /// <summary>One icon on the map: what, where (cell units, x = East, y = North; a cell centre is x + 0.5), how.</summary>
    public struct MapIcon
    {
        public MapIconKind Kind;
        public Vector2 Center;

        /// <summary>Degrees counter-clockwise (UI rotation around z).</summary>
        public float Rotation;

        /// <summary>Size in cells.</summary>
        public float Size;

        public Color Color;
        public bool Visible;
    }

    /// <summary>
    /// Placement rules of map icons (no Unity objects, testable): the stable "careless" tilt and shift of an object's
    /// icon (by its id, ТЗ: no runtime randomness), door orientation, player arrow rotation.
    /// </summary>
    public static class MapIconLayout
    {
        private const ulong TiltSalt = 0x6D61705469U;
        private const ulong ShiftXSalt = 0x6D61705378U;
        private const ulong ShiftYSalt = 0x6D61705379U;

        /// <summary>Stable turn in [-maxTilt, maxTilt] degrees for the object <paramref name="id"/>.</summary>
        public static float Tilt(string id, float maxTilt) => Signed(id, TiltSalt) * maxTilt;

        /// <summary>Stable shift, each axis in [-maxShift, maxShift] cells, for the object <paramref name="id"/>.</summary>
        public static Vector2 Shift(string id, float maxShift) =>
            new Vector2(Signed(id, ShiftXSalt), Signed(id, ShiftYSalt)) * maxShift;

        /// <summary>
        /// The door sprite is a plank across a north-south passage (as door visuals at rotation 0); a door between walls
        /// to its north and south closes an east-west passage and is turned by 90°.
        /// </summary>
        public static float DoorRotation(LevelGrid grid, GridPosition cell)
        {
            var north = grid.GetCellOrWall(new GridPosition(cell.X, cell.Y + 1)) == CellType.Wall;
            var south = grid.GetCellOrWall(new GridPosition(cell.X, cell.Y - 1)) == CellType.Wall;
            var east = grid.GetCellOrWall(new GridPosition(cell.X + 1, cell.Y)) == CellType.Wall;
            var west = grid.GetCellOrWall(new GridPosition(cell.X - 1, cell.Y)) == CellType.Wall;
            return north && south && !(east && west) ? 90f : 0f;
        }

        /// <summary>The player sprite points north; turned to <paramref name="facing"/> (grid directions).</summary>
        public static float PlayerRotation(Vector2 facing) =>
            facing.sqrMagnitude > 1e-6f ? Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - 90f : 0f;

        /// <summary>Centre of a cell in map units.</summary>
        public static Vector2 CenterOf(GridPosition cell) => new Vector2(cell.X + 0.5f, cell.Y + 0.5f);

        /// <summary>Player position (grid units, a cell centre at integer coordinates) in map units.</summary>
        public static Vector2 CenterOf(Vector2 gridPosition) => gridPosition + new Vector2(0.5f, 0.5f);

        private static float Signed(string id, ulong salt)
        {
            var hash = StableHash.Combine(StableHash.Of(id ?? string.Empty), salt);
            return (hash >> 40) / (float)(1UL << 24) * 2f - 1f;
        }
    }
}
