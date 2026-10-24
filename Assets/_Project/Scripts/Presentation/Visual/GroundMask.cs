using System;
using Purge.Core.Grid;
using Purge.Core.Level;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Soft bare ground for the "Purge/Geometry" shader (display only, stage 7г): a Width x Height R8 texture, a texel
    /// per cell (255 = <see cref="CellSurface.Ground"/>), sampled bilinearly in world space, so trails merge into the
    /// grass with a soft, noisy edge instead of square cells; the ground itself is the theme's texture
    /// (<see cref="Core.Visual.ThemeGround"/>) in world space. Built once at load, never per frame. A level without
    /// ground cells, or a theme without a ground texture, gets no mask (no keyword, no cost).
    /// </summary>
    public sealed class GroundMask : IDisposable
    {
        private const byte Ground = 255;

        private Texture2D _texture;
        private readonly Texture2D _ground;
        private readonly Vector4 _size;
        private readonly Vector4 _parameters;

        private GroundMask(Texture2D texture, Texture2D ground, Vector4 size, Vector4 parameters)
        {
            _texture = texture;
            _ground = ground;
            _size = size;
            _parameters = parameters;
        }

        public Texture2D Texture => _texture;

        /// <summary>Mask of the level, or null when it has no ground cells or the theme no ground texture.</summary>
        public static GroundMask Create(LevelData level, int floor = 0)
        {
            var theme = level.VisualTheme;
            var settings = theme != null ? theme.Ground : null;
            if (settings == null || settings.Albedo == null)
                return null;

            var geometry = level.Geometry;
            var width = geometry.Width;
            var height = geometry.Height;
            var cells = new byte[width * height];
            var any = false;
            for (var i = 0; i < cells.Length; i++)
            {
                if (geometry.GetSurface(new GridPosition(i % width, i / width, floor)) != CellSurface.Ground)
                    continue;
                cells[i] = Ground;
                any = true;
            }

            if (!any)
                return null;

            var texture = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = "Purge Ground",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixelData(cells, 0);
            texture.Apply(false, true);

            var size = new Vector4(1f / width, 1f / height, 1f / Mathf.Max(0.25f, settings.TileCells), 0f);
            return new GroundMask(texture, settings.Albedo, size,
                new Vector4(settings.EdgeSoftness, settings.EdgeNoise, settings.NoiseScale, 0f));
        }

        /// <summary>Makes the shader use this mask. Only one level (one mask) is active at a time.</summary>
        public void Bind()
        {
            if (_texture == null) return;
            Shader.SetGlobalTexture(GeometryShader.GroundMaskId, _texture);
            Shader.SetGlobalTexture(GeometryShader.GroundTextureId, _ground);
            Shader.SetGlobalVector(GeometryShader.GroundSizeId, _size);
            Shader.SetGlobalVector(GeometryShader.GroundParamsId, _parameters);
            Shader.DisableKeyword(GeometryShader.SnowdriftKeyword);
            Shader.EnableKeyword(GeometryShader.GroundKeyword);
        }

        public void Dispose()
        {
            if (_texture == null) return;
            Shader.DisableKeyword(GeometryShader.GroundKeyword);
            UnityObjects.Destroy(_texture);
            _texture = null;
        }
    }
}
