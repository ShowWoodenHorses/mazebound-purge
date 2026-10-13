namespace Maze.Presentation.Map
{
    /// <summary>Groups of map icons the player switches on and off (a switch each on the map screen, saved in settings).</summary>
    public enum MapLayer
    {
        Player = 0,
        Fragments = 1,
        Zombies = 2,
        Weapons = 3,
        Medkits = 4,
        Keys = 5,
    }

    public static class MapLayers
    {
        public const int Count = 6;
    }
}
