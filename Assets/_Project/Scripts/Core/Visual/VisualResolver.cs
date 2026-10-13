using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Level;

namespace Maze.Core.Visual
{
    public enum VisualSource
    {
        None = 0,
        Override = 1,
        Assignment = 2,
        Default = 3,
    }

    /// <summary>
    /// Final visual for a cell or object, by priority: designer override > saved assignment > set default (ТЗ §28).
    /// Pure lookup, no randomness: this is what runtime uses.
    /// </summary>
    public static class VisualResolver
    {
        public static VisualChoice ResolveCell(LevelData level, GridPosition position, CellLayer layer) =>
            ResolveCell(level, position, layer, out _);

        /// <summary>None for a layer the cell does not have (Wall layer of a non-wall cell).</summary>
        public static VisualChoice ResolveCell(LevelData level, GridPosition position, CellLayer layer, out VisualSource source)
        {
            var geometry = level.Geometry;
            if (!CellLayers.Exists(geometry.GetCell(position), layer))
            {
                source = VisualSource.None;
                return VisualChoice.None;
            }

            var data = level.VisualData;
            if (data.TryGetCellOverride(position, layer, out var choice) && !choice.IsEmpty)
            {
                source = VisualSource.Override;
                return choice;
            }

            choice = data.GetCellAssignment(layer, geometry.ToIndex(position));
            if (!choice.IsEmpty)
            {
                source = VisualSource.Assignment;
                return choice;
            }

            var kind = CellLayers.Kind(layer);
            var defaultId = DefaultVariantId(level, kind);
            if (!string.IsNullOrEmpty(defaultId))
            {
                source = VisualSource.Default;
                var rotation = kind == VisualKind.Wall
                    ? WallShapes.Classify(new CellVisualContext(geometry, position).WallConnections).Rotation
                    : 0;
                return new VisualChoice(defaultId, rotation);
            }

            source = VisualSource.None;
            return VisualChoice.None;
        }

        public static VisualChoice ResolveDecor(LevelData level, GridPosition position) => ResolveDecor(level, position, out _);

        /// <summary>
        /// Decor of a floor cell: override (an empty one = "no decor here") > assignment; never a set default (decor is
        /// optional). None for walls and doors.
        /// </summary>
        public static VisualChoice ResolveDecor(LevelData level, GridPosition position, out VisualSource source)
        {
            var geometry = level.Geometry;
            if (!CellLayers.Exists(geometry.GetCell(position), CellLayer.Decor))
            {
                source = VisualSource.None;
                return VisualChoice.None;
            }

            var data = level.VisualData;
            if (data.TryGetCellOverride(position, CellLayer.Decor, out var choice))
            {
                source = VisualSource.Override;
                return choice;
            }

            choice = data.GetCellAssignment(CellLayer.Decor, geometry.ToIndex(position));
            source = choice.IsEmpty ? VisualSource.None : VisualSource.Assignment;
            return choice;
        }

        /// <summary>
        /// Where the decor of a cell stands relative to the cell centre (x = East, y = up, z = North) and its turn in
        /// degrees clockwise from above: the hand-set placement of manual decor, otherwise the centre and the quarter
        /// turn of <paramref name="decor"/>.
        /// </summary>
        public static void ResolveDecorPose(LevelData level, GridPosition position, VisualChoice decor,
            out UnityEngine.Vector3 offset, out float yaw)
        {
            var data = level.VisualData;
            if (data.TryGetDecorPlacement(position, out var placement) && placement.IsValid &&
                data.TryGetCellOverride(position, CellLayer.Decor, out var manual) && !manual.IsEmpty)
            {
                offset = new UnityEngine.Vector3(placement.Offset.x, placement.Height, placement.Offset.y);
                yaw = placement.Yaw;
                return;
            }

            offset = UnityEngine.Vector3.zero;
            yaw = 90f * decor.Rotation;
        }

        /// <summary>
        /// Where an object's view stands relative to its cell centre and its turn (degrees clockwise from above):
        /// the hand-set placement of a placeable pickup, otherwise the centre and the quarter turn of
        /// <paramref name="choice"/>.
        /// </summary>
        public static void ResolveObjectPose(LevelData level, LevelEntityData entity, VisualChoice choice,
            out UnityEngine.Vector3 offset, out float yaw)
        {
            if (VisualKinds.IsPlaceable(entity) &&
                level.VisualData.TryGetObjectPlacement(entity.Id, out var placement) && placement.IsValid)
            {
                offset = new UnityEngine.Vector3(placement.Offset.x, placement.Height, placement.Offset.y);
                yaw = placement.Yaw;
                return;
            }

            offset = UnityEngine.Vector3.zero;
            yaw = 90f * choice.Rotation;
        }

        public static VisualChoice ResolveObject(LevelData level, LevelEntityData entity) => ResolveObject(level, entity, out _);

        public static VisualChoice ResolveObject(LevelData level, LevelEntityData entity, out VisualSource source)
        {
            var data = level.VisualData;
            if (data.TryGetObjectOverride(entity.Id, out var choice) && !choice.IsEmpty)
            {
                source = VisualSource.Override;
                return choice;
            }

            choice = data.GetObjectAssignment(entity.Id);
            if (!choice.IsEmpty)
            {
                source = VisualSource.Assignment;
                return choice;
            }

            if (VisualKinds.TryGetForEntity(entity, out var kind, out _))
            {
                var defaultId = DefaultVariantId(level, kind);
                if (!string.IsNullOrEmpty(defaultId))
                {
                    source = VisualSource.Default;
                    return new VisualChoice(defaultId, ObjectOrientation.For(kind, level.Geometry, entity.Position));
                }
            }

            source = VisualSource.None;
            return VisualChoice.None;
        }

        // Explicit Unity null checks: '?.' would bypass UnityEngine.Object's missing-reference check.
        private static string DefaultVariantId(LevelData level, VisualKind kind)
        {
            var theme = level.VisualTheme;
            if (theme == null)
                return null;

            var set = theme.GetSet(kind);
            return set == null ? null : set.DefaultVariantId;
        }

        /// <summary>
        /// Visual distribution diagnostics (ТЗ §93): resolved variant id -> number of cells using it in a layer.
        /// The Floor layer covers every cell (including floor under walls), the Wall layer only wall cells.
        /// </summary>
        public static Dictionary<string, int> CountCellVariants(LevelData level, CellLayer layer)
        {
            var counts = new Dictionary<string, int>();
            var geometry = level.Geometry;
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var position = geometry.ToPosition(i);
                if (!CellLayers.Exists(geometry.GetCell(position), layer))
                    continue;

                var id = ResolveCell(level, position, layer).VariantId ?? string.Empty;
                counts.TryGetValue(id, out var count);
                counts[id] = count + 1;
            }

            return counts;
        }
    }
}
