using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Core.Common;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Level;
using Maze.Gameplay.Weapons;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// The weapon in the player's hands (ТЗ §39: WeaponRuntime → WeaponView). On load, the held prefabs of the
    /// weapons lying in this level are loaded (<see cref="WeaponVisualCatalog"/>, level asset owner) and instantiated
    /// hidden in the hands — nothing is loaded or created during play (and <see cref="LevelWarmup"/> draws them once). The active weapon is shown, the other slot hidden. A melee weapon sits rigidly in the
    /// right hand (<see cref="CharacterWeaponRig.MeleeSocket"/>); a gun is posed every frame after animation by
    /// <see cref="CharacterWeaponRig.PoseGun"/>: grip in the right palm, barrel where the clip points it, chest turned
    /// toward the facing, left hand on the gun's <see cref="WeaponModel.GripLeft"/>. Must be registered after <see cref="PlayerViewPresenter"/>
    /// (same load stage and late tick: the player is placed first).
    /// </summary>
    public sealed class PlayerWeaponPresenter : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private readonly LevelData _level;
        private readonly PlayerViewPresenter _playerView;
        private readonly WeaponSystem _weapons;
        private readonly WeaponVisualCatalog _catalog;
        private readonly IAssetOwner _assets;
        private readonly Dictionary<WeaponDefinition, GameObject> _prefabs = new Dictionary<WeaponDefinition, GameObject>();
        private readonly Dictionary<WeaponDefinition, GameObject> _instances = new Dictionary<WeaponDefinition, GameObject>();

        private CharacterWeaponRig _rig;
        private GameObject _shown;
        private WeaponModel _shownModel;
        private bool _shownIsGun;
        private Transform _shownGrip;
        private bool _subscribed;

        public PlayerWeaponPresenter(LevelData level, PlayerViewPresenter playerView, WeaponSystem weapons,
            WeaponVisualCatalog catalog, IAssetOwner assets)
        {
            _level = level;
            _playerView = playerView;
            _weapons = weapons;
            _catalog = catalog;
            _assets = assets;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnPlayer;

        /// <summary>The weapon model now in the player's hands, or null.</summary>
        public GameObject Shown => _shown;

        /// <summary>Marks of the weapon model in hands (muzzle, tip), or null.</summary>
        public WeaponModel ShownModel => _shown != null ? _shownModel : null;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _rig = _playerView.View != null ? _playerView.View.GetComponentInChildren<CharacterWeaponRig>() : null;
            if (_rig == null || !_rig.IsValid)
            {
                GameLog.Warning(LogChannel.Visual, "The player prefab has no weapon rig: weapons are not shown in hands.");
                return;
            }

            foreach (var pickup in _level.Weapons)
            {
                var definition = pickup.Definition;
                if (definition == null || _prefabs.ContainsKey(definition)) continue;

                var visual = _catalog != null ? _catalog.Find(definition) : null;
                if (visual?.HeldPrefab == null || !visual.HeldPrefab.RuntimeKeyIsValid())
                {
                    GameLog.Warning(LogChannel.Visual, $"Weapon '{definition.Id}' has no held look.");
                    _prefabs[definition] = null;
                    continue;
                }

                _prefabs[definition] = await _assets.LoadAsync<GameObject>(visual.HeldPrefab, cancellation);
                cancellation.ThrowIfCancellationRequested();
            }

            foreach (var definition in _prefabs.Keys)
            {
                var instance = GetOrCreate(definition);
                if (instance != null) instance.SetActive(false);
            }

            _weapons.Changed += Refresh;
            _subscribed = true;
            Refresh();
        }

        public void LateTick(float deltaTime)
        {
            if (_rig != null && _rig.IsValid)
                _rig.PoseGun(_shownIsGun && _shown != null ? _shown.transform : null, _shownGrip, deltaTime);
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            foreach (var instance in _instances.Values)
                if (instance != null)
                    objects.Add(instance);
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _weapons.Changed -= Refresh;
                _subscribed = false;
            }

            // The models are children of the player view, destroyed with it.
            _instances.Clear();
            _prefabs.Clear();
            _shown = null;
            _shownModel = null;
            _shownGrip = null;
        }

        private void Refresh()
        {
            var weapon = _weapons.Active;
            var next = weapon != null ? GetOrCreate(weapon.Definition) : null;
            if (next == _shown) return;

            if (_shown != null) _shown.SetActive(false);
            _shown = next;
            _shownIsGun = weapon != null && weapon.Slot == WeaponSlot.Ranged;
            _shownGrip = null;
            _shownModel = null;
            if (_shown == null) return;

            var model = _shown.GetComponent<WeaponModel>();
            _shownModel = model;
            _shownGrip = model != null ? model.GripLeft : null;

            _shown.SetActive(true);
            LateTick(0f);
        }

        private GameObject GetOrCreate(WeaponDefinition definition)
        {
            if (definition == null) return null;
            if (_instances.TryGetValue(definition, out var instance)) return instance;

            _prefabs.TryGetValue(definition, out var prefab);
            if (prefab == null)
            {
                _instances[definition] = null;
                return null;
            }

            var parent = definition.Slot == WeaponSlot.Ranged ? _rig.PalmRight : _rig.MeleeSocket;
            instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = "Weapon " + definition.Id;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            _instances[definition] = instance;
            return instance;
        }
    }
}
