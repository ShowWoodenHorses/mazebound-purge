using Purge.Core.Grid;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>Fits the themed stair mesh to the two-cell flight used by gameplay.</summary>
    public static class StairMeshPlacement
    {
        // Stairs_1x2 is authored along -X. Its bottom and top treads are at (5, 0) and (0, 5).
        // Rails are included in the 2.32 m measurement across the mesh.
        private const float ModelRun = 5f;
        private const float ModelRise = 5f;
        private const float ModelWidth = 2.32f;
        private const float FlightWidth = 0.92f;
        private static readonly Vector3 ModelOrigin = new Vector3(-ModelRun, 0f, 1f);

        public static Matrix4x4 Matrix(StairLink stair)
        {
            var run = stair.Top - stair.Bottom;
            run.y = 0f;
            var rotation = Quaternion.Euler(0f, run.x > 0f ? 180f : 90f, 0f);
            var scale = new Vector3(run.magnitude / ModelRun, GridPosition.FloorHeight / ModelRise, FlightWidth / ModelWidth);
            return Matrix4x4.TRS(stair.Bottom, rotation, scale) * Matrix4x4.Translate(ModelOrigin);
        }
    }
}
