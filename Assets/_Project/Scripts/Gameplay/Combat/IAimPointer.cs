using UnityEngine;

namespace Purge.Gameplay.Combat
{
    /// <summary>
    /// Where a screen point (the mouse) lies on the level floor, in grid units. Implemented by the presentation (it owns
    /// the camera); the player shoots at the mouse on a computer.
    /// </summary>
    public interface IAimPointer
    {
        bool TryGetGroundPoint(Vector2 screenPosition, out Vector2 gridPoint);
    }
}
