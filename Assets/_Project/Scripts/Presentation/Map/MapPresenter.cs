using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Platform;
using Purge.Application.Services;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Visual;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Objects;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using Purge.Gameplay.Zombies;
using Purge.Presentation.UI;
using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Presentation.Map
{
    /// <summary>
    /// Owns the map texture of the level (one pixel per cell) and the icons over it, and shows them on
    /// <see cref="MapScreen"/> (load stage InitializeUI). Fog (stage 7г): only cells of <see cref="MapExploration"/>
    /// are drawn, the rest is black; newly explored cells are painted into the pixels as they come and uploaded when
    /// needed (<see cref="ApplyPending"/>: the map opening, the mini-map). Icons are refreshed when the map opens and
    /// when a switch changes, never per frame.
    /// Icons: the start the player began at, doors (locked: a lock in the colour of its key pair) — explored cells only;
    /// each with its <see cref="MapLayer"/> switch: monsters in view (horde dots by rank, zombies), weapons, medkits and
    /// keys lying in explored cells (keys in the colour of their door pair, the chest key), objects (barrels, chests,
    /// the safe dome), the player (always last, on top). Icon slots are made at loading and never grow.
    /// The switches live in <see cref="SettingsService"/>. The texture is destroyed with the level.
    /// </summary>
    public sealed class MapPresenter : ILevelLoadStep, IDisposable
    {
        /// <summary>Most horde monsters shown at once on the full map (in view): the slots of their icons.</summary>
        public const int MonsterSlots = 160;

        private readonly UIRoot _ui;
        private readonly LevelData _level;
        private readonly LevelGrid _grid;
        private readonly PlayerSystem _player;
        private readonly DoorSystem _doors;
        private readonly SettingsService _settings;
        private readonly PickupSystem _pickups;
        private readonly ZombieSystem _zombies;
        private readonly PlatformService _platform;
        private readonly MapExploration _exploration;
        private readonly VisibilitySystem _visibility;
        private readonly HordeSystem _horde;
        private readonly BarrelSystem _barrels;
        private readonly ChestSystem _chests;
        private readonly RefugeSystem _refuge;
        private readonly List<MapIcon> _icons = new List<MapIcon>();
        private readonly List<Pickup> _lying = new List<Pickup>();
        private readonly Dictionary<string, Color> _pairColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private readonly Dictionary<string, Color> _keyColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private Texture2D _texture;
        private Color32[] _pixels;
        private bool _pending;
        private bool _bound;

        public MapPresenter(UIRoot ui, LevelData level, LevelGrid grid, PlayerSystem player, DoorSystem doors,
            SettingsService settings, PickupSystem pickups, ZombieSystem zombies, PlatformService platform = null,
            MapExploration exploration = null, VisibilitySystem visibility = null, HordeSystem horde = null,
            BarrelSystem barrels = null, ChestSystem chests = null, RefugeSystem refuge = null)
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
            _exploration = exploration;
            _visibility = visibility;
            _horde = horde;
            _barrels = barrels;
            _chests = chests;
            _refuge = refuge;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        /// <summary>The current map texture (also the mini-map's); null before loading and after disposal.</summary>
        public Texture2D Texture => _texture;

        /// <summary>
        /// Icons as last refreshed (for tests), in order: start, doors (as in the level), keys, the chest key, medkits,
        /// weapons (lying now), barrels, chests, the dome, zombies, horde monsters, player. Every kind keeps its slots
        /// (hidden when gone), so the count never grows after loading.
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
                if (_exploration != null) _exploration.Explored += OnExplored;
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

                if (_exploration != null) _exploration.Explored -= OnExplored;
                _bound = false;
            }

            if (_ui != null && _ui.Map != null)
                _ui.Map.Clear();
            UnityObjects.Destroy(_texture);
            _texture = null;
        }

        /// <summary>The player switched the layer on (the mini-map follows the same switches).</summary>
        public bool IsLayerShown(MapLayer layer) => IsShown(layer);

        /// <summary>Uploads the cells explored since the last call (the texture is shared with the mini-map).</summary>
        public void ApplyPending()
        {
            if (!_pending || _texture == null) return;
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
            _pending = false;
        }

        /// <summary>The cell is drawn on the map: explored (always, when the level has no exploration).</summary>
        public bool IsExplored(GridPosition cell) => _exploration == null || _exploration.IsExplored(cell);

        private void OnExplored(IReadOnlyList<GridPosition> cells)
        {
            if (_pixels == null) return;
            for (var i = 0; i < cells.Count; i++)
                if (cells[i].Floor == 0)
                    MapRenderer.RenderCell(_grid, _pixels, cells[i]);
            _pending = true;
        }

        private void OnOpened()
        {
            ApplyPending();
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
                case MapLayer.Objects: _settings.MapShowObjects = show; break;
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
            MapLayer.Objects => _settings.MapShowObjects,
            _ => true,
        };

        private void Redraw()
        {
            MapRenderer.Render(_grid, _pixels, 0, _exploration != null ? IsExplored : (Func<GridPosition, bool>)null);
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
            _pending = false;
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
                if (!IsExplored(door.Position))
                    _icons.Add(new MapIcon { Kind = MapIconKind.Door });
                else if (_doors.IsLocked(door.Id))
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

            // Items lying now in explored cells. Slots: as many as the level has (a dropped weapon takes the slot of one
            // taken, so there are never more weapons lying than the level started with).
            _pickups.GetAll(_lying);
            AddItemIcons(set, PickupKind.Key, _level.Keys.Count, IsShown(MapLayer.Keys));
            AddChestKeyIcon(set, IsShown(MapLayer.Keys));
            AddItemIcons(set, PickupKind.Medkit, _level.Medkits.Count, IsShown(MapLayer.Medkits));
            AddItemIcons(set, PickupKind.Weapon, _level.Weapons.Count, IsShown(MapLayer.Weapons));
            _lying.Clear();
            AddObjectIcons(set, _icons, IsShown(MapLayer.Objects));

            var showMonsters = IsShown(MapLayer.Zombies);
            foreach (var zombie in _zombies.Zombies)
                _icons.Add(new MapIcon
                {
                    Kind = MapIconKind.Zombie,
                    Center = MapIconLayout.CenterOf(zombie.Position),
                    Rotation = MapIconLayout.Tilt(zombie.Id, set.MaxTilt),
                    Size = set.ItemSize,
                    Color = set.ZombieColor,
                    Visible = showMonsters && zombie.IsAlive && InView(zombie.Cell),
                });
            AddMonsterIcons(set, _icons, MonsterSlots, showMonsters, null);

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
        /// Icons of the lying pickups of a kind in explored cells, then hidden ones up to <paramref name="slots"/>
        /// (a stable icon count).
        /// </summary>
        private void AddItemIcons(MapIconSet set, PickupKind kind, int slots, bool shown)
        {
            var added = 0;
            foreach (var pickup in _lying)
            {
                if (pickup.Kind != kind || pickup.IsDropped) continue; // monsters' drops: not on the map (icon count fixed)
                if (added >= slots) break;
                var color = kind switch
                {
                    PickupKind.Key => _keyColors.TryGetValue(pickup.Id, out var pair) ? pair : set.KeyColor,
                    PickupKind.Medkit => set.MedkitColor,
                    _ => set.WeaponColor,
                };
                var icon = ObjectIcon(set, KindOf(kind), pickup.Id, pickup.Cell, 0f, color);
                icon.Size = set.ItemSize;
                icon.Visible = shown && IsExplored(pickup.Cell);
                _icons.Add(icon);
                added++;
            }

            for (; added < slots; added++)
                _icons.Add(new MapIcon { Kind = KindOf(kind) });
        }

        /// <summary>The chest key lying (dropped by the first elite, stage 7в): one slot.</summary>
        private void AddChestKeyIcon(MapIconSet set, bool shown)
        {
            foreach (var pickup in _lying)
            {
                if (pickup.Kind != PickupKind.ChestKey) continue;
                var icon = ObjectIcon(set, MapIconKind.Key, "chest_key", pickup.Cell, 0f, set.ChestColor);
                icon.Size = set.ItemSize;
                icon.Visible = shown && IsExplored(pickup.Cell);
                _icons.Add(icon);
                return;
            }

            _icons.Add(new MapIcon { Kind = MapIconKind.Key });
        }

        /// <summary>
        /// Barrels standing, chests (the locked one with a lock; opened ones dim), the safe dome while it stands — in
        /// explored cells; a slot each, as many as the level has (also the mini-map's).
        /// </summary>
        public void AddObjectIcons(MapIconSet set, List<MapIcon> icons, bool shown)
        {
            if (_barrels != null)
                foreach (var barrel in _barrels.Barrels)
                {
                    var icon = ObjectIcon(set, MapIconKind.Barrel, barrel.Id, barrel.Cell, 0f, BarrelColor(set, barrel.Data.Kind));
                    icon.Size = set.ItemSize;
                    icon.Visible = shown && !barrel.IsBroken && IsExplored(barrel.Cell);
                    icons.Add(icon);
                }

            if (_chests != null)
                foreach (var chest in _chests.Chests)
                {
                    var opened = chest.State == ChestState.Open;
                    var kind = chest.Kind == ChestKind.Locked && !opened ? MapIconKind.LockedChest : MapIconKind.Chest;
                    var icon = ObjectIcon(set, kind, chest.Id, chest.Cell, 0f, opened ? set.OpenedChestColor : set.ChestColor);
                    icon.Visible = shown && IsExplored(chest.Cell);
                    icons.Add(icon);
                }

            if (_refuge != null)
                icons.Add(new MapIcon
                {
                    Kind = MapIconKind.Refuge,
                    Center = MapIconLayout.CenterOf(_refuge.Center),
                    Size = _refuge.Edge * 2f,
                    Color = set.RefugeColor,
                    Visible = shown && _refuge.Exists && _refuge.IsUp,
                });
        }

        private static Color BarrelColor(MapIconSet set, BarrelKind kind) => kind switch
        {
            BarrelKind.Gas => set.GasBarrelColor,
            BarrelKind.Acid => set.AcidBarrelColor,
            _ => set.FireBarrelColor,
        };

        /// <summary>
        /// Horde monsters standing in cells in view now: dots by rank (champions their own colour; all but the
        /// rank-and-file bigger), exactly <paramref name="slots"/> icons (the rest hidden). <paramref name="window"/>:
        /// only those inside it (grid units; the mini-map), null — anywhere.
        /// </summary>
        public void AddMonsterIcons(MapIconSet set, List<MapIcon> icons, int slots, bool shown, Rect? window)
        {
            var added = 0;
            if (_horde != null && _horde.IsActive && _visibility != null)
            {
                var positions = _horde.Positions;
                var states = _horde.States;
                var floor = _player.Floor;
                for (var agent = 0; agent < _horde.HighWater && added < slots; agent++)
                {
                    var state = states[agent];
                    if (state == HordeAgentState.Inactive || state == HordeAgentState.Dying) continue;
                    var position = positions[agent];
                    if (window.HasValue && !window.Value.Contains(position)) continue;
                    var definition = _horde.DefinitionOf(agent);
                    var rank = definition != null ? definition.Rank : MonsterRank.Mass;
                    // ТЗ: боссы видны всегда — out of view and with the layer off too (stage 8a).
                    var boss = rank == MonsterRank.Boss;
                    if (!boss && (!shown || !InView(PlayerMovement.CellOf(position, floor)))) continue;
                    var champion = _horde.IsChampion(agent);
                    icons.Add(new MapIcon
                    {
                        Kind = MapIconKind.Monster,
                        Center = MapIconLayout.CenterOf(position),
                        Size = boss ? set.BigMonsterSize * BossIconScale :
                            rank == MonsterRank.Mass && !champion ? set.MonsterSize : set.BigMonsterSize,
                        Color = champion ? set.ChampionColor : RankColor(set, rank),
                        Visible = true,
                    });
                    added++;
                }
            }

            for (; added < slots; added++)
                icons.Add(new MapIcon { Kind = MapIconKind.Monster });
        }

        /// <summary>× the big dot of a boss (stage 8a).</summary>
        private const float BossIconScale = 1.8f;

        private static Color RankColor(MapIconSet set, MonsterRank rank) => rank switch
        {
            MonsterRank.Officer => set.OfficerColor,
            MonsterRank.Elite => set.EliteColor,
            MonsterRank.Boss => set.BossColor,
            _ => set.MassColor,
        };

        private bool InView(GridPosition cell) => _visibility == null || _visibility.IsVisible(cell);

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
