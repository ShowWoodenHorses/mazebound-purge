using System;

namespace Purge.Gameplay.Horde
{
    /// <summary>
    /// Burning cells (stage 2 stub of cell zones): seconds of fire left per cell plus a compact list of the burning
    /// ones. Damages the horde monsters standing in them every <see cref="DamageInterval"/>. Views draw it as a mask
    /// texture (one quad, not an object per cell). Allocation-free.
    /// </summary>
    public sealed class FireField
    {
        public const float DamageInterval = 0.25f;

        private readonly float[] _left;
        private readonly float[] _lifetime;
        private readonly int[] _cells;
        private float _damageTimer;

        public FireField(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), $"Invalid grid size {width}x{height}.");
            Width = width;
            Height = height;
            _left = new float[width * height];
            _lifetime = new float[width * height];
            _cells = new int[width * height];
        }

        public int Width { get; }
        public int Height { get; }
        public int Count { get; private set; }

        /// <summary>Burning cell indices (row-major), first <see cref="Count"/> entries.</summary>
        public ReadOnlySpan<int> Cells => new ReadOnlySpan<int>(_cells, 0, Count);

        /// <summary>Share of the fire left in the cell, 0..1 (0 = not burning).</summary>
        public float Strength(int index) => _lifetime[index] > 0f ? _left[index] / _lifetime[index] : 0f;

        public bool IsBurning(int index) => _left[index] > 0f;

        public void Ignite(int x, int y, float lifetime)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height || lifetime <= 0f)
                return;
            var index = y * Width + x;
            if (_left[index] <= 0f)
                _cells[Count++] = index;
            if (_left[index] < lifetime)
            {
                _left[index] = lifetime;
                _lifetime[index] = lifetime;
            }
        }

        public void Clear()
        {
            for (var i = 0; i < Count; i++)
                _left[_cells[i]] = 0f;
            Count = 0;
        }

        public void Tick(float deltaTime, HordeSystem horde, float damagePerSecond)
        {
            if (Count == 0)
                return;

            _damageTimer -= deltaTime;
            var damage = _damageTimer <= 0f && damagePerSecond > 0f;
            if (damage)
                _damageTimer += DamageInterval;

            for (var i = Count - 1; i >= 0; i--)
            {
                var index = _cells[i];
                if (damage)
                {
                    var x = index % Width;
                    var y = index / Width;
                    for (var agent = horde.FirstInCell(x, y); agent >= 0; agent = horde.NextInCell(agent))
                        horde.Damage(agent, damagePerSecond * DamageInterval);
                }

                _left[index] -= deltaTime;
                if (_left[index] > 0f)
                    continue;

                _left[index] = 0f;
                _cells[i] = _cells[--Count];
            }
        }
    }
}
