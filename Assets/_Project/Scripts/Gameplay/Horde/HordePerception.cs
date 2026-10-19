using System;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Monsters;
using Purge.Core.Navigation;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Player;
using Purge.Gameplay.Sound;
using Purge.Gameplay.Visibility;
using UnityEngine;

namespace Purge.Gameplay.Horde
{
    /// <summary>What a horde monster knows about the player.</summary>
    public enum HordeAwareness : byte
    {
        /// <summary>Knows nothing: walks with its group (бродяга).</summary>
        Wander = 0,

        /// <summary>Heard a sound: runs to where it came from; nobody there — wanders again.</summary>
        Investigate = 1,

        /// <summary>Has just noticed the player: stands and roars (<see cref="HordePerception.AlertTime"/>), then chases.</summary>
        Alert = 2,

        /// <summary>Chases the player to the end (never loses them).</summary>
        Chase = 3,
    }

    /// <summary>How a monster moves this tick (the look picks idle / walk / run by it).</summary>
    public enum HordePace : byte
    {
        Still = 0,
        Walk = 1,
        Run = 2,
    }

    /// <summary>
    /// Perception of the horde (stage 5b, ТЗ: бродяга / зовущий / охотник; шум — геймплейная механика). Per slot, in flat
    /// arrays: <see cref="HordeAwareness"/>. Hunters chase from the start. Wanderers walk with one of
    /// <see cref="HordeDefinition.WanderGroups"/> groups to its random point (a shared flow field per group, a new
    /// point every <see cref="HordeDefinition.WanderRetarget"/> s; stage 6 replaces it with the wave's route).
    /// <list type="bullet">
    /// <item><b>Hearing.</b> Every sound of <see cref="SoundEventBus"/> spreads along the passages (<see cref="NoiseField"/>:
    /// walls stop it, a closed door × <see cref="ClosedDoorTransmission"/>; radius × difficulty). Sounds are gathered and
    /// handled every <see cref="ProcessInterval"/> s (bursts from one place merge into one field); the last
    /// <see cref="NoiseSlots"/> fields are kept. A wanderer that hears one runs to its source along the field; there —
    /// wanders again.</item>
    /// <item><b>Sight.</b> A circle, no cone: the monster's cell is in the player's view and the player is within its
    /// <see cref="MonsterStats.SightRange"/> (× difficulty).</item>
    /// <item><b>Noticing</b> (sight, a hit, a call): <see cref="HordeAwareness.Alert"/>, then
    /// <see cref="HordeAwareness.Chase"/> for good. A caller that noticed the player itself calls on the way
    /// (<see cref="SoundType.Call"/>, its <see cref="MonsterStats.CallRadius"/> × difficulty, along the passages):
    /// every monster that hears the call chases; the call does not pass on (no chain).</item>
    /// </list>
    /// Owned by <see cref="HordeSystem"/>; allocation-free per tick.
    /// </summary>
    public sealed class HordePerception : IDisposable
    {
        public const float AlertTime = 0.5f;
        public const float ProcessInterval = 0.2f;
        public const float ClosedDoorTransmission = 0.5f;
        public const int NoiseSlots = 8;
        public const int MaxNoiseRadius = 24;

        private const int MaxPending = 32;
        private const float MergeDistance = 2f;

        /// <summary>A wanderer this close to its group's point (path cost, half-steps) stands.</summary>
        private const int ArrivedCost = 4;

        private readonly HordeSystem _horde;
        private readonly LevelGrid _grid;
        private readonly DoorSystem _doors;
        private readonly SoundEventBus _sounds;
        private readonly VisibilitySystem _visibility;
        private readonly PlayerSystem _player;

        private HordeAwareness[] _awareness;
        private float[] _since;
        private bool[] _caller;
        private bool[] _willCall;
        private sbyte[] _noise;
        private int[] _noiseVersion;

        private NoiseField[] _fields;
        private SoundType[] _fieldType;
        private float[] _fieldTime;
        private PendingSound[] _pending;
        private int _pendingCount;
        private float _nextProcess;

        private WanderGroup[] _groups;
        private DeterministicRandom _random;
        private float _hearing = 1f;
        private float _sight = 1f;
        private float _call = 1f;
        private bool _subscribed;

        public HordePerception(HordeSystem horde, LevelGrid grid, DoorSystem doors, SoundEventBus sounds,
            VisibilitySystem visibility, PlayerSystem player)
        {
            _horde = horde;
            _grid = grid;
            _doors = doors;
            _sounds = sounds;
            _visibility = visibility;
            _player = player;
        }

        public ReadOnlySpan<HordeAwareness> Awareness => _awareness;

        /// <summary>Every monster hunts, whatever its data says (sandbox switch); on — the living ones chase at once.</summary>
        public bool AllHunters
        {
            get => _allHunters;
            set
            {
                _allHunters = value;
                if (!value || _awareness == null) return;
                for (var i = 0; i < _horde.HighWater; i++)
                    if (_awareness[i] != HordeAwareness.Chase)
                        SetAwareness(i, HordeAwareness.Chase);
            }
        }

        private bool _allHunters;

        /// <summary>Sound fields built so far (tests, metrics).</summary>
        public int FieldBuilds { get; private set; }

        /// <summary>Calls made by callers (tests, metrics).</summary>
        public int Calls { get; private set; }

        public HordeAwareness AwarenessOf(int agent) =>
            _awareness != null && (uint)agent < (uint)_awareness.Length ? _awareness[agent] : HordeAwareness.Chase;

        internal void Initialize(int capacity, HordeDefinition settings, DifficultyDefinition difficulty)
        {
            _awareness = new HordeAwareness[capacity];
            _since = new float[capacity];
            _caller = new bool[capacity];
            _willCall = new bool[capacity];
            _noise = new sbyte[capacity];
            _noiseVersion = new int[capacity];
            _fields = new NoiseField[NoiseSlots];
            _fieldType = new SoundType[NoiseSlots];
            _fieldTime = new float[NoiseSlots];
            for (var s = 0; s < NoiseSlots; s++)
            {
                _fields[s] = new NoiseField(MaxNoiseRadius);
                _fieldTime[s] = float.MinValue;
            }

            _pending = new PendingSound[MaxPending];
            _random = new DeterministicRandom((int)StableHash.Of(_horde.LevelName + "/perception"));

            _hearing = difficulty != null ? difficulty.Hearing : 1f;
            _sight = difficulty != null ? difficulty.Sight : 1f;
            _call = difficulty != null ? difficulty.Call : 1f;

            // Wander fields only when some kind can wander (the stress test's monsters all hunt).
            var wanders = false;
            foreach (var kind in _horde.KindList)
                wanders |= kind.Definition != null && kind.Definition.Perception != MonsterPerception.Hunter;
            if (wanders) CreateGroups(settings);

            if (!_subscribed && _sounds != null)
            {
                _sounds.Emitted += OnSound;
                _subscribed = true;
            }
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _sounds.Emitted -= OnSound;
                _subscribed = false;
            }
        }

        /// <param name="perception">What the new monster is (the sandbox may override its kind's).</param>
        internal void OnSpawn(int agent, MonsterPerception perception, float time)
        {
            _caller[agent] = perception == MonsterPerception.Caller;
            _willCall[agent] = false;
            _noise[agent] = -1;
            var hunts = _allHunters || perception == MonsterPerception.Hunter;
            if (!hunts && _groups == null) CreateGroups(_horde.Horde);
            _awareness[agent] = hunts ? HordeAwareness.Chase : HordeAwareness.Wander;
            _since[agent] = time;
        }

        /// <summary>A hit: the monster knows where the pain came from.</summary>
        internal void OnHit(int agent, float time) => Notice(agent, time, true);

        internal void OnDoorChanged()
        {
            if (_groups == null) return;
            foreach (var group in _groups) group.Dirty = true;
        }

        /// <summary>Alert → chase (and the call), sounds and sight every <see cref="ProcessInterval"/>, wander groups.</summary>
        internal void Tick(float time)
        {
            var count = _horde.HighWater;
            var states = _horde.States;
            for (var i = 0; i < count; i++)
            {
                if (_awareness[i] != HordeAwareness.Alert || !HordeSystem.IsAlive(states[i])) continue;
                if (time - _since[i] < AlertTime) continue;
                SetAwareness(i, HordeAwareness.Chase, time);
                if (_willCall[i])
                {
                    _willCall[i] = false;
                    Call(i);
                }
            }

            if (_groups != null) TickGroups(time);

            if (time < _nextProcess) return;
            _nextProcess = time + ProcessInterval;
            ProcessSounds(time);
            Look(time);
        }

        /// <summary>
        /// Where an unaware monster goes: <paramref name="direction"/> (unit, or zero) and its pace. Wanderers — along their
        /// group's field (still at the point); investigators — along the sound's field (at the source — wander again).
        /// </summary>
        internal void Steer(int agent, Vector2 position, float time, out Vector2 direction, out HordePace pace)
        {
            direction = Vector2.zero;
            pace = HordePace.Still;
            var x = Mathf.FloorToInt(position.x + 0.5f);
            var y = Mathf.FloorToInt(position.y + 0.5f);

            if (_awareness[agent] == HordeAwareness.Investigate)
            {
                var slot = _noise[agent];
                var field = slot >= 0 ? _fields[slot] : null;
                if (field != null && field.Version == _noiseVersion[agent] && field.TryStepToward(x, y, out var nx, out var ny))
                {
                    direction = Toward(position, nx, ny);
                    pace = HordePace.Run;
                    return;
                }

                // At the source (or the field is gone): nobody there.
                SetAwareness(agent, HordeAwareness.Wander, time);
            }

            if (_awareness[agent] != HordeAwareness.Wander || _groups == null) return;
            var flow = _groups[agent % _groups.Length].Flow;
            if (!flow.HasResult || !flow.IsInside(x, y)) return;
            var index = y * _grid.Width + x;
            if (flow.DistanceAt(index) <= ArrivedCost) return;
            var next = flow.NextAt(index);
            if (next < 0) return;
            direction = Toward(position, next % _grid.Width, next / _grid.Width);
            pace = HordePace.Walk;
        }

        private static Vector2 Toward(Vector2 position, int x, int y)
        {
            var offset = new Vector2(x, y) - position;
            var length = offset.magnitude;
            return length > 1e-4f ? offset / length : Vector2.zero;
        }

        private void Notice(int agent, float time, bool self)
        {
            var awareness = _awareness[agent];
            if (awareness == HordeAwareness.Alert || awareness == HordeAwareness.Chase) return;
            SetAwareness(agent, HordeAwareness.Alert, time);
            _willCall[agent] = self && _caller[agent];
        }

        private void SetAwareness(int agent, HordeAwareness awareness, float time = 0f)
        {
            _awareness[agent] = awareness;
            _since[agent] = time;
            if (awareness != HordeAwareness.Investigate) _noise[agent] = -1;
        }

        private void Call(int agent)
        {
            Calls++;
            var radius = _horde.StatsOf(agent).CallRadius * _call;
            var position = _horde.Positions[agent];
            if (_sounds != null) _sounds.Emit(SoundType.Call, position, radius, _player.Floor);
            else Enqueue(SoundType.Call, position, radius);
        }

        private void OnSound(SoundEvent sound)
        {
            if (_awareness == null || sound.Floor != _player.Floor) return;
            // The call carries its own radius (× difficulty already); everything else × hearing.
            Enqueue(sound.Type, sound.Position, sound.Type == SoundType.Call ? sound.Radius : sound.Radius * _hearing);
        }

        private void Enqueue(SoundType type, Vector2 position, float radius)
        {
            if (radius <= 0f) return;
            for (var p = 0; p < _pendingCount; p++)
            {
                ref var pending = ref _pending[p];
                if (pending.Type != type || (pending.Position - position).sqrMagnitude > MergeDistance * MergeDistance) continue;
                pending.Position = position; // a burst from one place: the latest spot, the loudest radius
                pending.Radius = Mathf.Max(pending.Radius, radius);
                return;
            }

            if (_pendingCount < MaxPending)
                _pending[_pendingCount++] = new PendingSound { Type = type, Position = position, Radius = radius };
        }

        private void ProcessSounds(float time)
        {
            for (var p = 0; p < _pendingCount; p++)
            {
                var sound = _pending[p];
                var slot = PickSlot(sound, time);
                var field = _fields[slot];
                var sx = Mathf.FloorToInt(sound.Position.x + 0.5f);
                var sy = Mathf.FloorToInt(sound.Position.y + 0.5f);
                field.Build(_grid.Width, _grid.Height, sx, sy, _player.Floor, sound.Radius,
                    new Acoustics(_grid, _doors, _player.Floor));
                FieldBuilds++;
                _fieldType[slot] = sound.Type;
                _fieldTime[slot] = time;
                Hear(slot, sound.Type == SoundType.Call, time);
            }

            _pendingCount = 0;
        }

        /// <summary>A field of the same sound from the same place is rebuilt; otherwise the oldest goes.</summary>
        private int PickSlot(PendingSound sound, float time)
        {
            var oldest = 0;
            for (var s = 0; s < NoiseSlots; s++)
            {
                var field = _fields[s];
                if (field.HasResult && _fieldType[s] == sound.Type &&
                    Mathf.Abs(field.SourceX - sound.Position.x) <= MergeDistance &&
                    Mathf.Abs(field.SourceY - sound.Position.y) <= MergeDistance)
                    return s;
                if (_fieldTime[s] < _fieldTime[oldest]) oldest = s;
            }

            return oldest;
        }

        private void Hear(int slot, bool call, float time)
        {
            var field = _fields[slot];
            var positions = _horde.Positions;
            var states = _horde.States;
            for (var i = 0; i < _horde.HighWater; i++)
            {
                var awareness = _awareness[i];
                if (awareness == HordeAwareness.Alert || awareness == HordeAwareness.Chase) continue;
                if (!HordeSystem.IsAlive(states[i])) continue;
                var p = positions[i];
                if (!field.IsHeardAt(Mathf.FloorToInt(p.x + 0.5f), Mathf.FloorToInt(p.y + 0.5f))) continue;

                if (call)
                {
                    Notice(i, time, false);
                    continue;
                }

                // The newest sound wins.
                _awareness[i] = HordeAwareness.Investigate;
                _since[i] = time;
                _noise[i] = (sbyte)slot;
                _noiseVersion[i] = field.Version;
            }
        }

        private void Look(float time)
        {
            if (_visibility == null || !_visibility.HasResult || !_player.IsSpawned) return;
            var positions = _horde.Positions;
            var states = _horde.States;
            var player = _player.Position;
            var floor = _player.Floor;
            for (var i = 0; i < _horde.HighWater; i++)
            {
                var awareness = _awareness[i];
                if (awareness == HordeAwareness.Alert || awareness == HordeAwareness.Chase) continue;
                if (!HordeSystem.IsAlive(states[i])) continue;
                var p = positions[i];
                var range = _horde.StatsOf(i).SightRange * _sight;
                if ((p - player).sqrMagnitude > range * range) continue;
                if (!_visibility.IsVisible(new GridPosition(Mathf.FloorToInt(p.x + 0.5f), Mathf.FloorToInt(p.y + 0.5f), floor)))
                    continue;
                Notice(i, time, true);
            }
        }

        private void CreateGroups(HordeDefinition settings)
        {
            if (settings == null) return;
            _groups = new WanderGroup[settings.WanderGroups];
            var retarget = settings.WanderRetarget;
            for (var g = 0; g < _groups.Length; g++)
                _groups[g] = new WanderGroup
                {
                    Flow = new FlowField(_grid.Width, _grid.Height),
                    Retarget = retarget,
                    // The first points at once; later ones spread out so the groups do not turn together.
                    NextRetarget = 0f,
                    Offset = retarget * g / _groups.Length,
                };
        }

        private void TickGroups(float time)
        {
            // At most one field per tick: a rebuild of a large level costs a few ms.
            for (var g = 0; g < _groups.Length; g++)
            {
                var group = _groups[g];
                if (time >= group.NextRetarget)
                {
                    var cell = _horde.RandomReachableCell(_random);
                    if (cell >= 0) group.Target = new GridPosition(cell % _grid.Width, cell / _grid.Width, _player.Floor);
                    group.NextRetarget = (group.NextRetarget <= 0f ? time + group.Offset : time) + group.Retarget;
                    group.Dirty = true;
                }

                if (!group.Dirty) continue;
                group.Dirty = false;
                _horde.BuildFlow(group.Flow, group.Target);
                return;
            }
        }

        /// <summary>Sound through cells: what blocks sight stops it (walls, tall obstacles; bushes and crates do not), a closed door muffles.</summary>
        private readonly struct Acoustics : IGridAcoustics
        {
            private readonly LevelGrid _grid;
            private readonly DoorSystem _doors;
            private readonly int _floor;

            public Acoustics(LevelGrid grid, DoorSystem doors, int floor)
            {
                _grid = grid;
                _doors = doors;
                _floor = floor;
            }

            public float Transmission(int x, int y)
            {
                var cell = new GridPosition(x, y, _floor);
                if (_grid.GetCellOrWall(cell) == CellType.Door)
                    return _doors.IsOpenAt(cell) ? 1f : ClosedDoorTransmission;
                return _grid.Has(cell, CellFlags.BlocksSight) ? 0f : 1f;
            }
        }

        private struct PendingSound
        {
            public SoundType Type;
            public Vector2 Position;
            public float Radius;
        }

        private sealed class WanderGroup
        {
            public FlowField Flow;
            public GridPosition Target;
            public float NextRetarget;
            public float Retarget;
            public float Offset;
            public bool Dirty;
        }
    }
}
