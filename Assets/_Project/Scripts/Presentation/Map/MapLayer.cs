namespace Purge.Presentation.Map
{
    /// <summary>Groups of map icons the player switches on and off (a switch each on the map screen, saved in settings).</summary>
    public enum MapLayer
    {
        Player = 0,
        Zombies = 1,
        Weapons = 2,
        Medkits = 3,
        Keys = 4,
    }

    public static class MapLayers
    {
        public const int Count = 5;
    }
}
