namespace Purge.Core.Content
{
    /// <summary>
    /// Rarity of a content item (ТЗ «Оружие и снаряжение»): gray → green → blue → purple → orange. A visual mark for the
    /// player, data for the game (higher rarity — smaller drop weight, higher price).
    /// </summary>
    public enum Rarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4,
    }
}
