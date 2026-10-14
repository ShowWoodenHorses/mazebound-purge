using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Platform;
using Purge.Application.Services;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Zombies;
using Purge.Presentation.UI;
using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Presentation.Map
{
    /// <summary>
    /// Owns the map texture of the level (one pixel per cell, the whole level) and the icons over it, and shows them on
    /// <see cref="MapScreen"/> (load stage InitializeUI). The texture is drawn once at loading; icons are refreshed when
    /// the map opens and when a switch changes, never per frame.
    /// Icons: the start the player began at, doors (locked: a lock in the colour of its key pair); each with its
    /// <see cref="MapLayer"/> switch: living zombies (where they are when the map opens: it pauses the game), weapons,
    /// medkits and keys lying in the level (keys in the colour of their door pair), the player (always last, on top).
    /// The switches live in <see cref="SettingsService"/>. The texture is destroyed with the level.
    /// </summary>
    public sealed class MapPresenter : ILevelLoadStep, IDisposable
    {
        private readonly UIRoot _ui;
        private readonly LevelData _level;
        private readonly LevelGrid _grid;
        private readonly PlayerSystem _player;
        private readonly DoorSystem _doors;
        private readonly SettingsService _settings;
        private readonly PickupSystem _pickups;
        private readonly ZombieSystem _zombies;
        private readonly PlatformService _platform;
        private readonly List<MapIcon> _icons = new List<MapIcon>();
        private readonly List<Pickup> _lying = new List<Pickup>();
        private readonly Dictionary<string, Color> _pairColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private readonly Dictionary<string, Color> _keyColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private Texture2D _texture;
        private Color32[] _pixels;
        private bool _bound;

        public MapPresenter(UIRoot ui, LevelData level, LevelGrid grid, PlayerSystem player, DoorSystem doors,
            SettingsService settings, PickupSystem pickups, ZombieSystem zombies, PlatformService platform = null)
        {
            _platform = platform;
            _pickups = pickups;
            _zombies = zombies;
            _ui = ui;
            _level = level;
            _grid = grid;
            _player = player;
            _doors = doors;
            _settings = settings;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        /// <summary>The current map texture (for tests); null before loading and after disposal.</summary>
        public Texture2D Texture => _texture;

        /// <summary>
        /// Icons as last refreshed (for tests), in order: start, doors (as in the level), keys, medkits, weapons (lying
        /// now), zombies, player. Pickups and zombies keep their slots (hidden when gone), so the count never grows after
        /// loading.
        /// </summary>
        public IReadOnlyList<MapIcon> Icons => _icons;

        private MapIconSet Set => _ui.Map.Icons;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_texture == null)
            {
                _texture = new Texture2D(_grid.Width, _grid.Height, TextureFormat.RGBA32, false)
                {
                    name = "Map " + _level.name,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _pixels = new Color32[_grid.FloorSize];
                CollectPairColors();
            }

            if (!_bound)
            {
                _ui.Map.Opened += OnOpened;
                _ui.Map.LayerChanged += OnLayerChanged;
                _bound = true;
            }

            _ui.Map.ShiftTopRightButtons(_platform?.Traits.TopRightButtonsShift ?? 0f);
            ApplyLayerSwitches();
            Redraw();
            RefreshIcons(); // Creates the icon images now, while loading.
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_bound)
            {
                if (_ui != null && _ui.Map != null)
                {
                    _ui.Map.Opened -= OnOpened;
                    _ui.Map.LayerChanged -= OnLayerChanged;
                }

                _bound = false;
            }

            if (_ui != null && _ui.Map != null)
                _ui.Map.Clear();
            UnityObjects.Destroy(_texture);
            _texture = null;
        }

        private void OnOpened()
        {
            ApplyLayerSwitches();
            RefreshIcons();
        }

        private void ApplyLayerSwitches()
        {
            for (var i = 0; i < MapLayers.Count; i++)
            {
                var layer = (MapLayer)i;
                _ui.Map.SetLayer(layer, IsShown(layer));
            }
        }

        private void OnLayerChanged(MapLayer layer, bool show)
        {
            switch (layer)
            {
                case MapLayer.Player: _settings.MapShowPlayer = show; break;
                case MapLayer.Zombies: _settings.MapShowZombies = show; break;
                case MapLayer.Weapons: _settings.MapShowWeapons = show; break;
                case MapLayer.Medkits: _settings.MapShowMedkits = show; break;
                case MapLayer.Keys: _settings.MapShowKeys = show; break;
            }

            RefreshIcons();
        }

        private bool IsShown(MapLayer layer) => layer switch
        {
            MapLayer.Player => _settings.MapShowPlayer,
            MapLayer.Zombies => _settings.MapShowZombies,
            MapLayer.Weapons => _settings.MapShowWeapons,
            MapLayer.Medkits => _settings.MapShowMedkits,
            MapLayer.Keys => _settings.MapShowKeys,
            _ => true,
        };

        private void Redraw()
        {
            MapRenderer.Render(_grid, _pixels);
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
            _ui.Map.SetMap(_texture);
        }

        private void RefreshIcons()
        {
            var set = Set;
            if (set == null || _texture == null) return;
            _icons.Clear();

            // The start the player began at.
            var starts = _level.PlayerStarts;
            var startIndex = _player.StartIndex;
            if (startIndex >= 0 && startIndex < starts.Count)
                AddObjectIcon(set, MapIconKind.Start, starts[startIndex].Id, starts[startIndex].Position, 0f, set.StartColor);
            else
                _icons.Add(new MapIcon { Kind = MapIconKind.Start }); // Not spawned yet: keeps the icon count (images made at loading).

            foreach (var door in _level.Doors)
            {
                if (_doors.IsLocked(door.Id))
                {
                    var color = _pairColors.TryGetValue(door.Id, out var pair) ? pair : set.LockedDoorColor;
                    AddObjectIcon(set, MapIconKind.LockedDoor, door.Id, door.Position, 0f, color);
                }
                else
                {
                    AddObjectIcon(set, MapIconKind.Door, door.Id, door.Position,
                        MapIconLayout.DoorRotation(_grid, door.Position), _doors.IsFrozen(door.Id) ? set.FrozenDoorColor : set.DoorColor);
                }
            }

            // Items lying now, everywhere. Slots: as many as the level has (a dropped weapon takes the slot of one
            // taken, so there are never more weapons lying than the level started with).
            _pickups.GetAll(_lying);
            AddItemIcons(set, PickupKind.Key, _level.Keys.Count, IsShown(MapLayer.Keys));
            AddItemIcons(set, PickupKind.Medkit, _level.Medkits.Count, IsShown(MapLayer.Medkits));
            AddItemIcons(set, PickupKind.Weapon, _level.Weapons.Count, IsShown(MapLayer.Weapons));
            _lying.Clear();

            var showZombies = IsShown(MapLayer.Zombies);
            foreach (var zombie in _zombies.Zombies)
                _icons.Add(new MapIcon
                {
                    Kind = MapIconKind.Zombie,
                    Center = MapIconLayout.CenterOf(zombie.Position),
                    Rotation = MapIconLayout.Tilt(zombie.Id, set.MaxTilt),
                    Size = set.ItemSize,
                    Color = set.ZombieColor,
                    Visible = showZombies && zombie.IsAlive, // Also out of sight: where they are now.
                });

            _icons.Add(new MapIcon
            {
                Kind = MapIconKind.Player,
                Center = MapIconLayout.CenterOf(_player.Position),
                Rotation = MapIconLayout.PlayerRotation(_player.Facing),
                Size = set.PlayerSize,
                Color = set.PlayerColor,
                Visible = IsShown(MapLayer.Player) && _player.IsSpawned,
            });

            _ui.Map.SetIcons(_icons);
        }

        /// <summary>
        /// Icons of the lying pickups of a kind, then hidden ones up to <paramref name="slots"/> (a stable icon count).
        /// </summary>
        private void AddItemIcons(MapIconSet set, PickupKind kind, int slots, bool shown)
        {
            var added = 0;
            foreach (var pickup in _lying)
            {
                if (pickup.Kind != kind) continue;
                var color = kind switch
                {
                    PickupKind.Key => _keyColors.TryGetValue(pickup.Id, out var pair) ? pair : set.KeyColor,
                    PickupKind.Medkit => set.MedkitColor,
                    _ => set.WeaponColor,
                };
                var icon = ObjectIcon(set, KindOf(kind), pickup.Id, pickup.Cell, 0f, color);
                icon.Size = set.ItemSize;
                icon.Visible = shown;
                _icons.Add(icon);
                added++;
            }

            for (; added < slots; added++)
                _icons.Add(new MapIcon { Kind = KindOf(kind) });
        }

        private static MapIconKind KindOf(PickupKind kind) => kind switch
        {
            PickupKind.Key => MapIconKind.Key,
            PickupKind.Medkit => MapIconKind.Medkit,
            _ => MapIconKind.Weapon,
        };

        private void AddObjectIcon(MapIconSet set, MapIconKind kind, string id, GridPosition cell, float rotation, Color color)
        {
            var icon = ObjectIcon(set, kind, id, cell, rotation, color);
            icon.Visible = true;
            _icons.Add(icon);
        }

        private static MapIcon ObjectIcon(MapIconSet set, MapIconKind kind, string id, GridPosition cell, float rotation, Color color) =>
            new MapIcon
            {
                Kind = kind,
                Center = MapIconLayout.CenterOf(cell) + MapIconLayout.Shift(id, set.MaxShift),
                Rotation = rotation + MapIconLayout.Tilt(id, set.MaxTilt),
                Size = set.Size,
                Color = color,
            };

        private void CollectPairColors()
        {
            _pairColors.Clear();
            _keyColors.Clear();
            foreach (var door in _level.Doors)
                if (door.RequiresKey &&
                    VisualColorTags.TryGetColor(VisualColorTags.TagOf(_level, VisualKind.Door, door), out var color))
                {
                    _pairColors[door.Id] = color;
                    _keyColors[door.KeyId] = color;
                }
        }
    }
}
