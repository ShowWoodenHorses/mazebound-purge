using System.Collections.Generic;
using System.Linq;
using Maze.Core.Definitions;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Lighting;
using Maze.Core.Visual;

namespace Maze.Core.Authoring
{
    /// <summary>
    /// Manual level editing operations (ТЗ §15): every operation keeps the level consistent —
    /// door cells and DoorData stay in sync, references follow renames, and visuals are updated locally
    /// without re-randomizing anything else. Editor/level-creation only.
    /// </summary>
    internal static class LevelEditing
    {
        // ------------------------------------------------------------ Geometry

        /// <summary>
        /// Paints a cell. Painting Door creates a DoorData; painting over a door removes it.
        /// The floor layer exists in every cell, so its manual override survives; a wall-layer override is
        /// dropped when the cell stops being a wall.
        /// </summary>
        public static bool SetCellType(LevelData level, GridPosition position, CellType type)
        {
            var geometry = level.Geometry;
            if (!geometry.IsInside(position))
                return false;

            var old = geometry.GetCell(position);
            if (old == type)
                return false;

            geometry.SetCell(position, type);

            if (old == CellType.Door)
                foreach (var door in level.Doors.Where(d => d.Position == position).ToList())
                    RemoveDoorData(level, door);

            if (type != CellType.Wall)
                level.VisualData.ClearCellOverride(position, CellLayer.Wall);
            if (type != CellType.Floor)
            {
                level.VisualData.ClearCellOverride(position, CellLayer.Decor);
                geometry.SetSurface(position, CellSurface.None); // only floor has a surface
            }

            VisualAssigner.ReassignAround(level, position);

            if (type == CellType.Door && level.Doors.All(d => d.Position != position))
            {
                var door = new DoorData(level.CreateUniqueId(DoorData.IdPrefix), position);
                level.MutableDoors.Add(door);
                VisualAssigner.AssignObject(level, door);
            }

            return true;
        }

        /// <summary>
        /// Ground of a Floor cell (e.g. a snowdrift). Its floor visual and auto decor are chosen again (a drift has its
        /// own floor variants and no auto decor). False when not a Floor cell or nothing changed.
        /// </summary>
        public static bool SetSurface(LevelData level, GridPosition position, CellSurface surface)
        {
            var geometry = level.Geometry;
            if (!geometry.IsInside(position) || geometry.GetCell(position) != CellType.Floor ||
                geometry.GetSurface(position) == surface)
                return false;

            geometry.SetSurface(position, surface);
            if (level.VisualData.HasCellAssignments(geometry.CellCount))
                level.VisualData.SetCellAssignment(CellLayer.Floor, geometry.ToIndex(position),
                    VisualAssigner.ChooseCell(level, position, CellLayer.Floor));
            VisualAssigner.AssignDecor(level, position);
            return true;
        }

        // ------------------------------------------------------------ Placement

        public static PlayerStartData AddPlayerStart(LevelData level, GridPosition position) =>
            Add(level, level.MutablePlayerStarts, new PlayerStartData(level.CreateUniqueId(PlayerStartData.IdPrefix), position));

        public static ExitData AddExit(LevelData level, GridPosition position) =>
            Add(level, level.MutableExits, new ExitData(level.CreateUniqueId(ExitData.IdPrefix), position));

        public static MedkitData AddMedkit(LevelData level, GridPosition position) =>
            Add(level, level.MutableMedkits, new MedkitData(level.CreateUniqueId(MedkitData.IdPrefix), position));

        public static ZombieSpawnData AddZombie(LevelData level, GridPosition position, ZombieDefinition definition, Direction facing) =>
            Add(level, level.MutableZombieSpawns,
                new ZombieSpawnData(level.CreateUniqueId(ZombieSpawnData.IdPrefix), position, definition, facing));

        public static WeaponPickupData AddWeapon(LevelData level, GridPosition position, WeaponDefinition definition) =>
            Add(level, level.MutableWeapons, new WeaponPickupData(level.CreateUniqueId(WeaponPickupData.IdPrefix), position, definition));

        public static MapFragmentData AddMapFragment(LevelData level, GridPosition position, GridRect region) =>
            Add(level, level.MutableMapFragments, new MapFragmentData(level.CreateUniqueId(MapFragmentData.IdPrefix), position, region));

        /// <summary>
        /// Adds a key; if <paramref name="door"/> is given, links it (one key = one door) and colours both.
        /// The key wins its cell: auto decor there is removed (decor placed by hand stays).
        /// </summary>
        public static KeyData AddKey(LevelData level, GridPosition position, DoorData door = null)
        {
            var key = Add(level, level.MutableKeys, new KeyData(level.CreateUniqueId(KeyData.IdPrefix), position));
            VisualAssigner.AssignDecor(level, position);
            if (door != null)
                LinkKey(level, door, key);
            return key;
        }

        private static T Add<T>(LevelData level, List<T> list, T entity) where T : LevelEntityData
        {
            list.Add(entity);
            VisualAssigner.AssignObject(level, entity);
            return entity;
        }

        // ------------------------------------------------------------ Remove / move / rename

        public static void Remove(LevelData level, LevelEntityData entity)
        {
            switch (entity)
            {
                case DoorData door:
                    // A door cell without a door is invalid: removing the door opens the passage.
                    level.Geometry.SetCell(door.Position, CellType.Floor);
                    RemoveDoorData(level, door);
                    VisualAssigner.ReassignAround(level, door.Position);
                    return;

                case KeyData key:
                    level.MutableKeys.Remove(key);
                    VisualAssigner.AssignDecor(level, key.Position);
                    foreach (var linked in level.Doors.Where(d => d.KeyId == key.Id).ToList())
                    {
                        linked.KeyId = null;
                        VisualAssigner.AssignObject(level, linked);
                    }
                    break;

                case ZombieSpawnData zombie:
                    level.MutableZombieSpawns.Remove(zombie);
                    if (zombie.HasPatrol && level.ZombieSpawns.All(z => z.PatrolId != zombie.PatrolId))
                        level.MutablePatrols.RemoveAll(p => p.Id == zombie.PatrolId);
                    break;

                case PlayerStartData start: level.MutablePlayerStarts.Remove(start); break;
                case ExitData exit: level.MutableExits.Remove(exit); break;
                case MedkitData medkit: level.MutableMedkits.Remove(medkit); break;
                case WeaponPickupData weapon: level.MutableWeapons.Remove(weapon); break;
                case MapFragmentData fragment: level.MutableMapFragments.Remove(fragment); break;
            }

            level.VisualData.RemoveObject(entity.Id);
        }

        /// <summary>
        /// Erase brush: the cell becomes empty floor — its objects (a door opens the passage), light sources, surface
        /// (snowdrift) and decor ("no decor here", so auto placement leaves it empty) are removed, a wall becomes floor.
        /// Patrol points and fragment regions passing through the cell stay. False when there was nothing to erase.
        /// </summary>
        public static bool ClearCell(LevelData level, GridPosition position)
        {
            if (!level.Geometry.IsInside(position))
                return false;

            var changed = false;
            foreach (var entity in level.AllEntities().Where(e => e.Position == position).ToList())
            {
                Remove(level, entity);
                changed = true;
            }

            if (level.MutableLights.RemoveAll(light => light.Cell == position) > 0)
                changed = true;

            if (level.Geometry.GetCell(position) == CellType.Wall)
                changed |= SetCellType(level, position, CellType.Floor);

            changed |= SetSurface(level, position, CellSurface.None);

            if (!VisualResolver.ResolveDecor(level, position).IsEmpty)
            {
                level.VisualData.SetCellOverride(position, CellLayer.Decor, VisualChoice.None);
                changed = true;
            }

            return changed;
        }

        /// <summary>Moves an object. A door takes its cell type with it (old cell becomes Floor).</summary>
        public static bool Move(LevelData level, LevelEntityData entity, GridPosition to)
        {
            var geometry = level.Geometry;
            if (!geometry.IsInside(to) || entity.Position == to)
                return false;

            if (entity is DoorData && geometry.GetCell(to) == CellType.Door)
                return false;

            var from = entity.Position;
            entity.Position = to;
            if (from != to)
                level.VisualData.ClearObjectPlacement(entity.Id); // Another cell, another surrounding: back to its centre.

            if (entity is DoorData)
            {
                geometry.SetCell(from, CellType.Floor);
                geometry.SetCell(to, CellType.Door);
                geometry.SetSurface(to, CellSurface.None);
                level.VisualData.ClearCellOverride(to, CellLayer.Wall);
                level.VisualData.ClearCellOverride(to, CellLayer.Decor);
                VisualAssigner.ReassignAround(level, from);
                VisualAssigner.ReassignAround(level, to);
            }

            if (entity is KeyData)
            {
                VisualAssigner.AssignDecor(level, from);
                VisualAssigner.AssignDecor(level, to);
            }

            VisualAssigner.UpdateOrientation(level, entity);
            return true;
        }

        /// <summary>Renames an object, updating door->key links and its visual design. False if the id is empty or taken.</summary>
        public static bool Rename(LevelData level, LevelEntityData entity, string newId)
        {
            if (!IsFreeId(level, newId) || entity.Id == newId)
                return false;

            var oldId = entity.Id;
            entity.Id = newId;
            if (entity is KeyData)
                foreach (var door in level.Doors.Where(d => d.KeyId == oldId))
                    door.KeyId = newId;

            level.VisualData.RenameObject(oldId, newId);
            return true;
        }

        public static bool RenamePatrol(LevelData level, PatrolData patrol, string newId)
        {
            if (!IsFreeId(level, newId) || patrol.Id == newId)
                return false;

            foreach (var zombie in level.ZombieSpawns.Where(z => z.PatrolId == patrol.Id))
                zombie.PatrolId = newId;

            patrol.Id = newId;
            return true;
        }

        public static bool IsFreeId(LevelData level, string id) =>
            !string.IsNullOrWhiteSpace(id) && level.AllIds().All(existing => existing != id);

        // ------------------------------------------------------------ Keys and doors

        /// <summary>
        /// Links <paramref name="key"/> to <paramref name="door"/> (null = door needs no key). One key opens one door:
        /// the key is unlinked from any other door. Door colour is re-picked, the key takes the door's colour.
        /// </summary>
        public static void LinkKey(LevelData level, DoorData door, KeyData key)
        {
            var previousKeyId = door.KeyId;

            if (key != null)
                foreach (var other in level.Doors.Where(d => d != door && d.KeyId == key.Id).ToList())
                {
                    other.KeyId = null;
                    VisualAssigner.AssignObject(level, other);
                }

            door.KeyId = key?.Id;
            VisualAssigner.AssignObject(level, door);

            if (key != null)
                VisualAssigner.AssignObject(level, key);

            var previousKey = level.Keys.FirstOrDefault(k => k.Id == previousKeyId);
            if (previousKey != null && previousKey != key)
                VisualAssigner.AssignObject(level, previousKey);
        }

        /// <summary>A frozen door starts closed: opening it initially thaws it.</summary>
        public static void SetDoorInitiallyOpen(DoorData door, bool open)
        {
            door.IsInitiallyOpen = open;
            if (open) door.IceHits = 0;
        }

        /// <summary>Frozen door: <paramref name="hits"/> melee hits break its ice (0 = not frozen; then it starts closed).</summary>
        public static void SetDoorIce(DoorData door, int hits)
        {
            door.IceHits = hits;
            if (door.IsFrozen) door.IsInitiallyOpen = false;
        }

        // ------------------------------------------------------------ Definitions

        public static void SetZombie(LevelData level, ZombieSpawnData zombie, ZombieDefinition definition, Direction facing)
        {
            var definitionChanged = zombie.Definition != definition;
            zombie.Definition = definition;
            zombie.Facing = facing;
            if (definitionChanged)
                VisualAssigner.AssignObject(level, zombie);
        }

        public static void SetWeaponDefinition(LevelData level, WeaponPickupData weapon, WeaponDefinition definition)
        {
            if (weapon.Definition == definition)
                return;

            weapon.Definition = definition;
            VisualAssigner.AssignObject(level, weapon);
        }

        // ------------------------------------------------------------ Patrols and map fragments

        /// <summary>Appends a patrol point, creating the zombie's patrol on first use.</summary>
        public static PatrolData AddPatrolPoint(LevelData level, ZombieSpawnData zombie, GridPosition point)
        {
            var patrol = level.Patrols.FirstOrDefault(p => zombie.HasPatrol && p.Id == zombie.PatrolId);
            if (patrol == null)
            {
                patrol = new PatrolData(level.CreateUniqueId(PatrolData.IdPrefix));
                level.MutablePatrols.Add(patrol);
                zombie.PatrolId = patrol.Id;
            }

            patrol.MutablePoints.Add(point);
            return patrol;
        }

        public static void RemoveLastPatrolPoint(LevelData level, ZombieSpawnData zombie)
        {
            var patrol = level.Patrols.FirstOrDefault(p => zombie.HasPatrol && p.Id == zombie.PatrolId);
            if (patrol != null && patrol.Points.Count > 0)
                patrol.MutablePoints.RemoveAt(patrol.Points.Count - 1);
        }

        /// <summary>Removes the zombie's patrol (and the patrol itself if no other zombie uses it).</summary>
        public static void ClearPatrol(LevelData level, ZombieSpawnData zombie)
        {
            if (!zombie.HasPatrol)
                return;

            var patrolId = zombie.PatrolId;
            zombie.PatrolId = null;
            if (level.ZombieSpawns.All(z => z.PatrolId != patrolId))
                level.MutablePatrols.RemoveAll(p => p.Id == patrolId);
        }

        public static void SetFragmentRegion(MapFragmentData fragment, GridRect region) => fragment.Region = region;

        // ------------------------------------------------------------ Visual overrides

        public static void SetCellOverride(LevelData level, GridPosition position, CellLayer layer, VisualChoice choice) =>
            level.VisualData.SetCellOverride(position, layer, choice);

        /// <summary>
        /// Moves / lifts / turns the decor of a floor cell (values clamped: shift ≤ half a cell, height
        /// <see cref="DecorPlacement.MinHeight"/>..<see cref="DecorPlacement.MaxHeight"/>). Auto placed decor becomes
        /// manual (an override with the same variant), so regeneration keeps it. Pickups put on this decor move, lift and
        /// turn with it (around the decor). False when the cell has no decor.
        /// </summary>
        public static bool SetDecorPlacement(LevelData level, GridPosition cell, UnityEngine.Vector2 offset, float height, float yaw)
        {
            var decor = VisualResolver.ResolveDecor(level, cell);
            if (decor.IsEmpty)
                return false;

            var before = DecorPose(level, cell, decor);
            var after = new Pose(offset, height, yaw);
            var group = new List<(LevelEntityData Entity, Pose Pose)>();
            foreach (var entity in ObjectsOnDecor(level, cell))
            {
                var pose = ObjectPose(level, entity);
                group.Add((entity, new Pose(Follow(pose.Offset, before, after), pose.Height + after.Height - before.Height,
                    pose.Yaw + after.Yaw - before.Yaw)));
            }

            Commit(level, cell, after, group);
            return true;
        }

        /// <summary>
        /// Shifts / lifts / turns the view of a pickup (key, medkit, weapon, map fragment) inside its cell; clamped like
        /// decor. Purely visual. A pickup put on decor carries the decor (and other pickups on it) along: same shift,
        /// same turn around the pickup; lifting it does not lift the decor. False for other objects.
        /// </summary>
        public static bool SetObjectPlacement(LevelData level, LevelEntityData entity, UnityEngine.Vector2 offset, float height, float yaw)
        {
            if (!VisualKinds.IsPlaceable(entity))
                return false;

            if (!IsOnDecor(level, entity))
            {
                level.VisualData.SetObjectPlacement(entity.Id, offset, height, yaw);
                return true;
            }

            var cell = entity.Position;
            var before = ObjectPose(level, entity);
            var after = new Pose(offset, height, yaw);
            var decor = DecorPose(level, cell, VisualResolver.ResolveDecor(level, cell));
            var group = new List<(LevelEntityData Entity, Pose Pose)> { (entity, after) };
            foreach (var other in ObjectsOnDecor(level, cell))
                if (other != entity)
                {
                    var pose = ObjectPose(level, other);
                    group.Add((other, new Pose(Follow(pose.Offset, before, after), pose.Height, pose.Yaw + after.Yaw - before.Yaw)));
                }

            var decorAfter = new Pose(Follow(decor.Offset, before, after), decor.Height, decor.Yaw + after.Yaw - before.Yaw);
            Commit(level, cell, decorAfter, group);
            return true;
        }

        /// <summary>
        /// "Put on decor": the pickup stands at the given pose (the inspector computes the height of the decor's top)
        /// and from now on moves and turns together with the decor of its cell. False for other objects or no decor.
        /// </summary>
        public static bool PutOnDecor(LevelData level, LevelEntityData entity, UnityEngine.Vector2 offset, float height, float yaw)
        {
            if (!VisualKinds.IsPlaceable(entity) || VisualResolver.ResolveDecor(level, entity.Position).IsEmpty)
                return false;

            level.VisualData.SetObjectPlacement(entity.Id, offset, height, yaw);
            level.VisualData.TryGetObjectPlacement(entity.Id, out var placement);
            placement.OnDecor = true;
            return true;
        }

        /// <summary>The pickup keeps its pose but no longer moves with the decor.</summary>
        public static bool DetachFromDecor(LevelData level, LevelEntityData entity)
        {
            if (!level.VisualData.TryGetObjectPlacement(entity.Id, out var placement) || !placement.OnDecor)
                return false;

            placement.OnDecor = false;
            return true;
        }

        /// <summary>Put on decor and the cell still has decor (a link to removed decor does nothing).</summary>
        public static bool IsOnDecor(LevelData level, LevelEntityData entity) =>
            VisualKinds.IsPlaceable(entity) &&
            level.VisualData.TryGetObjectPlacement(entity.Id, out var placement) && placement.OnDecor &&
            !VisualResolver.ResolveDecor(level, entity.Position).IsEmpty;

        /// <summary>Back to the cell centre on the floor (also detaches it from decor).</summary>
        public static bool ResetObjectPlacement(LevelData level, LevelEntityData entity) =>
            level.VisualData.ClearObjectPlacement(entity.Id);

        /// <summary>
        /// Back to the cell centre and the quarter turn of the choice; the decor stays manual. Pickups on it follow.
        /// </summary>
        public static bool ResetDecorPlacement(LevelData level, GridPosition cell) =>
            MoveWithDecor(level, cell, () => level.VisualData.ClearDecorPlacement(cell));

        /// <summary>Quarter turn of manual decor without a hand-set placement; pickups on it turn with it.</summary>
        public static void SetDecorRotation(LevelData level, GridPosition cell, int rotation)
        {
            var decor = VisualResolver.ResolveDecor(level, cell);
            if (!decor.IsEmpty)
                MoveWithDecor(level, cell,
                    () => level.VisualData.SetCellOverride(cell, CellLayer.Decor, new VisualChoice(decor.VariantId, rotation)));
        }

        // ------------------------------------------------------------ Pickups on decor

        /// <summary>Shift (x = East, y = North), height and turn (degrees clockwise from above) in a cell.</summary>
        private readonly struct Pose
        {
            public Pose(UnityEngine.Vector2 offset, float height, float yaw)
            {
                Offset = offset;
                Height = height;
                Yaw = yaw;
            }

            public UnityEngine.Vector2 Offset { get; }
            public float Height { get; }
            public float Yaw { get; }
        }

        private static Pose DecorPose(LevelData level, GridPosition cell, VisualChoice decor)
        {
            VisualResolver.ResolveDecorPose(level, cell, decor, out var offset, out var yaw);
            return new Pose(new UnityEngine.Vector2(offset.x, offset.z), offset.y, yaw);
        }

        private static Pose ObjectPose(LevelData level, LevelEntityData entity)
        {
            VisualResolver.ResolveObjectPose(level, entity, VisualResolver.ResolveObject(level, entity), out var offset, out var yaw);
            return new Pose(new UnityEngine.Vector2(offset.x, offset.z), offset.y, yaw);
        }

        private static List<LevelEntityData> ObjectsOnDecor(LevelData level, GridPosition cell) =>
            level.AllEntities().Where(e => e.Position == cell && IsOnDecor(level, e)).ToList();

        /// <summary>
        /// Where a point rigidly attached to a moved "leader" ends up: it keeps its place relative to the leader,
        /// turned by the leader's turn (around the leader).
        /// </summary>
        private static UnityEngine.Vector2 Follow(UnityEngine.Vector2 point, Pose leaderBefore, Pose leaderAfter)
        {
            var radians = (leaderAfter.Yaw - leaderBefore.Yaw) * UnityEngine.Mathf.Deg2Rad;
            var cos = UnityEngine.Mathf.Cos(radians);
            var sin = UnityEngine.Mathf.Sin(radians);
            var local = point - leaderBefore.Offset;
            // Clockwise from above (North = +y): North turns to East.
            return leaderAfter.Offset + new UnityEngine.Vector2(local.x * cos + local.y * sin, -local.x * sin + local.y * cos);
        }

        /// <summary>
        /// Writes the decor and its pickups as one group, shifted together if needed so that every one stays inside
        /// the cell (clamping them one by one would pull them apart).
        /// </summary>
        private static void Commit(LevelData level, GridPosition cell, Pose decor, List<(LevelEntityData Entity, Pose Pose)> objects)
        {
            var offsets = new List<UnityEngine.Vector2> { decor.Offset };
            offsets.AddRange(objects.Select(o => o.Pose.Offset));
            var correction = new UnityEngine.Vector2(GroupCorrection(offsets.Select(o => o.x)), GroupCorrection(offsets.Select(o => o.y)));

            var choice = VisualResolver.ResolveDecor(level, cell, out var source);
            if (source != VisualSource.Override)
                level.VisualData.SetCellOverride(cell, CellLayer.Decor, choice);
            level.VisualData.SetDecorPlacement(cell, decor.Offset + correction, decor.Height, decor.Yaw);
            foreach (var (entity, pose) in objects)
                level.VisualData.SetObjectPlacement(entity.Id, pose.Offset + correction, pose.Height, pose.Yaw);
        }

        /// <summary>Smallest shift along one axis that brings all values into ±<see cref="DecorPlacement.MaxOffset"/>.</summary>
        private static float GroupCorrection(IEnumerable<float> values)
        {
            var max = DecorPlacement.MaxOffset;
            var low = float.MinValue;
            var high = float.MaxValue;
            foreach (var value in values)
            {
                low = UnityEngine.Mathf.Max(low, -max - value);
                high = UnityEngine.Mathf.Min(high, max - value);
            }

            return low > high ? (low + high) * 0.5f : UnityEngine.Mathf.Clamp(0f, low, high);
        }

        /// <summary>Applies a change of the decor's pose and moves the pickups on it the same way (each clamped).</summary>
        private static bool MoveWithDecor(LevelData level, GridPosition cell, System.Func<bool> change)
        {
            var decor = VisualResolver.ResolveDecor(level, cell);
            var objects = ObjectsOnDecor(level, cell).Select(e => (Entity: e, Pose: ObjectPose(level, e))).ToList();
            var before = DecorPose(level, cell, decor);
            var changed = change();
            var after = DecorPose(level, cell, VisualResolver.ResolveDecor(level, cell));

            foreach (var (entity, pose) in objects)
                level.VisualData.SetObjectPlacement(entity.Id, Follow(pose.Offset, before, after),
                    pose.Height + after.Height - before.Height, pose.Yaw + after.Yaw - before.Yaw);
            return changed;
        }

        private static bool MoveWithDecor(LevelData level, GridPosition cell, System.Action change) =>
            MoveWithDecor(level, cell, () =>
            {
                change();
                return true;
            });

        /// <summary>Recreates the auto placed decor (density, max auto height, VisualSeed); manual decor stays.</summary>
        public static void PlaceDecor(LevelData level) => VisualAssigner.AssignAllDecor(level);

        public static void ClearCellOverride(LevelData level, GridPosition position, CellLayer layer) =>
            level.VisualData.ClearCellOverride(position, layer);

        public static void SetObjectOverride(LevelData level, LevelEntityData entity, VisualChoice choice)
        {
            level.VisualData.SetObjectOverride(entity.Id, choice);
            RefreshPairedKey(level, entity);
        }

        public static void ClearObjectOverride(LevelData level, LevelEntityData entity)
        {
            level.VisualData.ClearObjectOverride(entity.Id);
            RefreshPairedKey(level, entity);
        }

        /// <summary>A key follows its door's colour, including the designer's manual door choice.</summary>
        private static void RefreshPairedKey(LevelData level, LevelEntityData entity)
        {
            if (!(entity is DoorData door) || !door.RequiresKey)
                return;

            var key = level.Keys.FirstOrDefault(k => k.Id == door.KeyId);
            if (key != null)
                VisualAssigner.AssignObject(level, key);
        }

        // ------------------------------------------------------------ Helpers

        private static void RemoveDoorData(LevelData level, DoorData door)
        {
            level.MutableDoors.Remove(door);
            level.VisualData.RemoveObject(door.Id);

            var key = level.Keys.FirstOrDefault(k => door.RequiresKey && k.Id == door.KeyId);
            if (key != null)
                VisualAssigner.AssignObject(level, key);
        }
    
        // ------------------------------------------------------------ Lights

        /// <summary>
        /// Light from auto placement (generated lights are replaced, hand-placed ones kept); same as the light part
        /// of Regenerate Visuals.
        /// </summary>
        public static void PlaceLights(LevelData level) => LightPlacer.PlaceAll(level, keepManual: true);

        /// <summary>
        /// Hand-placed light on a floor cell: shifted towards a wall next to it, if any; values of the theme's first
        /// light preset, otherwise defaults.
        /// </summary>
        public static LightSourceData AddLight(LevelData level, GridPosition cell)
        {
            var offset = UnityEngine.Vector2.zero;
            foreach (var side in DirectionExtensions.All)
            {
                var neighbour = cell + side.ToOffset();
                if (level.Geometry.IsInside(neighbour) && level.Geometry.GetCell(neighbour) != CellType.Wall)
                    continue;
                var step = side.ToOffset();
                offset = new UnityEngine.Vector2(step.X, step.Y) * LightPlacer.WallOffset;
                break;
            }

            var presets = level.VisualTheme != null ? level.VisualTheme.Lighting.LightPresets : null;
            var preset = presets != null && presets.Count > 0 && presets[0] != null ? presets[0] : new LightPreset();
            var light = new LightSourceData(level.CreateUniqueId(LightSourceData.IdPrefix), cell, offset, preset.Color,
                preset.Radius, preset.Intensity, preset.Flicker, isGenerated: false);
            light.Visual = VisualAssigner.ChooseLight(level, light.Id);
            level.MutableLights.Add(light);
            return light;
        }

        /// <summary>Fixture of a light (variant of the theme's Light set; empty = the set default). Makes it hand-placed.</summary>
        public static void SetLightVisual(LightSourceData light, string variantId)
        {
            light.Visual = string.IsNullOrEmpty(variantId) ? VisualChoice.None : new VisualChoice(variantId);
            light.IsGenerated = false;
        }

        public static void RemoveLight(LevelData level, LightSourceData light) => level.MutableLights.Remove(light);

        /// <summary>Any edit makes a light hand-placed, so auto placement keeps it.</summary>
        public static void SetLight(LightSourceData light, UnityEngine.Color color, float radius, float intensity, float flicker)
        {
            light.Color = color;
            light.Radius = UnityEngine.Mathf.Max(0.1f, radius);
            light.Intensity = UnityEngine.Mathf.Max(0f, intensity);
            light.Flicker = UnityEngine.Mathf.Clamp01(flicker);
            light.IsGenerated = false;
        }

        /// <summary>Moves a light to a point in grid units: it belongs to the cell the point is in.</summary>
        public static void MoveLight(LightSourceData light, UnityEngine.Vector2 point)
        {
            var cell = new GridPosition(UnityEngine.Mathf.RoundToInt(point.x), UnityEngine.Mathf.RoundToInt(point.y));
            light.Cell = cell;
            light.Offset = new UnityEngine.Vector2(
                UnityEngine.Mathf.Clamp(point.x - cell.X, -0.5f, 0.5f), UnityEngine.Mathf.Clamp(point.y - cell.Y, -0.5f, 0.5f));
            light.IsGenerated = false;
        }
    }
}
