using Purge.Core.Grid;
using UnityEngine;

namespace Purge.Gameplay.Grid
{
    /// <summary>The same foot path for the player and the horde, from landing through the flight to the other landing.</summary>
    internal struct StairTraversal
    {
        private const float FlightSpeed = 2.8f;
        private Vector3 _origin;
        private Vector3 _entry;
        private Vector3 _exit;
        private float _approachLength;
        private float _flightLength;
        private float _totalLength;
        private float _distance;

        public bool Active => _totalLength > 0f;
        public GridPosition Destination { get; private set; }
        public Vector2 Direction { get; private set; }
        public Vector3 Position { get; private set; }

        public void Begin(StairLink stair, GridPosition landing, Vector2 position)
        {
            Destination = stair.ExitFrom(landing);
            _origin = new Vector3(position.x, landing.ToWorld().y, position.y);
            var ascending = landing == stair.LowerLanding;
            _entry = ascending ? stair.Bottom : stair.Top;
            _exit = ascending ? stair.Top : stair.Bottom;
            var horizontal = new Vector2(_exit.x - _entry.x, _exit.z - _entry.z);
            _approachLength = Vector3.Distance(_origin, _entry);
            _flightLength = horizontal.magnitude;
            _totalLength = _approachLength + _flightLength + Vector3.Distance(_exit, Destination.ToWorld());
            Direction = horizontal.normalized;
            Position = _origin;
            _distance = 0f;
        }

        public bool Advance(float deltaTime)
        {
            _distance = Mathf.Min(_totalLength, _distance + Mathf.Max(0f, deltaTime) * FlightSpeed);
            var beyondApproach = _distance - _approachLength;
            if (beyondApproach < 0f)
                Position = Vector3.Lerp(_origin, _entry, _distance / _approachLength);
            else if (beyondApproach < _flightLength)
                Position = Vector3.Lerp(_entry, _exit, beyondApproach / _flightLength);
            else
                Position = Vector3.MoveTowards(_exit, Destination.ToWorld(), beyondApproach - _flightLength);
            return _distance >= _totalLength;
        }
    }
}
