using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Lighting;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Level;
using Maze.Gameplay.Visibility;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Light of the level's sources (<see cref="LevelData.Lights"/>) as a texture read by the shaders
    /// (MazeLighting.cginc): computed at load by <see cref="LightField"/>; when a door opens or closes only the
    /// lights reaching it are recomputed and the texture is uploaded again (rare event). No lights = no texture.
    /// </summary>
    public sealed class LevelLightMap : ILevelLoadStep, IDisposable
    {
        private static readonly int MapId = Shader.PropertyToID("_MazeLightMap");
        private static readonly int MapSizeId = Shader.PropertyToID("_MazeLightMapSize");

        private readonly LevelData _level;
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;

        private LightField _field;
        private Texture2D _texture;
        private bool _subscribed;

        public LevelLightMap(LevelData level, LevelGrid grid, DoorSystem doors)
        {
            _level = level;
            _grid = grid;
            _doors = doors;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>Null when the level has no lights.</summary>
        public LightField Field => _field;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            Clear();
            if (_level.Lights.Count == 0)
                return UniTask.CompletedTask;

            _field = new LightField(_grid.Width, _grid.Height, _level.Lights);
            _field.Rebuild(new LevelOpacity(_grid, _doors));
            _texture = new Texture2D(_field.TextureWidth, _field.TextureHeight, TextureFormat.RGBA32, false, true)
            {
                name = "Maze Light Map",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            Upload();
            Shader.SetGlobalTexture(MapId, _texture);
            Shader.SetGlobalVector(MapSizeId, new Vector4(1f / _grid.Width, 1f / _grid.Height, LightField.MaxLight, 0f));

            if (!_subscribed)
            {
                _doors.DoorChanged += OnDoorChanged;
                _subscribed = true;
            }

            GameLog.Info(LogChannel.Visual, $"Light map {_field.TextureWidth}x{_field.TextureHeight}, {_level.Lights.Count} light(s).");
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _doors.DoorChanged -= OnDoorChanged;
                _subscribed = false;
            }

            Clear();
        }

        private void OnDoorChanged(DoorData door, bool open)
        {
            if (_field == null) return;
            var region = _field.RebuildAround(door.Position, new LevelOpacity(_grid, _doors));
            if (region.width > 0) Upload();
        }

        private void Upload()
        {
            _texture.SetPixels32(_field.Pixels);
            _texture.Apply(false);
        }

        private void Clear()
        {
            Shader.SetGlobalVector(MapSizeId, Vector4.zero);
            Shader.SetGlobalTexture(MapId, Texture2D.blackTexture);
            UnityObjects.Destroy(_texture);
            _texture = null;
            _field = null;
        }
    }
}
