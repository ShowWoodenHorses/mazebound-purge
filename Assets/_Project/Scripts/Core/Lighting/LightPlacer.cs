using System.Collections.Generic;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Core.Level;
using UnityEngine;

namespace Maze.Core.Lighting
{
    /// <summary>
    /// Auto placement of light sources (editor, level creation): like torches on walls — a floor cell next to a wall,
    /// the light shifted towards it. Deterministic by VisualSeed: candidates (cell, wall side) are visited in hash
    /// order and taken while no other light is closer than the spacing given by
    /// <see cref="LevelGenerationSettings.LightDensity"/>; the kind of light is a weighted pick from the theme's
    /// <see cref="Visual.ThemeLighting.LightPresets"/>. Hand-placed lights stay (and keep the others away from them).
    /// Rules: no auto light in an exit cell; every key with a coloured visual first gets <see cref="KeyLightCount"/>
    /// lights of its colour within <see cref="KeyLightSteps"/> steps along the floor (a hint for the player; placed
    /// regardless of density, the other lights keep their spacing from them).
    /// </summary>
    internal static class LightPlacer
    {
        /// <summary>Shift from the cell centre towards the wall.</summary>
        public const float WallOffset = 0.42f;

        /// <summary>Distance between auto placed lights at density → 0 and at density 1, cells.</summary>
        public const float SparseSpacing = 9f;
        public const float DenseSpacing = 3f;

        /// <summary>Lights in a key's colour around each key and how far (steps along the floor) they may be.</summary>
        public const int KeyLightCount = 2;
        public const int KeyLightSteps = 3;

        /// <summary>Preferred distance between the lights of one key (closer only when nothing else fits).</summary>
        private const float KeyLightGap = 2f;

        private static readonly ulong Salt = StableHash.Of("light");

        public static void PlaceAll(LevelData level, bool keepManual)
        {
            var lights = level.MutableLights;
            lights.RemoveAll(light => light.IsGenerated || !keepManual);

            var presets = level.VisualTheme != null ? level.VisualTheme.Lighting.LightPresets : null;
            if (presets == null || TotalWeight(presets) <= 0f)
            {
                Visual.VisualAssigner.AssignLights(level);
                return;
            }

            var seed = StableHash.Combine(Salt, unchecked((ulong)level.Generation.VisualSeed));
            var exits = new HashSet<GridPosition>();
            foreach (var exit in level.Exits)
                exits.Add(exit.Position);

            var taken = new List<Vector2>();
            var usedCells = new HashSet<GridPosition>();
            foreach (var light in lights)
            {
                taken.Add(light.Point);
                usedCells.Add(light.Cell);
            }

            foreach (var key in level.Keys)
                PlaceKeyLights(level, key, presets, seed, exits, taken, usedCells);

            var density = level.Generation.LightDensity;
            if (density > 0f)
            {
                var spacing = Mathf.Lerp(SparseSpacing, DenseSpacing, Mathf.Clamp01(density));
                var candidates = Candidates(level.Geometry, seed, null);
                candidates.Sort((a, b) => a.Key.CompareTo(b.Key));

                foreach (var candidate in candidates)
                {
                    if (usedCells.Contains(candidate.Cell) || exits.Contains(candidate.Cell))
                        continue;

                    var point = candidate.Point;
                    if (IsNearAny(point, taken, spacing))
                        continue;

                    var preset = Pick(presets, StableHash.Combine(candidate.Key, 1UL));
                    Add(level, candidate, preset.Color, preset);
                    taken.Add(point);
                    usedCells.Add(candidate.Cell);
                }
            }

            Visual.VisualAssigner.AssignLights(level);
        }

        /// <summary>
        /// Lights of the key's colour on walls next to floor cells within <see cref="KeyLightSteps"/> of the key
        /// (walls and doors bound the search, so they light the key's own corridor). A key without colour gets none.
        /// </summary>
        private static void PlaceKeyLights(LevelData level, KeyData key, List<LightPreset> presets, ulong seed,
            HashSet<GridPosition> exits, List<Vector2> taken, HashSet<GridPosition> usedCells)
        {
            if (!TryGetKeyColor(level, key, out var color))
                return;

            var near = Reachable(level.Geometry, key.Position, KeyLightSteps);
            var candidates = Candidates(level.Geometry, StableHash.Combine(seed, StableHash.Of(key.Id)), near);
            candidates.RemoveAll(c => usedCells.Contains(c.Cell) || exits.Contains(c.Cell));
            candidates.Sort((a, b) => a.Key.CompareTo(b.Key));

            var placed = new List<Vector2>();
            for (var i = 0; i < KeyLightCount && candidates.Count > 0; i++)
            {
                var index = candidates.FindIndex(c => !IsNearAny(c.Point, placed, KeyLightGap));
                var candidate = candidates[index >= 0 ? index : 0];

                var preset = Pick(presets, StableHash.Combine(candidate.Key, 1UL));
                Add(level, candidate, color, preset);
                placed.Add(candidate.Point);
                taken.Add(candidate.Point);
                usedCells.Add(candidate.Cell);
                candidates.RemoveAll(c => c.Cell == candidate.Cell);
            }
        }

        /// <summary>Colour of the key's visual, else of its door's; false when neither is coloured.</summary>
        private static bool TryGetKeyColor(LevelData level, KeyData key, out Color color)
        {
            color = default;
            var theme = level.VisualTheme;
            if (theme == null)
                return false;

            string tag = null;
            var variant = theme.GetSet(Visual.VisualKind.Key)?.FindVariant(Visual.VisualResolver.ResolveObject(level, key).VariantId);
            if (variant != null && variant.HasColor)
                tag = variant.ColorTag;

            if (tag == null)
                foreach (var door in level.Doors)
                    if (door.KeyId == key.Id)
                        tag = Visual.VisualAssigner.ResolvedColor(level, door);

            return Visual.VisualColors.TryGetColor(tag, out color);
        }

        /// <summary>Floor cells reachable from the start in at most the given steps (4 directions, floor only).</summary>
        private static HashSet<GridPosition> Reachable(LevelGeometry geometry, GridPosition start, int steps)
        {
            var result = new HashSet<GridPosition> { start };
            var frontier = new List<GridPosition> { start };
            for (var step = 0; step < steps && frontier.Count > 0; step++)
            {
                var next = new List<GridPosition>();
                foreach (var cell in frontier)
                foreach (var side in DirectionExtensions.All)
                {
                    var neighbour = cell + side.ToOffset();
                    if (geometry.IsInside(neighbour) && geometry.GetCell(neighbour) == CellType.Floor && result.Add(neighbour))
                        next.Add(neighbour);
                }

                frontier = next;
            }

            return result;
        }

        private static void Add(LevelData level, Candidate candidate, Color color, LightPreset preset) =>
            level.MutableLights.Add(new LightSourceData(level.CreateUniqueId(LightSourceData.IdPrefix), candidate.Cell,
                candidate.Offset, color, preset.Radius, preset.Intensity, preset.Flicker, isGenerated: true));

        /// <summary>(Floor cell, wall side) pairs, optionally only in the given cells.</summary>
        private static List<Candidate> Candidates(LevelGeometry geometry, ulong seed, HashSet<GridPosition> cells)
        {
            var result = new List<Candidate>();
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var cell = geometry.ToPosition(i);
                if (geometry.GetCell(cell) != CellType.Floor || (cells != null && !cells.Contains(cell)))
                    continue;

                foreach (var side in DirectionExtensions.All)
                {
                    var neighbour = cell + side.ToOffset();
                    // Outside the grid counts as wall.
                    if (geometry.IsInside(neighbour) && geometry.GetCell(neighbour) != CellType.Wall)
                        continue;
                    result.Add(new Candidate(StableHash.Combine(seed, (ulong)(i * 4 + (int)side)), cell, side));
                }
            }

            return result;
        }

        private static bool IsNearAny(Vector2 point, List<Vector2> taken, float spacing)
        {
            var limit = spacing * spacing;
            foreach (var other in taken)
                if ((other - point).sqrMagnitude < limit)
                    return true;
            return false;
        }

        private static float TotalWeight(List<LightPreset> presets)
        {
            var total = 0f;
            foreach (var preset in presets)
                if (preset != null && preset.Weight > 0f)
                    total += preset.Weight;
            return total;
        }

        private static LightPreset Pick(List<LightPreset> presets, ulong key)
        {
            var target = (float)((key >> 11) * (1.0 / (1UL << 53))) * TotalWeight(presets);
            LightPreset last = null;
            foreach (var preset in presets)
            {
                if (preset == null || preset.Weight <= 0f) continue;
                last = preset;
                target -= preset.Weight;
                if (target < 0f) return preset;
            }

            return last;
        }

        private readonly struct Candidate
        {
            public Candidate(ulong key, GridPosition cell, Direction side)
            {
                Key = key;
                Cell = cell;
                Side = side;
            }

            public ulong Key { get; }
            public GridPosition Cell { get; }
            public Direction Side { get; }

            public Vector2 Offset
            {
                get
                {
                    var side = Side.ToOffset();
                    return new Vector2(side.X, side.Y) * WallOffset;
                }
            }

            public Vector2 Point => new Vector2(Cell.X, Cell.Y) + Offset;
        }
    }
}
