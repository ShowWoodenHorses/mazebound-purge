using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Generation.Steps
{
    [Serializable, ModuleInfo("Building", Category = "Layout")]
    public sealed class BuildingStep : GenerationStep
    {
        [SerializeField, Range(1, 2)] private int _aboveGroundFloors = 2;
        [SerializeField] private bool _basement;
        [SerializeField, Min(0)] private int _insetPerFloor = 2;
        [SerializeField, Range(2, 3)] private int _stairsPerFloor = 2;

        public bool Basement { get => _basement; internal set => _basement = value; }
        internal int InsetPerFloor { set => _insetPerFloor = value; }

        internal int GroundFloor => _basement ? 1 : 0;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            layout.Building = this;
            layout.BuildingRandom = random;
        }

        internal void Build(LevelGeometry geometry, LevelLayout layout)
        {
            var ground = geometry.GroundFloor;
            var floorCount = ground + Math.Clamp(_aboveGroundFloors, 1, 2);
            var inset = Math.Max(0, _insetPerFloor);
            for (var floor = 0; floor < floorCount; floor++)
            {
                if (floor == ground) continue;
                if (floor >= geometry.FloorCount) geometry.AddFloor();
                var border = Math.Max(1, layout.Perimeter) + Math.Abs(floor - ground) * inset;
                if (geometry.Width - border * 2 < 6 || geometry.Height - border * 2 < 6)
                {
                    layout.Problems.Add("Building: the inset leaves fewer than six cells for a floor.");
                    continue;
                }

                for (var y = border; y < geometry.Height - border; y++)
                for (var x = border; x < geometry.Width - border; x++)
                {
                    var p = new GridPosition(x, y, floor);
                    var edge = x == border || y == border || x == geometry.Width - border - 1 ||
                               y == geometry.Height - border - 1;
                    geometry.SetCell(p, edge ? CellType.Wall : CellType.Floor);
                }
            }

            var occupied = new HashSet<GridPosition>();
            foreach (var spawn in layout.Spawns) occupied.Add(spawn.OnFloor(ground));
            foreach (var barrel in layout.Barrels) occupied.Add(barrel.Cell.OnFloor(ground));
            foreach (var door in layout.Doors)
                if (door.Value.HasValue) occupied.Add(door.Value.Value.OnFloor(ground));
            for (var floor = 0; floor < floorCount - 1; floor++)
            {
                var candidates = new List<StairLink>();
                for (var y = 1; y < geometry.Height - 1; y++)
                for (var x = 1; x < geometry.Width - 1; x++)
                {
                    var position = new GridPosition(x, y, floor);
                    AddCandidate(new StairLink(position, false));
                    AddCandidate(new StairLink(position, true));
                }

                void AddCandidate(StairLink candidate)
                {
                    if (candidate.Fits(geometry) && !Conflicts(candidate))
                        candidates.Add(candidate);
                }

                var wanted = Math.Clamp(_stairsPerFloor, 2, 3);
                var placed = 0;
                while (placed < wanted && candidates.Count > 0)
                {
                    var index = layout.BuildingRandom.NextInt(candidates.Count);
                    var candidate = candidates[index];
                    candidates[index] = candidates[candidates.Count - 1];
                    candidates.RemoveAt(candidates.Count - 1);
                    if (Conflicts(candidate)) continue;
                    geometry.AddStair(candidate);
                    occupied.Add(candidate.First);
                    occupied.Add(candidate.Second);
                    occupied.Add(candidate.First.OnFloor(candidate.UpperFloor));
                    occupied.Add(candidate.Second.OnFloor(candidate.UpperFloor));
                    occupied.Add(candidate.LowerLanding);
                    occupied.Add(candidate.UpperLanding);
                    placed++;
                }

                if (placed < wanted)
                    layout.Problems.Add($"Building: placed {placed} of {wanted} stairs between floors {floor} and {floor + 1}.");
            }

            bool Conflicts(StairLink candidate)
            {
                var first = candidate.First;
                var second = candidate.Second;
                if (occupied.Contains(first) || occupied.Contains(second) ||
                    occupied.Contains(first.OnFloor(candidate.UpperFloor)) ||
                    occupied.Contains(second.OnFloor(candidate.UpperFloor)) ||
                    occupied.Contains(candidate.LowerLanding) || occupied.Contains(candidate.UpperLanding)) return true;
                if (first.Floor != ground && candidate.UpperFloor != ground) return false;
                if (layout.HasStart && candidate.Uses(layout.Start.OnFloor(ground))) return true;
                var landing = first.Floor == ground ? candidate.LowerLanding : candidate.UpperLanding;
                if (layout.SpawnArea.HasValue && layout.Arena != null &&
                    (layout.Arena.Outer.Contains(first) || layout.Arena.Outer.Contains(second) ||
                     layout.Arena.Outer.Contains(landing))) return true;
                return layout.IsKept(layout.Index(first)) || layout.IsKept(layout.Index(second)) ||
                       layout.IsKept(layout.Index(landing));
            }
        }
    }
}
