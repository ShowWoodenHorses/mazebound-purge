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

        public MinefieldView(LevelLaunchOptions options, SpecialRunBoosters boosters, ContentCatalog content,
            ItemVisualCatalog visuals, VisibilitySystem visibility, IAssetOwner assets, LevelViewRoot root)
        {
            _options = options;
            _boosters = boosters;
            _content = content;
            _visuals = visuals;
            _visibility = visibility;
            _assets = assets;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_options.HasBoost(SpecialBoosterKind.Minefield)) return;
            var mine = _content?.Find<ThrowableDefinition>("mine");
            var look = mine != null ? _visuals?.Find(mine)?.Flying : null;
            if (look == null || !look.RuntimeKeyIsValid()) return;
            var prefab = await _assets.LoadAsync<GameObject>(look, cancellation);
            if (prefab == null) return;
            for (var i = 0; i < _models.Length; i++)
            {
                _models[i] = UnityEngine.Object.Instantiate(prefab, _root.transform);
                _models[i].name = "Minefield mine";
                _models[i].SetActive(false);
            }
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            foreach (var model in _models)
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
        }

        public void Dispose()
        {
            foreach (var model in _models)
                if (model != null) UnityEngine.Object.Destroy(model);
        }
    }
}
