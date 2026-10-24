using System;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Objects;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Sound;
using UnityEngine;

namespace Purge.Gameplay.Player
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
        /// <summary>A throwable of another kind replaced the stack (which dropped).</summary>
        ThrowableTaken = 7,
        /// <summary>The opening of a chest started (stage 7в).</summary>
        ChestOpening = 8,
        /// <summary>A locked chest and no chest key.</summary>
        ChestLocked = 9,
    }

    /// <summary>What Interact would act on now (for the Use button).</summary>
    public enum InteractionTarget
    {
        None = 0,
        Weapon = 1,
        Door = 2,
        Throwable = 3,
        Chest = 4,
    }

    /// <summary>
    /// The Interact action (ТЗ §63 Interaction, §64 "door" button, §67 weapon pickup). Order:
    /// 1) a weapon lying in the player's cell is picked up;
    /// 2) otherwise a throwable of another kind lying there replaces the stack (the same kind is picked up by walking);
    /// 2½) otherwise a closed chest next to the player (stage 7в) starts opening (<see cref="ChestSystem"/>);
    /// 3) otherwise the door next to the player (the one they face first) is used: a locked door is unlocked with
    ///    its key (the door stays closed, the key is spent); an unlocked door opens or closes. A door is never closed
    ///    on the player or on a monster; a broken door (stage 5d) is not offered at all.
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
        private readonly HordeSystem _horde;
        private readonly ChestSystem _chests;

        public PlayerInteraction(IPlayerInput input, PlayerSystem player, PickupSystem pickups, DoorSystem doors,
            PlayerInventory inventory, OccupancyMap occupancy, SoundEventBus sounds, HordeSystem horde = null,
            ChestSystem chests = null)
        {
            _chests = chests;
            _sounds = sounds;
            _horde = horde;
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
            if (_pickups.ThrowableToSwap(_player.Cell) != null) return InteractionTarget.Throwable;
            if (_chests != null && _chests.FindUsable() != null) return InteractionTarget.Chest;
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

            if (_pickups.TryTakeThrowable(_player.Cell))
            {
                Interacted?.Invoke(InteractionResult.ThrowableTaken, null);
                return true;
            }

            var chest = _chests?.FindUsable();
            if (chest != null)
            {
                var use = _chests.TryUse(chest);
                if (use != ChestUse.None)
                {
                    if (use == ChestUse.Opening)
                        _sounds.Emit(SoundType.Door, chest.Position, _player.Definition.InteractionSoundRadius, chest.Cell.Floor);
                    Interacted?.Invoke(use == ChestUse.Opening ? InteractionResult.ChestOpening : InteractionResult.ChestLocked, null);
                    return true;
                }
            }

            var door = FindDoor();
            if (door == null)
                return false;

            var result = UseDoor(door);
            if (result == InteractionResult.DoorOpened || result == InteractionResult.DoorClosed || result == InteractionResult.DoorUnlocked)
                _sounds.Emit(SoundType.Door, new Vector2(door.Position.X, door.Position.Y), _player.Definition.InteractionSoundRadius,
                    door.Position.Floor);
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
                if (!_doors.TryGetDoor(cell, out var door) || _doors.IsBroken(door.Id))
                    continue;

                var offset = new Vector2(cell.X, cell.Y) - _player.Position;
                var score = Vector2.Dot(_player.Facing, offset.normalized);
                if (score > bestScore)
                {
                    best = door;
                    bestScore = score;
                }
            }

            if (best == null && _doors.TryGetDoor(_player.Cell, out var own) && !_doors.IsBroken(own.Id))
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

        /// <summary>A zombie or a monster of the horde stands in the door cell or the player's footprint overlaps it.</summary>
        private bool IsDoorwayOccupied(GridPosition cell)
        {
            if (_occupancy.ZombieCount(cell) > 0)
                return true;
            if (_horde != null && _horde.AnyBodyInCell(cell))
                return true;

            var reach = 0.5f + _player.Definition.BodyHalfSize;
            return Mathf.Abs(_player.Position.x - cell.X) < reach && Mathf.Abs(_player.Position.y - cell.Y) < reach;
        }
    }
}
