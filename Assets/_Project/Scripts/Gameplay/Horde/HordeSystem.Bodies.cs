using Purge.Core.Monsters;
using Purge.Core.Navigation;
using UnityEngine;

namespace Purge.Gameplay.Horde
{
    /// <summary>
    /// Wide bodies (stage 8a, bosses 2×2 and 3×3): a body whose footprint covers the cells around its centre cell
    /// (<see cref="HordeKind.BodyClearance"/>) walks its own path field — over the cells where all of those are free
    /// (the horde's walkability, closed doors it breaks included, shrunk by the clearance), built with the others only
    /// while such a monster lives. The crowd keeps clear of it explicitly (the buckets look one cell around).
    /// </summary>
    public sealed partial class HordeSystem
    {
        /// <summary>Most wide bodies alive at once that the crowd keeps clear of (a boss and its kind).</summary>
        public const int MaxWideBodies = 8;

        private bool _wideKinds; // some kind has a wide body
        private bool[] _bodyWalkable; // a body of clearance 1 fits with its centre here
        private FlowField _bodyFlow;
        private bool _bodyFlowBuilt;
        private readonly int[] _wide = new int[MaxWideBodies]; // living wide bodies this tick
        private int _wideCount;

        /// <summary>The field of wide bodies (bosses); null while none lives.</summary>
        public FlowField BodyFlow => _bodyFlowBuilt ? _bodyFlow : null;

        private bool IsWide(int agent) => _kinds[_kind[agent]].BodyClearance > 0;

        private void InitializeBodies()
        {
            foreach (var kind in _kinds)
                if (kind.BodyClearance > 0)
                    _wideKinds = true;
            if (!_wideKinds) return;
            _bodyWalkable = new bool[_walkable.Length];
            _bodyFlow = new FlowField(_grid.Width, _grid.Height);
        }

        /// <summary>Living wide bodies of this tick (before the moves).</summary>
        private void CollectWide()
        {
            _wideCount = 0;
            if (!_wideKinds) return;
            for (var i = 0; i < _highWater && _wideCount < MaxWideBodies; i++)
                if (IsAlive(_states[i]) && IsWide(i))
                    _wide[_wideCount++] = i;
        }

        private bool AnyWideAlive()
        {
            if (!_wideKinds) return false;
            for (var i = 0; i < _highWater; i++)
                if (IsAlive(_states[i]) && IsWide(i))
                    return true;
            return false;
        }

        /// <summary>Rebuilds the wide bodies' field when one lives (with the others: the player's cell, doors, blockers).</summary>
        private void RebuildBodyFlow()
        {
            _bodyFlowBuilt = false;
            if (!AnyWideAlive()) return;
            // Bosses break doors (by rank): their closed doors count as floor, as in the door field.
            var source = UsesDoorFlow ? _doorWalkable : _walkable;
            Shrink(source, _bodyWalkable, 1);
            _bodyFlow.Build(_flowTarget, _bodyWalkable);
            _bodyFlowBuilt = true;
        }

        /// <summary><paramref name="target"/>[cell] = every cell within <paramref name="clearance"/> of it is in <paramref name="source"/>.</summary>
        private void Shrink(bool[] source, bool[] target, int clearance)
        {
            var width = _grid.Width;
            var height = _grid.Height;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var fits = x >= clearance && y >= clearance && x < width - clearance && y < height - clearance;
                for (var dy = -clearance; fits && dy <= clearance; dy++)
                {
                    var row = (y + dy) * width + x;
                    for (var dx = -clearance; dx <= clearance; dx++)
                        if (!source[row + dx])
                        {
                            fits = false;
                            break;
                        }
                }

                target[y * width + x] = fits;
            }
        }

        /// <summary>The field the monster in <paramref name="agent"/> chases along (wide bodies — theirs once built).</summary>
        private FlowField ChaseFlow(int agent, MonsterDoorUse doors)
        {
            if (_bodyFlowBuilt && IsWide(agent)) return _bodyFlow;
            return UsesDoorFlow && (doors == MonsterDoorUse.Open || doors == MonsterDoorUse.Break) ? _doorFlow : _flow;
        }

        /// <summary>
        /// A boss wave (stage 8a): the monster of <paramref name="kind"/> climbs out (its kind's emerge time) at the cell
        /// nearest <paramref name="center"/> its whole body fits on, and hunts the player at once. Its slot, or -1 (no free
        /// slot, nowhere to stand within <paramref name="searchRadius"/> cells).
        /// </summary>
        public int SpawnBoss(Vector2 center, int kind, int searchRadius = 8)
        {
            if (!IsActive || (uint)kind >= (uint)_kinds.Length) return -1;
            var radius = _kinds[kind].Stats.Radius;
            var cx = Mathf.RoundToInt(center.x);
            var cy = Mathf.RoundToInt(center.y);
            for (var ring = 0; ring <= searchRadius; ring++)
            for (var dy = -ring; dy <= ring; dy++)
            for (var dx = -ring; dx <= ring; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue; // the ring's edge only
                var x = cx + dx;
                var y = cy + dy;
                if (!IsWalkable(x, y)) continue;
                var position = new Vector2(x, y);
                if (!FootprintFree(position, radius)) continue;
                return Place(y * _grid.Width + x, true, MonsterPerception.Hunter, position, kind);
            }

            return -1;
        }

        /// <summary>
        /// The next cell of the wide bodies' field from <paramref name="cell"/>; a body pressed against a wall stands on a
        /// cell the field does not cover — then the neighbour nearest the player along it (-1 = none).
        /// </summary>
        private int BodyNext(int cell)
        {
            if (cell < 0) return -1;
            var next = _bodyFlow.NextAt(cell);
            if (next >= 0 || _bodyFlow.DistanceAt(cell) != FlowField.Unreachable) return next;
            var width = _grid.Width;
            int cx = cell % width, cy = cell / width;
            var best = -1;
            var bestDistance = (int)FlowField.Unreachable;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= width || y >= _grid.Height) continue;
                var distance = _bodyFlow.DistanceAt(y * width + x);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = y * width + x;
            }

            return best;
        }

        /// <summary>Push away from the wide bodies near (the buckets only look one cell around a monster).</summary>
        private Vector2 WideSeparation(int i, Vector2 position, float radius, float ownInverse, float separation)
        {
            var push = Vector2.zero;
            for (var w = 0; w < _wideCount; w++)
            {
                var other = _wide[w];
                if (other == i) continue;
                var minDistance = radius + _kinds[_kind[other]].Stats.Radius;
                var offset = position - _positions[other];
                var squared = offset.sqrMagnitude;
                if (squared >= minDistance * minDistance) continue;
                if (squared < 1e-8f) offset = new Vector2(Mathf.Cos(i * 2.39996f), Mathf.Sin(i * 2.39996f)) * 1e-3f;
                var inverse = _kinds[_kind[other]].InverseMass;
                var sum = ownInverse + inverse;
                var share = sum > 0f ? ownInverse / sum : 0f;
                var length = offset.magnitude;
                push += offset / length * ((minDistance - length) * share * separation);
            }

            return push;
        }

        /// <summary>A wide body overlaps the cell (a door is not closed on it).</summary>
        private bool WideBodyInCell(int x, int y)
        {
            for (var w = 0; w < _wideCount; w++)
            {
                var agent = _wide[w];
                if (!IsAlive(_states[agent])) continue;
                var reach = 0.5f + _kinds[_kind[agent]].Stats.Radius * 0.9f;
                var position = _positions[agent];
                if (Mathf.Abs(position.x - x) < reach && Mathf.Abs(position.y - y) < reach) return true;
            }

            return false;
        }
    }
}
