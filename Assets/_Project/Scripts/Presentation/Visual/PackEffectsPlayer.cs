using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Visual;
using Purge.Gameplay.Drops;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Visibility;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// One-shot effects of Art/Effects (pack effects, stage Э) that content names by reference: muzzle flashes of weapons
    /// (<see cref="WeaponEffectsModule"/>), explosions (<see cref="IExplosionLook"/>), and the defaults of
    /// <see cref="CombatVisualDefinition"/> for content without its own. Display only.
    /// <para>While loading, every effect the level can show is loaded and pooled: the defaults, the weapons on the level,
    /// in the drop pool and the base weapon, the throwables of the pool and the slot. A reference not loaded then is not
    /// shown (a warning once) — nothing is loaded or created for the first time during play. Each kind has
    /// <see cref="PoolSize"/> instances; when all are showing, the oldest is restarted in the new place.</para>
    /// Effects start only in cells the player sees. An effect with a <see cref="CombatEffect.Radius"/> is scaled to the
    /// radius it is played with (explosions).
    /// <para>A looping effect (<see cref="CombatEffect.Looping"/>, stage Э2: streams) has one instance, kept playing by
    /// <see cref="Hold"/> every frame and faded out by <see cref="Release"/>; its radius is the reach, fitted by the
    /// particles' lifetime.</para>
    /// <para>A followed effect (stage Э3: trails of monsters' projectiles, what falls onto an area strike) loops on a
    /// pooled instance that <see cref="Move"/> carries every frame until <see cref="Unfollow"/>; a handle whose instance
    /// was taken by a newer play is ignored. Monsters' looks are loaded with the level: ability impacts and falls, death
    /// effects, the trails of their views, champions' death blasts, the rage flash.</para>
    /// </summary>
    public sealed class PackEffectsPlayer : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        /// <summary>Instances of one effect: a rifle's flash lives ~0.7 s at 10 shots a second.</summary>
        public const int PoolSize = 6;

        /// <summary>A followed effect not moved or let go for this long fades by itself (a lost handle).</summary>
        private const float FollowTimeout = 8f;

        private readonly CombatVisualDefinition _visual;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly VisibilitySystem _visibility;
        private readonly LevelData _level;
        private readonly DropPool _drops;
        private readonly PlayerDefinition _player;
        private readonly ThrowableSlot _slot;
        private readonly List<Instance> _all = new List<Instance>();

        private readonly Dictionary<string, Kind> _kinds = new Dictionary<string, Kind>();
        private readonly List<Shown> _shown = new List<Shown>();
        private readonly List<GameObject> _warmup = new List<GameObject>();
        private readonly HashSet<string> _missing = new HashSet<string>();
        private Transform _parent;

        public PackEffectsPlayer(CombatVisualDefinition visual, IAssetOwner assets, LevelViewRoot root,
            VisibilitySystem visibility, LevelData level, DropPool drops = null, PlayerDefinition player = null,
            ThrowableSlot slot = null)
        {
            _visual = visual;
            _assets = assets;
            _root = root;
            _visibility = visibility;
            _level = level;
            _drops = drops;
            _player = player;
            _slot = slot;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>Effects being shown now.</summary>
        public int ActiveEffects => _shown.Count;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var wanted = new List<AssetReferenceGameObject>();
            CollectWanted(wanted);
            if (wanted.Count == 0) return;

            _parent = new GameObject("Pack Effects").transform;
            _parent.SetParent(_root != null ? _root.transform : null, false);
            foreach (var reference in wanted)
            {
                var key = reference.AssetGUID;
                if (_kinds.ContainsKey(key)) continue;
                var prefab = await _assets.LoadAsync<GameObject>(reference, cancellation);
                cancellation.ThrowIfCancellationRequested();
                // A looping effect is held by its one user (the player's stream).
                var effect = prefab.GetComponent<CombatEffect>();
                var count = effect != null && effect.Looping ? 1 : PoolSize;
                var kind = new Kind { Instances = new Instance[count] };
                for (var i = 0; i < count; i++)
                {
                    kind.Instances[i] = Create(prefab);
                    kind.Instances[i].Id = _all.Count;
                    _all.Add(kind.Instances[i]);
                }
                _kinds[key] = kind;
                _warmup.Add(kind.Instances[0].View);
            }
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_warmup);

        /// <summary>
        /// Plays the effect at a point of the view root (+Z of <paramref name="rotation"/> = the effect's forward).
        /// <paramref name="radius"/> &gt; 0 scales an effect that has a radius to it. False: no such effect loaded,
        /// or the cell is not seen.
        /// </summary>
        public bool Play(AssetReferenceGameObject reference, Vector3 position, Quaternion rotation, float radius = 0f)
        {
            if (!CombatVisualDefinition.IsSet(reference)) return false;
            if (!_kinds.TryGetValue(reference.AssetGUID, out var kind))
            {
                if (_missing.Add(reference.AssetGUID))
                    GameLog.Warning(LogChannel.Visual, $"Effect {reference.AssetGUID} was not loaded with the level: not shown.");
                return false;
            }

            if (!_visibility.IsVisible(PlayerMovement.CellOf(new Vector2(position.x, position.z))))
                return false;

            var instance = Take(kind);
            var transform = instance.View.transform;
            transform.SetLocalPositionAndRotation(position, rotation);
            instance.Effect.SetSpread(radius > 0f && instance.Effect.Radius > 0f ? radius / instance.Effect.Radius : 1f);
            if (instance.Followed) instance.Effect.SetLoop(false);
            instance.Followed = false;
            instance.View.SetActive(true);
            instance.Effect.Play();
            _shown.Add(new Shown { Instance = instance, Left = instance.Effect.Duration });
            return true;
        }

        /// <summary>
        /// Starts a looping copy of the effect that follows something (<see cref="Move"/> every frame,
        /// <see cref="Unfollow"/> at the end). Returns its handle, or -1: not loaded, or the cell is not seen.
        /// </summary>
        public int Follow(AssetReferenceGameObject reference, Vector3 position, Quaternion rotation)
        {
            if (!IsLoaded(reference) || !_visibility.IsVisible(PlayerMovement.CellOf(new Vector2(position.x, position.z))))
                return -1;
            var kind = _kinds[reference.AssetGUID];
            if (kind.Instances.Length == 1 && kind.Instances[0].Held) return -1; // a stream's one copy
            var instance = Take(kind);
            instance.View.transform.SetLocalPositionAndRotation(position, rotation);
            instance.Effect.SetSpread(1f);
            instance.Effect.SetLoop(true);
            instance.Followed = true;
            instance.View.SetActive(true);
            instance.Effect.Play();
            _shown.Add(new Shown { Instance = instance, Left = FollowTimeout });
            return instance.Id | ((instance.Generation & 0x7FFF) << 16);
        }

        /// <summary>Carries a followed effect (ignored when its instance was taken by a newer play).</summary>
        public void Move(int handle, Vector3 position, Quaternion rotation)
        {
            var instance = Followed(handle);
            if (instance == null) return;
            instance.View.transform.SetLocalPositionAndRotation(position, rotation);
            var index = IndexOf(instance);
            if (index >= 0) _shown[index] = new Shown { Instance = instance, Left = FollowTimeout };
        }

        /// <summary>A followed effect stops emitting and fades out where it is.</summary>
        public void Unfollow(int handle)
        {
            var instance = Followed(handle);
            if (instance == null) return;
            instance.Effect.Stop();
            instance.Effect.SetLoop(false);
            instance.Followed = false;
            var index = IndexOf(instance);
            if (index >= 0) _shown[index] = new Shown { Instance = instance, Left = instance.Effect.Duration };
        }

        private Instance Followed(int handle)
        {
            if (handle < 0) return null;
            var id = handle & 0xFFFF;
            if (id >= _all.Count) return null;
            var instance = _all[id];
            return instance.Followed && instance.Busy && (instance.Generation & 0x7FFF) == (handle >> 16) ? instance : null;
        }

        /// <summary>The effect was loaded with the level (it can be played or held).</summary>
        public bool IsLoaded(AssetReferenceGameObject reference) =>
            CombatVisualDefinition.IsSet(reference) && _kinds.ContainsKey(reference.AssetGUID);

        /// <summary>
        /// Keeps a looping effect (a stream) playing at the point, called every frame while it should show; started
        /// when it is not. <paramref name="reach"/> &gt; 0 fits an effect with a radius to it by its particles'
        /// lifetime. <see cref="Release"/> stops it. False: not loaded.
        /// </summary>
        /// <param name="cut">&gt; 0: everything farther than this along the effect's forward is cut (a wall, stage Э3).</param>
        public bool Hold(AssetReferenceGameObject reference, Vector3 position, Quaternion rotation, float reach = 0f, float cut = 0f)
        {
            if (!IsLoaded(reference)) return false;
            var instance = _kinds[reference.AssetGUID].Instances[0];
            var transform = instance.View.transform;
            transform.SetLocalPositionAndRotation(position, rotation);
            // Every frame: a wall ahead shortens the reach of new particles and cuts what flies on (stage Э3).
            instance.Effect.SetLifetimeFactor(reach > 0f && instance.Effect.Radius > 0f ? reach / instance.Effect.Radius : 1f);
            if (cut > 0f) instance.Effect.SetClipPlane(transform.forward, transform.position + transform.forward * cut);
            else instance.Effect.ClearClipPlane();
            if (instance.Held) return true;

            var index = IndexOf(instance);
            if (index >= 0)
            {
                // Still fading from the last hold (a gap between attacks): emit on, keep the flame that is out.
                _shown.RemoveAt(index);
                instance.Effect.Resume();
            }
            else
            {
                instance.View.SetActive(true);
                instance.Effect.Play();
            }
            instance.Busy = true;
            instance.Held = true;
            _shown.Add(new Shown { Instance = instance, Left = float.PositiveInfinity });
            return true;
        }

        /// <summary>A held effect stops emitting and fades out.</summary>
        public void Release(AssetReferenceGameObject reference)
        {
            if (!IsLoaded(reference)) return;
            var instance = _kinds[reference.AssetGUID].Instances[0];
            if (!instance.Held) return;
            instance.Held = false;
            instance.Effect.Stop();
            var index = IndexOf(instance);
            if (index >= 0) _shown[index] = new Shown { Instance = instance, Left = instance.Effect.Duration };
        }

        public void LateTick(float deltaTime)
        {
            for (var i = _shown.Count - 1; i >= 0; i--)
            {
                var shown = _shown[i];
                shown.Left -= deltaTime;
                if (shown.Left > 0f)
                {
                    _shown[i] = shown;
                    continue;
                }

                shown.Instance.View.SetActive(false);
                shown.Instance.Busy = false;
                if (shown.Instance.Followed)
                {
                    shown.Instance.Effect.SetLoop(false);
                    shown.Instance.Followed = false;
                }
                _shown.RemoveAt(i);
            }
        }

        public void Dispose()
        {
            foreach (var kind in _kinds.Values)
            foreach (var instance in kind.Instances)
                if (instance != null) UnityObjects.Destroy(instance.View);
            _kinds.Clear();
            _all.Clear();
            _shown.Clear();
            _warmup.Clear();
            _missing.Clear();
            if (_parent != null) UnityObjects.Destroy(_parent.gameObject);
            _parent = null;
        }

        private void CollectWanted(List<AssetReferenceGameObject> wanted)
        {
            if (_visual != null)
            {
                foreach (var entry in _visual.MuzzleFlashes)
                    if (entry != null) Add(wanted, entry.Effect);
                Add(wanted, _visual.ExplosionSmall);
                Add(wanted, _visual.ExplosionMedium);
                Add(wanted, _visual.ExplosionLarge);
                Add(wanted, _visual.Stream);
                Add(wanted, _visual.ChainHit);
                Add(wanted, _visual.BarrelSmoke);
                Add(wanted, _visual.RageFlash);
            }

            CollectMonsters(wanted);
            // The run's level-up flash (stage 6e): levels with waves.
            if (_visual != null && _level != null && _level.Horde != null && _level.Horde.Waves != null)
                Add(wanted, _visual.LevelUp);

            if (_level != null)
            {
                foreach (var pickup in _level.Weapons) AddWeapon(wanted, pickup.Definition);
                AddWeapon(wanted, _level.StartWeapon); // the run's start weapon (stage 6e)
            }
            if (_drops != null)
            {
                foreach (var weapon in _drops.Weapons)
                {
                    AddWeapon(wanted, weapon);
                    if (weapon != null && _visual != null) Add(wanted, _visual.LootRayOf(weapon.Rarity)); // stage 6e
                }
                foreach (var throwable in _drops.Throwables) AddThrowable(wanted, throwable);
            }
            if (_player != null)
            {
                AddWeapon(wanted, _player.BaseWeapon);
                AddEffects(wanted, _player.ReviveEffects); // the revive's wave (stage 6d)
            }
            if (_slot != null) AddThrowable(wanted, _slot.Definition);
        }

        /// <summary>Looks of the monsters the level can have (the waves' and the called-up kinds) and of champions.</summary>
        private void CollectMonsters(List<AssetReferenceGameObject> wanted)
        {
            var settings = _level != null ? _level.Horde : null;
            if (settings == null) return;
            foreach (var monster in settings.AllMonsters()) AddMonster(wanted, monster);
            foreach (var view in settings.MonsterViews)
                if (view != null) Add(wanted, view.ProjectileTrail);
            foreach (var affix in settings.Champions.Affixes)
            {
                if (affix == null) continue;
                foreach (var module in affix.Modules)
                    if (module is DeathBlastAffix blast) AddEffects(wanted, blast.Effects);
            }
        }

        private static void AddMonster(List<AssetReferenceGameObject> wanted, MonsterDefinition monster)
        {
            if (monster == null) return;
            AddEffects(wanted, monster.DeathEffects);
            foreach (var ability in monster.Abilities)
                switch (ability)
                {
                    case ProjectileAbility projectile:
                        AddEffects(wanted, projectile.Impact);
                        break;
                    case AreaStrikeAbility area:
                        AddEffects(wanted, area.Impact);
                        Add(wanted, area.Fall);
                        break;
                }
        }

        private static void AddEffects(List<AssetReferenceGameObject> wanted, IReadOnlyList<EffectModule> effects)
        {
            if (effects == null) return;
            foreach (var effect in effects)
                if (effect is IExplosionLook look) Add(wanted, look.Look);
        }

        private static void AddWeapon(List<AssetReferenceGameObject> wanted, WeaponDefinition weapon)
        {
            if (weapon == null) return;
            if (weapon.Effects != null)
            {
                Add(wanted, weapon.Effects.MuzzleFlash);
                Add(wanted, weapon.Effects.Stream);
                Add(wanted, weapon.Effects.TargetHit);
                Add(wanted, weapon.Effects.BarrelSmoke);
            }
            if (weapon.Attack is ExplosiveAttack explosive) Add(wanted, explosive.Look);
        }

        private static void AddThrowable(List<AssetReferenceGameObject> wanted, ThrowableDefinition throwable)
        {
            if (throwable == null) return;
            AddEffects(wanted, throwable.Effects);
        }

        private static void Add(List<AssetReferenceGameObject> wanted, AssetReferenceGameObject reference)
        {
            if (CombatVisualDefinition.IsSet(reference)) wanted.Add(reference);
        }

        private Instance Create(GameObject prefab)
        {
            var view = UnityEngine.Object.Instantiate(prefab, _parent);
            view.name = prefab.name;
            view.SetActive(false);
            var effect = view.GetComponent<CombatEffect>() ?? view.AddComponent<CombatEffect>();
            return new Instance { View = view, Effect = effect };
        }

        /// <summary>A free instance, or the one shown longest (restarted in the new place).</summary>
        private Instance Take(Kind kind)
        {
            foreach (var instance in kind.Instances)
                if (!instance.Busy)
                {
                    instance.Busy = true;
                    instance.Generation++;
                    return instance;
                }

            for (var i = 0; i < _shown.Count; i++)
            {
                var oldest = _shown[i].Instance;
                if (oldest.Held || Array.IndexOf(kind.Instances, oldest) < 0) continue;
                _shown.RemoveAt(i); // the list is in start order: the first of the kind is the oldest
                oldest.Generation++;
                return oldest;
            }

            return kind.Instances[0];
        }

        private int IndexOf(Instance instance)
        {
            for (var i = 0; i < _shown.Count; i++)
                if (_shown[i].Instance == instance) return i;
            return -1;
        }

        private sealed class Kind
        {
            public Instance[] Instances;
        }

        private sealed class Instance
        {
            public GameObject View;
            public CombatEffect Effect;
            public bool Busy;
            public bool Held;
            public bool Followed;
            public int Id;
            public int Generation;
        }

        private struct Shown
        {
            public Instance Instance;
            public float Left;
        }
    }
}
