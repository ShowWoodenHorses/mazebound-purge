using UnityEngine;

namespace Purge.Presentation.Map
{
    /// <summary>Kinds of icons on the map (ТЗ §60).</summary>
    public enum MapIconKind
    {
        Start,
        Door,
        LockedDoor,
        Player,
        Zombie,
        Weapon,
        Medkit,
        Key,
        Monster,
        Barrel,
        Chest,
        LockedChest,
        Refuge,
    }

    /// <summary>
    /// Icons of the map screen: hand-drawn sprites (white shapes with a dark outline, tinted by <c>*Color</c>) and how
    /// they are placed. Made by Purge → Dev → Build Map Icons; the sprites may be replaced by any others without code.
    /// Referenced by the map screen in the Bootstrap scene.
    /// </summary>
    [CreateAssetMenu(menuName = "Purge/Map Icons", fileName = "MapIcons")]
    public sealed class MapIconSet : ScriptableObject
    {
        [Header("Sprites (pointing up / unrotated)")]
        public Sprite Start;
        [Tooltip("Door plank across a passage going north-south; turned for east-west passages.")]
        public Sprite Door;
        [Tooltip("Locked door: tinted with the colour of its key pair.")]
        public Sprite LockedDoor;
        [Tooltip("Points up (north); turned to the player's facing.")]
        public Sprite Player;
        public Sprite Zombie;
        public Sprite Weapon;
        public Sprite Medkit;
        [Tooltip("Tinted with the colour of its door pair.")]
        public Sprite Key;
        [Tooltip("A monster in view (stage 7г): a dot, tinted by rank.")]
        public Sprite Monster;
        public Sprite Barrel;
        public Sprite Chest;
        public Sprite LockedChest;
        [Tooltip("The safe dome: a ring around its area.")]
        public Sprite Refuge;

        [Header("Colours (tint)")]
        public Color StartColor = new Color(0.35f, 0.6f, 1f);
        public Color DoorColor = new Color(0.78f, 0.52f, 0.28f);
        [Tooltip("Locked door whose pair has no colour.")]
        public Color LockedDoorColor = new Color(0.85f, 0.85f, 0.85f);

        [Tooltip("Door whose ice is not broken yet (a frozen door without a key).")]
        public Color FrozenDoorColor = new Color(0.62f, 0.86f, 1f);
        public Color PlayerColor = new Color(1f, 0.45f, 0.2f);
        public Color ZombieColor = new Color(0.62f, 0.85f, 0.45f);
        public Color WeaponColor = new Color(0.8f, 0.82f, 0.86f);
        public Color MedkitColor = new Color(0.95f, 0.42f, 0.38f);
        [Tooltip("Key whose door has no colour.")]
        public Color KeyColor = new Color(0.95f, 0.85f, 0.45f);

        [Header("Monsters and level objects (stage 7г)")]
        public Color MassColor = new Color(0.9f, 0.3f, 0.26f);
        public Color OfficerColor = new Color(1f, 0.6f, 0.2f);
        public Color EliteColor = new Color(0.82f, 0.38f, 1f);
        public Color ChampionColor = new Color(1f, 0.86f, 0.25f);
        public Color BossColor = new Color(1f, 0.15f, 0.15f);
        [Tooltip("Rank-and-file dot size, cells; officers, elite, champions and bosses — Big Monster Size.")]
        [Min(0.2f)] public float MonsterSize = 0.8f;
        [Min(0.2f)] public float BigMonsterSize = 1.3f;
        public Color GasBarrelColor = new Color(0.55f, 0.85f, 0.35f);
        public Color FireBarrelColor = new Color(1f, 0.5f, 0.2f);
        public Color AcidBarrelColor = new Color(0.8f, 0.95f, 0.2f);
        public Color ChestColor = new Color(0.9f, 0.7f, 0.35f);
        [Tooltip("An opened chest: dim.")]
        public Color OpenedChestColor = new Color(0.55f, 0.5f, 0.45f, 0.6f);
        public Color RefugeColor = new Color(0.45f, 0.75f, 1f, 0.9f);

        [Header("Placement")]
        [Tooltip("Icon size in cells (icons are bigger than a cell).")]
        [Min(0.5f)] public float Size = 1.8f;
        [Min(0.5f)] public float PlayerSize = 1.6f;
        [Tooltip("Zombies, weapons, medkits and keys: smaller than landmarks (often several close together).")]
        [Min(0.5f)] public float ItemSize = 1.4f;
        [Tooltip("Smallest icon on screen, canvas units (big levels have tiny cells).")]
        [Min(0f)] public float MinScreenSize = 26f;
        [Tooltip("Largest random turn of an icon, degrees (stable per object id): a careless hand-drawn map.")]
        [Range(0f, 45f)] public float MaxTilt = 12f;
        [Tooltip("Largest random shift of an icon, cells.")]
        [Range(0f, 0.5f)] public float MaxShift = 0.15f;

        public Sprite SpriteOf(MapIconKind kind) => kind switch
        {
            MapIconKind.Start => Start,
            MapIconKind.Door => Door,
            MapIconKind.LockedDoor => LockedDoor,
            MapIconKind.Zombie => Zombie,
            MapIconKind.Weapon => Weapon,
            MapIconKind.Medkit => Medkit,
            MapIconKind.Key => Key,
            MapIconKind.Monster => Monster,
            MapIconKind.Barrel => Barrel,
            MapIconKind.Chest => Chest,
            MapIconKind.LockedChest => LockedChest,
            MapIconKind.Refuge => Refuge,
            _ => Player,
        };

        public Color ColorOf(MapIconKind kind) => kind switch
        {
            MapIconKind.Start => StartColor,
            MapIconKind.Door => DoorColor,
            MapIconKind.LockedDoor => LockedDoorColor,
            MapIconKind.Zombie => ZombieColor,
            MapIconKind.Weapon => WeaponColor,
            MapIconKind.Medkit => MedkitColor,
            MapIconKind.Key => KeyColor,
            MapIconKind.Monster => MassColor,
            MapIconKind.Barrel => FireBarrelColor,
            MapIconKind.Chest => ChestColor,
            MapIconKind.LockedChest => ChestColor,
            MapIconKind.Refuge => RefugeColor,
            _ => PlayerColor,
        };
    }
}
