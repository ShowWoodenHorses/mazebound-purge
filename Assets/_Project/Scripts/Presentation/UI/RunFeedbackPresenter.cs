using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Services;
using Purge.Core.Grid;
using Purge.Core.Visual;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Run;
using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The run's little rewards on screen (stage 6e), on levels with waves: a paying kill sends a coin from where the
    /// monster fell to the HUD's money (a pool of <see cref="PoolSize"/> made at load; a kill while all fly, or off
    /// screen, sends none — the counter still counts), the icon punches and clinks as one arrives; a level-up flashes
    /// around the player (<see cref="CombatVisualDefinition.LevelUp"/>).
    /// </summary>
    public sealed class RunFeedbackPresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        public const int PoolSize = 16;
        private const float FlightTime = 0.55f;
        private const float PopHeight = 70f;
        private const float BumpTime = 0.18f;
        private const float BumpScale = 1.35f;
        private const float KillHeight = 0.8f;

        private readonly UIRoot _ui;
        private readonly HordeSystem _horde;
        private readonly RunExperience _experience;
        private readonly PlayerSystem _player;
        private readonly TopDownCamera _camera;
        private readonly PackEffectsPlayer _effects;
        private readonly CombatVisualDefinition _visual;
        private readonly AudioService _audio;
        private readonly List<Coin> _coins = new List<Coin>();
        private readonly Stack<RectTransform> _free = new Stack<RectTransform>();
        private readonly List<RectTransform> _all = new List<RectTransform>();
        private float _bump = -1f;
        private bool _subscribed;

        public RunFeedbackPresenter(UIRoot ui, HordeSystem horde, RunExperience experience, PlayerSystem player,
            TopDownCamera camera, PackEffectsPlayer effects = null, CombatVisualDefinition visual = null,
            AudioService audio = null)
        {
            _ui = ui;
            _horde = horde;
            _experience = experience;
            _player = player;
            _camera = camera;
            _effects = effects;
            _visual = visual;
            _audio = audio;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        private HudScreen Hud => _ui != null ? _ui.Hud : null;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_subscribed || !_horde.UsesWaves) return UniTask.CompletedTask;
            var layer = Hud != null ? Hud.CoinLayer : null;
            if (layer != null && layer.childCount > 0)
            {
                var template = layer.GetChild(0).gameObject;
                for (var i = 0; i < PoolSize; i++)
                {
                    var coin = UnityEngine.Object.Instantiate(template, layer, false).GetComponent<RectTransform>();
                    coin.name = "Coin";
                    coin.gameObject.SetActive(false);
                    _all.Add(coin);
                    _free.Push(coin);
                }
            }

            _horde.Killed += OnKilled;
            _experience.LeveledUp += OnLeveledUp;
            _subscribed = true;
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _horde.Killed -= OnKilled;
                _experience.LeveledUp -= OnLeveledUp;
                _subscribed = false;
            }

            foreach (var coin in _all)
                if (coin != null) UnityEngine.Object.Destroy(coin.gameObject);
            _all.Clear();
            _free.Clear();
            _coins.Clear();
            Hud?.SetMoneyBump(1f);
        }

        public void LateTick(float deltaTime)
        {
            var hud = Hud;
            if (hud == null) return;
            var hasTarget = hud.TryGetCoinTarget(out var target);
            for (var i = _coins.Count - 1; i >= 0; i--)
            {
                var coin = _coins[i];
                coin.Age += deltaTime;
                var t = coin.Age / FlightTime;
                if (t >= 1f || !hasTarget)
                {
                    Return(coin.View);
                    _coins.RemoveAt(i);
                    if (hasTarget) Arrived();
                    continue;
                }

                // Up a little, then fast to the counter (ease-in), shrinking as it lands.
                var ease = t * t;
                var position = Vector2.LerpUnclamped(coin.From, target, ease) + Vector2.up * (PopHeight * 4f * t * (1f - t));
                coin.View.anchoredPosition = position;
                coin.View.localScale = Vector3.one * Mathf.Lerp(1.15f, 0.7f, ease);
                _coins[i] = coin;
            }

            if (_bump >= 0f)
            {
                _bump += deltaTime;
                var k = Mathf.Clamp01(_bump / BumpTime);
                hud.SetMoneyBump(Mathf.Lerp(BumpScale, 1f, k));
                if (k >= 1f) _bump = -1f;
            }
        }

        private void OnKilled(int agent, Vector2 position)
        {
            if (_free.Count == 0) return;
            var definition = _horde.DefinitionOf(agent);
            if (definition == null || definition.Money <= 0) return;
            var hud = Hud;
            var camera = _camera != null ? _camera.Camera : null;
            if (hud == null || camera == null || !hud.IsVisible) return;

            var screen = camera.WorldToScreenPoint(new Vector3(position.x, KillHeight + _player.Floor * GridPosition.FloorHeight,
                position.y));
            if (screen.z <= 0f || screen.x < 0f || screen.y < 0f || screen.x > Screen.width || screen.y > Screen.height) return;
            if (!hud.TryGetCoinPoint(screen, out var from)) return;

            var view = _free.Pop();
            view.anchoredPosition = from;
            view.localScale = Vector3.one;
            view.gameObject.SetActive(true);
            _coins.Add(new Coin { View = view, From = from });
        }

        private void Arrived()
        {
            _bump = 0f;
            if (_audio?.Catalog != null) _audio.Play(_audio.Catalog.Coin, SoundChannel.Level);
        }

        private void OnLeveledUp(int level)
        {
            if (_effects == null || _visual == null) return;
            var position = _player.Position;
            _effects.Play(_visual.LevelUp, new Vector3(position.x, _player.Floor * GridPosition.FloorHeight, position.y),
                Quaternion.identity);
        }

        private void Return(RectTransform view)
        {
            view.gameObject.SetActive(false);
            _free.Push(view);
        }

        private struct Coin
        {
            public RectTransform View;
            public Vector2 From;
            public float Age;
        }
    }
}
