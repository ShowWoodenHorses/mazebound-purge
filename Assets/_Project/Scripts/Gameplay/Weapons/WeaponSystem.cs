using System;
using Purge.Core.Definitions;
using Purge.Core.Level;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;

namespace Purge.Gameplay.Weapons
{
    /// <summary>
    /// One concrete weapon of the level (ТЗ §39: WeaponData → WeaponRuntime → WeaponView). A picked weapon keeps the id
    /// and saved visual of the pickup it came from, so it looks the same when dropped again; the base pistol of an
    /// empty slot has no pickup (<see cref="IsBase"/>) and never lies on the ground.
    /// </summary>
    public sealed class WeaponRuntime
    {
        public WeaponRuntime(WeaponPickupData source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Definition = source.Definition ?? throw new ArgumentException($"Weapon pickup '{source.Id}' has no definition.", nameof(source));
            Id = source.Id;
            Ammo = Definition.MagazineSize;
        }

        /// <summary>The base weapon of an empty slot.</summary>
        public WeaponRuntime(WeaponDefinition baseWeapon)
        {
            Definition = baseWeapon ?? throw new ArgumentNullException(nameof(baseWeapon));
            Id = "base_" + Definition.Id;
            Ammo = Definition.MagazineSize;
        }

        public string Id { get; }

        /// <summary>The pickup it came from; null for the base weapon.</summary>
        public WeaponPickupData Source { get; }

        public WeaponDefinition Definition { get; }

        /// <summary>The base pistol of an empty slot: replaced by a picked weapon, never dropped.</summary>
        public bool IsBase => Source == null;

        /// <summary>Rounds in the magazine.</summary>
        public int Ammo { get; internal set; }

        /// <summary>Seconds until the next attack is allowed.</summary>
        public float Cooldown { get; internal set; }

        /// <summary>Seconds left of the reload; 0 when not reloading.</summary>
        public float ReloadRemaining { get; internal set; }

        public bool IsReloading => ReloadRemaining > 0f;

        /// <summary>0..1 share of the reload done; 0 when not reloading.</summary>
        public float ReloadProgress => IsReloading && Definition.ReloadTime > 0f
            ? 1f - Math.Min(1f, ReloadRemaining / Definition.ReloadTime)
            : 0f;

        /// <summary>Interrupts the reload and the cooldown (the weapon leaves the hands); the magazine stays as it is.</summary>
        public void Stop()
        {
            Cooldown = 0f;
            ReloadRemaining = 0f;
        }
    }

    /// <summary>
    /// The player's two weapon slots (ТЗ: firearm or melee in any combination). An empty slot holds the base pistol
    /// (<see cref="PlayerDefinition.BaseWeapon"/>). A picked weapon takes a slot with the base pistol (the active one
    /// first), otherwise replaces the weapon in hands, which is returned to be dropped; the new one becomes active.
    /// Slot keys: a press chooses the slot, a press on the active slot asks for a reload (<see cref="ReloadRequested"/>),
    /// holding a slot key for <see cref="DropHoldTime"/> drops its weapon (<see cref="DropRequested"/>; the base pistol
    /// stays).
    /// </summary>
    public sealed class WeaponSystem : ILevelTickable
    {
        /// <summary>Seconds a slot key is held to drop the weapon.</summary>
        public const float DropHoldTime = 0.6f;

        private readonly IPlayerInput _input;
        private readonly WeaponDefinition _baseWeapon;
        private readonly WeaponRuntime[] _slots = new WeaponRuntime[2];
        private readonly float[] _held = { -1f, -1f };

        public WeaponSystem(IPlayerInput input, PlayerDefinition player)
        {
            _input = input;
            _baseWeapon = player != null ? player.BaseWeapon : null;
            for (var i = 0; i < _slots.Length; i++)
                _slots[i] = BaseFor();
        }

        /// <summary>This tick the player pressed the key of the active slot.</summary>
        public bool ReloadRequested { get; private set; }

        public WeaponSlot ActiveSlot { get; private set; } = WeaponSlot.First;

        /// <summary>Weapon in hands; null only without a base weapon and with an empty slot.</summary>
        public WeaponRuntime Active => _slots[(int)ActiveSlot];

        /// <summary>Raised after slots or the active slot changed.</summary>
        public event Action Changed;

        /// <summary>(slot) after the player switched the active slot (not on a pickup).</summary>
        public event Action<WeaponSlot> Switched;

        /// <summary>(slot) the player held the slot key to drop its weapon (not the base one).</summary>
        public event Action<WeaponSlot> DropRequested;

        public WeaponRuntime Get(WeaponSlot slot) => _slots[(int)slot];

        /// <summary>
        /// Puts a picked weapon into a slot (see the class remarks) and makes it active. Returns the replaced weapon to
        /// drop, or null (the base pistol is not dropped).
        /// </summary>
        public WeaponRuntime Equip(WeaponRuntime weapon)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            var slot = ActiveSlot;
            if (!IsEmpty(slot))
            {
                var other = Other(slot);
                if (IsEmpty(other)) slot = other;
            }

            var replaced = _slots[(int)slot];
            replaced?.Stop();
            _slots[(int)slot] = weapon;
            ActiveSlot = slot;
            Changed?.Invoke();
            return replaced != null && !replaced.IsBase ? replaced : null;
        }

        /// <summary>Takes the weapon out of the slot (the base pistol comes back). Null when there is nothing to drop.</summary>
        public WeaponRuntime Drop(WeaponSlot slot)
        {
            var weapon = _slots[(int)slot];
            if (weapon == null || weapon.IsBase) return null;
            weapon.Stop();
            _slots[(int)slot] = BaseFor();
            Changed?.Invoke();
            return weapon;
        }

        /// <summary>The slot holds nothing but the base pistol.</summary>
        public bool IsEmpty(WeaponSlot slot) => _slots[(int)slot] == null || _slots[(int)slot].IsBase;

        /// <summary>Makes the slot active if it holds a weapon.</summary>
        public bool Activate(WeaponSlot slot)
        {
            if (_slots[(int)slot] == null || ActiveSlot == slot)
                return false;

            Active?.Stop();
            ActiveSlot = slot;
            Changed?.Invoke();
            Switched?.Invoke(slot);
            return true;
        }

        public void Tick(float deltaTime)
        {
            ReloadRequested = false;
            Key(WeaponSlot.First, PlayerAction.Slot1, deltaTime);
            Key(WeaponSlot.Second, PlayerAction.Slot2, deltaTime);
        }

        private void Key(WeaponSlot slot, PlayerAction action, float deltaTime)
        {
            var index = (int)slot;
            if (_input.WasPressed(action))
            {
                _held[index] = 0f;
                if (ActiveSlot == slot) ReloadRequested = true;
                else Activate(slot);
            }

            if (!_input.IsHeld(action))
            {
                _held[index] = -1f;
                return;
            }

            if (_held[index] < 0f) return; // already dropped, or held since before the level
            _held[index] += deltaTime;
            if (_held[index] < DropHoldTime) return;
            _held[index] = -1f;
            if (!IsEmpty(slot)) DropRequested?.Invoke(slot);
        }

        private static WeaponSlot Other(WeaponSlot slot) => slot == WeaponSlot.First ? WeaponSlot.Second : WeaponSlot.First;

        private WeaponRuntime BaseFor() => _baseWeapon != null ? new WeaponRuntime(_baseWeapon) : null;
    }
}
