using System;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;

namespace Maze.Gameplay.Weapons
{
    /// <summary>
    /// One concrete weapon of the level (ТЗ §39: WeaponData → WeaponRuntime → WeaponView). Keeps the id and saved
    /// visual of the pickup it came from, so it looks the same when dropped again.
    /// </summary>
    public sealed class WeaponRuntime
    {
        public WeaponRuntime(WeaponPickupData source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (source.Definition == null)
                throw new ArgumentException($"Weapon pickup '{source.Id}' has no definition.", nameof(source));
            Reset();
        }

        public string Id => Source.Id;
        public WeaponPickupData Source { get; }
        public WeaponDefinition Definition => Source.Definition;
        public WeaponSlot Slot => Definition.Slot;

        /// <summary>Rounds in the magazine (ranged). Reserve is infinite (ТЗ §68).</summary>
        public int Ammo { get; internal set; }

        /// <summary>Seconds until the next attack is allowed (ТЗ §66: blocks only the next attack).</summary>
        public float Cooldown { get; internal set; }

        /// <summary>Seconds left of the automatic reload; 0 when not reloading (ТЗ §68: no manual reload).</summary>
        public float ReloadRemaining { get; internal set; }

        public bool IsReloading => ReloadRemaining > 0f;

        /// <summary>0..1 share of the reload done; 0 when not reloading.</summary>
        public float ReloadProgress => IsReloading && Definition.ReloadTime > 0f
            ? 1f - Math.Min(1f, ReloadRemaining / Definition.ReloadTime)
            : 0f;

        /// <summary>Fully ready: a lying weapon is always ready when picked up (ТЗ §67).</summary>
        public void Reset()
        {
            Ammo = Definition.Slot == WeaponSlot.Ranged ? Definition.MagazineSize : 0;
            Cooldown = 0f;
            ReloadRemaining = 0f;
        }
    }

    /// <summary>
    /// The player's two weapon slots, Melee and Ranged (ТЗ §67). A picked weapon becomes active at once; a weapon of
    /// the same slot is returned to be dropped, reset. SwitchMelee / SwitchRanged choose the active slot; SwitchRanged
    /// with the ranged weapon already in hands asks for a reload (<see cref="ReloadRequested"/>: a tap on the active
    /// ranged slot on screen).
    /// Attacks come with the combat stage.
    /// </summary>
    public sealed class WeaponSystem : ILevelTickable
    {
        private readonly IPlayerInput _input;
        private readonly WeaponRuntime[] _slots = new WeaponRuntime[2];

        public WeaponSystem(IPlayerInput input)
        {
            _input = input;
        }

        /// <summary>This tick the player pressed SwitchRanged with the ranged weapon already active.</summary>
        public bool ReloadRequested { get; private set; }

        /// <summary>Null while the player has no weapon.</summary>
        public WeaponSlot? ActiveSlot { get; private set; }

        public WeaponRuntime Active => ActiveSlot.HasValue ? _slots[(int)ActiveSlot.Value] : null;

        /// <summary>Raised after slots or the active slot changed.</summary>
        public event Action Changed;

        /// <summary>(slot) after the player switched the active slot (not on a pickup).</summary>
        public event Action<WeaponSlot> Switched;

        public WeaponRuntime Get(WeaponSlot slot) => _slots[(int)slot];

        /// <summary>Puts the weapon into its slot and makes it active. Returns the replaced weapon (reset) or null.</summary>
        public WeaponRuntime Equip(WeaponRuntime weapon)
        {
            var index = (int)weapon.Slot;
            var replaced = _slots[index];
            replaced?.Reset();
            _slots[index] = weapon;
            ActiveSlot = weapon.Slot;
            Changed?.Invoke();
            return replaced;
        }

        /// <summary>Makes the slot active if it holds a weapon.</summary>
        public bool Activate(WeaponSlot slot)
        {
            if (_slots[(int)slot] == null || ActiveSlot == slot)
                return false;

            ActiveSlot = slot;
            Changed?.Invoke();
            Switched?.Invoke(slot);
            return true;
        }

        public void Tick(float deltaTime)
        {
            ReloadRequested = false;
            if (_input.WasPressed(PlayerAction.SwitchMelee)) Activate(WeaponSlot.Melee);
            if (_input.WasPressed(PlayerAction.SwitchRanged))
            {
                if (ActiveSlot == WeaponSlot.Ranged) ReloadRequested = true;
                else Activate(WeaponSlot.Ranged);
            }
        }
    }
}
