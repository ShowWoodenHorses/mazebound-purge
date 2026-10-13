using System.Collections.Generic;
using Maze.Core.Common;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Lighting;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Creates saved visual assignments from the level's VisualTheme and VisualSeed (ТЗ §18–29, §95–96).
    /// Editor/level-creation only. Overrides are never touched unless explicitly cleared.
    /// </summary>
    internal static class VisualAssigner
    {
        /// <summary>"Regenerate Visuals": geometry and objects stay, assignments are recreated.</summary>
        public static void AssignAll(LevelData level, bool clearOverrides)
        {
            if (clearOverrides)
                level.VisualData.ClearOverrides();

            AssignAllCells(level);
            AssignAllObjects(level);
            LightPlacer.PlaceAll(level, keepManual: !clearOverrides);
        }

        /// <summary>"Place Decor": recreates only the auto placed decor (density, VisualSeed); overrides stay.</summary>
        public static void AssignAllDecor(LevelData level)
        {
            var geometry = level.Geometry;
            if (!level.VisualData.HasCellAssignments(geometry.CellCount))
            {
                AssignAll(level, clearOverrides: false);
                return;
            }

            for (var i = 0; i < geometry.CellCount; i++)
                level.VisualData.SetCellAssignment(CellLayer.Decor, i, ChooseDecor(level, geometry.ToPosition(i)));
        }

        public static void AssignAllCells(LevelData level)
        {
            var geometry = level.Geometry;
            level.VisualData.ResetCellAssignments(geometry.CellCount);
            for (var i = 0; i < geometry.CellCount; i++)
                AssignCell(level, geometry.ToPosition(i));
        }

        /// <summary>All layers of one cell (decor included); a layer the cell does not have is cleared.</summary>
        private static void AssignCell(LevelData level, GridPosition position)
        {
            var index = level.Geometry.ToIndex(position);
            foreach (var layer in CellLayers.All)
                level.VisualData.SetCellAssignment(layer, index, ChooseCell(level, position, layer));
            level.VisualData.SetCellAssignment(CellLayer.Decor, index, ChooseDecor(level, position));
        }

        /// <summary>Recreates the auto decor of one cell (e.g. a key came or left); its manual decor stays.</summary>
        public static void AssignDecor(LevelData level, GridPosition position)
        {
            var geometry = level.Geometry;
            if (geometry.IsInside(position) && level.VisualData.HasCellAssignments(geometry.CellCount))
                level.VisualData.SetCellAssignment(CellLayer.Decor, geometry.ToIndex(position), ChooseDecor(level, position));
        }

        /// <summary>
        /// Auto decor of a floor cell: present with probability <see cref="LevelGenerationSettings.DecorDensity"/>
        /// (stable per cell and VisualSeed), a weighted General variant not taller than
        /// <see cref="LevelGenerationSettings.MaxAutoDecorHeight"/>, a stable quarter turn. No set default.
        /// Never in a key's cell — a small key gets lost in a pile of props; only decor placed by hand may be there.
        /// Never on a snowdrift either (the drift is the cell's look).
        /// </summary>
        public static VisualChoice ChooseDecor(LevelData level, GridPosition position)
        {
            var theme = level.VisualTheme;
            var geometry = level.Geometry;
            if (theme == null || !CellLayers.Exists(geometry.GetCell(position), CellLayer.Decor) || HasKey(level, position) ||
                geometry.GetSurface(position) != CellSurface.None)
                return VisualChoice.None;

            var settings = level.Generation;
            var key = VisualSelector.CellKey(settings.VisualSeed, VisualKind.Decor, geometry.ToIndex(position));
            var roll = (StableHash.Mix(key ^ DecorChanceSalt) >> 40) / (float)(1UL << 24);
            if (roll >= settings.DecorDensity)
                return VisualChoice.None;

            var maxHeight = settings.MaxAutoDecorHeight;
            var variant = VisualSelector.Pick(theme.GetSet(VisualKind.Decor), VisualCategory.General, null, key,
                v => v.Height <= maxHeight);
            if (variant == null)
                return VisualChoice.None;

            var rotation = (int)(StableHash.Mix(key ^ DecorRotationSalt) & 3UL);
            return new VisualChoice(variant.Id, rotation);
        }

        /// <summary>
        /// Fixtures of the lights that have none (or one the Light set no longer has): a weighted General variant
        /// of the theme's Light set, stable per light id and VisualSeed. Chosen fixtures stay.
        /// </summary>
        public static void AssignLights(LevelData level)
        {
            var set = level.VisualTheme != null ? level.VisualTheme.GetSet(VisualKind.Light) : null;
            foreach (var light in level.Lights)
                if (light.Visual.IsEmpty || set == null || set.FindVariant(light.Visual.VariantId) == null)
                    light.Visual = ChooseLight(level, light.Id);
        }

        public static VisualChoice ChooseLight(LevelData level, string lightId)
        {
            var set = level.VisualTheme != null ? level.VisualTheme.GetSet(VisualKind.Light) : null;
            var key = VisualSelector.ObjectKey(level.Generation.VisualSeed, VisualKind.Light, lightId);
            var variant = VisualSelector.Pick(set, VisualCategory.General, null, key);
            return variant != null ? new VisualChoice(variant.Id) : VisualChoice.None;
        }

        private static bool HasKey(LevelData level, GridPosition position)
        {
            foreach (var key in level.Keys)
                if (key.Position == position)
                    return true;
            return false;
        }

        private const ulong DecorChanceSalt = 0x4465636F72UL;
        private const ulong DecorRotationSalt = 0x526F7444UL;

        /// <summary>
        /// Doors first (locked doors get distinct colours while colours last), then keys (each takes its door's
        /// colour), then every other object.
        /// </summary>
        public static void AssignAllObjects(LevelData level)
        {
            var ids = new HashSet<string>();
            var usedColors = new HashSet<string>();

            foreach (var door in level.Doors)
            {
                ids.Add(door.Id);
                level.VisualData.SetObjectAssignment(door.Id, ChooseDoor(level, door, usedColors));
                var color = ResolvedColor(level, door);
                if (door.RequiresKey && color != null)
                    usedColors.Add(color);
            }

            foreach (var key in level.Keys)
            {
                ids.Add(key.Id);
                level.VisualData.SetObjectAssignment(key.Id, ChooseKey(level, key));
            }

            foreach (var entity in level.AllEntities())
            {
                if (entity is DoorData || entity is KeyData)
                    continue;

                ids.Add(entity.Id);
                AssignObject(level, entity);
            }

            level.VisualData.RetainObjects(ids);
        }

        /// <summary>
        /// Re-assigns a cell whose geometry changed, plus everything whose shape depends on it:
        /// the 8 surrounding cells, and the orientation of objects standing on them.
        /// Object variants are kept: editing walls must not recolour a door or swap a model.
        /// </summary>
        public static void ReassignAround(LevelData level, GridPosition center)
        {
            var geometry = level.Geometry;
            if (!level.VisualData.HasCellAssignments(geometry.CellCount))
            {
                AssignAll(level, clearOverrides: false);
                return;
            }

            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var p = new GridPosition(center.X + dx, center.Y + dy);
                if (!geometry.IsInside(p))
                    continue;

                AssignCell(level, p);
                foreach (var entity in level.AllEntities())
                    if (entity.Position == p)
                        UpdateOrientation(level, entity);
            }
        }

        /// <summary>Keeps the assigned variant, recomputes rotation from current geometry.</summary>
        public static void UpdateOrientation(LevelData level, LevelEntityData entity)
        {
            if (!VisualKinds.TryGetForEntity(entity, out var kind, out _))
                return;

            var current = level.VisualData.GetObjectAssignment(entity.Id);
            if (current.IsEmpty)
            {
                AssignObject(level, entity);
                return;
            }

            var rotation = ObjectOrientation.For(kind, level.Geometry, entity.Position);
            level.VisualData.SetObjectAssignment(entity.Id, new VisualChoice(current.VariantId, rotation));
        }

        /// <summary>Assigns one object. Doors avoid colours of other locked doors; keys follow their door.</summary>
        public static void AssignObject(LevelData level, LevelEntityData entity)
        {
            if (!VisualKinds.TryGetForEntity(entity, out _, out _))
                return;

            VisualChoice choice;
            switch (entity)
            {
                case DoorData door:
                    var usedColors = new HashSet<string>();
                    foreach (var other in level.Doors)
                    {
                        var color = other != door && other.RequiresKey ? ResolvedColor(level, other) : null;
                        if (color != null)
                            usedColors.Add(color);
                    }

                    choice = ChooseDoor(level, door, usedColors);
                    break;
                case KeyData key:
                    choice = ChooseKey(level, key);
                    break;
                default:
                    choice = ChooseObject(level, entity);
                    break;
            }

            level.VisualData.SetObjectAssignment(entity.Id, choice);
        }

        /// <summary>
        /// Locked doors take a coloured variant, preferring colours not used by other locked doors;
        /// unlocked doors take an uncoloured one so they do not look locked.
        /// </summary>
        private static VisualChoice ChooseDoor(LevelData level, DoorData door, HashSet<string> usedColors)
        {
            var theme = level.VisualTheme;
            if (theme == null)
                return VisualChoice.None;

            var set = theme.GetSet(VisualKind.Door);
            var key = VisualSelector.ObjectKey(level.Generation.VisualSeed, VisualKind.Door, door.Id);
            VisualVariant variant;
            if (door.RequiresKey)
            {
                variant = VisualSelector.Pick(set, VisualCategory.General, null, key, v => v.HasColor && !usedColors.Contains(v.ColorTag))
                          ?? VisualSelector.PickOrDefault(set, VisualCategory.General, null, key, v => v.HasColor);
            }
            else
            {
                variant = VisualSelector.PickOrDefault(set, VisualCategory.General, null, key, v => !v.HasColor);
            }

            return variant == null
                ? VisualChoice.None
                : new VisualChoice(variant.Id, ObjectOrientation.For(VisualKind.Door, level.Geometry, door.Position));
        }

        /// <summary>A key always takes the colour of the (single) door it opens.</summary>
        private static VisualChoice ChooseKey(LevelData level, KeyData keyData)
        {
            var theme = level.VisualTheme;
            if (theme == null)
                return VisualChoice.None;

            DoorData door = null;
            foreach (var candidate in level.Doors)
                if (candidate.KeyId == keyData.Id)
                {
                    door = candidate;
                    break;
                }

            var doorColor = door != null ? ResolvedColor(level, door) : null;
            var key = VisualSelector.ObjectKey(level.Generation.VisualSeed, VisualKind.Key, keyData.Id);
            var variant = door != null
                ? VisualSelector.PickOrDefault(theme.GetSet(VisualKind.Key), VisualCategory.General, null, key,
                    v => v.HasColor && v.ColorTag == doorColor)
                : VisualSelector.PickOrDefault(theme.GetSet(VisualKind.Key), VisualCategory.General, null, key, v => v.HasColor);

            return variant == null ? VisualChoice.None : new VisualChoice(variant.Id);
        }

        /// <summary>Colour of the door's final visual (override included), or null.</summary>
        public static string ResolvedColor(LevelData level, DoorData door)
        {
            var theme = level.VisualTheme;
            if (theme == null)
                return null;

            var set = theme.GetSet(VisualKind.Door);
            if (set == null)
                return null;

            var variant = set.FindVariant(VisualResolver.ResolveObject(level, door).VariantId);
            return variant != null && variant.HasColor ? variant.ColorTag : null;
        }

        /// <summary>Floor layer: weighted floor variant for every cell. Wall layer: wall variant by neighbour shape.</summary>
        public static VisualChoice ChooseCell(LevelData level, GridPosition position, CellLayer layer)
        {
            var theme = level.VisualTheme;
            var geometry = level.Geometry;
            if (theme == null || !CellLayers.Exists(geometry.GetCell(position), layer))
                return VisualChoice.None;

            var kind = CellLayers.Kind(layer);
            var category = VisualCategory.General;
            var rotation = 0;

            if (layer == CellLayer.Wall)
                (category, rotation) = WallShapes.Classify(new CellVisualContext(geometry, position).WallConnections);
            else if (layer == CellLayer.Floor && geometry.GetSurface(position) == CellSurface.Snowdrift &&
                     HasAutoVariant(theme.GetSet(kind), VisualCategory.Snowdrift))
                category = VisualCategory.Snowdrift; // a theme without drift floors shows the ordinary floor

            var key = VisualSelector.CellKey(level.Generation.VisualSeed, kind, geometry.ToIndex(position));
            var variant = VisualSelector.PickOrDefault(theme.GetSet(kind), category, null, key);
            return variant == null ? VisualChoice.None : new VisualChoice(variant.Id, rotation);
        }

        private static bool HasAutoVariant(VisualSet set, VisualCategory category)
        {
            if (set == null) return false;
            foreach (var variant in set.Variants)
                if (variant.Weight > 0 && variant.Category == category)
                    return true;
            return false;
        }

        public static VisualChoice ChooseObject(LevelData level, LevelEntityData entity)
        {
            var theme = level.VisualTheme;
            if (theme == null || !VisualKinds.TryGetForEntity(entity, out var kind, out var definition))
                return VisualChoice.None;

            var key = VisualSelector.ObjectKey(level.Generation.VisualSeed, kind, entity.Id);
            var variant = VisualSelector.PickOrDefault(theme.GetSet(kind), VisualCategory.General, definition, key);
            return variant == null
                ? VisualChoice.None
                : new VisualChoice(variant.Id, ObjectOrientation.For(kind, level.Geometry, entity.Position));
        }
    }

    /// <summary>Orientation derived from geometry (not random). Prefabs are authored facing North.</summary>
    public static class ObjectOrientation
    {
        public static int For(VisualKind kind, LevelGeometry geometry, GridPosition position)
        {
            if (!geometry.IsInside(position))
                return 0;

            var context = new CellVisualContext(geometry, position);
            switch (kind)
            {
                // Rotation 0: door blocks a North-South passage (walls on East and West).
                case VisualKind.Door:
                    return context.East != CellType.Floor && context.West != CellType.Floor ? 0 : 1;

                // Exit faces an open side with a wall behind it (a ladder stands against that wall), so in a dead end
                // it faces the corridor; without such a side — the first open one.
                case VisualKind.Exit:
                    foreach (var direction in DirectionExtensions.All)
                        if (context.Get(direction) != CellType.Wall && context.Get(direction.Opposite()) == CellType.Wall)
                            return (int)direction;
                    foreach (var direction in DirectionExtensions.All)
                        if (context.Get(direction) != CellType.Wall)
                            return (int)direction;
                    return 0;

                default:
                    return 0;
            }
        }
    }
}
