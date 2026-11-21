using System;
using System.Collections.Generic;
using Purge.Gameplay.Level;
using UnityEngine;

namespace Purge.Gameplay.Visibility
{
    [Serializable]
    public struct SmokeCloud
    {
        public Vector2 Position;
        public int Floor;
        public float Radius;
        public float Seconds;
    }

    /// <summary>Active clouds that block monster sight and are kept in run checkpoints.</summary>
    public sealed class SmokeScreen : ILevelTickable
    {
        private readonly List<SmokeCloud> _clouds = new List<SmokeCloud>(16);

        public IReadOnlyList<SmokeCloud> Clouds => _clouds;

        public void Place(Vector2 at, int floor, float radius, float duration)
        {
            if (!(duration > 0f) || float.IsInfinity(duration)) return;
            _clouds.Add(new SmokeCloud { Position = at, Floor = floor, Radius = radius, Seconds = duration });
        }

        public bool Obscures(Vector2 from, Vector2 to, int floor)
        {
            var segment = to - from;
            var lengthSquared = segment.sqrMagnitude;
            foreach (var cloud in _clouds)
            {
                if (cloud.Floor != floor) continue;
                var closest = lengthSquared > 0f
                    ? from + segment * Mathf.Clamp01(Vector2.Dot(cloud.Position - from, segment) / lengthSquared)
                    : from;
                if ((closest - cloud.Position).sqrMagnitude <= cloud.Radius * cloud.Radius) return true;
            }
            return false;
        }

        public void Tick(float deltaTime)
        {
            for (var i = _clouds.Count - 1; i >= 0; i--)
            {
                var cloud = _clouds[i];
                cloud.Seconds -= deltaTime;
                if (cloud.Seconds <= 0f) _clouds.RemoveAt(i);
                else _clouds[i] = cloud;
            }
        }
    }
}
