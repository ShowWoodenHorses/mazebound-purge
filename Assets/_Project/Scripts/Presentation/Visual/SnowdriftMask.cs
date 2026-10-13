using System;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Soft snowdrifts for the "Maze/Geometry" shader (display only): a Width x Height R8 texture, a texel per cell
    /// (255 = the cell shows a drift floor of the theme), sampled bilinearly in world space, so neighbouring drift
    /// cells merge without seams and the edge is a soft, noisy line between cells. Walls and doors next to a drift get
    /// a partial value: the drift reaches the wall base without leaking along it. Built once at load, never per frame;
    /// gameplay (speed, routes) stays per cell. A level without drift floors gets no mask (no keyword, no cost).
    /// </summary>
    public sealed class SnowdriftMask : IDisposable
    {
        private const byte Drift = 255;
        private const byte NextToDrift = 153; // 0.6: above the edge on the drift side, below it on the clear side

        private Texture2D _texture;
        private readonly Vector4 _color;
        private readonly Vector4 _parameters;

        private SnowdriftMask(Texture2D texture, Color color, Vector4 parameters)
        {
            _texture = texture;
            _color = color.linear;
            _parameters = parameters;
        }

        public Texture2D Texture => _texture;

        /// <summary>Mask of the level, or null when no cell shows a drift floor (no snowdrifts or the theme has none).</summary>
        public static SnowdriftMask Create(LevelData level)
        {
            var geometry = level.Geometry;
            var theme = level.VisualTheme;
            var floors = theme != null ? theme.GetSet(VisualKind.Floor) : null;
            if (floors == null)
                return null;

            var width = geometry.Width;
            var height = geometry.Height;
            var cells = new byte[width * height];
            var any = false;
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = geometry.ToPosition(i);
                if (geometry.GetSurface(cell) != CellSurface.Snowdrift)
                    continue;

                var choice = VisualResolver.ResolveCell(level, cell, CellLayer.Floor);
                var variant = choice.IsEmpty ? null : floors.FindVariant(choice.VariantId);
                if (variant == null || variant.Category != VisualCategory.Snowdrift)
                    continue;

                cells[i] = Drift;
                any = true;
            }

            if (!any)
                return null;

            for (var i = 0; i < cells.Length; i++)
            {
                var cell = geometry.ToPosition(i);
                if (geometry.GetCell(cell) != CellType.Floor && NextTo(cells, width, height, cell))
                    cells[i] = NextToDrift;
            }

            var texture = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = "Maze Snowdrifts",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixelData(cells, 0);
            texture.Apply(false, true);

            var weather = theme.Weather ?? new ThemeWeather();
            return new SnowdriftMask(texture, weather.DriftColor, new Vector4(
                weather.DriftEdgeSoftness, weather.DriftEdgeNoise, weather.DriftNoiseScale, weather.DriftShadow));
        }

        private static bool NextTo(byte[] cells, int width, int height, GridPosition cell)
        {
            return IsDrift(cells, width, height, cell.X - 1, cell.Y) || IsDrift(cells, width, height, cell.X + 1, cell.Y) ||
                   IsDrift(cells, width, height, cell.X, cell.Y - 1) || IsDrift(cells, width, height, cell.X, cell.Y + 1);
        }

        private static bool IsDrift(byte[] cells, int width, int height, int x, int y) =>
            x >= 0 && y >= 0 && x < width && y < height && cells[y * width + x] == Drift;

        /// <summary>Makes the shader use this mask. Only one level (one mask) is active at a time.</summary>
        public void Bind()
        {
            if (_texture == null) return;
            Shader.SetGlobalTexture(GeometryShader.SnowdriftTextureId, _texture);
            Shader.SetGlobalVector(GeometryShader.SnowdriftSizeId, new Vector4(1f / _texture.width, 1f / _texture.height, 0f, 0f));
            Shader.SetGlobalVector(GeometryShader.SnowdriftColorId, _color);
            Shader.SetGlobalVector(GeometryShader.SnowdriftParamsId, _parameters);
            Shader.EnableKeyword(GeometryShader.SnowdriftKeyword);
        }

        public void Dispose()
        {
            if (_texture == null) return;
            Shader.DisableKeyword(GeometryShader.SnowdriftKeyword);
            UnityObjects.Destroy(_texture);
            _texture = null;
        }
    }
}
