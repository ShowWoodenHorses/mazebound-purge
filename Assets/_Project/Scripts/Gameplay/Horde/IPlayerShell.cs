using UnityEngine;

namespace Purge.Gameplay.Horde
{
    /// <summary>
    /// A body around the player that the horde meets instead of the player's own (the vehicle they drive, stage 9b):
    /// monsters reach for it and hit it (<see cref="HordeSystem.Shell"/>), and are put out of it at the end of a tick.
    /// </summary>
    public interface IPlayerShell
    {
        /// <summary>Farthest a point of the body is from the player's position, cells (which buckets to look at).</summary>
        float Reach { get; }

        /// <summary>From <paramref name="point"/> to the body's surface, cells; 0 inside.</summary>
        float DistanceTo(Vector2 point);

        /// <summary>
        /// The step that puts a round body (<paramref name="radius"/>) at <paramref name="point"/> just out of the shell;
        /// false when they do not overlap.
        /// </summary>
        bool PushOut(Vector2 point, float radius, out Vector2 step);
    }
}
