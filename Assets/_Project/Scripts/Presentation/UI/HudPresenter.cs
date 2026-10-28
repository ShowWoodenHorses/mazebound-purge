using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Platform;
using Purge.Application.Services;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Player;
using Purge.Gameplay.Weapons;
using Purge.Gameplay.Zombies;
using Purge.Presentation.Localization;
using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// Shows the level's player state on the HUD (load stage InitializeUI): HP, zombies killed, keys in
    /// their doors' colours, weapon slots (the on-screen cluster or the desktop slots), the Use target, short messages
    /// about interactions and a red pulse at the screen edges when the player is hurt. The HUD lives in the Bootstrap
    /// scene; this presenter lives with the level. Counters change on gameplay events; the weapon slots and the Use
    /// target are read every frame (reload, where the player stands), the views repaint only on a change.
    /// </summary>
    public sealed class HudPresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        private readonly UIRoot _ui;
        private readonly LevelData _level;
        private readonly PlayerHealth _health;
        private readonly PlayerInventory _inventory;
        private readonly WeaponSystem _weapons;
        private readonly PlayerInteraction _interaction;
        private readonly PlayerSystem _player;
        private readonly PickupSystem _pickups;
        private readonly ZombieSystem _zombies;
        private readonly WeaponVisualCatalog _visuals;
        private readonly LocalizationService _texts;
        private readonly LevelProgress _progress;
        private readonly PlatformService _platform;
        private readonly PlayerThrowing _throwing;
        private readonly ItemVisualCatalog _items;
        private readonly AmmoStock _ammo;
        private readonly PlayerCombat _combat;
        private readonly List<Color> _keyColors = new List<Color>();
        private int _lastHealth;
        private bool _bound;

        public HudPresenter(UIRoot ui, LevelData level, PlayerHealth health, PlayerInventory inventory,
            WeaponSystem weapons, PlayerInteraction interaction, PlayerSystem player,
            PickupSystem pickups, ZombieSystem zombies, WeaponVisualCatalog visuals, LocalizationService texts,
            LevelProgress progress, PlatformService platform = null, PlayerThrowing throwing = null, ItemVisualCatalog items = null,
            AmmoStock ammo = null, PlayerCombat combat = null)
        {
            _ammo = ammo;
            _combat = combat;
            _throwing = throwing;
            _items = items;
            _platform = platform;
            _progress = progress;
            _texts = texts;
            _ui = ui;
            _level = level;
            _health = health;
            _inventory = inventory;
            _weapons = weapons;
            _interaction = interaction;
            _player = player;
            _pickups = pickups;
            _zombies = zombies;
            _visuals = visuals;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_bound)
            {
                _health.Changed += OnHealthChanged;
                _health.Hurt += OnHurt;
                _inventory.KeysChanged += RefreshKeys;
                _interaction.Interacted += OnInteracted;
                _player.CellChanged += OnPlayerCellChanged;
                _zombies.Spawned += OnZombie;
                _zombies.Died += OnZombie;
                _bound = true;
            }

            _ui.Hud.ClearLevelInfo();
            _ui.Hud.ShiftTopRightButtons(_platform?.Traits.TopRightButtonsShift ?? 0f);
            _lastHealth = _health.Current;
            _ui.Hud.SetHealth(_health.Current, _health.Max);
            RefreshCounters();
            RefreshKeys();
            LateTick(0f); // No leftovers of the previous level on the slots.
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_bound) return;
            _health.Changed -= OnHealthChanged;
            _health.Hurt -= OnHurt;
            _inventory.KeysChanged -= RefreshKeys;
            _interaction.Interacted -= OnInteracted;
            _player.CellChanged -= OnPlayerCellChanged;
            _zombies.Spawned -= OnZombie;
            _zombies.Died -= OnZombie;
            _bound = false;
            if (_ui != null && _ui.Hud != null)
                _ui.Hud.ClearLevelInfo();
        }

        public void LateTick(float deltaTime)
        {
            _ui.Hud.SetTime(_progress.Elapsed);
            var melee = SlotState(WeaponSlot.First);
            var ranged = SlotState(WeaponSlot.Second);
            _ui.Hud.SetWeapons(melee, ranged);
            var throwable = ThrowableState();
            _ui.Hud.SetThrowable(throwable);

            var touch = _ui.TouchControls;
            if (touch == null) return;
            touch.SetDriving(_player.InVehicle);
            touch.SetWeapons(melee, ranged);
            touch.SetThrowable(throwable);
            touch.SetUse(_interaction.CurrentTarget() switch
            {
                InteractionTarget.Weapon => UseTarget.PickUp,
                InteractionTarget.Throwable => UseTarget.PickUp,
                InteractionTarget.Chest => UseTarget.PickUp,
                InteractionTarget.Door => UseTarget.Door,
                InteractionTarget.Vehicle => UseTarget.Vehicle,
                _ => UseTarget.None,
            });
        }

        /// <summary>The throwable slot as a weapon slot: the stack's max as the magazine, the count as rounds.</summary>
        private WeaponSlotState ThrowableState()
        {
            var slot = _throwing?.Slot;
            if (slot == null || slot.IsEmpty) return WeaponSlotState.Empty;
            return new WeaponSlotState
            {
                Equipped = true,
                Active = _throwing.IsAiming,
                Icon = _items?.Find(slot.Definition)?.Icon,
                Magazine = slot.Definition.MaxStack,
                Ammo = slot.Count,
                Reserve = -1,
            };
        }

        private WeaponSlotState SlotState(WeaponSlot slot)
        {
            var weapon = _weapons.Get(slot);
            if (weapon == null) return WeaponSlotState.Empty;
            var definition = weapon.Definition;
            var endless = !definition.UsesAmmo || definition.Ammo.InfiniteReserve || _ammo == null;
            return new WeaponSlotState
            {
                Equipped = true,
                Active = _weapons.ActiveSlot == slot,
                Icon = _visuals != null ? _visuals.Find(weapon.Definition)?.Icon : null,
                Magazine = _combat != null ? _combat.MagazineOf(definition) : definition.MagazineSize,
                Ammo = weapon.Ammo,
                Reloading = weapon.IsReloading,
                Reload = weapon.ReloadProgress,
                Reserve = endless ? -1 : _ammo.Get(definition.Ammo.Ammo),
                OutOfAmmo = _combat != null && _combat.IsOutOfAmmo(weapon),
            };
        }

        private const int PulseMinDamage = 5;

        private void OnHealthChanged(int current, int max)
        {
            // A blow pulses; a few points of a state or a zone ticking away do not (the screen would stay red).
            if (_lastHealth - current >= Math.Max(PulseMinDamage, max / 20))
                _ui.Hud.PulseDamage();
            _lastHealth = current;
            _ui.Hud.SetHealth(current, max);
        }

        /// <summary>A blow with a direction: the arc on its side of the screen.</summary>
        private void OnHurt(int amount, Vector2 source)
        {
            if (_player.IsSpawned) _ui.Hud.ShowDamageArc(source - _player.Position);
        }

        private void OnZombie(ZombieRuntime zombie) => RefreshCounters();

        private void RefreshCounters()
        {
            _ui.Hud.SetKills(_zombies.KilledCount, _zombies.TotalCount);
        }

        private void RefreshKeys()
        {
            _keyColors.Clear();
            foreach (var key in _inventory.Keys)
            {
                var tag = VisualColorTags.TagOf(_level, VisualKind.Key, key);
                _keyColors.Add(VisualColorTags.TryGetColor(tag, out var color) ? color : Color.white);
            }

            // The chest key (stage 7в): gold.
            for (var i = 0; i < _inventory.ChestKeys; i++)
                _keyColors.Add(new Color(0.914f, 0.714f, 0.31f, 1f));

            _ui.Hud.SetKeys(_keyColors);
        }

        /// <summary>Weapons are picked up by Interact, not on entering: tell the player how.</summary>
        private void OnPlayerCellChanged(GridPosition from, GridPosition to)
        {
            foreach (var pickup in _pickups.At(to))
                if (pickup.Kind == PickupKind.Weapon)
                {
                    _ui.Hud.ShowMessage(_texts.Format(TextKeys.HudWeaponHere, WeaponName(pickup.Weapon.Definition)));
                    return;
                }
        }

        private void OnInteracted(InteractionResult result, DoorData door)
        {
            switch (result)
            {
                case InteractionResult.DoorLocked:
                    // Per colour ("hud.locked.red"): the colour word agrees with "key" differently in every language.
                    var color = VisualColorTags.TagOf(_level, VisualKind.Door, door);
                    var key = color != null ? TextKeys.HudLockedPrefix + color : null;
                    _ui.Hud.ShowMessage(_texts.Get(key != null && _texts.Has(key) ? key : TextKeys.HudLocked));
                    break;
                case InteractionResult.DoorUnlocked:
                    _ui.Hud.ShowMessage(_texts.Get(TextKeys.HudDoorUnlocked));
                    break;
                case InteractionResult.DoorBlocked:
                    _ui.Hud.ShowMessage(_texts.Get(TextKeys.HudDoorBlocked));
                    break;
                case InteractionResult.DoorFrozen:
                    _ui.Hud.ShowMessage(_texts.Get(TextKeys.HudDoorFrozen));
                    break;
                case InteractionResult.VehicleExitBlocked:
                    _ui.Hud.ShowMessage(_texts.Get(TextKeys.HudVehicleBlocked));
                    break;
                case InteractionResult.WeaponTaken:
                    if (_weapons.Active != null)
                        _ui.Hud.ShowMessage(_texts.Format(TextKeys.HudWeaponTaken, WeaponName(_weapons.Active.Definition)));
                    break;
            }
        }

        /// <summary>"weapon.&lt;id&gt;" from the table; without a text — the id with spaces.</summary>
        private string WeaponName(WeaponDefinition weapon)
        {
            var id = string.IsNullOrEmpty(weapon.Id) ? weapon.name : weapon.Id;
            var key = TextKeys.WeaponPrefix + id;
            return _texts.Has(key) ? _texts.Get(key) : id.Replace('_', ' ');
        }
    }
}
