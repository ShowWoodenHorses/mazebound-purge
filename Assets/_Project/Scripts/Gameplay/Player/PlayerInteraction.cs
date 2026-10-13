using System;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Grid;
using Maze.Gameplay.Level;
using Maze.Gameplay.Pickups;
using Maze.Gameplay.Sound;
using UnityEngine;

namespace Maze.Gameplay.Player
{
    /// <summary>What happened on Interact, for UI feedback.</summary>
    public enum InteractionResult
    {
        WeaponTaken = 0,
        DoorOpened = 1,
        DoorClosed = 2,
        DoorUnlocked = 3,
        /// <summary>Locked and the player has no key for it.</summary>
        DoorLocked = 4,
        /// <summary>Cannot close: the player or a zombie is in the doorway.</summary>
        DoorBlocked = 5,
        /// <summary>Frozen: the ice must be broken with melee hits first.</summary>
        DoorFrozen = 6,
    }

    /// <summary>What Interact would act on now (for the Use button).</summary>
    public enum InteractionTarget
    {
        None = 0,
        Weapon = 1,
        Door = 2,
    }

    /// <summary>
    /// The Interact action (ТЗ §63 Interaction, §64 "door" button, §67 weapon pickup). Order:
    /// 1) a weapon lying in the player's cell is picked up;
    /// 2) otherwise the door next to the player (the one they face first) is used: a locked door is unlocked with
    ///    its key (the door stays closed, the key is spent); an unlocked door opens or closes. A door is never closed
    ///    on the player or on a zombie.
    /// Must be registered after <see cref="PlayerSystem"/> (it reads the player's cell of this tick).
    /// </summary>
    public sealed class PlayerInteraction : ILevelTickable
    {
        private readonly IPlayerInput _input;
        private readonly PlayerSystem _player;
        private readonly PickupSystem _pickups;
        private readonly DoorSystem _doors;
        private readonly PlayerInventory _inventory;
        private readonly OccupancyMap _occupancy;
        private readonly SoundEventBus _sounds;

        public PlayerInteraction(IPlayerInput input, PlayerSystem player, PickupSystem pickups, DoorSystem doors,
            PlayerInventory inventory, OccupancyMap occupancy, SoundEventBus sounds)
        {
            _sounds = sounds;
            _input = input;
            _player = player;
            _pickups = pickups;
            _doors = doors;
            _inventory = inventory;
            _occupancy = occupancy;
        }

        /// <summary>(result, door or null).</summary>
        public event Action<InteractionResult, DoorData> Interacted;

        public void Tick(float deltaTime)
        {
            if (_player.IsSpawned && _input.WasPressed(PlayerAction.Interact))
                Interact();
        }

        /// <summary>What <see cref="Interact"/> would use now (same order); cheap, no allocations.</summary>
        public InteractionTarget CurrentTarget()
        {
            if (!_player.IsSpawned) return InteractionTarget.None;
            var here = _pickups.At(_player.Cell);
            for (var i = 0; i < here.Count; i++)
                if (here[i].Kind == PickupKind.Weapon)
                    return InteractionTarget.Weapon;
            return FindDoor() != null ? InteractionTarget.Door : InteractionTarget.None;
        }

        /// <summary>Performs the Interact action now. False when there was nothing to interact with.</summary>
        public bool Interact()
        {
            if (_pickups.TryTakeWeapon(_player.Cell))
            {
                Interacted?.Invoke(InteractionResult.WeaponTaken, null);
                return true;
            }

            var door = FindDoor();
            if (door == null)
                return false;

            var result = UseDoor(door);
            if (result == InteractionResult.DoorOpened || result == InteractionResult.DoorClosed || result == InteractionResult.DoorUnlocked)
                _sounds.Emit(SoundType.Door, new Vector2(door.Position.X, door.Position.Y), _player.Definition.InteractionSoundRadius);
            Interacted?.Invoke(result, door);
            return true;
        }

        /// <summary>A door in a neighbouring cell, preferring the facing direction; the door the player stands in last.</summary>
        public DoorData FindDoor()
        {
            DoorData best = null;
            var bestScore = float.NegativeInfinity;
            foreach (var direction in DirectionExtensions.All)
            {
                var cell = _player.Cell.Neighbour(direction);
                if (!_doors.TryGetDoor(cell, out var door))
                    continue;

                var offset = new Vector2(cell.X, cell.Y) - _player.Position;
                var score = Vector2.Dot(_player.Facing, offset.normalized);
                if (score > bestScore)
                {
                    best = door;
                    bestScore = score;
                }
            }

            if (best == null && _doors.TryGetDoor(_player.Cell, out var own))
                best = own;
            return best;
        }

        private InteractionResult UseDoor(DoorData door)
        {
            if (_doors.IsFrozen(door.Id))
                return InteractionResult.DoorFrozen;

            if (_doors.IsLocked(door.Id))
            {
                if (!_inventory.UseKey(door.KeyId))
                    return InteractionResult.DoorLocked;

                _doors.Unlock(door.Id);
                return InteractionResult.DoorUnlocked;
            }

            if (!_doors.IsOpen(door.Id))
            {
                _doors.SetOpen(door.Id, true);
                return InteractionResult.DoorOpened;
            }

            if (IsDoorwayOccupied(door.Position))
                return InteractionResult.DoorBlocked;

            _doors.SetOpen(door.Id, false);
            return InteractionResult.DoorClosed;
        }

        /// <summary>A zombie stands in the door cell or the player's footprint overlaps it.</summary>
        private bool IsDoorwayOccupied(GridPosition cell)
        {
            if (_occupancy.ZombieCount(cell) > 0)
                return true;

            var reach = 0.5f + _player.Definition.BodyHalfSize;
            return Mathf.Abs(_player.Position.x - cell.X) < reach && Mathf.Abs(_player.Position.y - cell.Y) < reach;
        }
    }
}
