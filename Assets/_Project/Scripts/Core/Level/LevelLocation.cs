namespace Purge.Core.Level
{
    /// <summary>
    /// The level's location (ТЗ А.7, stage 11б): the level choice shows the five in this order, one tile each; a
    /// location without a level in the catalog is «СКОРО». Booster prices grow by location.
    /// </summary>
    public enum LevelLocation
    {
        Forest = 0,
        Castle = 1,
        Snow = 2,
        Desert = 3,
        Volcano = 4,
    }
}
