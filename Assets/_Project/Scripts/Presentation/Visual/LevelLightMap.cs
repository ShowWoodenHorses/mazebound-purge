using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Lighting;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Level;
using Purge.Gameplay.Visibility;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Light of the level's sources (<see cref="LevelData.Lights"/>) as a texture read by the shaders
    /// (PurgeLighting.cginc): computed at load by <see cref="LightField"/>; when a door opens or closes only the
    /// lights reaching it are recomputed and the texture is uploaded again (rare event). No lights = no texture.
    /// </summary>
    public sealed class LevelLightMap : ILevelLoadStep, IDisposable
    {
        private static readonly int MapId = Shader.PropertyToID("_PurgeLightMap");
        private static readonly int MapSizeId = Shader.PropertyToID("_PurgeLightMapSize");

        private readonly LevelData _level;
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;
        private readonly VisibilitySystem _visibility;

        private LightField[] _fields;
        private Texture2D[] _textures;
        private int _activeFloor = -1;
        private bool _subscribed;

        public LevelLightMap(LevelData level, LevelGrid grid, DoorSystem doors, VisibilitySystem visibility = null)
        {
            _level = level;
            _grid = grid;
            _doors = doors;
            _visibility = visibility;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>Null when the level has no lights.</summary>
        public LightField Field => _activeFloor >= 0 ? _fields?[_activeFloor] : null;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            Clear();
            if (_level.Lights.Count == 0)
                return UniTask.CompletedTask;

            _fields = new LightField[_grid.FloorCount];
            _textures = new Texture2D[_grid.FloorCount];
            foreach (var light in _level.Lights)
            {
                var floor = light.Cell.Floor;
                if ((uint)floor >= (uint)_fields.Length || _fields[floor] != null) continue;
                var field = new LightField(_grid.Width, _grid.Height, _level.Lights, floor);
                field.Rebuild(new LevelOpacity(_grid, _doors));
                _fields[floor] = field;
                _textures[floor] = new Texture2D(field.TextureWidth, field.TextureHeight, TextureFormat.RGBA32, false, true)
                {
                    name = $"Purge Light Map {floor}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                Upload(floor);
            }

            ShowFloor();

            if (!_subscribed)
            {
                _doors.DoorChanged += OnDoorChanged;
                if (_visibility != null) _visibility.Changed += ShowFloor;
                _subscribed = true;
            }

            GameLog.Info(LogChannel.Visual, $"Prepared light maps for {_level.Lights.Count} light(s).");
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _doors.DoorChanged -= OnDoorChanged;
                if (_visibility != null) _visibility.Changed -= ShowFloor;
                _subscribed = false;
            }

            Clear();
        }

        private void OnDoorChanged(DoorData door, bool open)
        {
            var floor = door.Position.Floor;
            if (_fields == null || (uint)floor >= (uint)_fields.Length) return;
            var field = _fields[floor];
            if (field == null) return;
            var region = field.RebuildAround(door.Position, new LevelOpacity(_grid, _doors));
            if (region.width > 0) Upload(floor);
        }

        private void ShowFloor()
        {
            var floor = _visibility?.Floor ?? 0;
            if (floor == _activeFloor) return;
            _activeFloor = floor;
            var texture = _textures?[floor];
            Shader.SetGlobalTexture(MapId, texture != null ? texture : Texture2D.blackTexture);
            Shader.SetGlobalVector(MapSizeId, texture != null
                ? new Vector4(1f / _grid.Width, 1f / _grid.Height, LightField.MaxLight, 0f)
                : Vector4.zero);
        }

        private void Upload(int floor)
        {
            _textures[floor].SetPixels32(_fields[floor].Pixels);
            _textures[floor].Apply(false);
        }

        private void Clear()
        {
            Shader.SetGlobalVector(MapSizeId, Vector4.zero);
            Shader.SetGlobalTexture(MapId, Texture2D.blackTexture);
            if (_textures != null)
                foreach (var texture in _textures) UnityObjects.Destroy(texture);
            _textures = null;
            _fields = null;
            _activeFloor = -1;
        }
    }
}
