using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Core.Visual;
using Purge.Gameplay.Level;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Visibility;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    public sealed class DeployablesView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        internal const float DeployedScale = 2.3f;

        private readonly Deployables _deployables;
        private readonly ItemVisualCatalog _visuals;
        private readonly VisibilitySystem _visibility;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly Dictionary<string, GameObject> _models = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Transform> _muzzles = new Dictionary<string, Transform>();
        private GameObject _decoy;
        private GameObject _turret;

        public DeployablesView(Deployables deployables, ItemVisualCatalog visuals, VisibilitySystem visibility,
            IAssetOwner assets, LevelViewRoot root)
        {
            _deployables = deployables;
            _visuals = visuals;
            _visibility = visibility;
            _assets = assets;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_visuals == null) return;
            foreach (var look in _visuals.Items)
            {
                if (!(look?.Item is ThrowableDefinition item) || item.Deployable == null ||
                    look.Pickup == null || !look.Pickup.RuntimeKeyIsValid()) continue;
                var prefab = await _assets.LoadAsync<GameObject>(look.Pickup, cancellation);
                if (prefab == null) continue;
                var model = UnityEngine.Object.Instantiate(prefab, _root.transform);
                model.name = item.name;
                model.transform.localScale = Vector3.one * DeployedScale;
                model.SetActive(false);
                _models.Add(item.Id, model);
                if (item.Deployable.Kind != DeployableKind.Turret) continue;
                foreach (var child in model.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name != "Muzzle") continue;
                    _muzzles.Add(item.Id, child);
                    break;
                }
            }
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_models.Values);

        public bool TryGetMuzzle(DeployedItem turret, out Transform muzzle)
        {
            if (!_muzzles.TryGetValue(turret.ThrowableId, out muzzle)) return false;
            Place(_models[turret.ThrowableId], turret, CellOf(turret));
            return true;
        }

        public void LateTick(float deltaTime)
        {
            Show(_deployables.Decoy, ref _decoy);
            Show(_deployables.Turret, ref _turret);
        }

        private void Show(DeployedItem item, ref GameObject previous)
        {
            GameObject model = null;
            if (item.Remaining > 0f)
            {
                var cell = CellOf(item);
                if (_visibility.IsVisible(cell) && _models.TryGetValue(item.ThrowableId, out model))
                    Place(model, item, cell);
            }
            if (previous == model) return;
            if (previous != null) previous.SetActive(false);
            if (model != null) model.SetActive(true);
            previous = model;
        }

        private static GridPosition CellOf(DeployedItem item) =>
            new GridPosition(Mathf.RoundToInt(item.Position.x), Mathf.RoundToInt(item.Position.y), item.Floor);

        private static void Place(GameObject model, DeployedItem item, GridPosition cell)
        {
            var heading = Mathf.Atan2(item.Facing.x, item.Facing.y) * Mathf.Rad2Deg;
            model.transform.SetPositionAndRotation(cell.ToWorld(), Quaternion.Euler(0f, heading, 0f));
        }

        public void Dispose()
        {
            foreach (var model in _models.Values) UnityEngine.Object.Destroy(model);
            _models.Clear();
            _muzzles.Clear();
        }
    }
}
