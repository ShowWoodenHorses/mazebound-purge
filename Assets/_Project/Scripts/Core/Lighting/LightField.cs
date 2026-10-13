using System;
using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visibility;
using UnityEngine;

namespace Maze.Core.Lighting
{
    /// <summary>
    /// Light of the level's sources over the grid, <see cref="TexelsPerCell"/>² texels per cell: computed once at
    /// load instead of Unity lights (each of which would redraw the geometry in Built-in RP). A texel gets the light
    /// of every source whose straight line to it is not blocked by an opaque cell (wall, closed door); the texel's
    /// own cell may be opaque, so walls are lit where the light reaches them. Falloff: (1 − d / radius)².
    /// When a door opens or closes only the lights reaching it are recomputed (<see cref="RebuildAround{T}"/>).
    /// Output for an RGBA32 texture: rgb = light / <see cref="MaxLight"/> (linear), a = share of flickering light.
    /// Texture texel (tx, ty) covers grid x from −0.5 + tx / TexelsPerCell (cell (x, y) spans x − 0.5..x + 0.5).
    /// </summary>
    public sealed class LightField
    {
        public const int TexelsPerCell = 4;

        /// <summary>Brightest light the texture stores.</summary>
        public const float MaxLight = 2f;

        private readonly IReadOnlyList<LightSourceData> _lights;
        private readonly float[] _light;   // rgb per texel
        private readonly float[] _flicker; // flickering part (luminance) per texel
        private readonly float[] _total;   // all light (luminance) per texel

        public LightField(int width, int height, IReadOnlyList<LightSourceData> lights)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            TextureWidth = width * TexelsPerCell;
            TextureHeight = height * TexelsPerCell;
            _lights = lights ?? Array.Empty<LightSourceData>();
            var count = TextureWidth * TextureHeight;
            _light = new float[count * 3];
            _flicker = new float[count];
            _total = new float[count];
            Pixels = new Color32[count];
        }

        public int Width { get; }
        public int Height { get; }
        public int TextureWidth { get; }
        public int TextureHeight { get; }

        /// <summary>Texture data, row by row from the south-west texel.</summary>
        public Color32[] Pixels { get; }

        public bool HasLights => _lights.Count > 0;

        public void Rebuild<T>(in T opacity) where T : IGridOpacity
        {
            Fill(new RectInt(0, 0, TextureWidth, TextureHeight), opacity);
        }

        /// <summary>
        /// Recomputes texels of every light reaching <paramref name="cell"/> (a door that opened or closed).
        /// Returns the changed texel rectangle (empty when no light reaches it).
        /// </summary>
        public RectInt RebuildAround<T>(GridPosition cell, in T opacity) where T : IGridOpacity
        {
            var region = new RectInt();
            var any = false;
            foreach (var light in _lights)
            {
                var point = light.Point;
                var reach = light.Radius + 0.75f; // the cell's corner
                if (Mathf.Abs(cell.X - point.x) > reach || Mathf.Abs(cell.Y - point.y) > reach)
                    continue;

                var box = TexelBox(light);
                region = any ? Union(region, box) : box;
                any = true;
            }

            if (any) Fill(region, opacity);
            return any ? region : new RectInt();
        }

        /// <summary>Light at a texel (linear, before encoding): tests and diagnostics.</summary>
        public Color GetTexel(int tx, int ty)
        {
            var i = (ty * TextureWidth + tx) * 3;
            return new Color(_light[i], _light[i + 1], _light[i + 2]);
        }

        /// <summary>Light at a point in grid units (nearest texel).</summary>
        public Color GetLight(Vector2 point)
        {
            var tx = Mathf.Clamp(Mathf.FloorToInt((point.x + 0.5f) * TexelsPerCell), 0, TextureWidth - 1);
            var ty = Mathf.Clamp(Mathf.FloorToInt((point.y + 0.5f) * TexelsPerCell), 0, TextureHeight - 1);
            return GetTexel(tx, ty);
        }

        private void Fill<T>(RectInt region, in T opacity) where T : IGridOpacity
        {
            for (var ty = region.yMin; ty < region.yMax; ty++)
            for (var tx = region.xMin; tx < region.xMax; tx++)
            {
                var i = ty * TextureWidth + tx;
                _light[i * 3] = _light[i * 3 + 1] = _light[i * 3 + 2] = 0f;
                _flicker[i] = _total[i] = 0f;
            }

            foreach (var light in _lights)
            {
                var box = TexelBox(light);
                var clipped = new RectInt();
                if (!Intersect(box, region, ref clipped)) continue;
                AddLight(light, clipped, opacity);
            }

            for (var ty = region.yMin; ty < region.yMax; ty++)
            for (var tx = region.xMin; tx < region.xMax; tx++)
                Encode(ty * TextureWidth + tx);
        }

        private void AddLight<T>(LightSourceData light, RectInt texels, in T opacity) where T : IGridOpacity
        {
            var point = light.Point;
            var radius = Mathf.Max(light.Radius, 0.01f);
            // Colors are authored in sRGB; the texture is read as linear.
            var color = light.Color.linear * Mathf.Max(light.Intensity, 0f);
            var luminance = color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
            var flicker = Mathf.Clamp01(light.Flicker);
            const float step = 1f / TexelsPerCell;

            for (var ty = texels.yMin; ty < texels.yMax; ty++)
            {
                var y = -0.5f + (ty + 0.5f) * step;
                for (var tx = texels.xMin; tx < texels.xMax; tx++)
                {
                    var x = -0.5f + (tx + 0.5f) * step;
                    var dx = x - point.x;
                    var dy = y - point.y;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distance >= radius) continue;
                    if (!GridLineOfSight.IsClear(point.x, point.y, x, y, opacity)) continue;

                    var falloff = 1f - distance / radius;
                    falloff *= falloff;
                    var i = ty * TextureWidth + tx;
                    _light[i * 3] += color.r * falloff;
                    _light[i * 3 + 1] += color.g * falloff;
                    _light[i * 3 + 2] += color.b * falloff;
                    _total[i] += luminance * falloff;
                    _flicker[i] += luminance * falloff * flicker;
                }
            }
        }

        private void Encode(int i)
        {
            Pixels[i] = new Color32(
                ToByte(_light[i * 3] / MaxLight),
                ToByte(_light[i * 3 + 1] / MaxLight),
                ToByte(_light[i * 3 + 2] / MaxLight),
                ToByte(_total[i] > 1e-5f ? _flicker[i] / _total[i] : 0f));
        }

        private static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        private RectInt TexelBox(LightSourceData light)
        {
            var point = light.Point;
            var radius = Mathf.Max(light.Radius, 0f);
            var xMin = Mathf.Clamp(Mathf.FloorToInt((point.x - radius + 0.5f) * TexelsPerCell), 0, TextureWidth);
            var yMin = Mathf.Clamp(Mathf.FloorToInt((point.y - radius + 0.5f) * TexelsPerCell), 0, TextureHeight);
            var xMax = Mathf.Clamp(Mathf.CeilToInt((point.x + radius + 0.5f) * TexelsPerCell), 0, TextureWidth);
            var yMax = Mathf.Clamp(Mathf.CeilToInt((point.y + radius + 0.5f) * TexelsPerCell), 0, TextureHeight);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static RectInt Union(RectInt a, RectInt b)
        {
            var xMin = Math.Min(a.xMin, b.xMin);
            var yMin = Math.Min(a.yMin, b.yMin);
            return new RectInt(xMin, yMin, Math.Max(a.xMax, b.xMax) - xMin, Math.Max(a.yMax, b.yMax) - yMin);
        }

        private static bool Intersect(RectInt a, RectInt b, ref RectInt result)
        {
            var xMin = Math.Max(a.xMin, b.xMin);
            var yMin = Math.Max(a.yMin, b.yMin);
            var xMax = Math.Min(a.xMax, b.xMax);
            var yMax = Math.Min(a.yMax, b.yMax);
            if (xMax <= xMin || yMax <= yMin) return false;
            result = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
            return true;
        }
    }
}
