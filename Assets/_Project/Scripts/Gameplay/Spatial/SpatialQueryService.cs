using System;
using System.Collections.Generic;
using Purge.Core.Grid;
using Purge.Core.Visibility;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using Purge.Gameplay.Doors;
using UnityEngine;

namespace Purge.Gameplay.Spatial
{
    /// <summary>Something with a position in the level that queries can find (zombies, later other runtime objects).</summary>
    public interface ISpatialObject
    {
        string Id { get; }

        /// <summary>Centre in grid units (x = East, y = North).</summary>
        Vector2 Position { get; }

        /// <summary>Hit radius in cells.</summary>
        float Radius { get; }

        /// <summary>Floor the object is on: queries find objects of their own floor only.</summary>
        int Floor => 0;
    }

    /// <summary>A target of attacks and bullets.</summary>
    public interface IDamageable : ISpatialObject
    {
        bool IsAlive { get; }

        /// <param name="direction">Unit direction the hit came from (attacker → target).</param>
        void ApplyDamage(float amount, Vector2 direction);
    }

    public enum SegmentHitKind
    {
        None = 0,
        /// <summary>Wall, closed door or the edge of the grid.</summary>
        Blocker = 1,
        Object = 2,
    }

    public readonly struct SegmentHit
    {
        public SegmentHit(SegmentHitKind kind, float t, Vector2 point, GridPosition cell, ISpatialObject target)
        {
            Kind = kind;
            T = t;
            Point = point;
            Cell = cell;
            Object = target;
        }

        public SegmentHitKind Kind { get; }

        /// <summary>Fraction 0..1 along the segment.</summary>
        public float T { get; }

        public Vector2 Point { get; }

        /// <summary>The blocking cell (Blocker) or the object's cell.</summary>
        public GridPosition Cell { get; }

        public ISpatialObject Object { get; }
    }

    /// <summary>
    /// Physics-free spatial queries over the grid and registered objects (ТЗ §78, §81). Used by melee, bullets,
    /// AI and interactions. Results are written into caller-owned lists; nothing allocates per query.
    /// Objects are few (a handful of zombies), so queries scan them directly; walls use the grid.
    /// </summary>
    public interface ISpatialQueryService
    {
        /// <summary>Objects in the cell (its floor included).</summary>
        void GetObjectsInCell(GridPosition cell, List<ISpatialObject> results);

        /// <summary>Objects of the cell's floor whose cell is within <paramref name="range"/> cells (square) of it.</summary>
        void GetObjectsAroundCell(GridPosition cell, int range, List<ISpatialObject> results);

        /// <summary>Objects of <paramref name="floor"/> whose circle overlaps the circle (<paramref name="center"/>, <paramref name="radius"/>).</summary>
        void QueryRadius(Vector2 center, float radius, List<ISpatialObject> results, int floor = 0);

        /// <summary>
        /// First bullet blocker (cell flag <see cref="CellFlags.BlocksBullets"/>, closed door) or object of the floor the
        /// segment hits (bullets: previous → current position).
        /// </summary>
        SegmentHit QuerySegment(Vector2 from, Vector2 to, int floor = 0);

        SegmentHit QueryDirection(Vector2 origin, Vector2 direction, float distance, int floor = 0);

        /// <summary>
        /// True when no cell with any of <paramref name="blockers"/> (sight by default; bullets for shots and melee) and no
        /// closed door lies between the points on the floor (objects ignored).
        /// </summary>
        bool IsClear(Vector2 from, Vector2 to, CellFlags blockers = CellFlags.BlocksSight, int floor = 0);
    }

    public sealed class SpatialQueryService : ISpatialQueryService
    {
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;
        private readonly List<ISpatialObject> _objects = new List<ISpatialObject>();

        public SpatialQueryService(LevelGrid grid, DoorSystem doors)
        {
            _grid = grid;
            _doors = doors;
        }

        public IReadOnlyList<ISpatialObject> Objects => _objects;

        public void Add(ISpatialObject spatialObject)
        {
            if (spatialObject == null) throw new ArgumentNullException(nameof(spatialObject));
            if (!_objects.Contains(spatialObject))
                _objects.Add(spatialObject);
        }

        public bool Remove(ISpatialObject spatialObject) => _objects.Remove(spatialObject);

        public void GetObjectsInCell(GridPosition cell, List<ISpatialObject> results)
        {
            results.Clear();
            foreach (var item in _objects)
                if (PlayerMovement.CellOf(item.Position, item.Floor) == cell)
                    results.Add(item);
        }

        public void GetObjectsAroundCell(GridPosition cell, int range, List<ISpatialObject> results)
        {
            results.Clear();
            foreach (var item in _objects)
            {
                var other = PlayerMovement.CellOf(item.Position, item.Floor);
                if (other.Floor == cell.Floor && Math.Abs(other.X - cell.X) <= range && Math.Abs(other.Y - cell.Y) <= range)
                    results.Add(item);
            }
        }

        public void QueryRadius(Vector2 center, float radius, List<ISpatialObject> results, int floor = 0)
        {
            results.Clear();
            foreach (var item in _objects)
            {
                if (item.Floor != floor) continue;
                var reach = radius + item.Radius;
                if ((item.Position - center).sqrMagnitude <= reach * reach)
                    results.Add(item);
            }
        }

        public SegmentHit QuerySegment(Vector2 from, Vector2 to, int floor = 0)
        {
            var opacity = LevelOpacity.ForBullets(_grid, _doors);
            var blocked = GridRaycast.Cast(from, to, opacity, out var wallT, out var wallCell, floor);
            var delta = to - from;

            ISpatialObject nearest = null;
            var nearestT = blocked ? wallT : float.PositiveInfinity;
            foreach (var item in _objects)
            {
                if (item.Floor != floor || item is IDamageable damageable && !damageable.IsAlive)
                    continue;
                if (SegmentCircle(from, delta, item.Position, item.Radius, out var t) && t <= nearestT && t <= 1f)
                {
                    nearest = item;
                    nearestT = t;
                }
            }

            if (nearest != null)
                return new SegmentHit(SegmentHitKind.Object, nearestT, from + delta * nearestT,
                    PlayerMovement.CellOf(nearest.Position, floor), nearest);
            if (blocked)
                return new SegmentHit(SegmentHitKind.Blocker, wallT, from + delta * wallT, wallCell, null);
            return new SegmentHit(SegmentHitKind.None, 1f, to, PlayerMovement.CellOf(to, floor), null);
        }

        public SegmentHit QueryDirection(Vector2 origin, Vector2 direction, float distance, int floor = 0) =>
            QuerySegment(origin, origin + direction.normalized * distance, floor);

        public bool IsClear(Vector2 from, Vector2 to, CellFlags blockers = CellFlags.BlocksSight, int floor = 0) =>
            !GridRaycast.Cast(from, to, new LevelOpacity(_grid, _doors, blockers), out _, out _, floor);

        /// <summary>Smallest t in [0, 1] where the segment from + delta·t touches the circle; inside counts as t = 0.</summary>
        private static bool SegmentCircle(Vector2 from, Vector2 delta, Vector2 center, float radius, out float t)
        {
            var offset = from - center;
            var c = offset.sqrMagnitude - radius * radius;
            if (c <= 0f)
            {
                t = 0f;
                return true;
            }

            var a = delta.sqrMagnitude;
            t = 0f;
            if (a <= 1e-8f)
                return false;

            var b = Vector2.Dot(offset, delta);
            var discriminant = b * b - a * c;
            if (b > 0f || discriminant < 0f)
                return false;

            t = (-b - Mathf.Sqrt(discriminant)) / a;
            return t <= 1f;
        }
    }
}
