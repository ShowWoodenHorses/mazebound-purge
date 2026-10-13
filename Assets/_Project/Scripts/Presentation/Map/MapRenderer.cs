using System;
using Purge.Core.Grid;
using UnityEngine;

namespace Purge.Presentation.Map
{
    /// <summary>
    /// Draws the map (ТЗ §60) into pixels, one pixel per cell (row-major from the south-west corner, as a texture).
    /// The source of geometry is <see cref="LevelGrid"/>; the whole level is drawn: walls and passages (a door cell is
    /// a passage, its icon shows the door). Doors, the start, the player and pickups are icons over the image
    /// (<see cref="MapPresenter"/>). Independent of current visibility (explored cells come with the fog stage).
    /// </summary>
    public static class MapRenderer
    {
        public static readonly Color32 Floor = new Color32(196, 188, 168, 255);
        public static readonly Color32 Wall = new Color32(64, 66, 74, 255);

        /// <param name="pixels">Width × Height of the grid.</param>
        public static void Render(LevelGrid grid, Color32[] pixels)
        {
            if (pixels == null || pixels.Length != grid.CellCount)
                throw new ArgumentException($"Expected {grid.CellCount} pixels.", nameof(pixels));

            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = ColorOf(grid.GetCell(grid.ToPosition(i)));
        }

        public static Color32 ColorOf(CellType type) => type == CellType.Wall ? Wall : Floor;
    }
}
