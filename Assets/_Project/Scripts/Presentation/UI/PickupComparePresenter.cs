using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Core.Definitions;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Weapons;
using Purge.Presentation.Localization;
using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// Comparison at a weapon on the ground (stage 6e2): while the player stands in a cell with a weapon, the HUD's
    /// <see cref="PickupComparePanel"/> hangs over it — rarity, then damage per second, magazine, range and mobility
    /// against the weapon of the active slot (<see cref="WeaponStats"/>). The player's setting «Weapon comparison»
    /// (<see cref="GameSettings.Compare"/>) chooses arrows, colours or no panel. Texts are refilled only when the weapon,
    /// the one in hands, the mode or the language change; the place follows the camera every frame.
    /// </summary>
    public sealed class PickupComparePresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        /// <summary>Height over the ground the panel's bottom hangs at, metres.</summary>
        private const float Height = 0.9f;

        private readonly UIRoot _ui;
        private readonly PlayerSystem _player;
        private readonly PickupSystem _pickups;
        private readonly WeaponSystem _weapons;
        private readonly SettingsService _settings;
        private readonly LocalizationService _texts;
        private readonly TopDownCamera _camera;
        private readonly LevelViewRoot _root;
        private bool _subscribed;
        private bool _dirty = true;
        private Pickup _shown;
        private WeaponDefinition _against;
        private PickupCompare _mode;

        public PickupComparePresenter(UIRoot ui, PlayerSystem player, PickupSystem pickups, WeaponSystem weapons,
            SettingsService settings, LocalizationService texts, TopDownCamera camera, LevelViewRoot root)
        {
            _ui = ui;
            _player = player;
            _pickups = pickups;
            _weapons = weapons;
            _settings = settings;
            _texts = texts;
            _camera = camera;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        private PickupComparePanel Panel => _ui != null && _ui.Hud != null ? _ui.Hud.ComparePanel : null;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_subscribed)
            {
                _texts.LanguageChanged += MarkDirty;
                _subscribed = true;
            }

            _shown = null;
            Panel?.SetVisible(false);
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_subscribed) _texts.LanguageChanged -= MarkDirty;
            _subscribed = false;
            if (Panel != null) Panel.SetVisible(false);
        }

        private void MarkDirty() => _dirty = true;

        public void LateTick(float deltaTime)
        {
            var panel = Panel;
            if (panel == null) return;
            var mode = _settings.Game.Compare;
            var pickup = mode != PickupCompare.Off && _player.IsSpawned ? WeaponAt() : null;
            if (pickup == null)
            {
                _shown = null;
                panel.SetVisible(false);
                return;
            }

            var against = _weapons.Active != null ? _weapons.Active.Definition : null;
            if (_dirty || pickup != _shown || against != _against || mode != _mode)
            {
                _dirty = false;
                _shown = pickup;
                _against = against;
                _mode = mode;
                Fill(panel, pickup.Weapon.Definition, against, mode);
            }

            panel.SetVisible(true);
            var world = _root.transform.TransformPoint(new Vector3(pickup.Position.x, Height, pickup.Position.y));
            panel.Place(_camera.Camera.WorldToScreenPoint(world));
        }

        /// <summary>The first weapon lying in the player's cell (not one flying to the player).</summary>
        private Pickup WeaponAt()
        {
            var lying = _pickups.At(_player.Cell);
            for (var i = 0; i < lying.Count; i++)
                if (lying[i].Kind == PickupKind.Weapon && lying[i].Weapon != null && !lying[i].Attracted)
                    return lying[i];
            return null;
        }

        private void Fill(PickupComparePanel panel, WeaponDefinition weapon, WeaponDefinition against, PickupCompare mode)
        {
            panel.SetHeader(WeaponName(weapon), weapon.Rarity);
            panel.SetCaption(0, _texts.Get(TextKeys.CompareDps));
            panel.SetCaption(1, _texts.Get(TextKeys.CompareMagazine));
            panel.SetCaption(2, _texts.Get(TextKeys.CompareRange));
            panel.SetCaption(3, _texts.Get(TextKeys.CompareMobility));

            var dps = WeaponStats.DamagePerSecond(weapon);
            panel.SetRow(0, Mathf.RoundToInt(dps).ToString(), Delta(dps, WeaponStats.DamagePerSecond(against), against), mode);

            // Melee has no magazine: a dash, nothing to compare.
            var magazine = WeaponStats.Magazine(weapon);
            var otherMagazine = WeaponStats.Magazine(against);
            panel.SetRow(1, magazine > 0 ? magazine.ToString() : "-",
                magazine > 0 && otherMagazine > 0 ? Delta(magazine, otherMagazine, against) : StatDelta.Same, mode);

            var range = WeaponStats.Range(weapon);
            panel.SetRow(2, range.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), Delta(range, WeaponStats.Range(against), against), mode);

            var mobility = WeaponStats.Mobility(weapon);
            panel.SetRow(3, _texts.Format(TextKeys.Percent, Mathf.RoundToInt(mobility * 100f)),
                Delta(mobility, WeaponStats.Mobility(against), against), mode);
        }

        private static StatDelta Delta(float value, float other, WeaponDefinition against) =>
            against != null ? WeaponStats.Compare(value, other) : StatDelta.Same;

        private string WeaponName(WeaponDefinition weapon)
        {
            var id = string.IsNullOrEmpty(weapon.Id) ? weapon.name : weapon.Id;
            var key = TextKeys.WeaponPrefix + id;
            return _texts.Has(key) ? _texts.Get(key) : id.Replace('_', ' ');
        }
    }
}
