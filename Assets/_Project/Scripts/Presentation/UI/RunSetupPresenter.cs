using System;
using System.Collections.Generic;
using Purge.Application.Levels;
using Purge.Application.Meta;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Core.Boosters;
using Purge.Core.Definitions;
using Purge.Core.Level;
using Purge.Gameplay.Level;
using Purge.Presentation.Localization;
using Purge.Presentation.Menu;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The run's choice and preparation (stage 11б) over <see cref="RunSetupScreen"/>: a tile per
    /// <see cref="LevelLocation"/> (its first catalog level), the difficulties of the chosen one, «Закрытая арена» from
    /// the normal difficulty, the weapon and throwable from what <see cref="RunPreparation"/> allows, the open boosters
    /// with their prices there. Opens on the next level to play at its highest open difficulty with the last
    /// preparation (<see cref="IProgressService.Loadout"/>: weapon, throwable and the boosters still open and affordable).
    /// «В БОЙ» stores the preparation and hands the launch to the router (<see cref="FightRequested"/>: the level, the
    /// options, the boosters' cost); the podium shows the chosen weapon meanwhile.
    /// </summary>
    public sealed class RunSetupPresenter : IDisposable
    {
        private static readonly LevelLocation[] Locations =
            { LevelLocation.Forest, LevelLocation.Castle, LevelLocation.Snow, LevelLocation.Desert, LevelLocation.Volcano };

        private static readonly string[] DifficultyNames = { "easy", "normal", "hard", "unreal" };

        private readonly UIRoot _ui;
        private readonly RunPreparation _preparation;
        private readonly IProgressService _progress;
        private readonly ILevelCatalog _catalog;
        private readonly SharedDefinitionsService _shared;
        private readonly LocalizationService _texts;
        private readonly Application.Flow.GameFlow _flow;
        private readonly MenuPodiumPresenter _podium;

        private readonly List<WeaponDefinition> _weapons = new List<WeaponDefinition>();
        private readonly List<ThrowableDefinition> _throwables = new List<ThrowableDefinition>();
        private readonly List<BoosterDefinition> _boosters = new List<BoosterDefinition>();
        private readonly HashSet<BoosterDefinition> _chosenBoosters = new HashSet<BoosterDefinition>();
        private readonly List<RunSetupScreen.Item> _items = new List<RunSetupScreen.Item>();
        private readonly List<RunSetupScreen.Booster> _rows = new List<RunSetupScreen.Booster>();
        private readonly List<BoosterDefinition> _launchBoosters = new List<BoosterDefinition>();
        private readonly List<string> _ids = new List<string>();

        private int _location;
        private int _difficulty;
        private bool _arena;
        private int _weapon; // 0 = the base pistol, i = _weapons[i - 1]
        private int _throwable; // 0 = none, i = _throwables[i - 1]
        private bool _initialized;

        public RunSetupPresenter(UIRoot ui, RunPreparation preparation, IProgressService progress, ILevelCatalog catalog,
            SharedDefinitionsService shared, LocalizationService texts, Application.Flow.GameFlow flow,
            MenuPodiumPresenter podium = null)
        {
            _ui = ui;
            _preparation = preparation;
            _progress = progress;
            _catalog = catalog;
            _shared = shared;
            _texts = texts;
            _flow = flow;
            _podium = podium;
        }

        private RunSetupScreen Screen => _ui != null ? _ui.RunSetup : null;

        /// <summary>The screen exists in this scene.</summary>
        public bool IsAvailable => Screen != null;

        public bool IsOpen { get; private set; }

        /// <summary>«В БОЙ»: (level id, launch options, the boosters' cost).</summary>
        public event Action<string, LevelLaunchOptions, int> FightRequested;

        /// <summary>Back: the main menu again.</summary>
        public event Action BackRequested;

        public void Initialize()
        {
            if (_initialized || Screen == null) return;
            _initialized = true;
            var screen = Screen;
            screen.BackClicked += OnBack;
            screen.FightClicked += OnFight;
            screen.LocationClicked += OnLocation;
            screen.DifficultyClicked += OnDifficulty;
            screen.ArenaChanged += OnArena;
            screen.WeaponClicked += OnWeapon;
            screen.ThrowableClicked += OnThrowable;
            screen.BoosterClicked += OnBooster;
            _texts.LanguageChanged += OnLanguageChanged;
        }

        public void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;
            _texts.LanguageChanged -= OnLanguageChanged;
            var screen = Screen;
            if (screen == null) return; // the scene is gone on quit
            screen.BackClicked -= OnBack;
            screen.FightClicked -= OnFight;
            screen.LocationClicked -= OnLocation;
            screen.DifficultyClicked -= OnDifficulty;
            screen.ArenaChanged -= OnArena;
            screen.WeaponClicked -= OnWeapon;
            screen.ThrowableClicked -= OnThrowable;
            screen.BoosterClicked -= OnBooster;
        }

        /// <summary>Fills the screen for <paramref name="levelId"/> (the next level to play) with the last preparation.</summary>
        public void Open(string levelId)
        {
            if (Screen == null) return;
            IsOpen = true;
            _location = LocationIndexOf(levelId);
            _difficulty = HighestOpenDifficulty(LevelAt(_location));
            _arena = false;

            _preparation.GetStartWeapons(_weapons);
            _preparation.GetStartThrowables(_throwables);
            _preparation.GetBoosters(_boosters);
            var loadout = _progress.Loadout;
            _weapon = 1 + _weapons.IndexOf(_preparation.Find<WeaponDefinition>(loadout?.WeaponId));
            _throwable = 1 + _throwables.IndexOf(_preparation.Find<ThrowableDefinition>(loadout?.ThrowableId));
            // Never chosen (or «none», not kept): the first open one — the grenade.
            if (_throwable == 0 && string.IsNullOrEmpty(loadout?.ThrowableId) && _throwables.Count > 0) _throwable = 1;
            _chosenBoosters.Clear();
            if (loadout != null)
                foreach (var id in loadout.Boosters)
                    if (_preparation.Find<BoosterDefinition>(id) is { } booster) _chosenBoosters.Add(booster);
            DropUnavailableBoosters();
            Refresh();
            ShowWeapon();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _podium?.EndPreview();
        }

        private void OnLanguageChanged()
        {
            if (IsOpen) Refresh();
        }

        // ---------------------------------------------------------------- the screen

        private void Refresh()
        {
            var screen = Screen;
            if (screen == null) return;
            screen.SetMoney(_progress.Money);

            for (var i = 0; i < screen.LocationCount && i < Locations.Length; i++)
            {
                var level = LevelAt(i);
                var open = level != null && _progress.IsUnlocked(level.LevelId);
                screen.SetLocation(i, _texts.Get(TextKeys.LocationPrefix + Locations[i].ToString().ToLowerInvariant()),
                    _texts.Get(TextKeys.SetupSoon), level != null, open, i == _location);
            }

            var chosen = LevelAt(_location);
            for (var d = 0; d < screen.DifficultyCount && d < DifficultyNames.Length; d++)
            {
                var open = chosen != null && _progress.IsDifficultyOpen(chosen.LevelId, d);
                var completed = chosen != null && _progress.IsCompletedOn(chosen.LevelId, d);
                screen.SetDifficulty(d, _texts.Get(TextKeys.DifficultyPrefix + DifficultyNames[d]), open, completed, d == _difficulty);
            }

            screen.SetArena(_difficulty >= 1, _arena && _difficulty >= 1);

            _items.Clear();
            _items.Add(new RunSetupScreen.Item { Icon = WeaponIcon(_preparation.BaseWeapon), Plain = true });
            foreach (var weapon in _weapons)
                _items.Add(new RunSetupScreen.Item { Icon = WeaponIcon(weapon), Rarity = weapon.Rarity });
            screen.SetWeapons(_items, _weapon);

            _items.Clear();
            _items.Add(new RunSetupScreen.Item { Icon = null, Plain = true });
            foreach (var throwable in _throwables)
                _items.Add(new RunSetupScreen.Item { Icon = ItemIcon(throwable), Rarity = throwable.Rarity });
            screen.SetThrowables(_items, _throwable);

            _rows.Clear();
            foreach (var booster in _boosters)
            {
                var price = PriceOf(booster);
                _rows.Add(new RunSetupScreen.Booster
                {
                    Icon = booster.Icon,
                    Name = TextOr(TextKeys.BoosterPrefix + booster.Id, booster.Id),
                    Text = _preparation.IsOpen(booster)
                        ? TextOr(TextKeys.BoosterPrefix + booster.Id + TextKeys.BoosterTextSuffix, string.Empty)
                        : _texts.Get(TextKeys.SetupBoostersLocked),
                    Price = price,
                    Open = IsOffered(booster),
                    Chosen = _chosenBoosters.Contains(booster),
                });
            }

            screen.SetBoosters(_rows);
            var total = Total();
            screen.SetTotal(total, chosen != null && _progress.IsDifficultyOpen(chosen.LevelId, _difficulty) &&
                                   total <= _progress.Money);
        }

        private void ShowWeapon() => _podium?.Preview(_weapon > 0 ? _weapons[_weapon - 1] : null);

        // ---------------------------------------------------------------- events

        private void OnLocation(int index)
        {
            var level = LevelAt(index);
            if (level == null || !_progress.IsUnlocked(level.LevelId)) return;
            _location = index;
            _difficulty = HighestOpenDifficulty(level);
            DropUnavailableBoosters();
            Refresh();
        }

        private void OnDifficulty(int index)
        {
            var level = LevelAt(_location);
            if (level == null || !_progress.IsDifficultyOpen(level.LevelId, index)) return;
            _difficulty = index;
            DropUnavailableBoosters();
            Refresh();
        }

        private void OnArena(bool on)
        {
            _arena = on;
            Refresh();
        }

        private void OnWeapon(int index)
        {
            if (index < 0 || index > _weapons.Count) return;
            _weapon = index;
            Refresh();
            ShowWeapon();
        }

        private void OnThrowable(int index)
        {
            if (index < 0 || index > _throwables.Count) return;
            _throwable = index;
            Refresh();
        }

        private void OnBooster(int index)
        {
            if (index < 0 || index >= _boosters.Count) return;
            var booster = _boosters[index];
            if (!IsOffered(booster)) return;
            if (!_chosenBoosters.Remove(booster)) _chosenBoosters.Add(booster);
            Refresh();
        }

        private void OnBack() => BackRequested?.Invoke();

        private void OnFight()
        {
            var level = LevelAt(_location);
            if (level == null || !_progress.IsDifficultyOpen(level.LevelId, _difficulty)) return;
            var cost = Total();
            if (cost > _progress.Money) return;

            var weapon = _weapon > 0 ? _weapons[_weapon - 1] : null;
            var throwable = _throwable > 0 ? _throwables[_throwable - 1] : null;
            _launchBoosters.Clear();
            _ids.Clear();
            foreach (var booster in _boosters)
                if (_chosenBoosters.Contains(booster))
                {
                    _launchBoosters.Add(booster);
                    _ids.Add(booster.Id);
                }

            _progress.SaveLoadout(weapon != null ? weapon.Id : string.Empty, throwable != null ? throwable.Id : string.Empty, _ids);
            var options = new LevelLaunchOptions(difficulty: _flow.DifficultyAt(_difficulty), startWeapon: weapon,
                startThrowable: throwable, boosters: new List<BoosterDefinition>(_launchBoosters),
                closeArena: _arena && _difficulty >= 1);
            FightRequested?.Invoke(level.LevelId, options, cost);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>The first catalog level of the location tile; null — none yet («СКОРО»).</summary>
        private LevelCatalogEntry LevelAt(int index)
        {
            if (index < 0 || index >= Locations.Length) return null;
            foreach (var level in _catalog.Levels)
                if (level.Location == Locations[index])
                    return level;
            return null;
        }

        private int LocationIndexOf(string levelId)
        {
            var entry = levelId != null ? _catalog.Find(levelId) : null;
            var index = entry != null ? Array.IndexOf(Locations, entry.Location) : -1;
            return index >= 0 ? index : 0;
        }

        private int HighestOpenDifficulty(LevelCatalogEntry level)
        {
            if (level == null) return 0;
            for (var d = DifficultyNames.Length - 1; d > 0; d--)
                if (_progress.IsDifficultyOpen(level.LevelId, d))
                    return d;
            return 0;
        }

        private int PriceOf(BoosterDefinition booster) =>
            _preparation.PriceOf(booster, Locations[Mathf.Clamp(_location, 0, Locations.Length - 1)], _difficulty);

        /// <summary>Open and offered here (has a price on this location and difficulty).</summary>
        private bool IsOffered(BoosterDefinition booster) => _preparation.IsOpen(booster) && PriceOf(booster) > 0;

        private void DropUnavailableBoosters() => _chosenBoosters.RemoveWhere(booster => !IsOffered(booster));

        private int Total()
        {
            var total = 0;
            foreach (var booster in _chosenBoosters) total += PriceOf(booster);
            return total;
        }

        private string TextOr(string key, string fallback) => _texts.Has(key) ? _texts.Get(key) : fallback;

        private Sprite WeaponIcon(WeaponDefinition weapon) =>
            weapon != null && _shared.WeaponVisuals != null ? _shared.WeaponVisuals.Find(weapon)?.Icon : null;

        private Sprite ItemIcon(ThrowableDefinition throwable) =>
            throwable != null && _shared.ItemVisuals != null ? _shared.ItemVisuals.Find(throwable)?.Icon : null;
    }
}
