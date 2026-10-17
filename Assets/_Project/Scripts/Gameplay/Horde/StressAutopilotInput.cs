using Purge.Core.Level;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Horde
{
    /// <summary>
    /// Player input of the stress test levels: the real input, but while <see cref="Enabled"/> and the player does
    /// not move by themselves, the player runs a circle around the level centre (the benchmark repeats the same
    /// route: monsters keep coming, the flow field is rebuilt as in play). Registered instead of the project's
    /// <see cref="IPlayerInput"/> only in a stress test level scope.
    /// </summary>
    public sealed class StressAutopilotInput : IPlayerInput
    {
        private const float UserDeadZone = 0.1f;
        private const float StuckCheck = 0.3f;
        private const float StuckDistance = 0.15f;
        private const float SidestepTime = 0.5f;

        private readonly IPlayerInput _inner;
        private readonly Vector2 _center;
        private readonly float _radius;
        private PlayerSystem _player;
        private Vector2 _checkPosition;
        private float _checkTime;
        private float _sidestepUntil;
        private float _sidestepSign = 1f;

        public StressAutopilotInput(IPlayerInput inner, LevelData level)
        {
            _inner = inner;
            _center = new Vector2((level.Geometry.Width - 1) * 0.5f, (level.Geometry.Height - 1) * 0.5f);
            _radius = level.StressTest != null ? level.StressTest.AutopilotRadius : 8f;
        }

        public bool Enabled { get; set; } = true;

        /// <summary>The player whose position the route follows (set after the scope is built: no cycle).</summary>
        public void Attach(PlayerSystem player) => _player = player;

        public Vector2 Move
        {
            get
            {
                var user = _inner.Move;
                if (!Enabled || _player == null || !_player.IsSpawned || user.sqrMagnitude > UserDeadZone * UserDeadZone)
                    return user;

                var radial = _player.Position - _center;
                var distance = radial.magnitude;
                if (distance < 0.01f)
                    return Vector2.right;
                radial /= distance;
                var tangent = new Vector2(-radial.y, radial.x);
                // Back towards the circle: inward when outside, outward when inside.
                var correction = radial * Mathf.Clamp((_radius - distance) / _radius * 2f, -1f, 1f);
                var move = (tangent + correction).normalized;

                // Stuck on a pillar: step sideways (across the circle) for a moment, alternating sides.
                var now = Time.unscaledTime;
                if (now - _checkTime >= StuckCheck)
                {
                    if ((_player.Position - _checkPosition).sqrMagnitude < StuckDistance * StuckDistance && now >= _sidestepUntil)
                    {
                        _sidestepUntil = now + SidestepTime;
                        _sidestepSign = -_sidestepSign;
                    }

                    _checkPosition = _player.Position;
                    _checkTime = now;
                }

                return now < _sidestepUntil ? (radial * _sidestepSign + tangent * 0.3f).normalized : move;
            }
        }

        public Vector2 Look => _inner.Look;
        public bool LookIsPointer => _inner.LookIsPointer;
        public bool AttackHeld => _inner.AttackHeld;
        public bool WasPressed(PlayerAction action) => _inner.WasPressed(action);
        public bool IsHeld(PlayerAction action) => _inner.IsHeld(action);
    }
}
