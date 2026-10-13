using Maze.Core.Grid;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// A rectangle of cells whose static geometry (floor, walls) is one combined mesh (ТЗ §105).
    /// Single cells are hidden by the shader mask; a chunk without visible cells is switched off entirely.
    /// </summary>
    public sealed class VisibilityChunk
    {
        public VisibilityChunk(GridRect cells, GameObject gameObject, Mesh mesh)
        {
            Cells = cells;
            GameObject = gameObject;
            Mesh = mesh;
        }

        public GridRect Cells { get; }
        public GameObject GameObject { get; }

        /// <summary>The chunk's own mesh; owned by the chunk and destroyed with the level.</summary>
        public Mesh Mesh { get; }

        public bool IsVisible => GameObject != null && GameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            if (GameObject != null && GameObject.activeSelf != visible)
                GameObject.SetActive(visible);
        }
    }
}
