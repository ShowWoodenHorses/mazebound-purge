using System;
using Purge.Core.Grid;
using UnityEngine;

namespace Purge.Presentation.Map
{
    /// <summary>
    /// Draws the map (ТЗ §60) into pixels, one pixel per cell (row-major from the south-west corner, as a texture).
    /// The source of geometry is <see cref="LevelGrid"/>: walls and passages (a door cell is a passage, its icon shows
    /// the door). With an exploration (stage 7г) only explored cells are drawn, the rest is <see cref="Unknown"/>.
    /// Doors, the start, the player, monsters, pickups and objects are icons over the image (<see cref="MapPresenter"/>).
    /// </summary>
    public static class MapRenderer
    {
        public static readonly Color32 Floor = new Color32(196, 188, 168, 255);
        public static readonly Color32 Wall = new Color32(64, 66, 74, 255);

        /// <summary>Floor with an obstacle the player cannot walk through (trees, rocks).</summary>
        public static readonly Color32 Obstacle = new Color32(104, 104, 96, 255);

        /// <summary>Not explored yet: black.</summary>
        public static readonly Color32 Unknown = new Color32(8, 8, 10, 255);

        /// <summary>Void cells of upper floors: nothing there.</summary>
        public static readonly Color32 Empty = new Color32(0, 0, 0, 0);

        /// <param name="pixels">Width × Height of the grid: one floor.</param>
        /// <param name="explored">Null: the whole level is drawn (no fog).</param>
        public static void Render(LevelGrid grid, Color32[] pixels, int floor = 0, Func<GridPosition, bool> explored = null)
        {
            if (pixels == null || pixels.Length != grid.FloorSize)
                throw new ArgumentException($"Expected {grid.FloorSize} pixels.", nameof(pixels));

            for (var i = 0; i < pixels.Length; i++)
            {
                var cell = new GridPosition(i % grid.Width, i / grid.Width, floor);
                pixels[i] = explored == null || explored(cell) ? ColorOf(grid, cell) : Unknown;
            }
        }

        /// <summary>Redraws one explored cell.</summary>
        public static void RenderCell(LevelGrid grid, Color32[] pixels, GridPosition cell) =>
            pixels[cell.Y * grid.Width + cell.X] = ColorOf(grid, cell);

        public static Color32 ColorOf(LevelGrid grid, GridPosition cell)
        {
            var type = grid.GetCell(cell);
            return type == CellType.Floor && grid.Has(cell, CellFlags.BlocksMovement) ? Obstacle : ColorOf(type);
        }

        public static Color32 ColorOf(CellType type) => type == CellType.Wall ? Wall : type == CellType.Void ? Empty : Floor;
    }
}
