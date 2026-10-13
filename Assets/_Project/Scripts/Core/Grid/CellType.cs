namespace Maze.Core.Grid
{
    /// <summary>
    /// Logical cell type. Defines gameplay semantics (movement, LOS, bullets, navigation);
    /// visual prefabs never do.
    /// </summary>
    public enum CellType : byte
    {
        Wall = 0,
        Floor = 1,
        Door = 2,
    }
}
