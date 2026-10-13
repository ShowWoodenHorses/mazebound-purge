using System;
using System.Collections.Generic;
using System.Linq;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Navigation;
using Maze.Core.Visual;

namespace Maze.Core.Validation
{
    /// <summary>
    /// Level validator (ТЗ §90–92): structural, visual and gameplay checks.
    /// Read-only: never changes the level and never "fixes" anything silently.
    /// </summary>
    public static class LevelValidator
    {
        private const ValidationCategory Structural = ValidationCategory.Structural;
        private const ValidationCategory Visual = ValidationCategory.Visual;
        private const ValidationCategory Gameplay = ValidationCategory.Gameplay;
        private const ValidationSeverity Error = ValidationSeverity.Error;
        private const ValidationSeverity Warning = ValidationSeverity.Warning;
        private const ValidationSeverity Info = ValidationSeverity.Info;

        public static ValidationReport Validate(LevelData level) => Validate(level, null);

        /// <summary>
        /// <paramref name="additionalChecks"/> lets editor-only code (e.g. asset existence checks) add issues
        /// to the same report; it runs only when geometry is consistent.
        /// </summary>
        internal static ValidationReport Validate(LevelData level, Action<LevelData, ValidationReport> additionalChecks)
        {
            var report = new ValidationReport();
            var geometry = level.Geometry;

            if (geometry == null || !geometry.IsConsistent)
            {
                report.Add(Error, Structural, ValidationCodes.GeometryCorrupted, "Level geometry is missing or corrupted.");
                return report;
            }

            ValidateStructure(level, report);
            ValidateVisuals(level, report);
            ValidateGameplay(level, report);
            additionalChecks?.Invoke(level, report);

            report.AddTruncationNotes();
            return report;
        }

        // ---------------------------------------------------------------- Structural

        /// <remarks>
        /// Size parity is not checked: odd size is a requirement of the generator only; a finished, manually
        /// edited level may have any size.
        /// </remarks>
        private static void ValidateStructure(LevelData level, ValidationReport report)
        {
            if (level.PlayerStarts.Count == 0)
                report.Add(Error, Structural, ValidationCodes.NoPlayerStart, "Level has no player start.");

            if (level.Exits.Count == 0)
                report.Add(Error, Structural, ValidationCodes.NoExit, "Level has no exit.");

            ValidateIds(level, report);
            ValidatePlacement(level, report);
            ValidateReferences(level, report);
            ValidateMapFragments(level, report);
            ValidateSurfaces(level, report);
            ValidateTutorial(level, report);

            foreach (var door in level.Doors.Where(d => d.IsFrozen && d.IsInitiallyOpen))
                report.Add(Error, Structural, ValidationCodes.FrozenDoorInitiallyOpen,
                    $"Door '{door.Id}' is frozen and initially open: a frozen door starts closed.", door.Position, door.Id);
        }

        /// <summary>
        /// Tutorial steps: a text key; a target that exists and suits the goal; a Reach cell inside the level on the floor.
        /// </summary>
        private static void ValidateTutorial(LevelData level, ValidationReport report)
        {
            var tutorial = level.Tutorial;
            if (tutorial == null) return;
            if (tutorial.Steps.Count == 0)
            {
                report.Add(Warning, Structural, ValidationCodes.TutorialEmpty, "Tutorial script has no steps.");
                return;
            }

            for (var i = 0; i < tutorial.Steps.Count; i++)
            {
                var step = tutorial.Steps[i];
                var name = $"Tutorial step {i + 1}";
                if (string.IsNullOrEmpty(step.Text))
                    report.Add(Error, Structural, ValidationCodes.TutorialStepInvalid, $"{name} has no text key.");

                var target = level.FindEntity(step.Target);
                if (step.HasTarget && target == null)
                {
                    report.Add(Error, Structural, ValidationCodes.TutorialStepInvalid,
                        $"{name}: target '{step.Target}' is not in the level.", null, step.Target);
                    continue;
                }

                var problem = step.Goal switch
                {
                    TutorialGoal.Reach when !step.HasTarget && !level.Geometry.IsInside(step.Cell) =>
                        $"cell {step.Cell} is outside the level",
                    TutorialGoal.Reach when !step.HasTarget && level.Geometry.GetCell(step.Cell) != CellType.Floor =>
                        $"cell {step.Cell} is not floor",
                    TutorialGoal.Collect when !(target is KeyData || target is MedkitData || target is WeaponPickupData ||
                                                target is MapFragmentData) =>
                        "Collect needs a key, medkit, weapon or map fragment as the target",
                    TutorialGoal.UnlockDoor when !(target is DoorData door && door.RequiresKey) =>
                        "Unlock Door needs a locked door as the target",
                    TutorialGoal.OpenDoor when !(target is DoorData) => "Open Door needs a door as the target",
                    TutorialGoal.Kill when !(target is ZombieSpawnData) => "Kill needs a zombie as the target",
                    _ => null,
                };
                if (problem != null)
                    report.Add(Error, Structural, ValidationCodes.TutorialStepInvalid, $"{name}: {problem}.",
                        target?.Position, step.Target);
            }
        }

        /// <summary>Snowdrifts and the like: only on Floor cells; the array must match the size (or be empty).</summary>
        private static void ValidateSurfaces(LevelData level, ValidationReport report)
        {
            var geometry = level.Geometry;
            if (!geometry.SurfacesConsistent)
            {
                report.Add(Error, Structural, ValidationCodes.SurfacesOutOfSync,
                    "Cell surfaces (snowdrifts) do not match the level size; they are ignored.");
                return;
            }

            for (var i = 0; i < geometry.CellCount; i++)
            {
                var p = geometry.ToPosition(i);
                var surface = geometry.GetSurface(p);
                if (surface != CellSurface.None && geometry.GetCell(p) != CellType.Floor)
                    report.Add(Warning, Structural, ValidationCodes.SurfaceNotOnFloor,
                        $"{surface} at {p} is on a {geometry.GetCell(p)} cell and is ignored.", p);
            }
        }

        private static void ValidateIds(LevelData level, ValidationReport report)
        {
            var seen = new HashSet<string>();
            foreach (var id in level.AllIds())
            {
                if (string.IsNullOrEmpty(id))
                    report.Add(Error, Structural, ValidationCodes.EmptyId, "Object has an empty id.");
                else if (!seen.Add(id))
                    report.Add(Error, Structural, ValidationCodes.DuplicateId, $"Id '{id}' is used more than once.", entityId: id);
            }
        }

        private static void ValidatePlacement(LevelData level, ValidationReport report)
        {
            var geometry = level.Geometry;
            var occupants = new Dictionary<GridPosition, List<LevelEntityData>>();
            var doorCells = new HashSet<GridPosition>();

            foreach (var entity in level.AllEntities())
            {
                var p = entity.Position;
                if (!geometry.IsInside(p))
                {
                    report.Add(Error, Structural, ValidationCodes.OutOfBounds, $"'{entity.Id}' at {p} is outside the level.", p, entity.Id);
                    continue;
                }

                var cell = geometry.GetCell(p);
                if (entity is DoorData)
                {
                    doorCells.Add(p);
                    if (cell != CellType.Door)
                        report.Add(Error, Structural, ValidationCodes.DoorNotOnDoorCell, $"Door '{entity.Id}' at {p} stands on a {cell} cell.", p, entity.Id);
                }
                else if (cell != CellType.Floor)
                {
                    report.Add(Error, Structural, ValidationCodes.InsideWall, $"'{entity.Id}' at {p} stands on a {cell} cell.", p, entity.Id);
                }

                if (!occupants.TryGetValue(p, out var list))
                    occupants[p] = list = new List<LevelEntityData>();
                list.Add(entity);
            }

            for (var i = 0; i < geometry.CellCount; i++)
            {
                var p = geometry.ToPosition(i);
                if (geometry.GetCell(p) == CellType.Door && !doorCells.Contains(p))
                    report.Add(Error, Structural, ValidationCodes.DoorCellWithoutDoor, $"Door cell {p} has no door object.", p);
            }

            foreach (var pair in occupants)
            {
                var nonZombies = pair.Value.Where(e => !(e is ZombieSpawnData)).ToList();
                if (nonZombies.Count > 1)
                    report.Add(Error, Structural, ValidationCodes.Intersection,
                        $"Cell {pair.Key} holds several objects: {string.Join(", ", nonZombies.Select(e => e.Id))}.", pair.Key);

                if (pair.Value.Any(e => e is ZombieSpawnData) && pair.Value.Any(e => e is PlayerStartData))
                    report.Add(Error, Structural, ValidationCodes.ZombieOnPlayerStart,
                        $"Zombie spawns on player start at {pair.Key}.", pair.Key);
            }
        }

        private static void ValidateReferences(LevelData level, ValidationReport report)
        {
            var geometry = level.Geometry;
            var keyIds = new HashSet<string>(level.Keys.Select(k => k.Id));
            var usedKeyIds = new HashSet<string>();

            foreach (var door in level.Doors)
            {
                if (!door.RequiresKey)
                    continue;

                // Strict rule: one key opens exactly one door and is consumed.
                if (!usedKeyIds.Add(door.KeyId))
                    report.Add(Error, Structural, ValidationCodes.KeySharedByDoors,
                        $"Key '{door.KeyId}' is used by several doors (including '{door.Id}'); one key opens one door.",
                        door.Position, door.Id);

                if (!keyIds.Contains(door.KeyId))
                    report.Add(Error, Structural, ValidationCodes.MissingKeyReference,
                        $"Door '{door.Id}' requires missing key '{door.KeyId}'.", door.Position, door.Id);
            }

            foreach (var key in level.Keys)
                if (!usedKeyIds.Contains(key.Id))
                    report.Add(Warning, Structural, ValidationCodes.UnusedKey, $"Key '{key.Id}' does not open any door.", key.Position, key.Id);

            var patrolIds = new HashSet<string>(level.Patrols.Select(p => p.Id));
            var usedPatrolIds = new HashSet<string>();

            foreach (var zombie in level.ZombieSpawns)
            {
                if (zombie.Definition == null)
                    report.Add(Error, Structural, ValidationCodes.MissingDefinition,
                        $"Zombie '{zombie.Id}' has no ZombieDefinition.", zombie.Position, zombie.Id);

                if (!zombie.HasPatrol)
                    continue;

                usedPatrolIds.Add(zombie.PatrolId);
                if (!patrolIds.Contains(zombie.PatrolId))
                    report.Add(Error, Structural, ValidationCodes.MissingPatrolReference,
                        $"Zombie '{zombie.Id}' uses missing patrol '{zombie.PatrolId}'.", zombie.Position, zombie.Id);
            }

            foreach (var weapon in level.Weapons)
                if (weapon.Definition == null)
                    report.Add(Error, Structural, ValidationCodes.MissingDefinition,
                        $"Weapon '{weapon.Id}' has no WeaponDefinition.", weapon.Position, weapon.Id);

            foreach (var patrol in level.Patrols)
            {
                if (patrol.Points.Count < 2)
                    report.Add(Warning, Structural, ValidationCodes.PatrolTooShort,
                        $"Patrol '{patrol.Id}' has {patrol.Points.Count} point(s); at least 2 are needed.", entityId: patrol.Id);

                foreach (var point in patrol.Points)
                    if (!geometry.IsInside(point) || geometry.GetCell(point) == CellType.Wall)
                        report.Add(Error, Structural, ValidationCodes.InvalidPatrolPoint,
                            $"Patrol '{patrol.Id}' point {point} is outside the level or inside a wall.", point, patrol.Id);

                if (!usedPatrolIds.Contains(patrol.Id))
                    report.Add(Warning, Structural, ValidationCodes.UnusedPatrol, $"Patrol '{patrol.Id}' is not used by any zombie.", entityId: patrol.Id);
            }
        }

        private static void ValidateMapFragments(LevelData level, ValidationReport report)
        {
            var geometry = level.Geometry;
            var fragments = level.MapFragments;

            foreach (var fragment in fragments)
            {
                var r = fragment.Region;
                if (r.IsEmpty || r.X < 0 || r.Y < 0 || r.XMax > geometry.Width || r.YMax > geometry.Height)
                    report.Add(Error, Structural, ValidationCodes.InvalidFragmentRegion,
                        $"Map fragment '{fragment.Id}' region {r} is empty or outside the level.", fragment.Position, fragment.Id);
            }

            for (var i = 0; i < fragments.Count; i++)
            for (var j = i + 1; j < fragments.Count; j++)
                if (fragments[i].Region.Overlaps(fragments[j].Region))
                    report.Add(Error, Structural, ValidationCodes.FragmentOverlap,
                        $"Map fragments '{fragments[i].Id}' and '{fragments[j].Id}' overlap.", entityId: fragments[i].Id);
        }

        // ---------------------------------------------------------------- Visual

        private static void ValidateVisuals(LevelData level, ValidationReport report)
        {
            ValidateLights(level, report);

            var theme = level.VisualTheme;
            if (theme == null)
            {
                report.Add(Error, Visual, ValidationCodes.NoVisualTheme, "Level has no VisualTheme.");
                return;
            }

            var requiredKinds = new HashSet<VisualKind> { VisualKind.Floor, VisualKind.Wall };
            foreach (var entity in level.AllEntities())
                if (VisualKinds.TryGetForEntity(entity, out var kind, out _))
                    requiredKinds.Add(kind);
            if (theme.GetSet(VisualKind.Decor) != null || UsesDecor(level))
                requiredKinds.Add(VisualKind.Decor);
            if (theme.GetSet(VisualKind.Light) != null || level.Lights.Any(l => !l.Visual.IsEmpty))
                requiredKinds.Add(VisualKind.Light);

            foreach (var kind in requiredKinds.OrderBy(k => k))
            {
                var set = theme.GetSet(kind);
                if (set == null)
                    report.Add(Error, Visual, ValidationCodes.MissingVisualSet, $"VisualTheme has no {kind} set.");
                else
                    ValidateSet(set, kind, report);
            }

            ValidateCellVisuals(level, theme, report);
            ValidateObjectVisuals(level, theme, report);
            ValidateLightVisuals(level, theme, report);
            ValidateKeyDoorColors(level, theme, report);

            if (!theme.Weather.HasIceOverlay)
                foreach (var door in level.Doors.Where(d => d.IsFrozen))
                    report.Add(Warning, Visual, ValidationCodes.FrozenDoorWithoutIceVisual,
                        $"Door '{door.Id}' is frozen, but the theme has no ice overlay (Weather): the ice is not shown.",
                        door.Position, door.Id);
        }

        /// <summary>A light's fixture must exist in the theme's Light set (a missing set is reported as MissingVisualSet).</summary>
        private static void ValidateLightVisuals(LevelData level, VisualTheme theme, ValidationReport report)
        {
            var set = theme.GetSet(VisualKind.Light);
            if (set == null)
                return;

            foreach (var light in level.Lights)
            {
                var choice = LightFixtures.Resolve(level, light);
                if (!choice.IsEmpty && set.FindVariant(choice.VariantId) == null)
                    report.Add(Error, Visual, ValidationCodes.UnknownVariant,
                        $"Light '{light.Id}' uses unknown fixture variant '{choice.VariantId}'.", light.Cell, light.Id);
            }
        }

        /// <summary>A light belongs to a floor cell, stays inside it and has a positive radius.</summary>
        private static void ValidateLights(LevelData level, ValidationReport report)
        {
            var geometry = level.Geometry;
            foreach (var light in level.Lights)
            {
                if (!geometry.IsInside(light.Cell) || geometry.GetCell(light.Cell) != CellType.Floor)
                    report.Add(Error, Visual, ValidationCodes.LightNotOnFloor,
                        $"Light '{light.Id}' is not on a floor cell.", light.Cell, light.Id);

                if (!(light.Radius > 0f) || light.Intensity < 0f ||
                    Math.Abs(light.Offset.x) > 0.5f || Math.Abs(light.Offset.y) > 0.5f)
                    report.Add(Error, Visual, ValidationCodes.InvalidLight,
                        $"Light '{light.Id}' needs a positive radius, non-negative intensity and an offset within its cell.",
                        light.Cell, light.Id);
            }
        }

        /// <summary>A key must have the colour of its door; distinct pairs should have distinct colours (ТЗ §40).</summary>
        private static void ValidateKeyDoorColors(LevelData level, VisualTheme theme, ValidationReport report)
        {
            var doorSet = theme.GetSet(VisualKind.Door);
            if (doorSet == null)
                return;

            var keySet = theme.GetSet(VisualKind.Key);
            var doorsByColor = new Dictionary<string, List<string>>();

            foreach (var door in level.Doors)
            {
                var doorColor = VisualAssigner.ResolvedColor(level, door);
                if (!door.RequiresKey)
                {
                    if (doorColor != null)
                        report.Add(Warning, Visual, ValidationCodes.UnlockedDoorWithColor,
                            $"Door '{door.Id}' needs no key but uses coloured visual '{doorColor}'.", door.Position, door.Id);
                    continue;
                }

                if (doorColor == null)
                {
                    report.Add(Error, Visual, ValidationCodes.KeyDoorColorMismatch,
                        $"Locked door '{door.Id}' has no colour; it cannot be matched with key '{door.KeyId}'.", door.Position, door.Id);
                    continue;
                }

                if (!doorsByColor.TryGetValue(doorColor, out var doors))
                    doorsByColor[doorColor] = doors = new List<string>();
                doors.Add(door.Id);

                var key = level.Keys.FirstOrDefault(k => k.Id == door.KeyId);
                if (key == null || keySet == null)
                    continue;

                var keyVariant = keySet.FindVariant(VisualResolver.ResolveObject(level, key).VariantId);
                var keyColor = keyVariant != null && keyVariant.HasColor ? keyVariant.ColorTag : null;
                if (keyColor != doorColor)
                    report.Add(Error, Visual, ValidationCodes.KeyDoorColorMismatch,
                        $"Key '{key.Id}' is '{keyColor ?? "uncoloured"}' but its door '{door.Id}' is '{doorColor}'.", key.Position, key.Id);
            }

            foreach (var pair in doorsByColor)
                if (pair.Value.Count > 1)
                    report.Add(Warning, Visual, ValidationCodes.RepeatedKeyColor,
                        $"Colour '{pair.Key}' is used by several locked doors: {string.Join(", ", pair.Value)}.");
        }

        /// <summary>
        /// Placed decor must name an existing variant of the theme's Decor set; a hand-set placement must be within
        /// its limits and belong to manual decor of a floor cell.
        /// </summary>
        private static void ValidateDecor(LevelData level, VisualTheme theme, ValidationReport report)
        {
            var geometry = level.Geometry;
            foreach (var placement in level.VisualData.DecorPlacements)
            {
                var cell = placement.Cell;
                if (!placement.IsValid)
                    report.Add(Error, Visual, ValidationCodes.InvalidDecorPlacement,
                        $"Decor placement at {cell} is out of limits (shift up to half a cell, height " +
                        $"{DecorPlacement.MinHeight}..{DecorPlacement.MaxHeight} m).", cell);
                else if (!geometry.IsInside(cell) ||
                         !level.VisualData.TryGetCellOverride(cell, CellLayer.Decor, out var manual) || manual.IsEmpty ||
                         !CellLayers.Exists(geometry.GetCell(cell), CellLayer.Decor))
                    report.Add(Warning, Visual, ValidationCodes.StaleDecorPlacement,
                        $"Decor placement at {cell} is ignored: the cell has no manual decor.", cell);
            }

            var set = theme.GetSet(VisualKind.Decor);
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var p = geometry.ToPosition(i);
                var choice = VisualResolver.ResolveDecor(level, p);
                if (choice.IsEmpty || set == null)
                    continue; // A missing set is reported once as MissingVisualSet.

                if (set.FindVariant(choice.VariantId) == null)
                    report.Add(Error, Visual, ValidationCodes.UnknownVariant,
                        $"Cell {p} uses unknown decor variant '{choice.VariantId}'.", p);
            }
        }

        /// <summary>A pickup's hand-set placement must be within the limits.</summary>
        private static void ValidateObjectPlacements(LevelData level, ValidationReport report)
        {
            foreach (var placement in level.VisualData.ObjectPlacements)
            {
                if (placement.IsValid)
                    continue;

                var entity = level.AllEntities().FirstOrDefault(e => e.Id == placement.EntityId);
                report.Add(Error, Visual, ValidationCodes.InvalidObjectPlacement,
                    $"Placement of '{placement.EntityId}' is out of limits (shift up to half a cell, height " +
                    $"{DecorPlacement.MinHeight}..{DecorPlacement.MaxHeight} m).", entity?.Position, placement.EntityId);
            }
        }

        private static bool UsesDecor(LevelData level)
        {
            var geometry = level.Geometry;
            for (var i = 0; i < geometry.CellCount; i++)
                if (!VisualResolver.ResolveDecor(level, geometry.ToPosition(i)).IsEmpty)
                    return true;
            return false;
        }

        private static void ValidateSet(VisualSet set, VisualKind kind, ValidationReport report)
        {
            if (set.Kind != kind)
                report.Add(Error, Visual, ValidationCodes.VisualSetKindMismatch, $"Set '{set.name}' is {set.Kind} but used as {kind}.");

            var ids = new HashSet<string>();
            foreach (var variant in set.Variants)
            {
                if (string.IsNullOrEmpty(variant.Id) || !ids.Add(variant.Id))
                    report.Add(Error, Visual, ValidationCodes.DuplicateVariantId, $"Set '{set.name}' has an empty or duplicate variant id '{variant.Id}'.");

                if (variant.Prefab == null || !variant.Prefab.RuntimeKeyIsValid())
                    report.Add(Error, Visual, ValidationCodes.BrokenPrefabReference, $"Variant '{variant.Id}' in set '{set.name}' has no valid prefab reference.");
            }

            if (!string.IsNullOrEmpty(set.DefaultVariantId) && set.FindVariant(set.DefaultVariantId) == null)
                report.Add(Error, Visual, ValidationCodes.InvalidDefaultVariant,
                    $"Default variant '{set.DefaultVariantId}' of set '{set.name}' does not exist.");
        }

        private static void ValidateCellVisuals(LevelData level, VisualTheme theme, ValidationReport report)
        {
            var geometry = level.Geometry;
            var data = level.VisualData;

            if (!data.HasCellAssignments(geometry.CellCount))
                report.Add(Error, Visual, ValidationCodes.AssignmentsOutOfSync,
                    $"Visual assignments do not match the level size {geometry.Width}x{geometry.Height}. Run Regenerate Visuals.");

            foreach (var entry in data.CellOverrides)
            {
                if (!geometry.IsInside(entry.Position))
                    report.Add(Error, Visual, ValidationCodes.OverrideOutOfBounds, $"Visual override at {entry.Position} is outside the level.", entry.Position);
                else if (!CellLayers.Exists(geometry.GetCell(entry.Position), entry.Layer))
                    report.Add(Warning, Visual, ValidationCodes.OverrideLayerMismatch,
                        entry.Layer == CellLayer.Decor
                            ? $"Decor at {entry.Position} is ignored: decor is allowed on floor cells only, not in walls and doors."
                            : $"{entry.Layer} override at {entry.Position} is ignored: the cell is not a wall.", entry.Position);
            }

            ValidateDecor(level, theme, report);
            ValidateObjectPlacements(level, report);

            var staleWalls = 0;
            var wallCategoriesWithoutVariants = new Dictionary<VisualCategory, int>();
            var usedVariants = new Dictionary<VisualKind, HashSet<string>>();
            var cellCounts = new Dictionary<VisualKind, int>();

            for (var i = 0; i < geometry.CellCount; i++)
            foreach (var layer in CellLayers.All)
            {
                var p = geometry.ToPosition(i);
                if (!CellLayers.Exists(geometry.GetCell(p), layer))
                    continue;

                var kind = CellLayers.Kind(layer);
                var set = theme.GetSet(kind);
                if (set == null)
                    continue;

                var choice = VisualResolver.ResolveCell(level, p, layer, out var source);

                // Root cause first: a wall category without variants explains the missing/default visuals below.
                var checkWallShape = layer == CellLayer.Wall && source != VisualSource.Override;
                var category = VisualCategory.General;
                var rotation = 0;
                var categoryHasVariants = true;
                if (checkWallShape)
                {
                    (category, rotation) = WallShapes.Classify(new CellVisualContext(geometry, p).WallConnections);
                    categoryHasVariants = HasAutoVariant(set, category);
                    if (!categoryHasVariants)
                    {
                        wallCategoriesWithoutVariants.TryGetValue(category, out var missing);
                        wallCategoriesWithoutVariants[category] = missing + 1;
                    }
                }

                if (choice.IsEmpty)
                {
                    report.Add(Error, Visual, ValidationCodes.MissingVisual, $"Cell {p} has no {kind.ToString().ToLowerInvariant()} visual.", p);
                    continue;
                }

                var variant = set.FindVariant(choice.VariantId);
                if (variant == null)
                {
                    report.Add(Error, Visual, ValidationCodes.UnknownVariant,
                        $"Cell {p} uses unknown {kind.ToString().ToLowerInvariant()} variant '{choice.VariantId}'.", p);
                    continue;
                }

                if (!usedVariants.TryGetValue(kind, out var used))
                    usedVariants[kind] = used = new HashSet<string>();
                used.Add(variant.Id);
                cellCounts.TryGetValue(kind, out var count);
                cellCounts[kind] = count + 1;

                if (checkWallShape && categoryHasVariants && (variant.Category != category || choice.Rotation != rotation))
                    staleWalls++;
            }

            var wallSet = theme.GetSet(VisualKind.Wall);
            foreach (var pair in wallCategoriesWithoutVariants)
            {
                var hasDefault = wallSet != null && wallSet.FindVariant(wallSet.DefaultVariantId) != null;
                report.Add(hasDefault ? Warning : Error, Visual, ValidationCodes.WallCategoryWithoutVariants,
                    $"{pair.Value} wall cell(s) of category {pair.Key} have no matching variants" +
                    (hasDefault ? "; the set default is used." : " and the set has no default."));
            }

            if (staleWalls > 0)
                report.Add(Warning, Visual, ValidationCodes.StaleWallVisual,
                    $"{staleWalls} wall cell(s) have visuals that no longer match their neighbours. Run Regenerate Visuals.");

            foreach (var kind in new[] { VisualKind.Floor, VisualKind.Wall })
            {
                var set = theme.GetSet(kind);
                if (set == null || !usedVariants.TryGetValue(kind, out var used))
                    continue;

                var autoVariants = set.Variants.Count(v => v.Weight > 0 && v.Category != VisualCategory.Special &&
                                                           v.Category != VisualCategory.Snowdrift);
                if (autoVariants >= 2 && used.Count == 1 && cellCounts[kind] >= 20)
                    report.Add(Warning, Visual, ValidationCodes.UniformDistribution,
                        $"All {cellCounts[kind]} cells use the same {kind.ToString().ToLowerInvariant()} variant '{used.First()}'.");
            }
        }

        private static bool HasAutoVariant(VisualSet set, VisualCategory category) =>
            set.Variants.Any(v => v.Weight > 0 && v.Category == category);

        private static void ValidateObjectVisuals(LevelData level, VisualTheme theme, ValidationReport report)
        {
            var ids = new HashSet<string>();
            foreach (var entity in level.AllEntities())
            {
                ids.Add(entity.Id);
                if (!VisualKinds.TryGetForEntity(entity, out var kind, out var definition))
                    continue;

                var set = theme.GetSet(kind);
                if (set == null)
                    continue;

                var choice = VisualResolver.ResolveObject(level, entity);
                if (choice.IsEmpty)
                {
                    report.Add(Error, Visual, ValidationCodes.MissingVisual, $"'{entity.Id}' has no visual.", entity.Position, entity.Id);
                    continue;
                }

                var variant = set.FindVariant(choice.VariantId);
                if (variant == null)
                    report.Add(Error, Visual, ValidationCodes.UnknownVariant,
                        $"'{entity.Id}' uses unknown variant '{choice.VariantId}'.", entity.Position, entity.Id);
                else if (variant.Definition != null && variant.Definition != definition)
                    report.Add(Warning, Visual, ValidationCodes.VariantDefinitionMismatch,
                        $"'{entity.Id}' uses variant '{variant.Id}' made for '{variant.Definition.name}'.", entity.Position, entity.Id);
            }

            foreach (var entry in level.VisualData.ObjectOverrides)
                if (!ids.Contains(entry.EntityId))
                    report.Add(Warning, Visual, ValidationCodes.OverrideForMissingEntity,
                        $"Visual override for missing object '{entry.EntityId}'.", entityId: entry.EntityId);
        }

        // ---------------------------------------------------------------- Gameplay

        private static void ValidateGameplay(LevelData level, ValidationReport report)
        {
            var geometry = level.Geometry;
            var starts = level.PlayerStarts
                .Where(s => geometry.IsInside(s.Position) && geometry.GetCell(s.Position) == CellType.Floor)
                .ToList();

            if (starts.Count == 0)
                return;

            var results = starts.Select(s => KeyDoorSolver.Solve(geometry, level.Doors, level.Keys, s.Position)).ToList();
            var pathfinder = new GridPathfinder(geometry.Width, geometry.Height);
            var path = new List<GridPosition>();
            var exitReached = new bool[level.Exits.Count];

            for (var s = 0; s < starts.Count; s++)
            {
                var shortest = int.MaxValue;
                string nearestExit = null;

                for (var e = 0; e < level.Exits.Count; e++)
                {
                    var exit = level.Exits[e];
                    if (!results[s].IsReachable(exit.Position))
                        continue;

                    exitReached[e] = true;
                    if (pathfinder.TryFindPath(starts[s].Position, exit.Position, results[s].Passable, path) && path.Count - 1 < shortest)
                    {
                        shortest = path.Count - 1;
                        nearestExit = exit.Id;
                    }
                }

                if (nearestExit == null)
                    report.Add(Error, Gameplay, ValidationCodes.NoReachableExit,
                        $"No exit is reachable from '{starts[s].Id}'.", starts[s].Position, starts[s].Id);
                else
                    report.Add(Info, Gameplay, ValidationCodes.ShortestPath,
                        $"'{starts[s].Id}' -> '{nearestExit}': {shortest} steps.", starts[s].Position, starts[s].Id);
            }

            for (var e = 0; e < level.Exits.Count; e++)
                if (!exitReached[e])
                    report.Add(Error, Gameplay, ValidationCodes.ExitUnreachable,
                        $"Exit '{level.Exits[e].Id}' is unreachable from every start.", level.Exits[e].Position, level.Exits[e].Id);

            foreach (var key in level.Keys)
                ReportUnreachable(key, starts, results, ValidationCodes.KeyUnreachable, report);

            foreach (var entity in level.Weapons.Cast<LevelEntityData>()
                         .Concat(level.Medkits).Concat(level.MapFragments).Concat(level.ZombieSpawns))
                ReportUnreachable(entity, starts, results, ValidationCodes.ObjectUnreachable, report);

            ValidateDoorPassages(level, report);
            ValidatePatrolRoutes(level, pathfinder, path, report);
        }

        private static void ReportUnreachable(LevelEntityData entity, List<PlayerStartData> starts,
            List<ReachabilityResult> results, string code, ValidationReport report)
        {
            var from = new List<string>();
            for (var s = 0; s < starts.Count; s++)
                if (!results[s].IsReachable(entity.Position))
                    from.Add(starts[s].Id);

            if (from.Count > 0)
                report.Add(Warning, Gameplay, code,
                    $"'{entity.Id}' is unreachable from {string.Join(", ", from)}.", entity.Position, entity.Id);
        }

        private static void ValidateDoorPassages(LevelData level, ValidationReport report)
        {
            var geometry = level.Geometry;
            bool Open(GridPosition p) => geometry.IsInside(p) && geometry.GetCell(p) != CellType.Wall;

            foreach (var door in level.Doors)
            {
                var p = door.Position;
                if (!geometry.IsInside(p))
                    continue;

                var northSouth = Open(p.Neighbour(Direction.North)) && Open(p.Neighbour(Direction.South));
                var eastWest = Open(p.Neighbour(Direction.East)) && Open(p.Neighbour(Direction.West));
                if (!northSouth && !eastWest)
                    report.Add(Warning, Gameplay, ValidationCodes.DoorWithoutPassage,
                        $"Door '{door.Id}' at {p} does not connect two passable cells.", p, door.Id);
            }
        }

        /// <summary>Zombies do not open doors: closed doors block their routes (ТЗ §52).</summary>
        private static void ValidatePatrolRoutes(LevelData level, GridPathfinder pathfinder, List<GridPosition> path,
            ValidationReport report)
        {
            var geometry = level.Geometry;
            var openDoors = new HashSet<GridPosition>(level.Doors.Where(d => d.IsInitiallyOpen).Select(d => d.Position));
            var passable = new bool[geometry.CellCount];
            for (var i = 0; i < passable.Length; i++)
            {
                var p = geometry.ToPosition(i);
                var cell = geometry.GetCell(p);
                passable[i] = cell == CellType.Floor || (cell == CellType.Door && openDoors.Contains(p));
            }

            var zombiePassability = new CellMaskPassability(geometry.Width, geometry.Height, passable);
            // Duplicate ids are reported by ValidateIds; here the first patrol with an id wins.
            var patrols = new Dictionary<string, PatrolData>();
            foreach (var patrol in level.Patrols)
                if (!string.IsNullOrEmpty(patrol.Id) && !patrols.ContainsKey(patrol.Id))
                    patrols.Add(patrol.Id, patrol);

            foreach (var zombie in level.ZombieSpawns)
            {
                if (!zombie.HasPatrol || !patrols.TryGetValue(zombie.PatrolId, out var patrol) || patrol.Points.Count == 0)
                    continue;

                var route = new List<GridPosition> { zombie.Position };
                route.AddRange(patrol.Points);
                route.Add(patrol.Points[0]);

                for (var i = 0; i + 1 < route.Count; i++)
                {
                    if (pathfinder.TryFindPath(route[i], route[i + 1], zombiePassability, path))
                        continue;

                    report.Add(Warning, Gameplay, ValidationCodes.PatrolPointUnreachable,
                        $"Zombie '{zombie.Id}' cannot walk from {route[i]} to {route[i + 1]} on patrol '{patrol.Id}'.",
                        route[i + 1], zombie.Id);
                    break;
                }
            }
        }
    }
}
