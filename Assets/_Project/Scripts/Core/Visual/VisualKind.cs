namespace Maze.Core.Visual
{
    /// <summary>What a visual set represents. Each kind has its own slot in <see cref="VisualTheme"/>.</summary>
    public enum VisualKind
    {
        Floor = 0,
        Wall = 1,
        Door = 2,
        Exit = 3,
        Key = 4,
        Medkit = 5,
        Weapon = 6,
        Zombie = 7,
        MapFragment = 8,

        /// <summary>Purely visual props on floor cells (cell layer <see cref="CellLayer.Decor"/>).</summary>
        Decor = 9,

        /// <summary>Fixture of a light source on a wall, e.g. a torch (<see cref="Level.LightSourceData"/>).</summary>
        Light = 10,
    }

    /// <summary>
    /// Sub-category of a variant. Walls are categorized by their wall neighbours (ТЗ §32–33);
    /// everything else uses <see cref="General"/>. <see cref="Special"/> variants are never picked
    /// automatically: they exist for manual designer overrides only. <see cref="Snowdrift"/> floor variants are picked
    /// only for snowdrift cells (and nowhere else).
    /// </summary>
    public enum VisualCategory
    {
        General = 0,
        Straight = 1,
        Corner = 2,
        TJunction = 3,
        End = 4,
        Cross = 5,
        Isolated = 6,
        Special = 7,

        /// <summary>Floor of a snowdrift cell (<see cref="Grid.CellSurface.Snowdrift"/>).</summary>
        Snowdrift = 8,
    }
}
