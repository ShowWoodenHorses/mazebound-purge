using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Services;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// Arrows at the screen edge towards off-screen monsters (decision 2026-10-26; load stage InitializeUI,
    /// <see cref="MonsterArrowsView"/>). Directions from the player on screen are split into <see cref="Sectors"/>
    /// sectors; <see cref="RefreshRate"/> times a second each sector picks its nearest living monster beyond the screen
    /// (fog does not matter: off-screen is the rule). Every frame the picked ones are re-placed — an arrow goes away as
    /// soon as its monster is on screen. Farther monsters' arrows are fainter. Settings «Game» switch them off.
    /// </summary>
    public sealed class MonsterArrowsPresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        /// <summary>Sectors of directions around the player (an arrow at most per sector).</summary>
        public const int Sectors = 40;

        /// <summary>Picks per second.</summary>
        public const float RefreshRate = 10f;

        /// <summary>A monster this many times farther than the screen edge (along its direction) gets the faintest arrow.</summary>
        private const float FarRatio = 3f;

        private const float FarAlpha = 0.35f;

        private readonly UIRoot _ui;
        private readonly HordeSystem _horde;
        private readonly PlayerSystem _player;
        private readonly TopDownCamera _camera;
        private readonly LevelViewRoot _root;
        private readonly SettingsService _settings;
        private readonly BossSystem _bosses;
        private readonly int[] _agents = new int[Sectors];
        private readonly float[] _nearest = new float[Sectors];
        private Matrix4x4 _toClip;
        private Rect _screen;
        private float _refreshIn;
        private bool _enabled;
        private bool _subscribed;

        public MonsterArrowsPresenter(UIRoot ui, PlayerSystem player, TopDownCamera camera, LevelViewRoot root,
            SettingsService settings, HordeSystem horde = null, BossSystem bosses = null)
        {
            _bosses = bosses;
            _ui = ui;
            _player = player;
            _camera = camera;
            _root = root;
            _settings = settings;
            _horde = horde;
            for (var i = 0; i < Sectors; i++) _agents[i] = -1;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        private MonsterArrowsView View => _ui.Hud != null ? _ui.Hud.MonsterArrows : null;

        /// <summary>Agent each sector points at; -1 — no arrow (for tests).</summary>
        public ReadOnlySpan<int> Agents => _agents;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            View?.Prepare(Sectors);
            if (!_subscribed)
            {
                _settings.GameChanged += Apply;
                _subscribed = true;
            }

            Apply();
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _settings.GameChanged -= Apply;
                _subscribed = false;
            }

            View?.Clear();
        }

        public void LateTick(float deltaTime)
        {
            var view = View;
            if (view == null) return;
            var bosses = _bosses != null && (_bosses.IsBossAlive || _bosses.ShowArenaArrow);
            if (!_enabled && !bosses || _horde == null || !_horde.IsActive || !_player.IsSpawned || _camera == null ||
                _camera.Camera == null)
            {
                Forget(view);
                return;
            }

            var camera = _camera.Camera;
            _screen = camera.pixelRect;
            _toClip = camera.projectionMatrix * camera.worldToCameraMatrix * _root.transform.localToWorldMatrix;
            if (!TryScreen(_player.Position, out var origin)) return;

            if (_enabled)
            {
                _refreshIn -= deltaTime;
                if (_refreshIn <= 0f)
                {
                    _refreshIn = 1f / RefreshRate;
                    Pick(origin);
                }
            }
            else
            {
                for (var i = 0; i < Sectors; i++) _agents[i] = -1;
            }

            Place(view, origin);
            PlaceSpecial(view, origin);
        }

        /// <summary>
        /// The boss's arrow whenever it is off screen (ТЗ: боссы видны всегда — whatever the settings), the arena's while a
        /// boss waits in it and the arena is off screen (stage 8a).
        /// </summary>
        private void PlaceSpecial(MonsterArrowsView view, Vector2 origin)
        {
            var boss = _bosses != null && _bosses.IsBossAlive;
            var arena = _bosses != null && _bosses.ShowArenaArrow;
            if (!TryBounds(view, origin, out var bounds, out var localOrigin))
            {
                view.HideSpecial(MonsterArrowsView.Special.Boss);
                view.HideSpecial(MonsterArrowsView.Special.Arena);
                return;
            }

            ShowSpecial(view, MonsterArrowsView.Special.Boss, boss, boss ? _bosses.Position : default, origin, bounds, localOrigin);
            ShowSpecial(view, MonsterArrowsView.Special.Arena, arena, arena ? _bosses.ArenaCenter : default, origin, bounds,
                localOrigin);
        }

        private void ShowSpecial(MonsterArrowsView view, MonsterArrowsView.Special which, bool wanted, Vector2 target, Vector2 origin,
            Rect bounds, Vector2 localOrigin)
        {
            if (!wanted || !TryScreen(target, out var point) || _screen.Contains(point))
            {
                view.HideSpecial(which);
                return;
            }

            var direction = (point - origin).normalized;
            var local = localOrigin + direction * RayToEdge(localOrigin, direction, bounds);
            view.ShowSpecial(which, local, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }

        /// <summary>The layer's rectangle less the edge inset, and the player's point in it (clamped inside).</summary>
        private static bool TryBounds(MonsterArrowsView view, Vector2 origin, out Rect bounds, out Vector2 localOrigin)
        {
            var layer = view.Layer;
            bounds = layer.rect;
            var inset = view.EdgeInset;
            bounds.xMin += inset;
            bounds.yMin += inset;
            bounds.xMax -= inset;
            bounds.yMax -= inset;
            if (bounds.width <= 0f || bounds.height <= 0f ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, origin, null, out localOrigin))
            {
                localOrigin = default;
                return false;
            }

            localOrigin = Vector2.Max(bounds.min, Vector2.Min(bounds.max, localOrigin));
            return true;
        }

        private void Apply()
        {
            _enabled = _settings.Game.MonsterArrows;
            _refreshIn = 0f;
        }

        private void Forget(MonsterArrowsView view)
        {
            for (var i = 0; i < Sectors; i++) _agents[i] = -1;
            view.HideAll();
        }

        /// <summary>Each sector's nearest living monster beyond the screen.</summary>
        private void Pick(Vector2 origin)
        {
            for (var i = 0; i < Sectors; i++)
            {
                _agents[i] = -1;
                _nearest[i] = float.MaxValue;
            }

            var positions = _horde.Positions;
            var states = _horde.States;
            var boss = _bosses != null ? _bosses.Boss : -1;
            for (var agent = 0; agent < _horde.HighWater; agent++)
            {
                if (agent == boss) continue; // its own arrow
                if (!IsAlive(states[agent]) || !TryScreen(positions[agent], out var point) || _screen.Contains(point)) continue;
                var offset = point - origin;
                var distance = offset.sqrMagnitude;
                var sector = SectorOf(offset);
                if (distance >= _nearest[sector]) continue;
                _nearest[sector] = distance;
                _agents[sector] = agent;
            }
        }

        private void Place(MonsterArrowsView view, Vector2 origin)
        {
            if (!TryBounds(view, origin, out var bounds, out var localOrigin))
            {
                view.HideAll();
                return;
            }

            var positions = _horde.Positions;
            var states = _horde.States;
            for (var i = 0; i < Sectors; i++)
            {
                var agent = _agents[i];
                if (agent < 0 || agent >= _horde.HighWater || !IsAlive(states[agent]) ||
                    !TryScreen(positions[agent], out var point) || _screen.Contains(point))
                {
                    view.Hide(i);
                    continue;
                }

                var offset = point - origin;
                var direction = offset.normalized;
                var ratio = offset.magnitude / Mathf.Max(1f, RayToEdge(origin, direction, _screen));
                var alpha = Mathf.Lerp(1f, FarAlpha, (ratio - 1f) / (FarRatio - 1f));
                var local = localOrigin + direction * RayToEdge(localOrigin, direction, bounds);
                view.Show(i, local, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, alpha);
            }
        }

        private static bool IsAlive(HordeAgentState state) =>
            state != HordeAgentState.Inactive && state != HordeAgentState.Dying;

        private static int SectorOf(Vector2 offset)
        {
            var turn = (Mathf.Atan2(offset.y, offset.x) + Mathf.PI) / (2f * Mathf.PI);
            return Mathf.Clamp((int)(turn * Sectors), 0, Sectors - 1);
        }

        /// <summary>Distance from <paramref name="from"/> (inside) along <paramref name="direction"/> to the edge of <paramref name="rect"/>.</summary>
        private static float RayToEdge(Vector2 from, Vector2 direction, Rect rect)
        {
            var t = float.MaxValue;
            if (direction.x > 1e-5f) t = Mathf.Min(t, (rect.xMax - from.x) / direction.x);
            else if (direction.x < -1e-5f) t = Mathf.Min(t, (rect.xMin - from.x) / direction.x);
            if (direction.y > 1e-5f) t = Mathf.Min(t, (rect.yMax - from.y) / direction.y);
            else if (direction.y < -1e-5f) t = Mathf.Min(t, (rect.yMin - from.y) / direction.y);
            return t == float.MaxValue ? 0f : Mathf.Max(0f, t);
        }

        /// <summary>Screen point (pixels) of a grid position on the ground; false behind the camera.</summary>
        private bool TryScreen(Vector2 position, out Vector2 point)
        {
            var clip = _toClip * new Vector4(position.x, 0f, position.y, 1f);
            if (clip.w <= 1e-5f)
            {
                point = default;
                return false;
            }

            point = new Vector2(
                _screen.x + (clip.x / clip.w * 0.5f + 0.5f) * _screen.width,
                _screen.y + (clip.y / clip.w * 0.5f + 0.5f) * _screen.height);
            return true;
        }
    }
}
