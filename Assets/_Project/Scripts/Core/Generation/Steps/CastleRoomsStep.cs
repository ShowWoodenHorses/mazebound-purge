using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Generation.Steps
{
    [Serializable, ModuleInfo("Castle Rooms", Category = "Layout")]
    public sealed class CastleRoomsStep : GenerationStep
    {
        private const int CourtyardSize = 16;
        private const int Ring = 1;

        [SerializeField, Range(2, 4)] private int _corridorWidth = 3;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            var border = Math.Max(1, layout.Perimeter);
            var width = Math.Clamp(_corridorWidth, 2, 4);
            var courtyard = new GridRect((layout.Width - CourtyardSize) / 2,
                (layout.Height - CourtyardSize) / 2, CourtyardSize, CourtyardSize);
            var arena = layout.Arena ?? new ArenaData(courtyard, Ring);
            var outer = arena.Outer;
            var west = outer.X - width - 1;
            var east = outer.XMax + width;
            if (west - border < 2 || layout.Width - border - east - 1 < 2 ||
                outer.Y - border < 3 || layout.Height - border - outer.YMax < 3)
            {
                layout.Problems.Add("Castle Rooms: the courtyard, corridors and rooms do not fit.");
                return;
            }

            if (layout.Arena == null)
            {
                var x = courtyard.X + courtyard.Width / 2 - 1;
                var y = courtyard.Y + courtyard.Height / 2 - 1;
                arena.MutableEntries.Add(new ArenaEntry(new GridRect(x, outer.Y, 3, Ring), Direction.South));
                arena.MutableEntries.Add(new ArenaEntry(new GridRect(x, courtyard.YMax, 3, Ring), Direction.North));
                arena.MutableEntries.Add(new ArenaEntry(new GridRect(outer.X, y, Ring, 3), Direction.West));
                arena.MutableEntries.Add(new ArenaEntry(new GridRect(courtyard.XMax, y, Ring, 3), Direction.East));
                layout.Arena = arena;
            }
            ArenaStep.Build(layout, arena);
            layout.SpawnArea = new GridRect(west + 1, border, east - west - 1, layout.Height - border * 2);

            var doors = new List<GridPosition>();
            BuildWing(west, border, west, doors);
            BuildWing(east, east + 1, layout.Width - border, doors);
            ArenaStep.Shuffle(doors, random);

            var keys = new List<GridPosition>();
            var reached = layout.Reachable();
            for (var y = border + 1; y < layout.Height - border - 1; y++)
            {
                var cell = new GridPosition(west + 1, y);
                var index = layout.Index(cell);
                if (layout.Cell(index) == CellType.Floor && !layout.IsKept(index) &&
                    !layout.IsOpen(index) && reached[index])
                    keys.Add(cell);
            }
            ArenaStep.Shuffle(keys, random);
            var wanted = random.NextInt(1, 3);
            var count = Math.Min(wanted, Math.Min(doors.Count, keys.Count));
            for (var i = 0; i < count; i++)
            {
                layout.Doors[doors[i]] = keys[i];
                layout.MarkTrail(layout.Index(keys[i]));
            }
            if (count < wanted)
                layout.Problems.Add($"Castle Rooms: placed {count} of {wanted} locked doors.");

            void BuildWing(int wallX, int firstRoomX, int endRoomX, List<GridPosition> generatedDoors)
            {
                var roomStart = border;
                var end = layout.Height - border;
                while (roomStart < end)
                {
                    var next = roomStart + random.NextInt(7, 11);
                    var roomEnd = end - next < 6 ? end : next;
                    var doorY = roomStart + (roomEnd - roomStart) / 2;
                    for (var y = roomStart; y < roomEnd; y++)
                    {
                        var cell = new GridPosition(wallX, y);
                        var index = layout.Index(cell);
                        if (layout.IsOpen(index) || layout.IsKept(index)) continue;
                        if (y == doorY)
                        {
                            layout.SetDoor(cell);
                            generatedDoors.Add(cell);
                        }
                        else
                            layout.SetWall(index);
                    }
                    if (roomEnd < end)
                        for (var x = firstRoomX; x < endRoomX; x++)
                        {
                            var index = layout.Index(x, roomEnd);
                            if (!layout.IsOpen(index)) layout.SetWall(index);
                        }
                    roomStart = roomEnd + 1;
                    if (roomStart < end) layout.SetWall(layout.Index(wallX, roomEnd));
                }
            }
        }
    }
}
