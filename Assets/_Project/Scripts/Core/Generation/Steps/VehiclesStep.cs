using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Modules;
using Purge.Core.Vehicles;
using UnityEngine;

namespace Purge.Core.Generation
{
    /// <summary>
    /// Vehicles (stage 9в): one of each of <see cref="_vehicles"/> (the light and the heavy one) on a trail or in a
    /// clearing, its body on free floor, with at least <see cref="_runway"/> cells of straight free way ahead to speed up.
    /// Away from the start, the arena, spawn points, kept cells and each other; its body cuts no walkable cell off from the
    /// start. Kept vehicles of the level count (one of the same kind is not added). Goes after the objects step: barrels
    /// and chests are in the way.
    /// </summary>
    [Serializable, ModuleInfo("Vehicles", Category = "Fill")]
    public sealed class VehiclesStep : GenerationStep
    {
        [Tooltip("One of each per level (the light and the heavy one).")]
        [SerializeField] private List<VehicleDefinition> _vehicles = new List<VehicleDefinition>();

        [Tooltip("Straight free cells ahead of the body: room to speed up.")]
        [SerializeField, Min(1)] private int _runway = 8;

        [Tooltip("Least distance from the player's start, cells.")]
        [SerializeField, Min(0)] private int _startDistance = 6;

        [Tooltip("Least distance between two vehicles, cells.")]
        [SerializeField, Min(1)] private int _spacing = 12;

        private static readonly float[] Headings = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

        private readonly List<GridPosition> _cells = new List<GridPosition>();

        internal List<VehicleDefinition> MutableVehicles => _vehicles;

        public override void Apply(LevelLayout layout, DeterministicRandom random)
        {
            if (!layout.HasStart || _vehicles.Count == 0) return;
            layout.Connect();
            var taken = new HashSet<int>();
            foreach (var barrel in layout.Barrels) taken.Add(layout.Index(barrel.Cell));
            foreach (var chest in layout.Chests) taken.Add(layout.Index(chest.Cell));
            TakeKept(layout, taken);
            var baseline = ObjectsStep.CountReachable(layout, taken);
            var candidates = Candidates(layout, taken);
            ArenaStep.Shuffle(candidates, random);

            foreach (var definition in _vehicles)
            {
                if (definition == null || HasKept(layout, definition)) continue;
                if (!TryPlace(layout, random, definition, candidates, taken, ref baseline))
                    layout.Problems.Add($"Vehicles: no place for '{definition.name}'.");
            }
        }

        /// <summary>Kept vehicles' blocked cells go to <paramref name="taken"/> (other steps keep off them).</summary>
        internal static void TakeKept(LevelLayout layout, HashSet<int> taken)
        {
            var cells = new List<GridPosition>();
            for (var i = 0; i < layout.KeptVehicleCount; i++)
            {
                Footprint(layout.Vehicles[i], VehicleFootprint.BlockMargin, cells);
                foreach (var cell in cells)
                    if (layout.InBounds(cell.X, cell.Y))
                        taken.Add(layout.Index(cell));
            }
        }

        private static bool HasKept(LevelLayout layout, VehicleDefinition definition)
        {
            for (var i = 0; i < layout.KeptVehicleCount; i++)
                if (layout.Vehicles[i].Definition == definition)
                    return true;
            return false;
        }

        private bool TryPlace(LevelLayout layout, DeterministicRandom random, VehicleDefinition definition,
            List<GridPosition> candidates, HashSet<int> taken, ref int baseline)
        {
            var first = random.NextInt(Headings.Length);
            foreach (var cell in candidates)
            {
                if (!FarFromVehicles(layout, cell)) continue;
                for (var h = 0; h < Headings.Length; h++)
                {
                    var vehicle = new LayoutVehicle(cell, definition, Headings[(first + h) % Headings.Length]);
                    // The whole body on free floor (no tree through it), the way ahead free of what blocks it.
                    Footprint(vehicle, 0f, _cells);
                    if (!AllFree(layout, _cells, taken)) continue;
                    if (!RunwayFree(layout, vehicle, taken)) continue;
                    Footprint(vehicle, VehicleFootprint.BlockMargin, _cells);
                    if (!TryTake(layout, _cells, taken, ref baseline)) continue;
                    layout.Vehicles.Add(vehicle);
                    return true;
                }
            }

            return false;
        }

        private bool FarFromVehicles(LevelLayout layout, GridPosition cell)
        {
            foreach (var other in layout.Vehicles)
                if (Distance(other.Cell, cell) < _spacing)
                    return false;
            return true;
        }

        /// <summary>The body slid straight ahead by up to <see cref="_runway"/> cells stays on free cells.</summary>
        private bool RunwayFree(LevelLayout layout, LayoutVehicle vehicle, HashSet<int> taken)
        {
            var hull = vehicle.Definition.Hull;
            var heading = vehicle.Heading * Mathf.Deg2Rad;
            var forward = VehicleFootprint.Forward(heading);
            var center = new Vector2(vehicle.Cell.X, vehicle.Cell.Y);
            for (var along = 0.5f; along <= _runway; along += 0.5f)
            {
                VehicleFootprint.Cells(center + forward * along, heading, hull.Length * 0.5f, hull.Width * 0.5f, 0,
                    VehicleFootprint.BlockMargin, _cells);
                if (!AllFree(layout, _cells, taken)) return false;
            }

            return true;
        }

        private static bool AllFree(LevelLayout layout, List<GridPosition> cells, HashSet<int> taken)
        {
            foreach (var cell in cells)
            {
                if (!layout.InBounds(cell.X, cell.Y)) return false;
                var index = layout.Index(cell);
                if (!layout.IsWalkable(index) || layout.IsKept(index) || taken.Contains(index) ||
                    layout.Cell(index) != CellType.Floor) return false;
            }

            return true;
        }

        /// <summary>Takes the cells when no walkable cell is cut off from the start by them.</summary>
        private static bool TryTake(LevelLayout layout, List<GridPosition> cells, HashSet<int> taken, ref int baseline)
        {
            var added = new List<int>();
            foreach (var cell in cells)
            {
                var index = layout.Index(cell);
                if (taken.Add(index)) added.Add(index);
            }

            var reached = ObjectsStep.CountReachable(layout, taken);
            if (reached == baseline - added.Count)
            {
                baseline = reached;
                return true;
            }

            foreach (var index in added) taken.Remove(index);
            return false;
        }

        /// <summary>Centres on trails or in clearings, reachable, away from the start, the arena and spawn points.</summary>
        private List<GridPosition> Candidates(LevelLayout layout, HashSet<int> taken)
        {
            var reached = layout.Reachable();
            var result = new List<GridPosition>();
            var arena = layout.Arena;
            for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var index = layout.Index(x, y);
                if (!reached[index] || taken.Contains(index) || layout.IsKept(index)) continue;
                var cell = new GridPosition(x, y);
                if (!layout.IsTrail(index) && !InClearing(layout, cell)) continue;
                if (Distance(cell, layout.Start) < _startDistance) continue;
                if (arena != null && !arena.IsEmpty && Expand(arena.Outer, 3).Contains(cell)) continue;
                var nearSpawn = false;
                foreach (var spawn in layout.Spawns)
                    if (Math.Max(Math.Abs(x - spawn.X), Math.Abs(y - spawn.Y)) <= 3)
                        nearSpawn = true;
                if (!nearSpawn) result.Add(cell);
            }

            return result;
        }

        private static bool InClearing(LevelLayout layout, GridPosition cell)
        {
            foreach (var clearing in layout.Clearings)
                if (Distance(cell, clearing.Center) <= clearing.Radius - 1f)
                    return true;
            return false;
        }

        private static void Footprint(LayoutVehicle vehicle, float margin, List<GridPosition> result)
        {
            result.Clear();
            if (vehicle.Definition == null) return;
            var hull = vehicle.Definition.Hull;
            VehicleFootprint.Cells(new Vector2(vehicle.Cell.X, vehicle.Cell.Y), vehicle.Heading * Mathf.Deg2Rad,
                hull.Length * 0.5f, hull.Width * 0.5f, 0, margin, result);
        }

        private static GridRect Expand(GridRect rect, int by) =>
            new GridRect(rect.X - by, rect.Y - by, rect.Width + 2 * by, rect.Height + 2 * by);

        private static float Distance(GridPosition a, GridPosition b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public override void Validate(List<string> problems)
        {
            if (_vehicles.Count == 0) problems.Add("Vehicles: no vehicles.");
            if (_vehicles.Contains(null)) problems.Add("Vehicles: an empty vehicle slot.");
        }
    }
}
