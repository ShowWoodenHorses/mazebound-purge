using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Application.Flow;
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
    /// Owns the podium combat loop shared by the main menu and run preparation. The character and demo
    /// assets load asynchronously on the first visit and stay available for the app's lifetime.
    /// </summary>
    public sealed class MenuPodiumPresenter : IMainMenuPreparation, IDisposable
    {
        private readonly MenuPodiumView _view;
        private readonly SharedDefinitionsService _definitions;
        private readonly IAddressablesService _addressables;
        private readonly Dictionary<WeaponDefinition, GameObject> _weapons = new Dictionary<WeaponDefinition, GameObject>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private IAssetOwner _assets;
        private GameFlow _flow;
        private GameObject _character;
        private Animator _animator;
        private CharacterWeaponRig _rig;
        private MenuPodiumDemo _demo;
        private bool _demoVisible = true;

        public MenuPodiumPresenter(SharedDefinitionsService definitions,
            IAddressablesService addressables, MenuPodiumView view = null)
        {
            _definitions = definitions;
            _addressables = addressables;
            _view = view;
        }

        public int LoadedAssetCount => _assets?.HandleCount ?? 0;

        public void SetDemoVisible(bool visible)
        {
            _demoVisible = visible;
            UpdateVisibility();
        }

        public void Initialize(GameFlow flow)
        {
            if (_flow != null || _view == null) return;
            _flow = flow;
            _flow.StateChanged += OnStateChanged;
            OnStateChanged(_flow.State);
        }

        public void Dispose()
        {
            if (_flow != null) _flow.StateChanged -= OnStateChanged;
            _flow = null;
            _lifetime.Cancel();
            _lifetime.Dispose();
            ReleaseAssets();
        }

        private void OnStateChanged(GameFlowState _) => UpdateVisibility();

        private void UpdateVisibility()
        {
            if (_flow == null) return;
            var visible = _demoVisible && _flow.State == GameFlowState.MainMenu && _demo?.IsReady == true;
            _view.SetVisible(visible);
            _view.SetDemonstrating(visible);
        }

        public async UniTask PrepareAsync(CancellationToken cancellation)
        {
            if (_view == null || _demo != null) return;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
            MenuPodiumDemo demo = null;
            try
            {
                await LoadCharacterAsync(lifetime.Token);
                if (_character == null) return;
                demo = new MenuPodiumDemo(_view, _animator, _rig, ShowLoadedWeapon);
                await demo.LoadAsync(_definitions, _assets, GetOrCreateAsync, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                if (!demo.IsReady) return;
                _view.SetDemo(demo);
                _demo = demo;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Decoration only: the menu works without it.
                GameLog.Warning(LogChannel.Visual, $"Menu podium: {exception.Message}");
            }
            finally
            {
                if (_demo == null)
                {
                    demo?.Destroy();
                    ReleaseAssets();
                }
            }
        }

        private void ReleaseAssets()
        {
            _demo?.Destroy();
            _demo = null;
            UnityObjects.Destroy(_character);
            _character = null;
            _assets?.Dispose();
            _assets = null;
            _weapons.Clear();
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

        private WeaponModel ShowLoadedWeapon(WeaponDefinition weapon) => ApplyWeapon(weapon, _weapons[weapon]);

        private WeaponModel ApplyWeapon(WeaponDefinition weapon, GameObject instance)
        {
            foreach (var pair in _weapons)
                if (pair.Value != null)
                    pair.Value.SetActive(pair.Value == instance);

            SetHold(weapon);
            var model = instance != null ? instance.GetComponent<WeaponModel>() : null;
            var gun = instance != null && !weapon.IsMelee ? instance.transform : null;
            // The base pistol uses the clip in combat, but its barrel should face forward on the podium.
            var alignWithFacing = gun != null && (weapon == _definitions.Player?.BaseWeapon || model?.AlignWithAim == true);
            _view.SetPose(_rig, gun, gun != null ? model : null, alignWithFacing);
            return model;
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
            if (model != null) instance.transform.localScale *= model.HeldScale;
            instance.SetActive(false);
            return _weapons[weapon] = instance;
        }
    }
}
