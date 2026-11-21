using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Flow;
using Purge.Application.Platform;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Objects;
using Purge.Gameplay.Player;
using Purge.Gameplay.Run;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Vehicles;
using Purge.Gameplay.Visibility;
using Purge.Gameplay.Weapons;
using Purge.Presentation.Localization;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>Evaluates the remaining optional hints at a low rate and opens at most one pause window at a time.</summary>
    public sealed class GameplayHintPresenter : ILevelLoadStep, ILevelLateTickable
    {
        private const float ProbeInterval = 0.25f;
        private const float BarrelDistanceSquared = 25f;

        private readonly UIRoot _ui;
        private readonly GameFlow _flow;
        private readonly SettingsService _settings;
        private readonly SaveService _save;
        private readonly LocalizationService _texts;
        private readonly AnalyticsService _analytics;
        private readonly PlayerSystem _player;
        private readonly WeaponSystem _weapons;
        private readonly HordeSystem _horde;
        private readonly VisibilitySystem _visibility;
        private readonly ChestSystem _chests;
        private readonly PlayerInventory _inventory;
        private readonly RefugeSystem _refuge;
        private readonly BarrelSystem _barrels;
        private readonly VehicleSystem _vehicles;
        private readonly BossSystem _boss;
        private readonly ThrowableSlot _throwables;
        private readonly CardSystem _cards;
        private readonly List<HintDefinition> _remaining = new List<HintDefinition>();
        private readonly List<int> _visibleMonsters = new List<int>();
        private float _untilProbe = ProbeInterval;
        private bool _monstersScanned;

        public GameplayHintPresenter(UIRoot ui, GameFlow flow, SettingsService settings, SaveService save,
            LocalizationService texts, AnalyticsService analytics, PlayerSystem player, WeaponSystem weapons,
            HordeSystem horde, VisibilitySystem visibility, ChestSystem chests, PlayerInventory inventory,
            RefugeSystem refuge, BarrelSystem barrels, VehicleSystem vehicles, BossSystem boss,
            ThrowableSlot throwables, CardSystem cards)
        {
            _ui = ui;
            _flow = flow;
            _settings = settings;
            _save = save;
            _texts = texts;
            _analytics = analytics;
            _player = player;
            _weapons = weapons;
            _horde = horde;
            _visibility = visibility;
            _chests = chests;
            _inventory = inventory;
            _refuge = refuge;
            _barrels = barrels;
            _vehicles = vehicles;
            _boss = boss;
            _throwables = throwables;
            _cards = cards;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_flow.IsTutorialLevel || _ui == null || _ui.Tutorial == null) return UniTask.CompletedTask;

            foreach (var hint in HintCatalog.LoadDefinitions())
                if (hint != null && !string.IsNullOrEmpty(hint.Id) && !_save.Data.SeenHints.Contains(hint.Id))
                    _remaining.Add(hint);

            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            if (_remaining.Count == 0 || !_settings.Game.GameplayHints || _flow.State != GameFlowState.Playing ||
                !_player.IsSpawned || _ui == null || _ui.Tutorial == null) return;

            _untilProbe -= deltaTime;
            if (_untilProbe > 0f) return;
            _untilProbe = ProbeInterval;
            _monstersScanned = false;

            for (var i = 0; i < _remaining.Count; i++)
            {
                var hint = _remaining[i];
                if (!Matches(hint)) continue;

                var screen = _ui.Tutorial;
                screen.Clear();
                screen.SetDialog(_texts.Get(TextKeys.GameplayHintTitle), _texts.Get(hint.TextKey));
                _flow.OpenTip();
                _remaining.RemoveAt(i);
                _save.Data.SeenHints.Add(hint.Id);
                _save.Save();
                if (!_flow.IsTestRun)
                {
                    var fields = _analytics.Begin();
                    fields["hint"] = hint.Id;
                    _analytics.Send("gameplay_hint_shown");
                }

                break;
            }
        }

        private bool Matches(HintDefinition hint)
        {
            switch (hint.Condition)
            {
                case HintCondition.EquippedThrowable:
                    return !_throwables.IsEmpty && _throwables.Definition.Id == hint.ThrowableId;
                case HintCondition.EvolutionIngredient:
                    foreach (var pair in _cards.Levels)
                        if (_cards.IsEvolutionIngredient(pair.Key)) return true;
                    return false;
                case HintCondition.SilencedWeapon:
                    var weapon = _weapons.Active?.Definition;
                    if (weapon?.Modules == null) return false;
                    foreach (var module in weapon.Modules)
                        if (module is SilencerModule) return true;
                    return false;
                case HintCondition.VisibleMonster:
                case HintCondition.ArmoredMonster:
                case HintCondition.Champion:
                    return MatchesVisibleMonster(hint);
                case HintCondition.Chest:
                    var chest = _chests.FindUsable();
                    return chest != null && chest.Kind == hint.ChestKind;
                case HintCondition.ChestKey:
                    return _inventory.ChestKeys > 0;
                case HintCondition.Refuge:
                    return _refuge.PlayerInside;
                case HintCondition.Barrel:
                    foreach (var barrel in _barrels.Barrels)
                        if (barrel.IsAlive && barrel.Floor == _player.Floor &&
                            (barrel.Position - _player.Position).sqrMagnitude <= BarrelDistanceSquared &&
                            IsVisible(barrel.Position)) return true;
                    return false;
                case HintCondition.Vehicle:
                    return _vehicles.Current != null || _vehicles.FindEnterable() != null;
                case HintCondition.Boss:
                    return _boss.IsBossAlive;
                case HintCondition.Stairs:
                    return _player.IsOnStairs;
                case HintCondition.ClosedArena:
                    return _flow.CurrentArenaClosed && _boss.GatesClosed;
                default:
                    return false;
            }
        }

        private bool MatchesVisibleMonster(HintDefinition hint)
        {
            if (!_monstersScanned)
            {
                _monstersScanned = true;
                _visibleMonsters.Clear();
                var states = _horde.States;
                var positions = _horde.Positions;
                for (var i = 0; i < _horde.HighWater; i++)
                    if (_horde.FloorOf(i) == _player.Floor && HordeSystem.IsAlive(states[i]) && IsVisible(positions[i]))
                        _visibleMonsters.Add(i);
            }

            foreach (var i in _visibleMonsters)
            {
                if (hint.Condition == HintCondition.Champion)
                {
                    if (_horde.IsChampion(i)) return true;
                    continue;
                }

                var monster = _horde.DefinitionOf(i);
                if (monster == null) continue;
                if (hint.Condition == HintCondition.ArmoredMonster && monster.Stats.FrontArmor > 0f)
                    return true;
                if (hint.Condition == HintCondition.VisibleMonster && !string.IsNullOrEmpty(hint.MonsterIdPrefix) &&
                    monster.Id.StartsWith(hint.MonsterIdPrefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private bool IsVisible(Vector2 position) =>
            _visibility.IsVisible(new GridPosition(Mathf.FloorToInt(position.x + 0.5f),
                Mathf.FloorToInt(position.y + 0.5f), _player.Floor));
    }
}
