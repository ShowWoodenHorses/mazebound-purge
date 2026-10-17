using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Core.Horde;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Visibility;
using UnityEngine;
using P = Purge.Presentation.Visual.MonsterAnimatorParameters;

namespace Purge.Presentation.Visual.Horde
{
    /// <summary>
    /// Views of the horde's officers, elite and bosses (ТЗ: Animator, not baked; stage 5c): kinds with a
    /// <see cref="MonsterViewDefinition"/> (<see cref="StressTestDefinition.ViewOf"/>). Their instances are made while
    /// loading — <see cref="PoolPerKind"/> per kind — and handed to the slots that show such a monster; a slot gives its
    /// view back when the corpse is gone. Every late tick: position and facing from the arrays, hidden outside the
    /// player's view (the simulation goes on, ТЗ §57), and the Animator's parameters (<see cref="MonsterAnimatorParameters"/>)
    /// from the gameplay's state: pace and playback by speed, attack, hit (not while casting), stun, roar
    /// (<see cref="HordeAwareness.Alert"/>), cast (<see cref="MonsterAbilities"/>, the clip fitted so it lets go when
    /// the warning ends), death by the side the killing push came from. Allocation-free per frame.
    /// </summary>
    public sealed class MonsterViewPresenter : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        public const int PoolPerKind = 12;
        private const float MinPlayback = 0.5f;
        private const float MaxPlayback = 2f;
        private const float HitRestart = 0.35f;
        private const float SidePush = 1.5f;

        /// <summary>
        /// Farthest the hips may stand from the monster's point on the ground, m: an attack's lunge or a hit's step back
        /// left in a clip (the officers' copies take most of it out, not all) would carry the body into the player.
        /// Idle sway stays under it.
        /// </summary>
        public const float MaxHipsDrift = 0.25f;

        private static readonly int PaceId = Animator.StringToHash(P.Pace);
        private static readonly int PlaybackId = Animator.StringToHash(P.Playback);
        private static readonly int AttackId = Animator.StringToHash(P.Attack);
        private static readonly int AttackIndexId = Animator.StringToHash(P.AttackIndex);
        private static readonly int AttackSpeedId = Animator.StringToHash(P.AttackSpeed);
        private static readonly int HitId = Animator.StringToHash(P.Hit);
        private static readonly int HitIndexId = Animator.StringToHash(P.HitIndex);
        private static readonly int StunnedId = Animator.StringToHash(P.Stunned);
        private static readonly int AlertId = Animator.StringToHash(P.Alert);
        private static readonly int CastId = Animator.StringToHash(P.Cast);
        private static readonly int CastSpeedId = Animator.StringToHash(P.CastSpeed);
        private static readonly int DeadId = Animator.StringToHash(P.Dead);
        private static readonly int DeathIndexId = Animator.StringToHash(P.DeathIndex);

        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly MonsterAbilities _abilities;
        private readonly VisibilitySystem _visibility;
        private readonly LevelViewRoot _root;
        private readonly BlobShadows _shadows;

        private KindViews[] _kinds; // per horde kind (null = not an Animator kind)
        private View[] _slots;      // per slot: its view (null = none)
        private int[] _slotGeneration;
        private float[] _lastAttack;
        private float[] _lastHit;
        private bool _warnedPool;

        public MonsterViewPresenter(LevelData level, HordeSystem horde, MonsterAbilities abilities, VisibilitySystem visibility,
            LevelViewRoot root, BlobShadows shadows)
        {
            _level = level;
            _horde = horde;
            _abilities = abilities;
            _visibility = visibility;
            _root = root;
            _shadows = shadows;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public bool IsActive => _slots != null;

        /// <summary>Views shown now (metrics).</summary>
        public int ShownCount { get; private set; }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var settings = _level.StressTest;
            if (!_horde.IsActive || settings == null) return UniTask.CompletedTask;

            var kinds = _horde.KindList;
            _kinds = new KindViews[kinds.Count];
            var any = false;
            for (var k = 0; k < kinds.Count; k++)
            {
                var definition = settings.ViewOf(kinds[k].Definition);
                if (definition == null) continue;
                if (definition.Prefab == null)
                {
                    GameLog.Warning(LogChannel.Gameplay, $"Monster view '{definition.name}' has no prefab (Build Monsters) — not drawn.");
                    continue;
                }

                var pool = new KindViews(definition);
                for (var n = 0; n < Mathf.Min(PoolPerKind, _horde.Capacity); n++)
                    pool.Free.Push(Create(pool, n));
                _kinds[k] = pool;
                any = true;
            }

            if (!any)
            {
                _kinds = null;
                return UniTask.CompletedTask;
            }

            var capacity = _horde.Capacity;
            _slots = new View[capacity];
            _slotGeneration = new int[capacity];
            _lastAttack = new float[capacity];
            _lastHit = new float[capacity];
            for (var i = 0; i < capacity; i++) _slotGeneration[i] = -1;
            return UniTask.CompletedTask;
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (_kinds == null) return;
            foreach (var kind in _kinds)
                if (kind != null && kind.Free.Count > 0)
                    objects.Add(kind.Free.Peek().GameObject);
        }

        public void LateTick(float deltaTime)
        {
            if (!IsActive) return;
            var positions = _horde.Positions;
            var facing = _horde.Facing;
            var states = _horde.States;
            var kinds = _horde.Kinds;
            var generations = _horde.Generations;
            var high = _horde.HighWater;
            var shown = 0;
            for (var i = 0; i < _slots.Length; i++)
            {
                var view = _slots[i];
                if (i >= high || states[i] == HordeAgentState.Inactive || _slotGeneration[i] != generations[i])
                {
                    if (view != null) Release(i);
                    view = null;
                }

                if (i >= high || states[i] == HordeAgentState.Inactive) continue;
                var pool = _kinds[kinds[i]];
                if (pool == null) continue;
                if (view == null)
                {
                    view = Acquire(i, pool);
                    if (view == null) continue;
                }

                var p = positions[i];
                var visible = !_visibility.HasResult ||
                              _visibility.IsVisible(new GridPosition(Mathf.FloorToInt(p.x + 0.5f), Mathf.FloorToInt(p.y + 0.5f)));
                if (view.GameObject.activeSelf != visible) view.GameObject.SetActive(visible);
                view.Transform.SetPositionAndRotation(new Vector3(p.x, 0f, p.y), Quaternion.Euler(0f, facing[i] * Mathf.Rad2Deg, 0f));
                if (!visible) continue;
                shown++;
                Animate(i, view, pool.Definition, states[i]);
                // The pose of this frame is evaluated already: the body stays where the gameplay put it (deaths fall away).
                if (states[i] != HordeAgentState.Dying) KeepInPlace(view);
            }

            ShownCount = shown;
        }

        public void Dispose()
        {
            if (_kinds == null) return;
            foreach (var kind in _kinds)
            {
                if (kind == null) continue;
                foreach (var view in kind.All)
                    UnityObjects.Destroy(view.GameObject);
            }

            _kinds = null;
            _slots = null;
        }

        private void Animate(int i, View view, MonsterViewDefinition definition, HordeAgentState state)
        {
            var animator = view.Animator;
            if (animator == null) return;
            var time = _horde.Time;
            if (state == HordeAgentState.Dying)
            {
                if (!view.Dead)
                {
                    view.Dead = true;
                    animator.SetInteger(DeathIndexId, DeathSide(_horde.PushVelocities[i], _horde.Facing[i], i + _horde.Generations[i]));
                    animator.SetBool(DeadId, true);
                    animator.SetBool(CastId, false);
                    animator.SetBool(StunnedId, false);
                    animator.SetBool(AlertId, false);
                }

                return;
            }

            var stats = _horde.StatsOf(i);
            var pace = state == HordeAgentState.Moving ? _horde.Paces[i] : HordePace.Still;
            animator.SetInteger(PaceId, (int)pace);
            var speedFactor = _horde.SpeedFactors[i];
            var playback = pace == HordePace.Run
                ? definition.RunSpeed > 0f ? _horde.Speeds[i] * speedFactor / definition.RunSpeed : 1f
                : definition.WalkSpeed > 0f ? stats.WanderSpeed * speedFactor / definition.WalkSpeed : 1f;
            animator.SetFloat(PlaybackId, Mathf.Clamp(playback, MinPlayback, MaxPlayback));
            animator.SetBool(StunnedId, _horde.Stunned[i]);
            animator.SetBool(AlertId, _horde.Perception.AwarenessOf(i) == HordeAwareness.Alert);

            var casting = false;
            if (_abilities != null && _abilities.IsActive)
            {
                var phase = _abilities.Phases[i];
                casting = phase != AbilityPhase.None;
                if (casting)
                {
                    var ability = _abilities.CastOf(i);
                    // Lets go exactly when the warning ends; after that — the rest of the clip at its own pace.
                    var speed = phase == AbilityPhase.Warning && ability != null
                        ? definition.CastMoment * definition.CastLength / Mathf.Max(0.05f, ability.Warning)
                        : 1f;
                    animator.SetFloat(CastSpeedId, speed);
                }
            }

            animator.SetBool(CastId, casting);

            var attackStart = _horde.AttackStart[i];
            if (state == HordeAgentState.Attacking && attackStart != _lastAttack[i] && attackStart <= time)
            {
                _lastAttack[i] = attackStart;
                animator.SetFloat(AttackSpeedId, 1f / Mathf.Max(0.1f, stats.AttackInterval));
                animator.SetInteger(AttackIndexId, (i + (int)(attackStart * 3f)) % P.AttackClips);
                animator.SetTrigger(AttackId);
            }

            var hitTime = _horde.HitTime[i];
            if (!casting && hitTime > _lastHit[i] + HitRestart && time - hitTime < 0.1f)
            {
                _lastHit[i] = hitTime;
                animator.SetInteger(HitIndexId, (i + (int)(hitTime * 7f)) % P.HitClips);
                animator.SetTrigger(HitId);
            }
        }

        private static void KeepInPlace(View view)
        {
            if (view.Hips == null) return;
            var offset = view.Hips.position - view.Transform.position;
            offset.y = 0f;
            var distance = offset.magnitude;
            if (distance > MaxHipsDrift) view.Hips.position -= offset * (1f - MaxHipsDrift / distance);
        }

        private View Acquire(int slot, KindViews pool)
        {
            if (pool.Free.Count == 0)
            {
                if (!_warnedPool)
                {
                    _warnedPool = true;
                    GameLog.Warning(LogChannel.Gameplay,
                        $"Monster views: more than {PoolPerKind} '{pool.Definition.name}' at once — the rest are not drawn.");
                }

                return null;
            }

            var view = pool.Free.Pop();
            view.Dead = false;
            _slots[slot] = view;
            _slotGeneration[slot] = _horde.Generations[slot];
            _lastAttack[slot] = _horde.AttackStart[slot];
            _lastHit[slot] = _horde.HitTime[slot];
            view.GameObject.SetActive(true);
            if (view.Animator != null)
            {
                // A fresh monster: from the default state, every parameter back to its default.
                view.Animator.Rebind();
                view.Animator.Update(0f);
            }

            return view;
        }

        private void Release(int slot)
        {
            var view = _slots[slot];
            _slots[slot] = null;
            _slotGeneration[slot] = -1;
            if (view == null) return;
            view.GameObject.SetActive(false);
            view.Pool.Free.Push(view);
        }

        private View Create(KindViews pool, int number)
        {
            var definition = pool.Definition;
            var parent = _root != null ? _root.transform : null;
            var instance = UnityEngine.Object.Instantiate(definition.Prefab, parent);
            instance.name = $"{definition.Prefab.name} #{number + 1}";
            _shadows?.Attach(instance);
            // The shadow is a child of the scaled model: keep it the size of the others'.
            var shadow = instance.transform.Find("BlobShadow");
            var scale = instance.transform.localScale.x;
            if (shadow != null && scale > 1e-3f) shadow.localScale /= scale;
            instance.SetActive(false);
            var view = new View(instance, pool);
            pool.All.Add(view);
            return view;
        }

        /// <summary>Death clip side by the killing push: pushed backwards = hit in the front (0), back 1, left 2, right 3.</summary>
        private static int DeathSide(Vector2 push, float facing, int seed)
        {
            if (push.sqrMagnitude < SidePush * SidePush) return (seed & int.MaxValue) % 4;
            var from = -push;
            var forward = new Vector2(Mathf.Sin(facing), Mathf.Cos(facing));
            var right = new Vector2(forward.y, -forward.x);
            var f = Vector2.Dot(from, forward);
            var r = Vector2.Dot(from, right);
            return Mathf.Abs(f) >= Mathf.Abs(r) ? f >= 0f ? 0 : 1 : r >= 0f ? 3 : 2;
        }

        private sealed class KindViews
        {
            public readonly MonsterViewDefinition Definition;
            public readonly Stack<View> Free = new Stack<View>();
            public readonly List<View> All = new List<View>();

            public KindViews(MonsterViewDefinition definition) => Definition = definition;
        }

        private sealed class View
        {
            public readonly GameObject GameObject;
            public readonly Transform Transform;
            public readonly Animator Animator;
            public readonly Transform Hips;
            public KindViews Pool;
            public bool Dead;

            public View(GameObject gameObject, KindViews pool)
            {
                GameObject = gameObject;
                Transform = gameObject.transform;
                Animator = gameObject.GetComponentInChildren<Animator>(true);
                Hips = Animator != null && Animator.isHuman ? Animator.GetBoneTransform(HumanBodyBones.Hips) : null;
                Pool = pool;
            }
        }
    }
}
