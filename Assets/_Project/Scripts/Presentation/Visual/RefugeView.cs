using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Common;
using Purge.Core.Content;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Objects;
using Purge.Gameplay.Level;
using Purge.Gameplay.Objects;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// The safe dome's look (stage 7в): <see cref="RefugeDefinition.Look"/> (a looping effect, radius 1 at scale 1)
    /// loaded with the level, scaled to the dome's edge, shown while it stands; in the last seconds of a wave's time with
    /// the player inside it flickers — by its renderers: the particles keep running (a restart per flash was costly).
    /// </summary>
    public sealed class RefugeView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private const float WarnTime = 3f;

        private readonly LevelData _level;
        private readonly RefugeSystem _refuge;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly ContentCatalog _content;
        private GameObject _dome;
        private ParticleSystem[] _systems;
        private Renderer[] _renderers;
        private bool _rendered = true;
        private float _time;

        public RefugeView(LevelData level, RefugeSystem refuge, IAssetOwner assets, LevelViewRoot root,
            ContentCatalog content = null)
        {
            _level = level;
            _refuge = refuge;
            _assets = assets;
            _root = root;
            _content = content;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_level.Settings.Refuge || _content == null) return;
            var definitions = new List<RefugeDefinition>();
            _content.GetAll(definitions);
            var look = definitions.Count > 0 ? definitions[0].Look : null;
            if (look == null || !look.RuntimeKeyIsValid())
            {
                GameLog.Warning(LogChannel.Visual, "The safe dome has no look (Data/Objects/Refuge).");
                return;
            }

            var prefab = await _assets.LoadAsync<GameObject>(look, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (prefab == null) return;
            _dome = UnityEngine.Object.Instantiate(prefab, _root != null ? _root.transform : null, false);
            _dome.name = "Safe Dome";
            // Scaled to the edge by its transform: the particles follow the hierarchy's scale.
            foreach (var system in _dome.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            _systems = CombatEffect.RootSystems(_dome);
            _renderers = _dome.GetComponentsInChildren<Renderer>(true);
            _dome.SetActive(false);
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (_dome != null) objects.Add(_dome);
        }

        public void LateTick(float deltaTime)
        {
            if (_dome == null) return;
            _time += deltaTime;
            var shown = _refuge.IsUp;
            if (_dome.activeSelf != shown)
            {
                _dome.SetActive(shown);
                if (shown) CombatEffect.Restart(_systems); // pack copies do not play on awake
            }

            if (!shown) return;
            var rendered = !(_refuge.IsLimited && _refuge.PlayerInside && _refuge.TimeLeft < WarnTime) ||
                           Mathf.Repeat(_time, 0.25f) > 0.08f;
            if (_rendered != rendered)
            {
                _rendered = rendered;
                foreach (var renderer in _renderers)
                    renderer.enabled = rendered;
            }

            var transform = _dome.transform;
            transform.localPosition = new Vector3(_refuge.Center.x, _refuge.Floor * GridPosition.FloorHeight, _refuge.Center.y);
            transform.localScale = Vector3.one * _refuge.Edge;
        }

        public void Dispose()
        {
            if (_dome != null) UnityObjects.Destroy(_dome);
            _dome = null;
        }
    }
}
