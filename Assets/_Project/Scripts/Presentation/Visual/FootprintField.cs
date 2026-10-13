using System.Collections.Generic;
using Maze.Core.Visual;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>A print on the floor (grid units: x = East, y = North).</summary>
    public struct Footprint
    {
        public Vector2 Position;

        /// <summary>Where the walker was going, normalized: the print's toes point there.</summary>
        public Vector2 Direction;

        public bool LeftFoot;
        public int Walker;
        public float Age;

        /// <summary>0..1 by distance to its walker and by age; a print at 0 is gone.</summary>
        public float Alpha;

        public bool Alive;
    }

    /// <summary>
    /// Footprints of the level (display only, no allocations after creation): walkers report their positions, every
    /// <see cref="FootprintKind.StepLength"/> walked leaves a print to the left or right of the path in turn. Prints fade
    /// as their walker moves away and with age; at most <see cref="Capacity"/> live at once (the oldest is reused).
    /// A jump longer than <see cref="TeleportDistance"/> leaves no print.
    /// </summary>
    public sealed class FootprintField
    {
        public const float TeleportDistance = 1f;

        /// <summary>Share of the lifetime after which a print starts to fade with age.</summary>
        private const float AgeFadeStart = 0.6f;

        private readonly Footprint[] _prints;
        private readonly List<Walker> _walkers = new List<Walker>();
        private int _next;

        public FootprintField(int capacity)
        {
            _prints = new Footprint[Mathf.Max(capacity, 1)];
        }

        public int Capacity => _prints.Length;

        /// <summary>All slots; only <see cref="Footprint.Alive"/> ones are prints.</summary>
        public Footprint[] Prints => _prints;

        public int AliveCount { get; private set; }

        /// <summary>A new walker leaving prints of <paramref name="kind"/>; returns its id.</summary>
        public int AddWalker(FootprintKind kind)
        {
            _walkers.Add(new Walker { Kind = kind });
            return _walkers.Count - 1;
        }

        public FootprintKind KindOf(int walker) => _walkers[walker].Kind;

        /// <summary>
        /// The walker is at <paramref name="position"/> now. Returns the slot of the print it left, or -1. The first call
        /// only places the walker; its first print comes after half a step.
        /// </summary>
        public int Move(int walker, Vector2 position)
        {
            var state = _walkers[walker];
            if (!state.Started)
            {
                state.Started = true;
                state.Last = position;
                state.Walked = state.Kind.StepLength * 0.5f;
                _walkers[walker] = state;
                return -1;
            }

            var delta = position - state.Last;
            var distance = delta.magnitude;
            state.Last = position;
            var printed = -1;

            if (distance > TeleportDistance)
            {
                state.Walked = state.Kind.StepLength * 0.5f;
            }
            else if (distance > 1e-5f)
            {
                state.Direction = delta / distance;
                state.Walked += distance;
                var step = Mathf.Max(state.Kind.StepLength, 0.05f);
                if (state.Walked >= step)
                {
                    state.Walked = Mathf.Min(state.Walked - step, step);
                    state.LeftFoot = !state.LeftFoot;
                    var left = new Vector2(-state.Direction.y, state.Direction.x);
                    var side = left * (state.LeftFoot ? state.Kind.Spacing : -state.Kind.Spacing);
                    printed = Add(new Footprint
                    {
                        Position = position + side,
                        Direction = state.Direction,
                        LeftFoot = state.LeftFoot,
                        Walker = walker,
                        Alpha = 1f,
                        Alive = true,
                    });
                }
            }

            _walkers[walker] = state;
            return printed;
        }

        /// <summary>Ages the prints and fades them by distance to their walker (its last known position) and age.</summary>
        public void Update(float deltaTime)
        {
            if (AliveCount == 0) return;

            for (var i = 0; i < _prints.Length; i++)
            {
                ref var print = ref _prints[i];
                if (!print.Alive) continue;

                var walker = _walkers[print.Walker];
                var kind = walker.Kind;
                print.Age += deltaTime;

                var distance = (walker.Last - print.Position).magnitude;
                var byDistance = 1f - Smooth((distance - kind.FadeFrom) / Mathf.Max(kind.FadeTo - kind.FadeFrom, 0.01f));
                var lifetime = Mathf.Max(kind.Lifetime, 0.01f);
                var byAge = 1f - Smooth((print.Age - lifetime * AgeFadeStart) / (lifetime * (1f - AgeFadeStart)));
                print.Alpha = byDistance * byAge;

                if (print.Alpha <= 0f)
                {
                    print.Alive = false;
                    AliveCount--;
                }
            }
        }

        public void Clear()
        {
            for (var i = 0; i < _prints.Length; i++)
                _prints[i] = default;
            AliveCount = 0;
            _next = 0;
            _walkers.Clear();
        }

        private int Add(Footprint print)
        {
            var slot = _next;
            _next = (_next + 1) % _prints.Length;
            if (!_prints[slot].Alive) AliveCount++;
            _prints[slot] = print;
            return slot;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private struct Walker
        {
            public FootprintKind Kind;
            public bool Started;
            public Vector2 Last;
            public Vector2 Direction;
            public float Walked;
            public bool LeftFoot;
        }
    }
}
