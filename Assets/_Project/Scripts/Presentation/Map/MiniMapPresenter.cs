using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Presentation.UI;
using UnityEngine;

namespace Purge.Presentation.Map
{
    /// <summary>
    /// The HUD mini-map (stage 7г; load stage InitializeUI, after <see cref="MapPresenter"/>): a window of
    /// <see cref="WindowCells"/> cells around the player over the map texture (fog included: only explored cells).
    /// Every frame only the window moves; newly explored cells are uploaded and the icons — monsters in view (dots by
    /// rank), barrels, chests, the dome, under the same switches as the full map — refreshed <see cref="RefreshRate"/>
    /// times a second. Icon slots are made at loading and never grow. A tap opens the full map (the HUD's Map button).
    /// </summary>
    public sealed class MiniMapPresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        /// <summary>Cells across the window.</summary>
        public const int WindowCells = 32;

        /// <summary>Icon refreshes per second.</summary>
        public const float RefreshRate = 10f;

        /// <summary>Most monsters drawn at once (in view inside the window).</summary>
        public const int MonsterSlots = 96;

        private readonly UIRoot _ui;
        private readonly MapPresenter _map;
        private readonly PlayerSystem _player;
        private readonly List<MapIcon> _icons = new List<MapIcon>();
        private float _refreshIn;

        public MiniMapPresenter(UIRoot ui, MapPresenter map, PlayerSystem player)
        {
            _ui = ui;
            _map = map;
            _player = player;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeUI;

        private MiniMapView View => _ui.Hud != null ? _ui.Hud.MiniMap : null;

        /// <summary>Icons as last refreshed (for tests): objects, then monsters.</summary>
        public IReadOnlyList<MapIcon> Icons => _icons;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var view = View;
            if (view == null || _map.Texture == null) return UniTask.CompletedTask;
            view.SetMap(_map.Texture, WindowCells);
            Refresh(view); // makes the icon images now, while loading
            return UniTask.CompletedTask;
        }

        public void Dispose() => View?.Clear();

        public void LateTick(float deltaTime)
        {
            var view = View;
            if (view == null || _map.Texture == null) return;

            _refreshIn -= deltaTime;
            if (_refreshIn <= 0f)
            {
                _refreshIn = 1f / RefreshRate;
                Refresh(view);
            }

            if (_player.IsSpawned)
                view.SetView(MapIconLayout.CenterOf(_player.Position), MapIconLayout.PlayerRotation(_player.Facing));
        }

        private void Refresh(MiniMapView view)
        {
            var set = _ui.Map != null ? _ui.Map.Icons : null;
            if (set == null) return;
            _map.ApplyPending();
            _icons.Clear();
            _map.AddObjectIcons(set, _icons, _map.IsLayerShown(MapLayer.Objects));
            var center = MapIconLayout.CenterOf(_player.Position);
            var half = WindowCells * 0.5f + 1f;
            var window = new Rect(center.x - half - 0.5f, center.y - half - 0.5f, half * 2f, half * 2f);
            _map.AddMonsterIcons(set, _icons, MonsterSlots, _map.IsLayerShown(MapLayer.Zombies), window);
            view.SetIcons(_icons, set);
            view.SetPlayer(set.Player, set.PlayerColor, _player.IsSpawned);
        }
    }
}
