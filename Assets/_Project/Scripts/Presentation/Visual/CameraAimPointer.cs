using Purge.Gameplay.Combat;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>The mouse on the floor of the level (y = 0) through the level camera: the player shoots there.</summary>
    public sealed class CameraAimPointer : IAimPointer
    {
        private static readonly Plane Floor = new Plane(Vector3.up, Vector3.zero);
        private readonly TopDownCamera _camera;

        public CameraAimPointer(TopDownCamera camera)
        {
            _camera = camera;
        }

        public bool TryGetGroundPoint(Vector2 screenPosition, out Vector2 gridPoint)
        {
            gridPoint = default;
            var camera = _camera != null ? _camera.Camera : null;
            if (camera == null) return false;
            var ray = camera.ScreenPointToRay(screenPosition);
            if (!Floor.Raycast(ray, out var distance)) return false;
            var point = ray.GetPoint(distance);
            gridPoint = new Vector2(point.x, point.z);
            return true;
        }
    }
}
