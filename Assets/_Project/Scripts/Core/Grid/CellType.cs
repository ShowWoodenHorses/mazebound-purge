namespace Purge.Core.Grid
{
    /// <summary>
    /// Structure of a cell — the base of its gameplay semantics (movement, sight, bullets come from
    /// <see cref="CellFlags"/>: structure + obstacle + surface, <see cref="CellRules"/>); visual prefabs never define it.
    /// </summary>
    public enum CellType : byte
    {
        Wall = 0,
        Floor = 1,
        Door = 2,

        /// <summary>
        /// No ground: the part of an upper floor outside its building (an upper floor may be smaller than the one below).
        /// Not walkable; sight and bullets pass. Nothing is drawn.
        /// </summary>
        Void = 3,
    }
}
