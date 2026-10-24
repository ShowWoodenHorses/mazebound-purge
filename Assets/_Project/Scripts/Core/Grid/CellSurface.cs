namespace Purge.Core.Grid
{
    /// <summary>
    /// Ground of a Floor cell (gameplay, set by hand in the Level Designer). Other cell types have none.
    /// Its flags — <see cref="CellRules.SurfaceFlags"/>.
    /// </summary>
    public enum CellSurface : byte
    {
        None = 0,

        /// <summary>Deep snow: the player and zombies move slower, zombie routes avoid it when a detour is short.</summary>
        Snowdrift = 1,

        /// <summary>Grass (stage 7): fire spreads over it; the cell shows grass decor (<see cref="Visual.VisualCategory.Grass"/>).</summary>
        Grass = 2,

        /// <summary>
        /// Bare ground (stage 7г: trails, clearings, the start): no gameplay; the floor shows the theme's ground
        /// (<see cref="Visual.VisualCategory.Ground"/> variant, soft edge drawn by Purge/Geometry), footsteps on ground.
        /// </summary>
        Ground = 3,
    }
}
