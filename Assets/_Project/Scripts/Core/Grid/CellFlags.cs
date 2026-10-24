using System;

namespace Purge.Core.Grid
{
    /// <summary>
    /// Gameplay properties of a cell (ТЗ: «свойства клеток флагами»): what it blocks and how it behaves. A cell's flags
    /// are its structure's, its obstacle's and its surface's together (<see cref="CellRules"/>), baked once by
    /// <see cref="LevelGrid"/>. Doors are dynamic: a closed door blocks everything on top of the flags (its systems
    /// check it). Grenades fly through what bullets fly through.
    /// </summary>
    [Flags]
    public enum CellFlags : byte
    {
        None = 0,
        BlocksMovement = 1 << 0,
        BlocksBullets = 1 << 1,
        BlocksSight = 1 << 2,

        /// <summary>Walkers move slower here (snowdrift, a slowing obstacle; the factor belongs to the source).</summary>
        Slows = 1 << 3,

        /// <summary>Fire spreads over the cell (grass, bushes).</summary>
        Flammable = 1 << 4,

        /// <summary>Everything a wall blocks.</summary>
        Solid = BlocksMovement | BlocksBullets | BlocksSight,
    }

    /// <summary>Flags of the parts of a cell.</summary>
    public static class CellRules
    {
        public static CellFlags StructureFlags(CellType type)
        {
            switch (type)
            {
                case CellType.Floor:
                case CellType.Door: // open or closed is runtime state (DoorSystem)
                    return CellFlags.None;
                case CellType.Void:
                    return CellFlags.BlocksMovement;
                default:
                    return CellFlags.Solid;
            }
        }

        public static CellFlags SurfaceFlags(CellSurface surface)
        {
            switch (surface)
            {
                case CellSurface.Snowdrift: return CellFlags.Slows;
                case CellSurface.Grass: return CellFlags.Flammable;
                default: return CellFlags.None;
            }
        }

        /// <summary>Obstacles and surfaces exist only on Floor cells.</summary>
        public static bool CanHaveContent(CellType type) => type == CellType.Floor;
    }
}
