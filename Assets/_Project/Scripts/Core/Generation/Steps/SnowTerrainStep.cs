using System;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Generation
{
    [Serializable, ModuleInfo("Snow terrain", Category = "Fill")]
    public sealed class SnowTerrainStep : GenerationStep
    {
        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            AddLake(layout, new Vector2(0.32f, 0.25f), new Vector2(0.12f, 0.11f));
            AddLake(layout, new Vector2(0.36f, 0.75f), new Vector2(0.15f, 0.13f));
        }

        private static void AddLake(LevelLayout layout, Vector2 center, Vector2 radius)
        {
            for (var i = 0; i < layout.CellCount; i++)
            {
                if (layout.IsOpen(i) || layout.IsKept(i) || layout.Cell(i) != CellType.Floor) continue;
                var cell = layout.Position(i);
                var point = new Vector2((cell.X + 0.5f) / layout.Width, (cell.Y + 0.5f) / layout.Height);
                var offset = point - center;
                var x = offset.x / radius.x;
                var y = offset.y / radius.y;
                var edge = 1f + 0.12f * Mathf.Sin(x * 7f + y * 4f);
                var distance = x * x + y * y;
                if (distance < edge)
                {
                    layout.MarkOpen(i);
                    layout.SetSurface(i, CellSurface.Ice);
                }
                else if (distance < edge * 1.6f && layout.Surface(i) == CellSurface.None)
                {
                    layout.MarkOpen(i);
                    layout.SetSurface(i, CellSurface.Snowdrift);
                }
            }
        }
    }
}
