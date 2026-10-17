using System;

namespace Purge.Core.Navigation
{
    /// <summary>How a cell lets sound through: 0 — blocks (a wall), 1 — free, between — muffles (× the loudness left).</summary>
    public interface IGridAcoustics
    {
        float Transmission(int x, int y);
    }

    /// <summary>
    /// How far one sound carries along the passages (ТЗ: стены гасят звук — громкость по пути через проходы, в пределах
    /// радиуса). The loudness left in a cell is the radius minus the path walked to it (orthogonal step 1, diagonal √2,
    /// no corner cutting); a muffling cell (a closed door) multiplies what is left by its transmission. A cell hears the
    /// sound while some loudness is left. Every reached cell also remembers its neighbour towards the source — walking
    /// those steps leads to it. Only a window of ±radius around the source is covered (sounds are local), so a build
    /// costs the radius², not the level. Allocation-free after construction.
    /// </summary>
    public sealed class NoiseField
    {
        private const float Diagonal = 1.41421356f;
        private const float Epsilon = 1e-4f;

        private static readonly int[] OffsetX = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] OffsetY = { 1, 0, -1, 0, 1, -1, -1, 1 };

        private readonly float[] _loudness;
        private readonly float[] _transmission;
        private readonly int[] _toward;
        private readonly int[] _heapCell;
        private readonly float[] _heapKey;
        private int _heapCount;

        public NoiseField(int maxRadius)
        {
            if (maxRadius < 1) throw new ArgumentOutOfRangeException(nameof(maxRadius));
            MaxRadius = maxRadius;
            var side = maxRadius * 2 + 1;
            var count = side * side;
            _loudness = new float[count];
            _transmission = new float[count];
            _toward = new int[count];
            // A cell is queued again only when reached louder — at most once per incoming direction.
            _heapCell = new int[count * 8 + 1];
            _heapKey = new float[count * 8 + 1];
        }

        /// <summary>Largest radius covered (louder sounds are cut to it).</summary>
        public int MaxRadius { get; }

        public int SourceX { get; private set; }
        public int SourceY { get; private set; }
        public int Floor { get; private set; }
        public float Radius { get; private set; }
        public bool HasResult { get; private set; }

        /// <summary>Incremented by every <see cref="Build{TAcoustics}"/>.</summary>
        public int Version { get; private set; }

        private int OriginX { get; set; }
        private int OriginY { get; set; }
        private int Side { get; set; }

        /// <summary>
        /// Spreads a sound of <paramref name="radius"/> cells from (<paramref name="sourceX"/>, <paramref name="sourceY"/>)
        /// over a grid of <paramref name="width"/>×<paramref name="height"/> (outside it — walls).
        /// </summary>
        public void Build<TAcoustics>(int width, int height, int sourceX, int sourceY, int floor, float radius,
            in TAcoustics acoustics) where TAcoustics : struct, IGridAcoustics
        {
            Version++;
            SourceX = sourceX;
            SourceY = sourceY;
            Floor = floor;
            Radius = Math.Min(radius, MaxRadius);
            var reach = Math.Max(1, Math.Min(MaxRadius, (int)Math.Ceiling(Radius)));
            Side = reach * 2 + 1;
            OriginX = sourceX - reach;
            OriginY = sourceY - reach;
            HasResult = true;

            var count = Side * Side;
            for (var i = 0; i < count; i++)
            {
                _loudness[i] = 0f;
                _toward[i] = -1;
                var x = OriginX + i % Side;
                var y = OriginY + i / Side;
                _transmission[i] = x >= 0 && x < width && y >= 0 && y < height
                    ? Math.Max(0f, Math.Min(1f, acoustics.Transmission(x, y)))
                    : 0f;
            }

            if (Radius <= 0f) return;
            var start = reach * Side + reach;
            _loudness[start] = Radius;
            _heapCount = 0;
            Push(start, Radius);

            while (_heapCount > 0)
            {
                var key = _heapKey[0];
                var cell = Pop();
                if (key < _loudness[cell] - Epsilon)
                    continue; // stale: reached louder later

                var cx = cell % Side;
                var cy = cell / Side;
                for (var d = 0; d < 8; d++)
                {
                    var nx = cx + OffsetX[d];
                    var ny = cy + OffsetY[d];
                    if (nx < 0 || nx >= Side || ny < 0 || ny >= Side) continue;
                    var neighbour = ny * Side + nx;
                    var transmission = _transmission[neighbour];
                    if (transmission <= 0f) continue;

                    var diagonal = d >= 4;
                    if (diagonal && (!Passes(cx + OffsetX[d], cy) || !Passes(cx, cy + OffsetY[d])))
                        continue;

                    var left = (key - (diagonal ? Diagonal : 1f)) * transmission;
                    if (left <= 0f || left <= _loudness[neighbour] + Epsilon) continue;
                    _loudness[neighbour] = left;
                    _toward[neighbour] = cell;
                    Push(neighbour, left);
                }
            }
        }

        /// <summary>Loudness left in the cell, cells of radius (0 = not heard; the source has the whole radius).</summary>
        public float LoudnessAt(int x, int y)
        {
            var index = WindowIndex(x, y);
            return index >= 0 ? _loudness[index] : 0f;
        }

        public bool IsHeardAt(int x, int y) => LoudnessAt(x, y) > 0f;

        /// <summary>The neighbour to step to from a cell that hears the sound, towards its source; false at the source or out of reach.</summary>
        public bool TryStepToward(int x, int y, out int nextX, out int nextY)
        {
            var index = WindowIndex(x, y);
            var next = index >= 0 ? _toward[index] : -1;
            if (next < 0)
            {
                nextX = x;
                nextY = y;
                return false;
            }

            nextX = OriginX + next % Side;
            nextY = OriginY + next / Side;
            return true;
        }

        private int WindowIndex(int x, int y)
        {
            if (!HasResult) return -1;
            var wx = x - OriginX;
            var wy = y - OriginY;
            return wx >= 0 && wx < Side && wy >= 0 && wy < Side ? wy * Side + wx : -1;
        }

        private bool Passes(int wx, int wy) => wx >= 0 && wx < Side && wy >= 0 && wy < Side && _transmission[wy * Side + wx] > 0f;

        // Max-heap on the loudness left.
        private void Push(int cell, float key)
        {
            var i = _heapCount++;
            while (i > 0)
            {
                var parent = (i - 1) >> 1;
                if (_heapKey[parent] >= key) break;
                _heapKey[i] = _heapKey[parent];
                _heapCell[i] = _heapCell[parent];
                i = parent;
            }

            _heapKey[i] = key;
            _heapCell[i] = cell;
        }

        private int Pop()
        {
            var top = _heapCell[0];
            var last = --_heapCount;
            if (last == 0) return top;

            var key = _heapKey[last];
            var cell = _heapCell[last];
            var i = 0;
            while (true)
            {
                var child = i * 2 + 1;
                if (child >= last) break;
                if (child + 1 < last && _heapKey[child + 1] > _heapKey[child]) child++;
                if (_heapKey[child] <= key) break;
                _heapKey[i] = _heapKey[child];
                _heapCell[i] = _heapCell[child];
                i = child;
            }

            _heapKey[i] = key;
            _heapCell[i] = cell;
            return top;
        }
    }
}
