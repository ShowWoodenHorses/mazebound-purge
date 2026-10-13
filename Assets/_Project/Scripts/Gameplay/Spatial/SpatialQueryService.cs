using System;
using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Visibility;
using Maze.Gameplay.Player;
using Maze.Gameplay.Visibility;
using Maze.Gameplay.Doors;
using UnityEngine;

namespace Maze.Gameplay.Spatial
{
    /// <summary>Something with a position in the level that queries can find (zombies, later other runtime objects).</summary>
    public interface ISpatialObject
    {
        string Id { get; }

        /// <summary>Centre in grid units (x = East, y = North).</summary>
        Vector2 Position { get; }

        /// <summary>Hit radius in cells.</summary>
        float Radius { get; }
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
        void GetObjectsInCell(GridPosition cell, List<ISpatialObject> results);

        /// <summary>Objects whose cell is within <paramref name="range"/> cells (square) of <paramref name="cell"/>.</summary>
        void GetObjectsAroundCell(GridPosition cell, int range, List<ISpatialObject> results);

        /// <summary>Objects whose circle overlaps the circle (<paramref name="center"/>, <paramref name="radius"/>).</summary>
        void QueryRadius(Vector2 center, float radius, List<ISpatialObject> results);

        /// <summary>First wall/closed door or object the segment hits (bullets: previous → current position).</summary>
        SegmentHit QuerySegment(Vector2 from, Vector2 to);

        SegmentHit QueryDirection(Vector2 origin, Vector2 direction, float distance);

        /// <summary>True when no wall or closed door lies between the points (objects ignored).</summary>
        bool IsClear(Vector2 from, Vector2 to);
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
                if (PlayerMovement.CellOf(item.Position) == cell)
                    results.Add(item);
        }

        public void GetObjectsAroundCell(GridPosition cell, int range, List<ISpatialObject> results)
        {
            results.Clear();
            foreach (var item in _objects)
            {
                var other = PlayerMovement.CellOf(item.Position);
                if (Math.Abs(other.X - cell.X) <= range && Math.Abs(other.Y - cell.Y) <= range)
                    results.Add(item);
            }
        }

        public void QueryRadius(Vector2 center, float radius, List<ISpatialObject> results)
        {
            results.Clear();
            foreach (var item in _objects)
            {
                var reach = radius + item.Radius;
                if ((item.Position - center).sqrMagnitude <= reach * reach)
                    results.Add(item);
            }
        }

        public SegmentHit QuerySegment(Vector2 from, Vector2 to)
        {
            var opacity = new LevelOpacity(_grid, _doors);
            var blocked = GridRaycast.Cast(from, to, opacity, out var wallT, out var wallCell);
            var delta = to - from;

            ISpatialObject nearest = null;
            var nearestT = blocked ? wallT : float.PositiveInfinity;
            foreach (var item in _objects)
            {
                if (item is IDamageable damageable && !damageable.IsAlive)
                    continue;
                if (SegmentCircle(from, delta, item.Position, item.Radius, out var t) && t <= nearestT && t <= 1f)
                {
                    nearest = item;
                    nearestT = t;
                }
            }

            if (nearest != null)
                return new SegmentHit(SegmentHitKind.Object, nearestT, from + delta * nearestT,
                    PlayerMovement.CellOf(nearest.Position), nearest);
            if (blocked)
                return new SegmentHit(SegmentHitKind.Blocker, wallT, from + delta * wallT, wallCell, null);
            return new SegmentHit(SegmentHitKind.None, 1f, to, PlayerMovement.CellOf(to), null);
        }

        public SegmentHit QueryDirection(Vector2 origin, Vector2 direction, float distance) =>
            QuerySegment(origin, origin + direction.normalized * distance);

        public bool IsClear(Vector2 from, Vector2 to) =>
            !GridRaycast.Cast(from, to, new LevelOpacity(_grid, _doors), out _, out _);

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
