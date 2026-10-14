using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Visibility;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using UnityEngine;

namespace Purge.Gameplay.Horde
{
    public enum StressWeapon
    {
        None = 0,
        MachineGun = 1,
        Shotgun = 2,
        Flamethrower = 3,
    }

    /// <summary>
    /// Weapons of the stress test (stub: stage 4 rebuilds weapons from modules) against the horde: a stream of
    /// bullets in flat arrays (walls by <see cref="GridRaycast"/>, monsters by <see cref="HordeSystem.QuerySegment"/>),
    /// a shotgun, a flamethrower (cone damage, sets cells on fire) and the burning cells (<see cref="FireField"/>).
    /// Fires at the nearest monster by itself (<see cref="AutoFire"/>) or while attack is held, along the facing.
    /// Allocation-free per tick.
    /// </summary>
    public sealed class StressWeapons : ILevelLoadStep, ILevelTickable
    {
        public const int MaxBullets = 1024;
        public const int HitHistory = 256;
        private const float FlameInterval = 0.1f;
        private const float AimRange = 12f;
        private const float FireRefill = 0.5f;

        private readonly LevelData _level;
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;
        private readonly HordeSystem _horde;
        private readonly PlayerSystem _player;
        private readonly IPlayerInput _input;
        private readonly Vector2[] _bulletPositions = new Vector2[MaxBullets];
        private readonly Vector2[] _bulletDirections = new Vector2[MaxBullets];
        private readonly float[] _bulletLeft = new float[MaxBullets];
        private readonly Vector2[] _hits = new Vector2[HitHistory];
        private readonly bool[] _hitMonster = new bool[HitHistory];
        private readonly int[] _coneResults = new int[512];
        private StressWeaponSettings _settings;
        private DeterministicRandom _random;
        private float _cooldown;
        private float _flameTimer;
        private float _refillTimer;

        public StressWeapons(LevelData level, LevelGrid grid, DoorSystem doors, HordeSystem horde, PlayerSystem player,
            IPlayerInput input)
        {
            _level = level;
            _grid = grid;
            _doors = doors;
            _horde = horde;
            _player = player;
            _input = input;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool IsActive => _settings != null;
        public StressWeaponSettings Settings => _settings;
        public FireField Fire { get; private set; }

        public StressWeapon Weapon { get; set; } = StressWeapon.MachineGun;

        /// <summary>Shoot at the nearest monster without input (autopilot, benchmark).</summary>
        public bool AutoFire { get; set; } = true;

        /// <summary>Fire rate multiplier: the bullet storm of the benchmark.</summary>
        public float RateMultiplier { get; set; } = 1f;

        /// <summary>Keep at least this many burning cells around the player (0 = only what the flamethrower lights).</summary>
        public int KeepFireCells { get; set; }

        public int BulletCount { get; private set; }
        public ReadOnlySpan<Vector2> BulletPositions => new ReadOnlySpan<Vector2>(_bulletPositions, 0, BulletCount);
        public ReadOnlySpan<Vector2> BulletDirections => new ReadOnlySpan<Vector2>(_bulletDirections, 0, BulletCount);

        /// <summary>Total hits so far; the last <see cref="HitHistory"/> are in <see cref="HitAt"/> (ring buffer).</summary>
        public int HitCount { get; private set; }

        public int ShotsFired { get; private set; }

        /// <summary>True during ticks the flamethrower burns; the view emits flame along <see cref="AimDirection"/>.</summary>
        public bool Flaming { get; private set; }

        public Vector2 AimDirection { get; private set; } = Vector2.up;
        public float TickMs { get; private set; }

        public Vector2 HitAt(int hit, out bool monster)
        {
            var index = hit % HitHistory;
            monster = _hitMonster[index];
            return _hits[index];
        }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_level.StressTest == null || !_horde.IsActive)
                return UniTask.CompletedTask;
            _settings = _level.StressTest.Weapons;
            Fire = new FireField(_grid.Width, _grid.Height);
            _random = new DeterministicRandom((int)StableHash.Of(_level.name + "/weapons"));
            KeepFireCells = 0;
            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive || deltaTime <= 0f)
                return;

            var started = Stopwatch.GetTimestamp();
            var origin = _player.Position;
            var target = AutoFire ? _horde.FindNearest(origin, AimRange) : -1;
            if (target >= 0)
            {
                var offset = _horde.Positions[target] - origin;
                if (offset.sqrMagnitude > 1e-6f) AimDirection = offset.normalized;
            }
            else
            {
                AimDirection = _player.Facing;
            }

            var firing = (AutoFire && target >= 0) || _input.AttackHeld;
            Flaming = false;
            _cooldown = Mathf.Max(_cooldown - deltaTime, -1f);
            var rate = Mathf.Max(0.01f, RateMultiplier);
            switch (Weapon)
            {
                case StressWeapon.MachineGun:
                    while (firing && _cooldown <= 0f)
                    {
                        SpawnBullet(origin, Spread(AimDirection, _settings.GunSpread));
                        _cooldown += 1f / (_settings.GunRate * rate);
                    }
                    break;
                case StressWeapon.Shotgun:
                    while (firing && _cooldown <= 0f)
                    {
                        for (var p = 0; p < _settings.Pellets; p++)
                            SpawnBullet(origin, Spread(AimDirection, _settings.ShotgunSpread * 0.5f));
                        _cooldown += 1f / (_settings.ShotgunRate * rate);
                    }
                    break;
                case StressWeapon.Flamethrower:
                    if (firing)
                    {
                        Flaming = true;
                        _flameTimer -= deltaTime;
                        if (_flameTimer <= 0f)
                        {
                            _flameTimer += FlameInterval;
                            Flame(origin, AimDirection);
                        }
                    }
                    break;
            }

            TickBullets(deltaTime);
            RefillFire(deltaTime, origin);
            Fire.Tick(deltaTime, _horde, _settings.FireDps);
            TickMs = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        }

        public void ClearAll()
        {
            BulletCount = 0;
            Fire?.Clear();
        }

        private void SpawnBullet(Vector2 origin, Vector2 direction)
        {
            ShotsFired++;
            if (BulletCount >= MaxBullets)
                return;
            _bulletPositions[BulletCount] = origin;
            _bulletDirections[BulletCount] = direction;
            _bulletLeft[BulletCount] = _settings.BulletRange;
            BulletCount++;
        }

        private Vector2 Spread(Vector2 direction, float degrees)
        {
            if (degrees <= 0f) return direction;
            var angle = ((float)_random.NextDouble() * 2f - 1f) * degrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(angle);
            var sin = Mathf.Sin(angle);
            return new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);
        }

        private void TickBullets(float deltaTime)
        {
            var opacity = LevelOpacity.ForBullets(_grid, _doors);
            var step = _settings.BulletSpeed * deltaTime;
            for (var i = BulletCount - 1; i >= 0; i--)
            {
                var from = _bulletPositions[i];
                var distance = Mathf.Min(step, _bulletLeft[i]);
                var to = from + _bulletDirections[i] * distance;
                var blocked = GridRaycast.Cast(from, to, opacity, out var wallT, out _, _player.Floor);
                var agent = _horde.QuerySegment(from, to, out var agentT);
                if (agent >= 0 && (!blocked || agentT <= wallT))
                {
                    _horde.Damage(agent, Weapon == StressWeapon.Shotgun ? _settings.PelletDamage : _settings.BulletDamage);
                    RecordHit(from + (to - from) * agentT, true);
                    RemoveBullet(i);
                    continue;
                }

                if (blocked)
                {
                    RecordHit(from + (to - from) * wallT, false);
                    RemoveBullet(i);
                    continue;
                }

                _bulletPositions[i] = to;
                _bulletLeft[i] -= distance;
                if (_bulletLeft[i] <= 0f)
                    RemoveBullet(i);
            }
        }

        private void RemoveBullet(int index)
        {
            BulletCount--;
            _bulletPositions[index] = _bulletPositions[BulletCount];
            _bulletDirections[index] = _bulletDirections[BulletCount];
            _bulletLeft[index] = _bulletLeft[BulletCount];
        }

        private void RecordHit(Vector2 point, bool monster)
        {
            var index = HitCount % HitHistory;
            _hits[index] = point;
            _hitMonster[index] = monster;
            HitCount++;
        }

        private void Flame(Vector2 origin, Vector2 direction)
        {
            var range = _settings.FlameRange;
            var halfAngle = _settings.FlameAngle * 0.5f;
            var count = _horde.QueryCone(origin, direction, range, Mathf.Cos(halfAngle * Mathf.Deg2Rad), _coneResults);
            for (var i = 0; i < count; i++)
                _horde.Damage(_coneResults[i], _settings.FlameDps * FlameInterval);

            // Cells along five rays of the cone catch fire up to the first wall.
            for (var r = -2; r <= 2; r++)
            {
                var ray = Rotate(direction, halfAngle * r / 2f * Mathf.Deg2Rad);
                for (var d = 1f; d <= range; d += 0.7f)
                {
                    var point = origin + ray * d;
                    var x = Mathf.FloorToInt(point.x + 0.5f);
                    var y = Mathf.FloorToInt(point.y + 0.5f);
                    if (!_horde.IsWalkable(x, y)) break;
                    Fire.Ignite(x, y, _settings.FireLifetime);
                }
            }
        }

        private void RefillFire(float deltaTime, Vector2 origin)
        {
            if (KeepFireCells <= 0) return;
            _refillTimer -= deltaTime;
            if (_refillTimer > 0f) return;
            _refillTimer = FireRefill;
            for (var attempt = 0; attempt < 64 && Fire.Count < KeepFireCells; attempt++)
            {
                var angle = (float)_random.NextDouble() * Mathf.PI * 2f;
                var distance = 2f + (float)_random.NextDouble() * 5f;
                var x = Mathf.FloorToInt(origin.x + Mathf.Cos(angle) * distance + 0.5f);
                var y = Mathf.FloorToInt(origin.y + Mathf.Sin(angle) * distance + 0.5f);
                if (_horde.IsWalkable(x, y))
                    Fire.Ignite(x, y, _settings.FireLifetime);
            }
        }

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }
}
