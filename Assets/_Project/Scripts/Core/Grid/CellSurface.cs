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
    }
}
