using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Application.Flow;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Core.Visual;
using Purge.Presentation.Visual;
using UnityEngine;
using P = Purge.Presentation.Visual.PlayerAnimatorParameters;

namespace Purge.Presentation.Menu
{
    /// <summary>
    /// The character on the main menu's podium (stage 11a): the player's look with the weapon of slot 1 of the last
    /// preparation (<see cref="IProgressService.Loadout"/>; none or unknown — the base pistol), its hold set on the
    /// Animator. Loaded on the first main menu by its own asset owner and kept for the application's lifetime (the
    /// player's look is shared with the levels anyway); a weapon not shown yet is loaded when the loadout names it. The
    /// stage is shown only in <see cref="GameFlowState.MainMenu"/>.
    /// </summary>
    public sealed class MenuPodiumPresenter : IDisposable
    {
        private readonly GameFlow _flow;
        private readonly MenuPodiumView _view;
        private readonly SharedDefinitionsService _definitions;
        private readonly IProgressService _progress;
        private readonly IAddressablesService _addressables;
        private readonly Dictionary<WeaponDefinition, GameObject> _weapons = new Dictionary<WeaponDefinition, GameObject>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private IAssetOwner _assets;
        private GameObject _character;
        private Animator _animator;
        private CharacterWeaponRig _rig;
        private WeaponDefinition _shown;
        private bool _loading;
        private bool _initialized;

        public MenuPodiumPresenter(GameFlow flow, SharedDefinitionsService definitions, IProgressService progress,
            IAddressablesService addressables, MenuPodiumView view = null)
        {
            _flow = flow;
            _definitions = definitions;
            _progress = progress;
            _addressables = addressables;
            _view = view;
        }

        /// <summary>The character or a weapon is loading (set as soon as the main menu opens).</summary>
        public bool IsLoading => _loading;

        public void Initialize()
        {
            if (_initialized || _view == null) return;
            _initialized = true;
            _flow.StateChanged += OnStateChanged;
            OnStateChanged(_flow.State);
        }

        public void Dispose()
        {
            if (_initialized) _flow.StateChanged -= OnStateChanged;
            _initialized = false;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _assets?.Dispose();
            _assets = null;
            _weapons.Clear();
        }

        private void OnStateChanged(GameFlowState state)
        {
            var menu = state == GameFlowState.MainMenu;
            if (_view != null) _view.SetVisible(menu);
            if (menu) RefreshAsync(_lifetime.Token).Forget();
        }

        private async UniTaskVoid RefreshAsync(CancellationToken cancellation)
        {
            if (_loading) return;
            _loading = true;
            try
            {
                if (_character == null) await LoadCharacterAsync(cancellation);
                if (_character == null) return;
                await ShowWeaponAsync(LoadoutWeapon(), cancellation);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                // Decoration only: the menu works without it.
                GameLog.Warning(LogChannel.Visual, $"Menu podium: {exception.Message}");
            }
            finally
            {
                _loading = false;
            }
        }

        private async UniTask LoadCharacterAsync(CancellationToken cancellation)
        {
            var visual = _definitions.PlayerVisual;
            if (visual == null || visual.Prefab == null || !visual.Prefab.RuntimeKeyIsValid()) return;
            _assets ??= _addressables.CreateOwner("Menu Podium");
            var prefab = await _assets.LoadAsync<GameObject>(visual.Prefab, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (prefab == null || _view == null) return;

            _character = UnityEngine.Object.Instantiate(prefab, _view.Anchor, false);
            _character.name = "Menu Character";
            _character.transform.localPosition = Vector3.zero;
            _character.transform.localRotation = Quaternion.identity;
            _animator = _character.GetComponentInChildren<Animator>();
            if (_animator != null) _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _rig = _character.GetComponentInChildren<CharacterWeaponRig>();
        }

        /// <summary>Slot 1 of the last preparation; empty or no longer in the game — the base pistol.</summary>
        private WeaponDefinition LoadoutWeapon()
        {
            var id = _progress.Loadout?.WeaponId;
            var weapon = !string.IsNullOrEmpty(id) ? _definitions.Content?.Find<WeaponDefinition>(id) : null;
            return weapon != null ? weapon : _definitions.Player?.BaseWeapon;
        }

        private async UniTask ShowWeaponAsync(WeaponDefinition weapon, CancellationToken cancellation)
        {
            if (weapon == _shown && weapon != null) return;
            var instance = weapon != null ? await GetOrCreateAsync(weapon, cancellation) : null;
            cancellation.ThrowIfCancellationRequested();

            foreach (var pair in _weapons)
                if (pair.Value != null)
                    pair.Value.SetActive(pair.Value == instance);
            _shown = weapon;

            SetHold(weapon);
            var model = instance != null ? instance.GetComponent<WeaponModel>() : null;
            var gun = instance != null && !weapon.IsMelee ? instance.transform : null;
            _view.SetPose(_rig, gun, gun != null && model != null ? model.GripLeft : null);
        }

        private void SetHold(WeaponDefinition weapon)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            foreach (var parameter in _animator.parameters)
                if (parameter.nameHash == P.WeaponHash)
                    _animator.SetInteger(P.WeaponHash, weapon == null ? P.WeaponNone : P.SetOf(weapon.Hold));
        }

        private async UniTask<GameObject> GetOrCreateAsync(WeaponDefinition weapon, CancellationToken cancellation)
        {
            if (_weapons.TryGetValue(weapon, out var existing)) return existing;
            if (_rig == null || !_rig.IsValid) return _weapons[weapon] = null;

            var visual = _definitions.WeaponVisuals != null ? _definitions.WeaponVisuals.Find(weapon) : null;
            if (visual?.HeldPrefab == null || !visual.HeldPrefab.RuntimeKeyIsValid())
            {
                GameLog.Warning(LogChannel.Visual, $"Menu podium: weapon '{weapon.Id}' has no held look.");
                return _weapons[weapon] = null;
            }

            var prefab = await _assets.LoadAsync<GameObject>(visual.HeldPrefab, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var parent = weapon.IsMelee ? _rig.MeleeSocket : _rig.PalmRight;
            var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = "Weapon " + weapon.Id;
            instance.transform.localPosition = Vector3.zero;
            var model = instance.GetComponent<WeaponModel>();
            instance.transform.localRotation = model != null ? model.GripRotation : Quaternion.identity;
            return _weapons[weapon] = instance;
        }
    }
}
