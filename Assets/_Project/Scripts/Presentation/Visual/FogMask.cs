using System;
using System.Collections.Generic;
using Maze.Core.Grid;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Per-cell fog values (0 = hidden, 255 = visible) with a one-cell hidden border around the grid, so the fog
    /// texture is (Width + 2) × (Height + 2). Values move toward their target over <c>fadeSeconds</c>: only cells
    /// that are still fading are stepped, so the cost is zero while nothing changes.
    /// </summary>
    public sealed class FogMask
    {
        private const byte Visible = 255;
        private const byte Hidden = 0;

        private readonly byte[] _target;
        private readonly float[] _current;
        private readonly byte[] _pixels;
        private readonly List<int> _fading = new List<int>();
        private readonly bool[] _isFading;
        private readonly List<GridPosition> _covered = new List<GridPosition>();
        private List<int> _revealed = new List<int>();
        private List<int> _previous = new List<int>();

        public FogMask(int width, int height)
        {
            Width = width;
            Height = height;
            TextureWidth = width + 2;
            TextureHeight = height + 2;
            var size = TextureWidth * TextureHeight;
            _target = new byte[size];
            _current = new float[size];
            _pixels = new byte[size];
            _isFading = new bool[size];
        }

        public int Width { get; }
        public int Height { get; }
        public int TextureWidth { get; }
        public int TextureHeight { get; }

        /// <summary>Texture data, row-major from the south-west corner of the border.</summary>
        public byte[] Pixels => _pixels;

        public bool IsFading => _fading.Count > 0;

        /// <summary>Cells that became fully covered by fog in the last <see cref="Step"/> (their geometry can go).</summary>
        public IReadOnlyList<GridPosition> JustCovered => _covered;

        /// <summary>Current value of a grid cell, 0..1 (tests and diagnostics).</summary>
        public float ValueOf(GridPosition cell) => _current[IndexOf(cell)];

        /// <summary>
        /// Sets the revealed cells (everything else becomes hidden). <paramref name="instant"/> skips the fade
        /// (first reveal after loading). Returns true when the pixels changed right away.
        /// </summary>
        public bool SetRevealed(IReadOnlyList<GridPosition> revealed, bool instant)
        {
            (_previous, _revealed) = (_revealed, _previous);
            foreach (var index in _previous)
                _target[index] = Hidden;
            _revealed.Clear();

            for (var i = 0; i < revealed.Count; i++)
            {
                var cell = revealed[i];
                if (cell.X < 0 || cell.Y < 0 || cell.X >= Width || cell.Y >= Height) continue;
                var index = IndexOf(cell);
                _target[index] = Visible;
                _revealed.Add(index);
            }

            if (instant)
            {
                _fading.Clear();
                Array.Clear(_isFading, 0, _isFading.Length);
                for (var i = 0; i < _target.Length; i++)
                {
                    _current[i] = _target[i] / 255f;
                    _pixels[i] = _target[i];
                }

                return true;
            }

            // Only previously and newly revealed cells can change: both lists are the size of the view window.
            StartFading(_previous);
            StartFading(_revealed);
            return false;
        }

        /// <summary>Moves fading cells toward their targets. Returns true when the pixels changed.</summary>
        public bool Step(float deltaTime, float fadeSeconds)
        {
            _covered.Clear();
            if (_fading.Count == 0) return false;

            var step = fadeSeconds <= 0f ? 1f : deltaTime / fadeSeconds;
            for (var i = _fading.Count - 1; i >= 0; i--)
            {
                var index = _fading[i];
                var target = _target[index] / 255f;
                var value = _current[index];
                value = value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);
                _current[index] = value;
                _pixels[index] = (byte)(value * 255f + 0.5f);

                if (value == target)
                {
                    _isFading[index] = false;
                    _fading.RemoveAt(i);
                    if (target == 0f)
                        _covered.Add(new GridPosition(index % TextureWidth - 1, index / TextureWidth - 1));
                }
            }

            return true;
        }

        private void StartFading(List<int> indices)
        {
            foreach (var index in indices)
                if (_pixels[index] != _target[index] && !_isFading[index])
                {
                    _isFading[index] = true;
                    _fading.Add(index);
                }
        }

        private int IndexOf(GridPosition cell) => (cell.Y + 1) * TextureWidth + cell.X + 1;
    }
}
