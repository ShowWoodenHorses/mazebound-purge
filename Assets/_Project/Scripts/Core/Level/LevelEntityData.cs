using System;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using UnityEngine;

namespace Purge.Core.Level
{
    /// <summary>Base for every object placed in a cell of the level.</summary>
    [Serializable]
    public abstract class LevelEntityData
    {
        [SerializeField] private string _id;
        [SerializeField] private GridPosition _position;

        protected LevelEntityData(string id, GridPosition position)
        {
            _id = id;
            _position = position;
        }

        public string Id { get => _id; internal set => _id = value; }
        public GridPosition Position { get => _position; internal set => _position = value; }
    }

    [Serializable]
    public sealed class DoorData : LevelEntityData
    {
        public const string IdPrefix = "door";

        [SerializeField] private string _keyId;
        [SerializeField] private bool _isInitiallyOpen;

        [Tooltip("Frozen door: melee hits needed to break the ice before it can be opened; 0 = not frozen.")]
        [SerializeField] private int _iceHits;

        [Tooltip("Damage monsters that break doors (the Brute, bosses) deal before it falls; 0 = the default.")]
        [SerializeField] private int _strength;

        /// <summary>Strength of a door that does not set its own (draft; balance — stage 9).</summary>
        public const int DefaultStrength = 60;

        public DoorData(string id, GridPosition position, string keyId = null, bool isInitiallyOpen = false)
            : base(id, position)
        {
            _keyId = keyId;
            _isInitiallyOpen = isInitiallyOpen;
        }

        /// <summary>Id of the key that unlocks this door. Empty means the door needs no key.</summary>
        public string KeyId { get => _keyId; internal set => _keyId = value; }
        public bool RequiresKey => !string.IsNullOrEmpty(_keyId);
        public bool IsInitiallyOpen { get => _isInitiallyOpen; internal set => _isInitiallyOpen = value; }

        /// <summary>Melee hits that break the door's ice; 0 = not frozen. A frozen door starts closed.</summary>
        public int IceHits { get => _iceHits; internal set => _iceHits = Math.Max(0, value); }
        public bool IsFrozen => _iceHits > 0;

        /// <summary>Damage from monsters that break doors before the door falls (stage 5d).</summary>
        public int Strength { get => _strength > 0 ? _strength : DefaultStrength; internal set => _strength = Math.Max(0, value); }
    }

    [Serializable]
    public sealed class KeyData : LevelEntityData
    {
        public const string IdPrefix = "key";

        public KeyData(string id, GridPosition position) : base(id, position) { }
    }

    [Serializable]
    public sealed class PlayerStartData : LevelEntityData
    {
        public const string IdPrefix = "start";

        public PlayerStartData(string id, GridPosition position) : base(id, position) { }
    }

    /// <summary>
    /// Where the waves' monsters climb out (stage 6: ТЗ «монстры появляются из фиксированных точек локации»), within a
    /// couple of cells around it. Not in <c>LevelData.AllEntities</c>: it has no look and does not take the cell.
    /// </summary>
    [Serializable]
    public sealed class MonsterSpawnData : LevelEntityData
    {
        public const string IdPrefix = "spawn";

        public MonsterSpawnData(string id, GridPosition position) : base(id, position) { }
    }

    /// <summary>Medkit always heals the player to full HP and is consumed only if HP is below max.</summary>
    [Serializable]
    public sealed class MedkitData : LevelEntityData
    {
        public const string IdPrefix = "medkit";

        public MedkitData(string id, GridPosition position) : base(id, position) { }
    }

    [Serializable]
    public sealed class ZombieSpawnData : LevelEntityData
    {
        public const string IdPrefix = "zombie";

        [SerializeField] private Direction _facing;
        [SerializeField] private ZombieDefinition _definition;

        public ZombieSpawnData(string id, GridPosition position, ZombieDefinition definition,
            Direction facing = Direction.North)
            : base(id, position)
        {
            _definition = definition;
            _facing = facing;
        }

        public Direction Facing { get => _facing; internal set => _facing = value; }
        public ZombieDefinition Definition { get => _definition; internal set => _definition = value; }
    }

    /// <summary>What a barrel spills when it blows up (stage 7в).</summary>
    public enum BarrelKind
    {
        Gas = 0,
        Fire = 1,
        Acid = 2,
    }

    /// <summary>
    /// An explosive barrel (ТЗ 7в): shot or hit, it blows up and leaves a zone of its kind; the cell is free afterwards.
    /// Placed by the Level Designer brush (stage 7б); gameplay and look — stage 7в.
    /// </summary>
    [Serializable]
    public sealed class BarrelData : LevelEntityData
    {
        public const string IdPrefix = "barrel";

        [SerializeField] private BarrelKind _kind;

        public BarrelData(string id, GridPosition position, BarrelKind kind) : base(id, position)
        {
            _kind = kind;
        }

        public BarrelKind Kind { get => _kind; internal set => _kind = value; }
    }

    /// <summary>
    /// A vehicle standing on the level from the start of the run (ТЗ 9, «Машина-таран»): the player gets in with Use and
    /// rams the horde; single use. <see cref="LevelEntityData.Position"/> is the cell under the centre of its body.
    /// </summary>
    [Serializable]
    public sealed class VehicleData : LevelEntityData
    {
        public const string IdPrefix = "vehicle";

        [SerializeField] private Vehicles.VehicleDefinition _definition;

        [Tooltip("Where its front points, degrees clockwise from North (0 = North, 90 = East).")]
        [SerializeField] private float _heading;

        public VehicleData(string id, GridPosition position, Vehicles.VehicleDefinition definition, float heading)
            : base(id, position)
        {
            _definition = definition;
            _heading = heading;
        }

        public Vehicles.VehicleDefinition Definition { get => _definition; internal set => _definition = value; }
        public float Heading { get => _heading; internal set => _heading = Mathf.Repeat(value, 360f); }
    }

    /// <summary>What it takes to open a chest and what it gives (stage 7в).</summary>
    public enum ChestKind
    {
        /// <summary>Use → one item of the officer drop table.</summary>
        Common = 0,

        /// <summary>Needs the chest key → a weapon a rarity step above the officer drop.</summary>
        Locked = 1,

        /// <summary>Use → champions climb out around it; the locked chest's reward after they die.</summary>
        Challenge = 2,
    }

    /// <summary>A chest (ТЗ 7в): opened once per run. Placed by the Level Designer brush (7б); gameplay and look — 7в.</summary>
    [Serializable]
    public sealed class ChestData : LevelEntityData
    {
        public const string IdPrefix = "chest";

        [SerializeField] private ChestKind _kind;

        public ChestData(string id, GridPosition position, ChestKind kind) : base(id, position)
        {
            _kind = kind;
        }

        public ChestKind Kind { get => _kind; internal set => _kind = value; }
    }

    [Serializable]
    public sealed class WeaponPickupData : LevelEntityData
    {
        public const string IdPrefix = "weapon";

        [SerializeField] private WeaponDefinition _definition;

        public WeaponPickupData(string id, GridPosition position, WeaponDefinition definition) : base(id, position)
        {
            _definition = definition;
        }

        public WeaponDefinition Definition { get => _definition; internal set => _definition = value; }
    }
}
