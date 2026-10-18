using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Grid;
using Purge.Core.Monsters;
using Purge.Core.Visual;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using UnityEngine;

namespace Purge.Presentation.Visual.Horde
{
    /// <summary>
    /// Pack effects of the monsters themselves (stage Э3), display only, for the rank-and-file and the Animator kinds
    /// alike: dust and earth around one climbing out of the ground (<see cref="CombatVisualDefinition.Emerge"/>, emitted —
    /// a wave climbs out at once), a puff where a corpse starts sinking (<see cref="CombatVisualDefinition.CorpsePuff"/>;
    /// the renderers sink it), a flash when a rage begins (<see cref="CombatVisualDefinition.RageFlash"/>; the glow is
    /// the renderers'), and what falls onto an area strike at the end of its warning (<see cref="AreaStrikeAbility.Fall"/>:
    /// a followed effect lobbed from the caster's side, landing as the strike goes off). Only in cells the player sees;
    /// allocation-free per frame.
    /// </summary>
    public sealed class MonsterEffectsView : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        private const float DustHeight = 0.05f;
        private const float PuffHeight = 0.25f;
        private const float RageHeight = 0.9f;
        /// <summary>Where a fall starts: this high, and this far towards its caster (straight down would not read from above).</summary>
        private const float FallHeight = 5f;
        private const float FallBack = 2.5f;

        private readonly HordeSystem _horde;
        private readonly VisibilitySystem _visibility;
        private readonly PlayerSystem _player;
        private readonly CombatVisualDefinition _visual;
        private readonly PackEffectsPlayer _packEffects;
        private readonly PackEmitters _emitters;
        private readonly MonsterAbilities _abilities;

        private PackEmitters.Emitter _emerge, _puff;
        private int[] _generation;   // per slot: generation the per-slot state belongs to
        private bool[] _puffed;      // the corpse's puff was shown (or skipped out of view)
        private bool[] _raged;       // the rage's flash was shown
        private int[] _fall;         // handle of the falling look (-1 = none)
        private float[] _fallCast;   // start of the warning the fall belongs to
        private float _lastTime;

        public MonsterEffectsView(HordeSystem horde, VisibilitySystem visibility, PlayerSystem player,
            CombatVisualDefinition visual = null, PackEffectsPlayer packEffects = null, PackEmitters emitters = null,
            MonsterAbilities abilities = null)
        {
            _horde = horde;
            _visibility = visibility;
            _player = player;
            _visual = visual;
            _packEffects = packEffects;
            _emitters = emitters;
            _abilities = abilities;
        }

        // After the horde and its abilities (stage SpawnZombies, registered before the views).
        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool IsActive => _generation != null;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_horde.IsActive || _visual == null) return UniTask.CompletedTask;
            _emerge = _emitters?.Get(_visual.Emerge);
            _puff = _emitters?.Get(_visual.CorpsePuff);
            var capacity = _horde.Capacity;
            _generation = new int[capacity];
            _puffed = new bool[capacity];
            _raged = new bool[capacity];
            _fall = new int[capacity];
            _fallCast = new float[capacity];
            for (var i = 0; i < capacity; i++)
            {
                _generation[i] = -1;
                _fall[i] = -1;
                _fallCast[i] = -1f;
            }

            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            if (!IsActive) return;
            var time = _horde.Time;
            var step = Mathf.Max(0f, time - _lastTime);
            _lastTime = time;
            var positions = _horde.Positions;
            var states = _horde.States;
            var stateStart = _horde.StateStart;
            var generations = _horde.Generations;
            var floorHeight = _player.Floor * GridPosition.FloorHeight;
            for (var i = 0; i < _horde.HighWater; i++)
            {
                if (_generation[i] != generations[i])
                {
                    // A new monster in the slot.
                    _generation[i] = generations[i];
                    _puffed[i] = _raged[i] = false;
                    EndFall(i);
                }

                var state = states[i];
                if (state == HordeAgentState.Inactive)
                {
                    EndFall(i);
                    continue;
                }

                var at = positions[i];
                switch (state)
                {
                    case HordeAgentState.Emerging:
                        if (_emerge != null && step > 0f && Visible(at))
                            _emerge.Emit(new Vector3(at.x, floorHeight + DustHeight, at.y), _visual.EmergeRate * step);
                        break;
                    case HordeAgentState.Dying:
                        if (!_puffed[i] && _visual.CorpseSink(time - stateStart[i], _horde.StatsOf(i).CorpseTime) > 0f)
                        {
                            _puffed[i] = true;
                            if (_puff != null && Visible(at))
                                _puff.Emit(new Vector3(at.x, floorHeight + PuffHeight, at.y), _visual.CorpsePuffCount);
                        }

                        EndFall(i);
                        continue;
                }

                if (!_raged[i] && _horde.IsEnraged(i))
                {
                    _raged[i] = true;
                    _packEffects?.Play(_visual.RageFlash, new Vector3(at.x, floorHeight + RageHeight, at.y), Quaternion.identity);
                }

                TickFall(i, floorHeight, time);
            }
        }

        /// <summary>An area strike's look falling from the caster's side onto the spot during the end of the warning.</summary>
        private void TickFall(int i, float floorHeight, float time)
        {
            if (_packEffects == null || _abilities == null || !_abilities.IsActive) return;
            var area = _abilities.Phases[i] == AbilityPhase.Warning ? _abilities.CastOf(i) as AreaStrikeAbility : null;
            if (area == null || !CombatVisualDefinition.IsSet(area.Fall))
            {
                EndFall(i);
                return;
            }

            var castStart = _abilities.PhaseStart[i];
            var fallTime = Mathf.Min(area.FallTime, area.Warning);
            var since = time - castStart - (area.Warning - fallTime);
            if (since < 0f) return;

            var aim = _abilities.Aims[i];
            var back = _abilities.Origins[i] - aim;
            back = back.sqrMagnitude > 1e-4f ? back.normalized * FallBack : Vector2.up * FallBack;
            var top = new Vector3(aim.x + back.x, floorHeight + FallHeight, aim.y + back.y);
            var ground = new Vector3(aim.x, floorHeight + 0.1f, aim.y);
            var rotation = Quaternion.LookRotation(ground - top);
            if (_fall[i] < 0 && _fallCast[i] != castStart)
            {
                _fall[i] = _packEffects.Follow(area.Fall, top, rotation);
                if (_fall[i] >= 0) _fallCast[i] = castStart;
            }

            if (_fall[i] < 0) return;
            var t = Mathf.Clamp01(since / Mathf.Max(0.05f, fallTime));
            _packEffects.Move(_fall[i], Vector3.Lerp(top, ground, t * t), rotation); // speeding up as it falls
        }

        private void EndFall(int i)
        {
            if (_fall[i] < 0) return;
            _packEffects?.Unfollow(_fall[i]);
            _fall[i] = -1;
        }

        private bool Visible(Vector2 at) => _visibility.IsVisible(PlayerMovement.CellOf(at));

        public void Dispose()
        {
            _generation = null;
        }
    }
}
