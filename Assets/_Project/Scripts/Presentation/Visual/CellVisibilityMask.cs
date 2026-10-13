using System;
using Maze.Core.Grid;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Per-cell visibility for the "Maze/Geometry" shader: a Width x Height R8 texture (255 = visible), bound
    /// globally while the level is active. Changes are written to a CPU buffer and uploaded by <see cref="Apply"/>,
    /// once per visibility update, never per frame.
    /// </summary>
    public sealed class CellVisibilityMask : IDisposable
    {
        private const byte Visible = 255;
        private const byte Hidden = 0;

        private readonly byte[] _cells;
        private Texture2D _texture;
        private bool _dirty;

        public CellVisibilityMask(int width, int height)
        {
            Width = width;
            Height = height;
            _cells = new byte[width * height];
            _texture = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = "Maze Cell Visibility",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            SetAll(true);
            Apply();
        }

        public int Width { get; }
        public int Height { get; }
        public Texture2D Texture => _texture;

        public bool IsVisible(GridPosition cell) => _cells[cell.Y * Width + cell.X] == Visible;

        public void SetVisible(GridPosition cell, bool visible)
        {
            var value = visible ? Visible : Hidden;
            var index = cell.Y * Width + cell.X;
            if (_cells[index] == value) return;
            _cells[index] = value;
            _dirty = true;
        }

        public void SetAll(bool visible)
        {
            var value = visible ? Visible : Hidden;
            for (var i = 0; i < _cells.Length; i++)
                _cells[i] = value;
            _dirty = true;
        }

        /// <summary>True when at least one cell of the rectangle is visible.</summary>
        public bool AnyVisible(GridRect rect)
        {
            for (var y = rect.Y; y < rect.YMax; y++)
            for (var x = rect.X; x < rect.XMax; x++)
                if (_cells[y * Width + x] == Visible)
                    return true;

            return false;
        }

        public void Apply()
        {
            if (!_dirty || _texture == null) return;
            _texture.SetPixelData(_cells, 0);
            _texture.Apply(false, false);
            _dirty = false;
        }

        /// <summary>Makes the shader use this mask. Only one level (one mask) is active at a time.</summary>
        public void Bind()
        {
            Shader.SetGlobalTexture(GeometryShader.VisibilityTextureId, _texture);
            Shader.SetGlobalVector(GeometryShader.VisibilitySizeId, new Vector4(1f / Width, 1f / Height, Width, Height));
            Shader.EnableKeyword(GeometryShader.VisibilityKeyword);
        }

        public void Dispose()
        {
            if (_texture == null) return;
            Shader.DisableKeyword(GeometryShader.VisibilityKeyword);
            UnityObjects.Destroy(_texture);
            _texture = null;
        }
    }
}
