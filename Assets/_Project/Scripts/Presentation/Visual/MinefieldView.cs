using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Boosters;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Visual;
using Purge.Gameplay.Level;
using Purge.Gameplay.Run;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Visibility;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    public sealed class MinefieldView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private readonly LevelLaunchOptions _options;
        private readonly SpecialRunBoosters _boosters;
        private readonly ContentCatalog _content;
        private readonly ItemVisualCatalog _visuals;
        private readonly VisibilitySystem _visibility;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly GameObject[] _models = new GameObject[4];
        private readonly GameObject[] _plantedModels = new GameObject[PlayerThrowing.MineLimit];
        private readonly PlayerThrowing _throwing;

        public MinefieldView(LevelLaunchOptions options, SpecialRunBoosters boosters, ContentCatalog content,
            ItemVisualCatalog visuals, VisibilitySystem visibility, IAssetOwner assets, LevelViewRoot root,
            PlayerThrowing throwing)
        {
            _options = options;
            _boosters = boosters;
            _content = content;
            _visuals = visuals;
            _visibility = visibility;
            _assets = assets;
            _root = root;
            _throwing = throwing;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var mine = _content?.Find<ThrowableDefinition>("mine");
            var look = mine != null ? _visuals?.Find(mine)?.Flying : null;
            if (look == null || !look.RuntimeKeyIsValid()) return;
            var prefab = await _assets.LoadAsync<GameObject>(look, cancellation);
            if (prefab == null) return;
            if (_options.HasBoost(SpecialBoosterKind.Minefield)) BuildModels(prefab, _models);
            if (mine.Mine != null) BuildModels(prefab, _plantedModels);
        }

        private void BuildModels(GameObject prefab, GameObject[] models)
        {
            for (var i = 0; i < models.Length; i++)
            {
                models[i] = UnityEngine.Object.Instantiate(prefab, _root.transform);
                models[i].name = "Placed mine";
                models[i].SetActive(false);
            }
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            foreach (var model in _models)
                if (model != null) objects.Add(model);
            foreach (var model in _plantedModels)
                if (model != null) objects.Add(model);
        }

        public void LateTick(float deltaTime)
        {
            var mines = _boosters.Mines;
            for (var i = 0; i < _models.Length; i++)
            {
                var model = _models[i];
                if (model == null) continue;
                var shown = false;
                if (i < mines.Count)
                {
                    var mine = mines[i];
                    var cell = new GridPosition((int)mine.x, (int)mine.y, _boosters.MineFloor);
                    shown = _visibility.IsVisible(cell);
                    if (shown) model.transform.position = cell.ToWorld() + Vector3.up * 0.08f;
                }
                if (model.activeSelf != shown) model.SetActive(shown);
            }
            var planted = _throwing.Mines;
            for (var i = 0; i < _plantedModels.Length; i++)
            {
                var model = _plantedModels[i];
                if (model == null) continue;
                var shown = false;
                if (i < planted.Count)
                {
                    var mine = planted[i];
                    var cell = new GridPosition(Mathf.RoundToInt(mine.Position.x), Mathf.RoundToInt(mine.Position.y), mine.Floor);
                    shown = _visibility.IsVisible(cell);
                    if (shown)
                        model.transform.position = new Vector3(mine.Position.x, cell.ToWorld().y + 0.08f, mine.Position.y);
                }

                if (model.activeSelf != shown) model.SetActive(shown);
            }
        }

        public void Dispose()
        {
            foreach (var model in _models)
                if (model != null) UnityEngine.Object.Destroy(model);
            foreach (var model in _plantedModels)
                if (model != null) UnityEngine.Object.Destroy(model);
        }
    }
}
