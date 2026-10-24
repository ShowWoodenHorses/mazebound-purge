using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Content;
using Purge.Core.Level;
using Purge.Core.Objects;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Spatial;
using Purge.Gameplay.Waves;
using UnityEngine;

namespace Purge.Gameplay.Objects
{
    /// <summary>
    /// The safe dome at the start (stage 7в, ТЗ «Безопасная зона»), on a level with <see cref="LevelSettings.Refuge"/>:
    /// a circle of <see cref="Edge"/> = radius + ½ cells around the start cell.
    /// <list type="bullet">
    /// <item>While it stands, monsters do not step into the cells it touches (<see cref="HordeSystem.SetRefuge"/>; those
    /// inside are put out) and their plain blows, strikes and landings do not reach under it; bullets, projectiles,
    /// blasts and zones do not cross its edge either way (<see cref="IShotBarrier"/> of the spatial queries); the zones
    /// inside go out when it lights up.</item>
    /// <item>It heals the player inside by <see cref="RefugeDefinition.HealPerSecond"/> of the max HP.</item>
    /// <item>During a wave the player may stay inside <see cref="RefugeDefinition.WaveTime"/> s in all (only time inside
    /// counts); then it pushes them out (<see cref="RefugeDefinition.PushTime"/>) and goes out until the pause. Before the
    /// first wave and in the pauses it stands without a limit and the time is full again.</item>
    /// </list>
    /// </summary>
    public sealed class RefugeSystem : ILevelLoadStep, ILevelTickable, IShotBarrier, IDisposable
    {
        private const float PlayerBody = 0.35f;

        private readonly LevelData _level;
        private readonly SpatialQueryService _spatial;
        private readonly HordeSystem _horde;
        private readonly ZoneSystem _zones;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _health;
        private readonly WaveSystem _waves;
        private readonly ContentCatalog _content;
        private RefugeDefinition _definition;
        private float _healCarry;
        private float _pushLeft;

        public RefugeSystem(LevelData level, SpatialQueryService spatial, HordeSystem horde, ZoneSystem zones,
            PlayerSystem player, PlayerHealth health, WaveSystem waves = null, ContentCatalog content = null)
        {
            _level = level;
            _spatial = spatial;
            _horde = horde;
            _zones = zones;
            _player = player;
            _health = health;
            _waves = waves;
            _content = content;
        }

        // After the player is placed: the dome stands over the cell they start in.
        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>The level has the dome.</summary>
        public bool Exists { get; private set; }

        /// <summary>The dome stands now.</summary>
        public bool IsUp { get; private set; }

        public Vector2 Center { get; private set; }
        public int Floor { get; private set; }

        /// <summary>Radius of its edge, cells.</summary>
        public float Edge { get; private set; }

        /// <summary>Seconds the player may still stay inside in this wave.</summary>
        public float TimeLeft { get; private set; }

        /// <summary>The wave's limit: the time counts while the player is inside.</summary>
        public bool IsLimited => _waves != null && _waves.Phase == WavePhase.Running;

        public float WaveTime => _definition != null ? _definition.WaveTime : 10f;

        /// <summary>The player's centre is under it.</summary>
        public bool PlayerInside => IsUp && _player.IsSpawned && Contains(_player.Position, _player.Floor);

        /// <summary>Pushing the player out: the wave's time is over.</summary>
        public bool IsPushing => _pushLeft > 0f;

        /// <summary>The dome lit up / went out.</summary>
        public event Action Raised;
        public event Action Lowered;

        /// <summary>The wave's time is over: the player is pushed out.</summary>
        public event Action PushStarted;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_level.Settings.Refuge || !_player.IsSpawned) return UniTask.CompletedTask;
            var definitions = new List<RefugeDefinition>();
            _content?.GetAll(definitions);
            _definition = definitions.Count > 0 ? definitions[0] : null;
            var cell = _player.Cell;
            Center = new Vector2(cell.X, cell.Y);
            Floor = cell.Floor;
            Edge = _level.Settings.RefugeRadius + 0.5f;
            Exists = true;
            TimeLeft = WaveTime;
            _spatial.Barrier = this;
            Raise();
            GameLog.Info(LogChannel.Gameplay, $"Safe dome at {cell}, radius {_level.Settings.RefugeRadius}.");
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (ReferenceEquals(_spatial.Barrier, this)) _spatial.Barrier = null;
        }

        public void Tick(float deltaTime)
        {
            if (!Exists) return;
            if (!IsLimited)
            {
                // Before the first wave and in the pauses: no limit, the time is full again.
                _pushLeft = 0f;
                TimeLeft = WaveTime;
                if (!IsUp) Raise();
            }
            else if (IsUp && _pushLeft <= 0f && PlayerInside)
            {
                TimeLeft = Mathf.Max(0f, TimeLeft - deltaTime);
                if (TimeLeft <= 0f) StartPush();
            }

            if (_pushLeft > 0f)
            {
                _pushLeft -= deltaTime;
                if (_pushLeft <= 0f) Lower();
            }

            if (PlayerInside && !_health.IsDead) Heal(deltaTime);
        }

        public bool Contains(Vector2 point, int floor) =>
            IsUp && floor == Floor && (point - Center).sqrMagnitude < Edge * Edge;

        public bool Crosses(Vector2 from, Vector2 to, int floor, out float t)
        {
            t = 0f;
            if (!IsUp || floor != Floor) return false;
            var delta = to - from;
            var offset = from - Center;
            var a = delta.sqrMagnitude;
            if (a <= 1e-10f) return false;
            var b = Vector2.Dot(offset, delta);
            var c = offset.sqrMagnitude - Edge * Edge;
            var discriminant = b * b - a * c;
            if (discriminant <= 0f) return false;
            var root = Mathf.Sqrt(discriminant);
            // Inside: where it leaves; outside: where it enters (a shot through the dome stops at it).
            t = c < 0f ? (-b + root) / a : (-b - root) / a;
            return t >= 0f && t <= 1f;
        }

        private void Heal(float deltaTime)
        {
            var share = _definition != null ? _definition.HealPerSecond : 0.05f;
            _healCarry += _health.Max * share * deltaTime;
            var whole = Mathf.FloorToInt(_healCarry);
            if (whole <= 0) return;
            _healCarry -= whole;
            if (!_health.IsFull) _health.Heal(whole);
        }

        private void StartPush()
        {
            var push = _definition != null ? _definition.PushTime : 0.5f;
            _pushLeft = push;
            var offset = _player.Position - Center;
            var direction = offset.sqrMagnitude > 1e-4f ? offset.normalized : _player.Facing.sqrMagnitude > 0f ? -_player.Facing : Vector2.down;
            var distance = Edge - offset.magnitude + PlayerBody + 0.3f;
            _player.Knock(direction, distance);
            GameLog.Info(LogChannel.Gameplay, "Safe dome: the wave's time is over, the player is pushed out.");
            PushStarted?.Invoke();
        }

        private void Raise()
        {
            IsUp = true;
            _horde.SetRefuge(Center, Edge, Floor);
            _zones.ClearInside(this);
            Raised?.Invoke();
        }

        private void Lower()
        {
            _pushLeft = 0f;
            IsUp = false;
            _horde.ClearRefuge();
            Lowered?.Invoke();
        }
    }
}
